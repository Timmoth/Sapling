#!/usr/bin/env python3
"""Evaluate a saved human-policy checkpoint against a held-out JSONL split."""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import torch
from torch.utils.data import DataLoader

from model import HumanMovePolicy
from train import PositionDataset, collate_positions, evaluate


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--checkpoint", required=True, type=Path)
    parser.add_argument("--dataset-dir", required=True, type=Path)
    parser.add_argument("--split", choices=("validation", "test"), default="test")
    parser.add_argument("--output", type=Path)
    parser.add_argument("--batch-size", type=int, default=128)
    parser.add_argument("--workers", type=int, default=4)
    args = parser.parse_args()

    device = torch.device("cuda" if torch.cuda.is_available() else "cpu")
    checkpoint = torch.load(args.checkpoint, map_location=device)
    if checkpoint.get("schema_version") != 1:
        raise ValueError("checkpoint schema version does not match encoder v1")
    model = HumanMovePolicy().to(device)
    model.load_state_dict(checkpoint["model_state_dict"])
    dataset = PositionDataset(args.dataset_dir / f"{args.split}.jsonl", "none")
    loader = DataLoader(
        dataset, batch_size=args.batch_size, num_workers=args.workers,
        pin_memory=device.type == "cuda", collate_fn=collate_positions,
    )
    result = {
        "checkpoint": str(args.checkpoint),
        "dataset_dir": str(args.dataset_dir),
        "split": args.split,
        "positions": len(dataset),
        "checkpoint_epoch": checkpoint.get("epoch"),
        "metrics": evaluate(model, loader, device),
    }
    rendered = json.dumps(result, indent=2) + "\n"
    if args.output:
        args.output.write_text(rendered, encoding="utf-8")
    print(rendered, end="")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
