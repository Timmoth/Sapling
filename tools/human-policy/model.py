"""Small shared-board / per-candidate rating-conditioned move scorer."""

from __future__ import annotations

import torch
from torch import nn


BOARD_CHANNELS = 18
MOVE_FEATURES = 134


class HumanMovePolicy(nn.Module):
    """Return one raw logit for each legal move candidate."""

    def __init__(self) -> None:
        super().__init__()
        self.board_encoder = nn.Sequential(
            nn.Conv2d(BOARD_CHANNELS, 32, kernel_size=3, padding=1),
            nn.ReLU(),
            nn.Conv2d(32, 32, kernel_size=3, padding=1),
            nn.ReLU(),
            nn.Flatten(),
            nn.Linear(32 * 8 * 8, 128),
            nn.ReLU(),
        )
        self.move_scorer = nn.Sequential(
            nn.Linear(128 + MOVE_FEATURES + 1, 64),
            nn.ReLU(),
            nn.Linear(64, 1),
        )

    def forward(self, board: torch.Tensor, moves: torch.Tensor, rating: torch.Tensor) -> torch.Tensor:
        """Inputs are board[B,18,8,8], moves[B,N,134], rating[B,1]."""
        if board.ndim != 4 or board.shape[1:] != (BOARD_CHANNELS, 8, 8):
            raise ValueError(f"board must have shape [B,18,8,8], got {tuple(board.shape)}")
        if moves.ndim != 3 or moves.shape[2] != MOVE_FEATURES:
            raise ValueError(f"moves must have shape [B,N,134], got {tuple(moves.shape)}")
        if rating.ndim != 2 or rating.shape[1] != 1:
            raise ValueError(f"rating must have shape [B,1], got {tuple(rating.shape)}")
        if board.shape[0] != moves.shape[0] or board.shape[0] != rating.shape[0]:
            raise ValueError("board, moves, and rating batch dimensions must match")

        batch, candidate_count, _ = moves.shape
        board_features = self.board_encoder(board).unsqueeze(1).expand(-1, candidate_count, -1)
        rating_features = rating.unsqueeze(1).expand(-1, candidate_count, -1)
        joined = torch.cat((board_features, moves, rating_features), dim=-1)
        return self.move_scorer(joined).squeeze(-1)
