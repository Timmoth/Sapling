#!/usr/bin/env python3
"""Compare two human-policy checkpoints on game-disjoint scored positions."""

from __future__ import annotations

import argparse
import hashlib
import json
from collections import defaultdict
from functools import partial
from pathlib import Path

import chess
import numpy as np
import torch
from torch.utils.data import DataLoader, Dataset

from runtime_calibration import settings_for_rating
from model import HumanMovePolicy
from train import PositionDataset, collate_positions


METRICS = (
    "nll",
    "brier",
    "expected_match",
    "top1",
    "top3",
    "expected_regret_cp",
    "probability_on_300cp_regrets",
    "probability_on_direct_capture_risk",
    "observed_human_regret_cp",
    "observed_human_300cp_regret_rate",
    "observed_human_direct_capture_risk_rate",
)


class RowsWithMetadata(Dataset):
    def __init__(self, dataset: PositionDataset):
        self.dataset = dataset

    def __len__(self):
        return len(self.dataset)

    def __getitem__(self, index):
        return self.dataset[index], self.dataset.rows[index]


def direct_capture_risk_after_move(board: chess.Board, move: chess.Move) -> bool:
    """Mirror the runtime proxy: an immediate capture of a minor+ piece with no recapture."""
    after = board.copy(stack=False)
    after.push(move)
    for capture in list(after.legal_moves):
        if not after.is_capture(capture) or after.is_en_passant(capture):
            continue
        victim = after.piece_at(capture.to_square)
        if victim is None or victim.piece_type < chess.KNIGHT:
            continue
        capture_square = capture.to_square
        after_capture = after.copy(stack=False)
        after_capture.push(capture)
        piece_on_capture_square = after_capture.piece_at(capture_square)
        recaptured = any(
            after_capture.is_capture(reply) and reply.to_square == capture_square
            and after_capture.piece_at(reply.to_square) == piece_on_capture_square
            for reply in after_capture.legal_moves
        )
        if not recaptured:
            return True
    return False


def collate_with_metadata(items, compute_direct_risk: bool):
    encoded = collate_positions([item for item, _ in items])
    rows = [row for _, row in items]
    regrets = np.zeros_like(encoded["mask"].numpy(), dtype=np.float32)
    direct_risk = np.zeros_like(regrets, dtype=np.float32)
    for index, row in enumerate(rows):
        board = chess.Board(row["fen"])
        legal = list(board.legal_moves)
        scores = {move: int(score) for move, score in row["engine_scores"].items()}
        best = max(scores.values())
        regrets[index, :len(legal)] = [best - scores[move.uci()] for move in legal]
        if compute_direct_risk:
            direct_risk[index, :len(legal)] = [
                float(direct_capture_risk_after_move(board, move)) for move in legal
            ]
    encoded["regrets"] = torch.from_numpy(regrets)
    encoded["direct_risk"] = torch.from_numpy(direct_risk)
    encoded["game_ids"] = [str(row["game_id"]) for row in rows]
    encoded["rating_bands"] = [
        f"{(min(int(row['rating']), 2199) // 200) * 200}-"
        f"{(min(int(row['rating']), 2199) // 200) * 200 + 199}"
        for row in rows
    ]
    return encoded


