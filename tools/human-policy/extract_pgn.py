#!/usr/bin/env python3
"""Stream a Lichess monthly PGN archive into deterministic position JSONL splits."""

from __future__ import annotations

import argparse
import collections
import concurrent.futures
import hashlib
import io
import itertools
import json
import random
import re
import sys
import time
from collections import deque
from pathlib import Path

import chess
import chess.pgn
import zstandard


RAPID_MIN_SECONDS = 480
RAPID_MAX_SECONDS = 1499
RATING_BAND_WIDTH = 200
PROMPT_SCHEMA_VERSION = 1
TIME_CONTROL = re.compile(r"^(\d+(?:\.\d+)?)(?:\+(\d+(?:\.\d+)?))?$")


def open_pgn(path: Path):
    if str(path) == "-":
        # Read externally decompressed PGN text from stdin.
        return sys.stdin
    raw = path.open("rb")
    if path.name.endswith(".zst"):
        reader = zstandard.ZstdDecompressor().stream_reader(raw)
        return io.TextIOWrapper(reader, encoding="utf-8", errors="replace")
    return io.TextIOWrapper(raw, encoding="utf-8", errors="replace")


def iter_pgn_text_games(stream, chunk_size: int = 1024 * 1024):
    """Yield raw games by scanning large text chunks for line-start Event tags."""
    current: list[str] = []
    overlap = ""
    first_chunk = True
    delimiter = "[Event "
    overlap_size = len(delimiter) + 1  # delimiter plus its preceding newline
    while True:
        chunk = stream.read(chunk_size)
        if not chunk:
            break
        data = overlap + chunk
        overlap = ""
        if first_chunk:
            data = data.lstrip("\ufeff")
            first_chunk = False

        boundaries = []
        search_from = 0
        while True:
            index = data.find(delimiter, search_from)
            if index < 0:
                break
            if index == 0 or data[index - 1] == "\n":
                boundaries.append(index)
            search_from = index + len(delimiter)

        if not boundaries:
            split_at = max(0, len(data) - overlap_size)
            if current:
                current.append(data[:split_at])
            overlap = data[split_at:]
            continue

        if current:
            current.append(data[:boundaries[0]])
            yield "".join(current)
        for start, end in zip(boundaries, boundaries[1:]):
            yield data[start:end]
        current = [data[boundaries[-1]:]]

    if current:
        current.append(overlap)
        yield "".join(current)


def headers_from_pgn_text(pgn_text: str):
    separator = re.search(r"\r?\n\s*\r?\n", pgn_text)
    if separator is None:
        return chess.pgn.Headers()
    return chess.pgn.read_headers(io.StringIO(pgn_text[:separator.start()] + "\n\n"))


def rapid_time_control(value: str) -> bool:
    """Apply Lichess's estimated duration rule to simple base+increment controls."""
    match = TIME_CONTROL.fullmatch(value.strip())
    if not match:
        return False
    base = float(match.group(1))
    increment = float(match.group(2) or 0)
    estimated_seconds = base + 40 * increment
    return RAPID_MIN_SECONDS <= estimated_seconds <= RAPID_MAX_SECONDS


def parse_rating(value: str | None, minimum: int, maximum: int) -> int | None:
    if value is None or not value.isdigit():
        return None
    rating = int(value)
    return rating if minimum <= rating <= maximum else None


def game_id(game: chess.pgn.Game) -> str:
    site = game.headers.get("Site", "").strip()
    if site:
        return site.rstrip("/").rsplit("/", 1)[-1]
    digest = hashlib.sha256()
    for key in ("Event", "Date", "Round", "White", "Black", "WhiteElo", "BlackElo"):
        digest.update(game.headers.get(key, "").encode("utf-8", errors="replace"))
        digest.update(b"\0")
    for move in game.mainline_moves():
        digest.update(move.uci().encode("ascii"))
    return digest.hexdigest()


