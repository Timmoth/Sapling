#!/usr/bin/env python3
"""Train the Sapling human move policy on a GPU or CPU using scored JSONL splits."""

from __future__ import annotations

import argparse
from collections import Counter
import json
import math
import random
import sys
from pathlib import Path

import chess
import numpy as np
import torch
import torch.nn.functional as F
import yaml
from torch.utils.data import DataLoader, Dataset, WeightedRandomSampler

from dataset import MAX_RATING, SCHEMA_VERSION, encode_position
from model import HumanMovePolicy


class PositionDataset(Dataset):
    def __init__(self, path: Path, ablation: str):
        self.rows = [json.loads(line) for line in path.open(encoding="utf-8") if line.strip()]
        self.ablation = ablation
        if not self.rows:
            raise ValueError(f"split contains no positions: {path}")

    def __len__(self):
        return len(self.rows)

    def __getitem__(self, index):
        row = self.rows[index]
        board = chess.Board(row["fen"])
        legal_moves = list(board.legal_moves)
        played_move = chess.Move.from_uci(row["move"])
        engine_scores = row.get("engine_scores")
        if not isinstance(engine_scores, dict) or row.get("engine_depth") is None:
            raise ValueError(f"position {row.get('game_id')}:{row.get('ply')} lacks engine analysis")
        board_features, move_features, rating, target = encode_position(
            board, legal_moves, played_move, int(row["rating"]),
            {uci: int(score) for uci, score in engine_scores.items()},
        )
        board_array = np.asarray(board_features, dtype=np.float32).reshape(18, 8, 8)
        move_array = np.asarray(move_features, dtype=np.float32)
        if self.ablation == "rating":
            rating = 0.0
        elif self.ablation == "engine":
            move_array[:, 132:134] = 0.0
        return {
            "board": board_array,
            "moves": move_array,
            "rating": np.asarray([rating], dtype=np.float32),
            "target": target,
            "ply": int(row["ply"]),
            "rating_band": rating_band(int(row["rating"])),
        }