def sha256_file(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as stream:
        for block in iter(lambda: stream.read(8 * 1024 * 1024), b""):
            digest.update(block)
    return digest.hexdigest()


def load_model(path: Path, device: torch.device) -> HumanMovePolicy:
    checkpoint = torch.load(path, map_location=device, weights_only=False)
    if checkpoint.get("schema_version") != 1:
        raise ValueError(f"unsupported checkpoint schema: {path}")
    model = HumanMovePolicy().to(device)
    model.load_state_dict(checkpoint["model_state_dict"], strict=True)
    model.eval()
    return model


def load_calibration(path: Path | None) -> dict[str, dict] | None:
    if path is None:
        return None
    doc = json.loads(path.read_text(encoding="utf-8"))
    anchors = doc.get("calibration_anchors") or doc.get("constrained_expected_match_anchors")
    if not anchors:
        raise ValueError(f"calibration map has no supported anchors: {path}")
    return {row["human_rating_band"]: row for row in anchors}


def load_risk_penalties(path: Path | None) -> dict[str, float]:
    if path is None:
        return {}
    doc = json.loads(path.read_text(encoding="utf-8"))
    return {row["human_rating_band"]: float(row["risk_penalty"])
            for row in doc.get("penalties", [])}


def summarize_groups(groups: dict[str, dict[str, list[float]]], replicates: int,
                    rng: np.random.Generator) -> dict:
    result = {}
    for scope, games in groups.items():
        game_ids = sorted(games)
        counts = np.asarray([games[game][0] for game in game_ids], dtype=np.float64)
        cells = np.asarray([games[game][1:] for game in game_ids], dtype=np.float64)
        positions = int(counts.sum())
        cells = cells.reshape(len(game_ids), 2, len(METRICS)).transpose(0, 2, 1)
        metric_result = {}
        for metric_index, metric in enumerate(METRICS):
            sums = cells[:, metric_index, :]
            means = sums.sum(axis=0) / positions
            deltas = []
            for start in range(0, replicates, 100):
                batch_count = min(100, replicates - start)
                sample = rng.integers(0, len(game_ids), size=(batch_count, len(game_ids)))
                sampled_sums = sums[sample].sum(axis=1)
                sampled_counts = counts[sample].sum(axis=1)
                deltas.extend((sampled_sums[:, 1] - sampled_sums[:, 0]) / sampled_counts)
            ci = np.quantile(np.asarray(deltas), [0.025, 0.975]).tolist()
            metric_result[metric] = {
                "baseline": float(means[0]),
                "candidate": float(means[1]),
                "delta": float(means[1] - means[0]),
                "delta_ci95": [float(ci[0]), float(ci[1])],
            }
        result[scope] = {"positions": positions, "games": len(game_ids), "metrics": metric_result}
    return result


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--baseline", required=True, type=Path)
    parser.add_argument("--candidate", required=True, type=Path)
    parser.add_argument("--dataset-dir", required=True, type=Path)
    parser.add_argument("--baseline-calibration", type=Path)
    parser.add_argument("--candidate-calibration", type=Path)
    parser.add_argument("--baseline-risk-map", type=Path)
    parser.add_argument("--candidate-risk-map", type=Path)
    parser.add_argument("--split", choices=("validation", "test"), default="test")
    parser.add_argument("--batch-size", type=int, default=128)
    parser.add_argument("--workers", type=int, default=8)
    parser.add_argument("--threads", type=int, default=16)
    parser.add_argument("--replicates", type=int, default=2000)
    parser.add_argument("--seed", type=int, default=20261028)
    parser.add_argument("--output", required=True, type=Path)
    args = parser.parse_args()

    torch.set_num_threads(args.threads)
    device = torch.device("cpu")
    baseline = load_model(args.baseline, device)
    candidate = load_model(args.candidate, device)
    baseline_calibration = load_calibration(args.baseline_calibration)
    candidate_calibration = load_calibration(args.candidate_calibration)
    baseline_risk_map = load_risk_penalties(args.baseline_risk_map)
    candidate_risk_map = load_risk_penalties(args.candidate_risk_map)
    needs_direct_risk = True  # Observed human risk is reported even without policy penalties.
    source = args.dataset_dir / f"{args.split}.jsonl"
    data = PositionDataset(source, "none")
    loader = DataLoader(RowsWithMetadata(data), batch_size=args.batch_size, shuffle=False,
                        num_workers=args.workers,
                        collate_fn=partial(collate_with_metadata,
                                           compute_direct_risk=needs_direct_risk))

    # scope -> game -> [position count, baseline metric sums..., candidate sums...]
    groups: dict[str, dict[str, list[float]]] = defaultdict(dict)
    with torch.inference_mode():
        for batch in loader:
            board = batch["board"].to(device)
            moves = batch["moves"].to(device)
            rating = batch["rating"].to(device)
            mask = batch["mask"].to(device)
            targets = batch["target"].to(device)
            regrets = batch["regrets"].to(device)
            direct_risk = batch["direct_risk"].to(device)
            bands = batch["rating_bands"]
            settings = []
            for calibration, risk_map in (
                (baseline_calibration, baseline_risk_map),
                (candidate_calibration, candidate_risk_map),
            ):
                exact_ratings = torch.round(rating[:, 0] * 700 + 1500).tolist()
                runtime_settings = [settings_for_rating(value, calibration, risk_map)
                                    for value in exact_ratings]
                rating_tensor = torch.tensor(
                    [[(value[0] - 1500) / 700.0] for value in runtime_settings], dtype=torch.float32)
                temperatures = torch.tensor([value[1] for value in runtime_settings], dtype=torch.float32)
                penalties = torch.tensor([value[2] for value in runtime_settings], dtype=torch.float32)
                settings.append((rating_tensor, temperatures, penalties))
            logits_by_model = []
            for model, (model_rating, temperatures, penalties) in zip(
                    (baseline, candidate), settings):
                logits = model(board, moves, model_rating).masked_fill(~mask, -1e9)
                logits = logits - direct_risk * penalties[:, None]
                logits_by_model.append(logits / temperatures[:, None])
            row_metrics = []
            for logits in logits_by_model:
                log_probabilities = torch.log_softmax(logits, dim=-1)
                probabilities = log_probabilities.exp()
                target_log_probability = log_probabilities.gather(1, targets[:, None]).squeeze(1)
                target_probability = probabilities.gather(1, targets[:, None]).squeeze(1)
                top_indices = logits.topk(min(3, logits.shape[1]), dim=1).indices
                top1 = top_indices[:, 0].eq(targets).float()
                top3 = top_indices.eq(targets[:, None]).any(dim=1).float()
                regret_mass = (probabilities * (regrets >= 300).float()).sum(dim=1)
                direct_risk_mass = (probabilities * direct_risk).sum(dim=1)
                expected_regret = (probabilities * regrets).sum(dim=1)
                # The recorded move supplies an empirical error-rate reference. Reducing
                # every mistake can otherwise make a low-rating policy unrealistically strong.
                observed_regret = regrets.gather(1, targets[:, None]).squeeze(1)
                observed_risk = direct_risk.gather(1, targets[:, None]).squeeze(1)
                row_metrics.append(torch.stack((
                    -target_log_probability,
                    probabilities.square().sum(dim=1) - 2 * target_probability + 1, target_probability,
                    top1, top3, expected_regret, regret_mass, direct_risk_mass,
                    observed_regret, (observed_regret >= 300).float(), observed_risk,
                ), dim=1).cpu().numpy())

            for index, game_id in enumerate(batch["game_ids"]):
                values = np.concatenate((row_metrics[0][index], row_metrics[1][index])).tolist()
                band = batch["rating_bands"][index]
                for scope in ("overall", band):
                    cell = groups[scope].setdefault(game_id, [0.0] * (1 + 2 * len(METRICS)))
                    cell[0] += 1
                    for metric_index, value in enumerate(values):
                        cell[1 + metric_index] += value

    rng = np.random.default_rng(args.seed)
    report = {
        "baseline_checkpoint": str(args.baseline),
        "baseline_sha256": sha256_file(args.baseline),
        "candidate_checkpoint": str(args.candidate),
        "candidate_sha256": sha256_file(args.candidate),
        "baseline_calibration": str(args.baseline_calibration) if args.baseline_calibration else None,
        "baseline_calibration_sha256": sha256_file(args.baseline_calibration) if args.baseline_calibration else None,
        "candidate_calibration": str(args.candidate_calibration) if args.candidate_calibration else None,
        "candidate_calibration_sha256": sha256_file(args.candidate_calibration) if args.candidate_calibration else None,
        "baseline_risk_map": str(args.baseline_risk_map) if args.baseline_risk_map else None,
        "baseline_risk_map_sha256": sha256_file(args.baseline_risk_map) if args.baseline_risk_map else None,
        "candidate_risk_map": str(args.candidate_risk_map) if args.candidate_risk_map else None,
        "candidate_risk_map_sha256": sha256_file(args.candidate_risk_map) if args.candidate_risk_map else None,
        "input": str(source),
        "input_sha256": sha256_file(source),
        "split": args.split,
        "calibration_application": "exact position rating with runtime anchor interpolation",
        "bootstrap_unit": "game, preserving all sampled positions from a game",
        "bootstrap_seed": args.seed,
        "bootstrap_replicates": args.replicates,
        "results": summarize_groups(groups, args.replicates, rng),
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(report, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(report, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