def split_for(game_key: str) -> str:
    shard = int.from_bytes(hashlib.sha256(game_key.encode("utf-8")).digest()[:8], "big") % 100
    if shard < 90:
        return "train"
    if shard < 95:
        return "validation"
    return "test"


def is_standard_rated_rapid(headers) -> tuple[bool, str]:
    headers = getattr(headers, "headers", headers)
    variant = headers.get("Variant", "Standard").strip().lower()
    if variant not in ("standard", "chess"):
        return False, "variant"
    if "FEN" in headers and headers["FEN"].strip() != chess.STARTING_FEN:
        return False, "nonstandard_start"
    if not re.search(r"\brated\b", headers.get("Event", ""), re.IGNORECASE):
        return False, "not_rated"
    if not rapid_time_control(headers.get("TimeControl", "")):
        return False, "not_rapid_or_unknown_time_control"
    if any(headers.get(f"{color}Title", "").strip().upper() == "BOT" for color in ("White", "Black")):
        return False, "bot"
    return True, ""


def rating_band(rating: int) -> str:
    lower = (rating // RATING_BAND_WIDTH) * RATING_BAND_WIDTH
    return f"{lower}-{lower + RATING_BAND_WIDTH - 1}"


def collect_game_positions(game: chess.pgn.Game, minimum: int, maximum: int):
    headers = game.headers
    white_rating = parse_rating(headers.get("WhiteElo"), minimum, maximum)
    black_rating = parse_rating(headers.get("BlackElo"), minimum, maximum)
    if white_rating is None or black_rating is None:
        return None, "missing_or_out_of_range_rating"

    key = game_id(game)
    split = split_for(key)
    board = game.board()
    if board.chess960 or not board.is_valid():
        return None, "invalid_start_position"

    rows = []
    try:
        for ply, move in enumerate(game.mainline_moves()):
            if not board.is_legal(move):
                return None, "illegal_move"
            mover_rating = white_rating if board.turn == chess.WHITE else black_rating
            # Keep the FEN target square even if no legal en-passant capture exists.
            fen = board.fen(en_passant="fen")
            rows.append({
                "game_id": key,
                "ply": ply,
                "fen": fen,
                "move": move.uci(),
                "rating": mover_rating,
                "pool": "rapid",
                "split": split,
            })
            board.push(move)
    except (ValueError, AssertionError):
        return None, "corrupt_game"
    if game.errors:
        return None, "pgn_errors"
    return rows, ""


def process_pgn_text(pgn_text: str, minimum: int, maximum: int):
    """Parse and validate one game. Kept top-level so worker processes can pickle it."""
    try:
        headers = headers_from_pgn_text(pgn_text)
    except (ValueError, UnicodeError):
        return None, "pgn_header_error"
    keep, reason = is_standard_rated_rapid(headers)
    if not keep:
        return None, f"skip_{reason}"
    if (parse_rating(headers.get("WhiteElo"), minimum, maximum) is None
            or parse_rating(headers.get("BlackElo"), minimum, maximum) is None):
        return None, "skip_missing_or_out_of_range_rating"
    try:
        game = chess.pgn.read_game(io.StringIO(pgn_text))
    except (ValueError, UnicodeError):
        return None, "pgn_read_error"
    if game is None:
        return None, "pgn_read_error"
    if game.errors:
        return None, "skip_pgn_errors"
    rows, reason = collect_game_positions(game, minimum, maximum)
    if rows is None:
        return None, f"skip_{reason}"
    return rows, ""


def process_pgn_batch(pgn_texts: list[str], minimum: int, maximum: int):
    """Amortize process-pool IPC by parsing a bounded batch in each task."""
    return [process_pgn_text(text, minimum, maximum) for text in pgn_texts]


def iter_batches(items, batch_size: int):
    batch = []
    for item in items:
        batch.append(item)
        if len(batch) == batch_size:
            yield batch
            batch = []
    if batch:
        yield batch


def iter_processed_games(pgn_texts, workers: int, minimum: int, maximum: int,
                        batch_size: int = 64):
    """Process games concurrently while yielding in archive order for deterministic sampling."""
    if workers == 1:
        for pgn_text in pgn_texts:
            yield process_pgn_text(pgn_text, minimum, maximum)
        return

    # Keep the queue bounded: a whole monthly archive must never be submitted eagerly.
    pending = deque()
    max_pending = workers * 2
    with concurrent.futures.ProcessPoolExecutor(max_workers=workers) as pool:
        for batch in iter_batches(pgn_texts, batch_size):
            pending.append(pool.submit(process_pgn_batch, batch, minimum, maximum))
            if len(pending) >= max_pending:
                yield from pending.popleft().result()
        while pending:
            yield from pending.popleft().result()


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--input", required=True, type=Path,
                        help="Monthly .pgn.zst or plain .pgn archive; '-' reads PGN text from stdin")
    parser.add_argument("--pool", choices=("rapid",), default="rapid")
    parser.add_argument("--min-rating", type=int, default=800)
    parser.add_argument("--max-rating", type=int, default=2200)
    parser.add_argument("--max-games", type=int, default=10000, help="Maximum games scanned; 0 means no limit")
    parser.add_argument("--max-positions-per-band", type=int, default=30000,
                        help="Reservoir limit for each 200-point mover-rating band")
    parser.add_argument("--workers", type=int, default=1,
                        help="Parallel PGN game parsers; output sampling stays deterministic")
    parser.add_argument("--batch-size", type=int, default=64,
                        help="Games parsed per worker task to reduce process-pool overhead")
    parser.add_argument("--allow-truncated-zstd", action="store_true",
                        help="Keep complete games before an incomplete final Zstandard frame")
    parser.add_argument("--seed", type=int, default=1729)
    parser.add_argument("--archive-sha256", default=None)
    parser.add_argument("--preprocessing-commit", default=None)
    parser.add_argument("--output", required=True, type=Path, help="Directory for split JSONL and manifest")
    args = parser.parse_args()

    if args.min_rating < 0 or args.max_rating <= args.min_rating:
        parser.error("require 0 <= --min-rating < --max-rating")
    if args.max_games < 0 or args.max_positions_per_band < 1 or args.workers < 1 or args.batch_size < 1:
        parser.error("--max-games must be >= 0, --max-positions-per-band, --workers, and --batch-size must be >= 1")
    if str(args.input) != "-" and not args.input.is_file():
        parser.error(f"input archive does not exist: {args.input}")

    print(json.dumps({
        "input": str(args.input), "pool": args.pool,
        "rating_range": [args.min_rating, args.max_rating],
        "max_games": args.max_games,
        "max_positions_per_band": args.max_positions_per_band,
        "workers": args.workers,
        "batch_size": args.batch_size,
        "seed": args.seed, "output": str(args.output),
    }, sort_keys=True), file=sys.stderr)

    rng = random.Random(args.seed)
    samples: dict[str, list[dict]] = collections.defaultdict(list)
    seen_by_band: collections.Counter = collections.Counter()
    counters: collections.Counter = collections.Counter()
    scanned = accepted_games = 0
    started = time.monotonic()
    try:
        with open_pgn(args.input) as stream:
            pgn_texts = iter_pgn_text_games(stream)
            if args.max_games:
                pgn_texts = itertools.islice(pgn_texts, args.max_games)
            for rows, reason in iter_processed_games(
                    pgn_texts, args.workers, args.min_rating, args.max_rating, args.batch_size):
                scanned += 1
                if scanned % 100_000 == 0:
                    elapsed = time.monotonic() - started
                    rate = scanned / elapsed if elapsed else 0.0
                    print(f"extract progress: games_scanned={scanned} accepted={accepted_games} "
                          f"positions_seen={sum(seen_by_band.values())} "
                          f"games_per_second={rate:.1f}", file=sys.stderr, flush=True)
                if rows is None:
                    counters[reason] += 1
                    continue
                accepted_games += 1
                counters["accepted_games"] += 1
                for row in rows:
                    band = rating_band(row["rating"])
                    seen_by_band[band] += 1
                    row["rating_band"] = band
                    bucket = samples[band]
                    if len(bucket) < args.max_positions_per_band:
                        bucket.append(row)
                    else:
                        slot = rng.randrange(seen_by_band[band])
                        if slot < args.max_positions_per_band:
                            bucket[slot] = row
    except zstandard.ZstdError:
        if not args.allow_truncated_zstd:
            raise
        counters["truncated_zstd_tail"] += 1

    args.output.mkdir(parents=True, exist_ok=True)
    paths = {split: args.output / f"{split}.jsonl" for split in ("train", "validation", "test")}
    for path in paths.values():
        path.write_text("", encoding="utf-8")
    final_counts: dict[str, dict[str, int]] = {split: {} for split in paths}
    final_games: dict[str, set[str]] = {split: set() for split in paths}
    with (paths["train"].open("w", encoding="utf-8") as train,
          paths["validation"].open("w", encoding="utf-8") as validation,
          paths["test"].open("w", encoding="utf-8") as test):
        output_streams = {"train": train, "validation": validation, "test": test}
        for band in sorted(samples):
            for row in sorted(samples[band], key=lambda item: (item["game_id"], item["ply"])):
                split = row["split"]
                output_streams[split].write(json.dumps(row, separators=(",", ":")) + "\n")
                final_counts[split][band] = final_counts[split].get(band, 0) + 1
                final_games[split].add(row["game_id"])

    manifest = {
        "schema_version": PROMPT_SCHEMA_VERSION,
        "source": {
            "archive": args.input.name,
            "sha256": args.archive_sha256,
            "official_source": "https://database.lichess.org/standard/",
        },
        "filters": {
            "variant": "standard",
            "rated": True,
            "pool": "rapid",
            "rapid_rule": "initial seconds + 40 * increment seconds in [480, 1499]",
            "rating_tags": ["WhiteElo", "BlackElo"],
            "rating_range": [args.min_rating, args.max_rating],
            "mover_rating": True,
            "exclude_bot_title": True,
            "starting_position": "standard chess start only",
            "plies": "all nonterminal pre-move positions",
        },
        "sampling": {
            "max_games_scanned": args.max_games,
            "compressed_input_bytes": args.input.stat().st_size if args.input.name.endswith(".zst") else None,
            "allow_truncated_zstd": args.allow_truncated_zstd,
            "games_scanned": scanned,
            "accepted_games_before_sampling": accepted_games,
            "max_positions_per_200_point_band": args.max_positions_per_band,
            "seed": args.seed,
            "split_by_game_sha256_mod_100": {"train": "0-89", "validation": "90-94", "test": "95-99"},
        },
        "counts": {
            "skip_reasons": dict(sorted((k, v) for k, v in counters.items() if k.startswith("skip_") or k == "pgn_read_error")),
            "sampled_positions_by_split_and_rating_band": final_counts,
            "sampled_games_by_split": {split: len(ids) for split, ids in final_games.items()},
        },
        "files": {split: path.name for split, path in paths.items()},
        "preprocessing_commit": args.preprocessing_commit,
        "encoder_schema_version": 1,
    }
    (args.output / "manifest.json").write_text(json.dumps(manifest, indent=2) + "\n", encoding="utf-8")
    print(json.dumps({
        "games_scanned": scanned,
        "accepted_games": accepted_games,
        "positions": {split: sum(counts.values()) for split, counts in final_counts.items()},
        "skips": manifest["counts"]["skip_reasons"],
        "manifest": str(args.output / "manifest.json"),
    }, indent=2), file=sys.stderr)
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
