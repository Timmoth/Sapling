#!/usr/bin/env python3
"""Attach Sapling's fixed-depth score for every legal move in sampled positions."""

from __future__ import annotations

import argparse
import atexit
import concurrent.futures
import hashlib
import json
import os
import subprocess
import sys
from pathlib import Path

import chess


_ENGINE = None
_DEPTH = None


def read_until(proc: subprocess.Popen, terminal: str) -> list[str]:
    lines = []
    while True:
        line = proc.stdout.readline()
        if not line:
            raise RuntimeError("Sapling exited while processing rank output")
        line = line.strip()
        lines.append(line)
        if line == terminal:
            return lines


def start_engine(engine: str, depth: int) -> None:
    global _ENGINE, _DEPTH
    _ENGINE = subprocess.Popen(
        [engine], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL,
        text=True, bufsize=1,
    )
    _DEPTH = depth
    _ENGINE.stdin.write("uci\n")
    _ENGINE.stdin.flush()
    read_until(_ENGINE, "uciok")
    _ENGINE.stdin.write("isready\n")
    _ENGINE.stdin.flush()
    read_until(_ENGINE, "readyok")
    atexit.register(_ENGINE.terminate)


def analyze_row(row: dict) -> tuple[dict | None, str | None]:
    try:
        board = chess.Board(row["fen"])
        if board.chess960:
            return None, "chess960_position"
        legal = {move.uci() for move in board.legal_moves}
        if row["move"] not in legal:
            return None, "label_not_legal"

        _ENGINE.stdin.write(f"position fen {row['fen']}\nrank {_DEPTH}\n")
        _ENGINE.stdin.flush()
        output = read_until(_ENGINE, "rankdone")
        scores: dict[str, int] = {}
        for line in output:
            if line.startswith("rankmove "):
                _, uci, score = line.split()
                if uci in scores:
                    return None, "duplicate_engine_move"
                scores[uci] = int(score)
        if set(scores) != legal:
            return None, "engine_legal_move_mismatch"

        enriched = dict(row)
        enriched["engine_depth"] = _DEPTH
        enriched["engine_scores"] = scores
        return enriched, None
    except (KeyError, ValueError, RuntimeError, OSError) as exc:
        return None, f"analysis_error:{type(exc).__name__}:{exc}"


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(8 * 1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def analyze_split(input_path: Path, output_path: Path, rejected_path: Path, workers: int,
                  engine: str, depth: int) -> tuple[int, int, dict[str, int]]:
    failures: dict[str, int] = {}
    written = rejected = 0
    with input_path.open("r", encoding="utf-8") as source, \
            output_path.open("w", encoding="utf-8") as output, \
            rejected_path.open("w", encoding="utf-8") as rejected_output:
        with concurrent.futures.ProcessPoolExecutor(
            max_workers=workers,
            initializer=start_engine,
            initargs=(engine, depth),
        ) as pool:
            pending = []
            for line in source:
                if not line.strip():
                    continue
                row = json.loads(line)
                pending.append((pool.submit(analyze_row, row), row))
                if len(pending) >= max(32, workers * 8):
                    future, source_row = pending.pop(0)
                    result, reason = future.result()
                    if result is None:
                        rejected += 1
                        failures[reason or "unknown"] = failures.get(reason or "unknown", 0) + 1
                        rejected_output.write(json.dumps({"row": source_row, "reason": reason}) + "\n")
                    else:
                        output.write(json.dumps(result, separators=(",", ":")) + "\n")
                        written += 1
                    if (written + rejected) % 100 == 0:
                        print(f"{input_path.name}: analyzed={written} rejected={rejected}", file=sys.stderr)
            for future, source_row in pending:
                result, reason = future.result()
                if result is None:
                    rejected += 1
                    failures[reason or "unknown"] = failures.get(reason or "unknown", 0) + 1
                    rejected_output.write(json.dumps({"row": source_row, "reason": reason}) + "\n")
                else:
                    output.write(json.dumps(result, separators=(",", ":")) + "\n")
                    written += 1
    return written, rejected, failures


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", type=Path, required=True, help="Directory produced by extract_pgn.py")
    parser.add_argument("--engine", type=Path, required=True, help="Executable Sapling build with the rank command")
    parser.add_argument("--depth", type=int, default=8)
    parser.add_argument("--workers", type=int, default=1)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    if args.depth < 1 or args.depth > 64:
        parser.error("--depth must be in [1,64]")
    if args.workers < 1:
        parser.error("--workers must be positive")
    engine = str(args.engine.resolve())
    if not Path(engine).is_file() or not os.access(engine, os.X_OK):
        parser.error(f"engine must be an executable file: {engine}")
    if not (args.input / "manifest.json").is_file():
        parser.error(f"input manifest not found in {args.input}")

    print(json.dumps({
        "input": str(args.input), "engine": engine, "depth": args.depth,
        "workers": args.workers, "output": str(args.output),
    }, sort_keys=True), file=sys.stderr)
    args.output.mkdir(parents=True, exist_ok=True)
    summaries = {}
    for split in ("train", "validation", "test"):
        source = args.input / f"{split}.jsonl"
        if not source.is_file():
            parser.error(f"missing split file: {source}")
        written, rejected, failures = analyze_split(
            source,
            args.output / f"{split}.jsonl",
            args.output / f"{split}.rejected.jsonl",
            args.workers,
            engine,
            args.depth,
        )
        summaries[split] = {"positions_scored": written, "positions_rejected": rejected, "reasons": failures}

    manifest = json.loads((args.input / "manifest.json").read_text(encoding="utf-8"))
    engine_files = [Path(engine), Path(engine).with_suffix(".dll"), Path(engine).parent / "Sapling.Engine.dll"]
    engine_hashes = {
        path.name: sha256_file(path)
        for path in engine_files
        if path.is_file()
    }
    manifest["engine_analysis"] = {
        "engine_executable": Path(engine).name,
        "engine_artifact_sha256": engine_hashes,
        "rank_depth": args.depth,
        "score_perspective": "side to move at root",
        "score_contract": "Sapling rankmove integer; candidate scores must map one-to-one by UCI",
        "workers": args.workers,
        "results": summaries,
    }
    manifest["files"] = {
        split: f"{split}.jsonl" for split in ("train", "validation", "test")
    }
    (args.output / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(summaries, indent=2), file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
