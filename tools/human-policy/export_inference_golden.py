#!/usr/bin/env python3
"""Create cross-runtime inference fixtures from a trained checkpoint."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import chess
import torch

from dataset import encode_board, encode_move, encode_rating
from model import HumanMovePolicy


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--checkpoint", required=True, type=Path)
    parser.add_argument("--positions", required=True, type=Path)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()

    checkpoint = torch.load(args.checkpoint, map_location="cpu", weights_only=False)
    if checkpoint.get("schema_version") != 1:
        raise ValueError("checkpoint schema_version must be 1")
    model = HumanMovePolicy()
    model.load_state_dict(checkpoint["model_state_dict"])
    model.eval()

    source = json.loads(args.positions.read_text(encoding="utf-8"))
    ratings = (800, 1100, 1500, 1900, 2200)
    cases = []
    with torch.inference_mode():
        for index, position in enumerate(source["cases"]):
            board = chess.Board(position["fen"])
            legal_moves = list(board.legal_moves)
            scores = {
                move.uci(): ((move.from_square * 113 + move.to_square * 37 +
                              (move.promotion or 0) * 19) % 1801) - 900
                for move in legal_moves
            }
            best_score = max(scores.values())
            move_features = [
                encode_move(move, scores[move.uci()], best_score) for move in legal_moves
            ]
            rating = ratings[index % len(ratings)]
            logits = model(
                torch.tensor(encode_board(board), dtype=torch.float32).reshape(1, 18, 8, 8),
                torch.tensor(move_features, dtype=torch.float32).reshape(1, len(legal_moves), 134),
                torch.tensor([[encode_rating(rating)]], dtype=torch.float32),
            )[0].tolist()
            cases.append({
                "name": position["name"],
                "fen": position["fen"],
                "rating": rating,
                "engine_scores": scores,
                "expected_logits": {
                    move.uci(): float(logit) for move, logit in zip(legal_moves, logits)
                },
            })

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps({"schema_version": 1, "cases": cases}, indent=2) + "\n",
                           encoding="utf-8")
    print(f"wrote {len(cases)} inference parity cases to {args.output}")


if __name__ == "__main__":
    main()
