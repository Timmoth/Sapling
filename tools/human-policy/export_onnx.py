#!/usr/bin/env python3
"""Export the trained policy to ONNX and verify CPU inference parity."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import chess
import numpy as np
import onnxruntime as ort
import torch

from dataset import (
    MAX_RATING,
    MIN_RATING,
    SCHEMA_VERSION,
    encode_board,
    encode_move,
    encode_position,
    encode_rating,
)
from model import HumanMovePolicy


def row_to_inputs(row: dict) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    board = chess.Board(row["fen"])
    legal_moves = list(board.legal_moves)
    _, moves, rating, _ = encode_position(
        board,
        legal_moves,
        chess.Move.from_uci(row["move"]),
        int(row["rating"]),
        {uci: int(score) for uci, score in row["engine_scores"].items()},
    )
    return (
        np.asarray(encode_board(board), dtype=np.float32).reshape(1, 18, 8, 8),
        np.asarray(moves, dtype=np.float32).reshape(1, len(legal_moves), 134),
        np.asarray([[rating]], dtype=np.float32),
    )


def parity_inputs(data_dir: Path, golden_path: Path, count: int):
    inputs = []
    train_path = data_dir / "train.jsonl"
    if not train_path.is_file():
        raise ValueError(f"training split not found: {train_path}")
    with train_path.open(encoding="utf-8") as stream:
        for line in stream:
            if not line.strip():
                continue
            inputs.append(row_to_inputs(json.loads(line)))
            if len(inputs) >= count:
                break
    if len(inputs) < count:
        raise ValueError(f"wanted {count} PGN parity positions, found {len(inputs)}")

    fixtures = json.loads(golden_path.read_text(encoding="utf-8"))
    for case in fixtures["cases"]:
        board = chess.Board(case["fen"])
        legal_moves = list(board.legal_moves)
        moves = np.asarray(
            [encode_move(move, 0, 0) for move in legal_moves], dtype=np.float32
        ).reshape(1, len(legal_moves), 134)
        inputs.append((
            np.asarray(encode_board(board), dtype=np.float32).reshape(1, 18, 8, 8),
            moves,
            np.asarray([[encode_rating(1500)]], dtype=np.float32),
        ))
    return inputs


def policy_probability_total_variation(expected: np.ndarray, actual: np.ndarray) -> float:
    """Compare move policies, ignoring harmless shared offsets in their logits."""
    expected = np.asarray(expected, dtype=np.float64)
    actual = np.asarray(actual, dtype=np.float64)
    if expected.shape != actual.shape or expected.ndim != 2:
        raise ValueError("expected and actual logits must have the same [B,N] shape")

    def softmax(logits: np.ndarray) -> np.ndarray:
        shifted = logits - np.max(logits, axis=-1, keepdims=True)
        weights = np.exp(shifted)
        return weights / np.sum(weights, axis=-1, keepdims=True)

    return float(np.max(np.sum(np.abs(softmax(expected) - softmax(actual)), axis=-1) / 2.0))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--checkpoint", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    parser.add_argument("--data-dir", required=True, type=Path,
                        help="Engine-analyzed directory with train.jsonl")
    parser.add_argument("--golden-fixtures", type=Path,
                        default=Path(__file__).parent / "tests" / "golden_cases.json")
    parser.add_argument("--parity-position-count", type=int, default=20)
    parser.add_argument("--probability-tolerance", type=float, default=1e-3,
                        help="maximum total-variation distance between move probabilities")
    parser.add_argument("--opset", type=int, default=17)
    args = parser.parse_args()
    if args.parity_position_count < 1:
        parser.error("--parity-position-count must be positive")

    checkpoint = torch.load(args.checkpoint, map_location="cpu")
    if checkpoint.get("schema_version") != SCHEMA_VERSION:
        raise ValueError("checkpoint schema version does not match the exporter")
    model = HumanMovePolicy()
    model.load_state_dict(checkpoint["model_state_dict"])
    model.eval()

    args.output.parent.mkdir(parents=True, exist_ok=True)
    example = (
        torch.zeros((1, 18, 8, 8), dtype=torch.float32),
        torch.zeros((1, 20, 134), dtype=torch.float32),
        torch.zeros((1, 1), dtype=torch.float32),
    )
    torch.onnx.export(
        model,
        example,
        str(args.output),
        input_names=["board", "moves", "rating"],
        output_names=["logits"],
        dynamic_axes={"moves": {1: "candidate_count"},
                      "logits": {1: "candidate_count"}},
        opset_version=args.opset,
        do_constant_folding=True,
        dynamo=False,
    )

    session_options = ort.SessionOptions()
    session_options.intra_op_num_threads = 1
    session_options.inter_op_num_threads = 1
    session = ort.InferenceSession(
        str(args.output), sess_options=session_options,
        providers=["CPUExecutionProvider"],
    )
    inputs = parity_inputs(args.data_dir, args.golden_fixtures,
                           args.parity_position_count)
    max_difference = 0.0
    max_probability_tv = 0.0
    top1_mismatches = 0
    with torch.no_grad():
        for board, moves, rating in inputs:
            expected = model(
                torch.from_numpy(board), torch.from_numpy(moves),
                torch.from_numpy(rating),
            ).numpy()
            actual = session.run(None, {
                "board": board, "moves": moves, "rating": rating,
            })[0]
            if actual.shape != expected.shape:
                raise AssertionError(
                    f"ONNX output shape {actual.shape} differs from PyTorch {expected.shape}"
                )
            max_difference = max(
                max_difference, float(np.max(np.abs(actual - expected)))
            )
            max_probability_tv = max(
                max_probability_tv,
                policy_probability_total_variation(expected, actual),
            )
            top1_mismatches += int(
                np.argmax(actual, axis=-1)[0] != np.argmax(expected, axis=-1)[0]
            )
    if max_probability_tv >= args.probability_tolerance or top1_mismatches:
        raise AssertionError(
            "PyTorch/ONNX policy mismatch: "
            f"max probability total-variation distance {max_probability_tv:.8g} "
            f"(tolerance {args.probability_tolerance}), "
            f"top-1 mismatches {top1_mismatches}/{len(inputs)}"
        )

    manifest = {
        "model_version": "human_policy_v1",
        "schema_version": SCHEMA_VERSION,
        "inputs": {
            "board": {"shape": [1, 18, 8, 8], "dtype": "float32"},
            "moves": {"shape": [1, "N", 134], "dtype": "float32"},
            "rating": {"shape": [1, 1], "dtype": "float32"},
        },
        "output": {"name": "logits", "shape": [1, "N"], "dtype": "float32"},
        "rating_bounds": [MIN_RATING, MAX_RATING],
        "onnx_opset": args.opset,
        "parity_positions": len(inputs),
        "max_abs_logit_difference": max_difference,
        "max_probability_total_variation": max_probability_tv,
        "top1_mismatches": top1_mismatches,
        "parity_metric": "softmax total-variation distance plus exact top-1 agreement",
        "probability_tolerance": args.probability_tolerance,
    }
    args.output.with_suffix(".json").write_text(
        json.dumps(manifest, indent=2) + "\n", encoding="utf-8"
    )
    print(json.dumps(manifest, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
