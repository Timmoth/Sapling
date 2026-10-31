"""Schema-v1 board, move, and rating encoders for the Sapling human policy."""

from __future__ import annotations

import math

import chess

SCHEMA_VERSION = 1
BOARD_CHANNELS = 18
MOVE_FEATURES = 134
MIN_RATING = 500
MAX_RATING = 2200
_PIECE_ORDER = (chess.PAWN, chess.KNIGHT, chess.BISHOP,
                chess.ROOK, chess.QUEEN, chess.KING)
_PROMOTION_ORDER = (chess.QUEEN, chess.ROOK, chess.BISHOP, chess.KNIGHT)


def encode_board(board: chess.Board) -> list[float]:
    """Flatten [18,8,8] with square index a1=0, b1=1, ..., h8=63."""
    if board.chess960:
        raise ValueError("Schema v1 supports standard chess only; Chess960 is excluded.")

    encoded = [0.0] * (BOARD_CHANNELS * 64)
    for color_offset, color in ((0, chess.WHITE), (6, chess.BLACK)):
        for piece_offset, piece_type in enumerate(_PIECE_ORDER):
            channel = color_offset + piece_offset
            for square in board.pieces(piece_type, color):
                encoded[channel * 64 + square] = 1.0

    if board.turn == chess.WHITE:
        encoded[12 * 64:13 * 64] = [1.0] * 64
    # Encode the FEN castling-right bits as written. Do not infer rights from
    # whether the expected king/rook pieces currently occupy their home squares.
    rights = (
        bool(board.castling_rights & chess.BB_H1),
        bool(board.castling_rights & chess.BB_A1),
        bool(board.castling_rights & chess.BB_H8),
        bool(board.castling_rights & chess.BB_A8),
    )
    for offset, enabled in enumerate(rights, start=13):
        if enabled:
            encoded[offset * 64:(offset + 1) * 64] = [1.0] * 64

    # board.ep_square preserves the target recorded in FEN, including when no
    # pawn can legally capture there. This matches Sapling's EnPassantFile.
    if board.ep_square is not None:
        encoded[17 * 64 + board.ep_square] = 1.0
    return encoded


def encode_move(move: chess.Move, root_score: int, best_root_score: int) -> list[float]:
    """Encode a move plus bounded mover-perspective Sapling score and score gap."""
    encoded = [0.0] * MOVE_FEATURES
    encoded[move.from_square] = 1.0
    encoded[64 + move.to_square] = 1.0
    if move.promotion is not None:
        encoded[128 + _PROMOTION_ORDER.index(move.promotion)] = 1.0
    encoded[132] = math.tanh(root_score / 1000.0)
    encoded[133] = math.tanh(max(0, best_root_score - root_score) / 1000.0)
    return encoded


def encode_rating(rating: int) -> float:
    if not MIN_RATING <= rating <= MAX_RATING:
        raise ValueError(f"rating must be in [{MIN_RATING}, {MAX_RATING}], got {rating}")
    return (rating - 1500) / 700.0


def encode_position(
    board: chess.Board,
    legal_moves: list[chess.Move],
    played_move: chess.Move,
    rating: int,
    engine_scores: dict[str, int],
) -> tuple[list[float], list[list[float]], float, int]:
    """Encode a training row and fail if its label is not among legal candidates."""
    if not legal_moves:
        raise ValueError("cannot encode a position with no legal moves")
    try:
        target = legal_moves.index(played_move)
    except ValueError as exc:
        raise ValueError(f"played move {played_move.uci()} is absent from legal candidates") from exc
    legal_uci = {move.uci() for move in legal_moves}
    if set(engine_scores) != legal_uci:
        missing = sorted(legal_uci - set(engine_scores))
        unexpected = sorted(set(engine_scores) - legal_uci)
        raise ValueError(f"engine score set must exactly match legal moves; missing={missing}, unexpected={unexpected}")
    best_score = max(engine_scores.values())
    return (
        encode_board(board),
        [encode_move(move, engine_scores[move.uci()], best_score) for move in legal_moves],
        encode_rating(rating),
        target,
    )