def rating_band(rating: int) -> str:
    # The configured maximum is inclusive and belongs to the final calibration band.
    bounded = min(rating, MAX_RATING - 1)
    lower = (bounded // 200) * 200
    return f"{lower}-{lower + 199}"


def rating_balanced_weights(rows):
    """Give each represented rating band equal total sampling weight."""
    bands = [rating_band(int(row["rating"])) for row in rows]
    counts = Counter(bands)
    return [1.0 / counts[band] for band in bands]


def collate_positions(rows):
    batch_size = len(rows)
    max_moves = max(len(row["moves"]) for row in rows)
    boards = np.stack([row["board"] for row in rows])
    moves = np.zeros((batch_size, max_moves, 134), dtype=np.float32)
    mask = np.zeros((batch_size, max_moves), dtype=np.bool_)
    targets = np.empty(batch_size, dtype=np.int64)
    ratings = np.stack([row["rating"] for row in rows])
    for index, row in enumerate(rows):
        count = len(row["moves"])
        moves[index, :count] = row["moves"]
        mask[index, :count] = True
        targets[index] = row["target"]
    return {
        "board": torch.from_numpy(boards),
        "moves": torch.from_numpy(moves),
        "rating": torch.from_numpy(ratings),
        "mask": torch.from_numpy(mask),
        "target": torch.from_numpy(targets),
        "ply": [row["ply"] for row in rows],
        "rating_band": [row["rating_band"] for row in rows],
    }


def evaluate(model, loader, device):
    model.eval()
    total_nll = 0.0
    total_positions = 0
    total_top1 = 0
    total_top3 = 0
    total_uniform_nll = 0.0
    total_expected_match = 0.0
    by_rating = {}
    by_stage = {"opening": {}, "middlegame": {}, "endgame": {}}
    with torch.no_grad():
        for batch in loader:
            board = batch["board"].to(device, non_blocking=True)
            moves = batch["moves"].to(device, non_blocking=True)
            rating = batch["rating"].to(device, non_blocking=True)
            mask = batch["mask"].to(device, non_blocking=True)
            target = batch["target"].to(device, non_blocking=True)
            logits = model(board, moves, rating).masked_fill(~mask, -1e9)
            log_probs = F.log_softmax(logits, dim=-1)
            losses = -log_probs.gather(1, target[:, None]).squeeze(1)
            predictions = logits.topk(min(3, logits.shape[1]), dim=1).indices
            top1 = predictions[:, 0].eq(target)
            top3 = predictions.eq(target[:, None]).any(dim=1)
            valid_counts = mask.sum(dim=1).float()
            uniform = valid_counts.log()

            total_expected_match += (-losses).exp().sum().item()
            total_nll += losses.sum().item()
            total_positions += len(target)
            total_top1 += top1.sum().item()
            total_top3 += top3.sum().item()
            total_uniform_nll += uniform.sum().item()
            for i, band in enumerate(batch["rating_band"]):
                row = by_rating.setdefault(band, {"nll": 0.0, "top1": 0, "top3": 0, "expected_match": 0.0, "count": 0})
                row["expected_match"] += float((-losses[i]).exp().item())
                row["nll"] += losses[i].item()
                row["top1"] += int(top1[i].item())
                row["top3"] += int(top3[i].item())
                row["count"] += 1
                stage = "opening" if batch["ply"][i] < 20 else "middlegame" if batch["ply"][i] < 60 else "endgame"
                stage_row = by_stage[stage].setdefault(band, {"nll": 0.0, "count": 0})
                stage_row["nll"] += losses[i].item()
                stage_row["count"] += 1

    def finalize(rows):
        return {
            band: {
                "positions": row["count"],
                "nll": row["nll"] / row["count"],
                **({"top1": row["top1"] / row["count"], "top3": row["top3"] / row["count"], "expected_match": row["expected_match"] / row["count"]}
                   if "top1" in row else {}),
            }
            for band, row in sorted(rows.items())
        }

    return {
        "positions": total_positions,
        "nll": total_nll / total_positions,
        "expected_match": total_expected_match / total_positions,
        "top1": total_top1 / total_positions,
        "top3": total_top3 / total_positions,
        "uniform_legal_move_nll": total_uniform_nll / total_positions,
        "by_rating_band": finalize(by_rating),
        "by_stage_and_rating_band": {stage: finalize(rows) for stage, rows in by_stage.items()},
    }


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--config", type=Path, required=True)
    args = parser.parse_args()
    config = yaml.safe_load(args.config.read_text(encoding="utf-8"))
    if config.get("ablation", "none") not in ("none", "rating", "engine"):
        parser.error("ablation must be none, rating, or engine")
    device_name = str(config.get("device", "cuda"))
    if device_name == "cuda" and not torch.cuda.is_available():
        raise RuntimeError("CUDA was requested but torch.cuda.is_available() is false")
    if device_name not in {"cuda", "cpu"}:
        raise ValueError("device must be 'cuda' or 'cpu'")
    device = torch.device(device_name)
    torch.set_num_threads(int(config.get("torch_num_threads", torch.get_num_threads())))
    dataset_dir = Path(config["dataset_dir"])
    manifest = json.loads((dataset_dir / "manifest.json").read_text(encoding="utf-8"))
    if manifest.get("encoder_schema_version") != SCHEMA_VERSION or "engine_analysis" not in manifest:
        raise ValueError("dataset manifest is missing the v1 encoder schema or engine analysis metadata")

    seed = int(config.get("seed", 1729))
    random.seed(seed)
    np.random.seed(seed)
    torch.manual_seed(seed)
    if device.type == "cuda":
        torch.cuda.manual_seed_all(seed)
    if config.get("deterministic", False):
        torch.use_deterministic_algorithms(True)

    train_data = PositionDataset(dataset_dir / "train.jsonl", config.get("ablation", "none"))
    validation_data = PositionDataset(dataset_dir / "validation.jsonl", config.get("ablation", "none"))
    workers = int(config.get("num_workers", 4))
    loader_options = {
        "batch_size": int(config.get("batch_size", 128)),
        "num_workers": workers,
        "pin_memory": device.type == "cuda",
        "collate_fn": collate_positions,
    }
    sampler = None
    if config.get("rating_balanced_sampling", False):
        sampler = WeightedRandomSampler(rating_balanced_weights(train_data.rows),
                                        len(train_data), replacement=True)
    train_loader = DataLoader(train_data, shuffle=sampler is None, sampler=sampler, **loader_options)
    validation_loader = DataLoader(validation_data, shuffle=False, **loader_options)
    model = HumanMovePolicy()
    initial_checkpoint = config.get("initial_checkpoint")
    if initial_checkpoint:
        initial = torch.load(Path(initial_checkpoint), map_location="cpu", weights_only=False)
        if initial.get("schema_version") != SCHEMA_VERSION:
            raise ValueError("initial checkpoint schema version does not match")
        model.load_state_dict(initial["model_state_dict"], strict=True)
    model = model.to(device)
    optimizer = torch.optim.AdamW(
        model.parameters(),
        lr=float(config.get("learning_rate", 1e-3)),
        weight_decay=float(config.get("weight_decay", 1e-4)),
    )
    output_dir = Path(config["output_dir"])
    output_dir.mkdir(parents=True, exist_ok=True)
    best_nll = math.inf
    stale_epochs = 0
    history = []
    patience = int(config.get("early_stopping_patience", 3))

    resolved_device = torch.cuda.get_device_name(0) if device.type == "cuda" else "CPU"
    print(json.dumps({"config": config, "device": resolved_device,
                      "train_positions": len(train_data), "validation_positions": len(validation_data)}, sort_keys=True),
          file=sys.stderr)
    for epoch in range(1, int(config.get("epochs", 10)) + 1):
        model.train()
        for batch in train_loader:
            board = batch["board"].to(device, non_blocking=True)
            moves = batch["moves"].to(device, non_blocking=True)
            rating = batch["rating"].to(device, non_blocking=True)
            mask = batch["mask"].to(device, non_blocking=True)
            target = batch["target"].to(device, non_blocking=True)
            optimizer.zero_grad(set_to_none=True)
            logits = model(board, moves, rating).masked_fill(~mask, -1e9)
            loss = F.cross_entropy(logits, target)
            loss.backward()
            optimizer.step()

        metrics = evaluate(model, validation_loader, device)
        metrics["epoch"] = epoch
        history.append(metrics)
        print(json.dumps(metrics, sort_keys=True), file=sys.stderr)
        if metrics["nll"] < best_nll:
            best_nll = metrics["nll"]
            stale_epochs = 0
            torch.save({
                "model_state_dict": model.state_dict(),
                "schema_version": SCHEMA_VERSION,
                "config": config,
                "initial_checkpoint": str(initial_checkpoint) if initial_checkpoint else None,
                "epoch": epoch,
                "validation_metrics": metrics,
                "engine_analysis": manifest["engine_analysis"],
            }, output_dir / "best.pt")
        else:
            stale_epochs += 1
            if stale_epochs >= patience:
                break

    (output_dir / "training_report.json").write_text(json.dumps({
        "config": config,
        "dataset_manifest": manifest,
        "history": history,
        "best_validation_nll": best_nll,
    }, indent=2) + "\n", encoding="utf-8")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
