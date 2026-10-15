#!/usr/bin/env python3
"""Validate viriformat ("marlinformat") binpacks produced by Sapling datagen.

Replays every game with python-chess and checks that:
  - headers decode to a valid board (including unmoved-rook castling rights),
  - every recorded move is legal in the replayed position,
  - evals fit in i16 and WDL bytes are 0/1/2.

Usage: python3 validate_binpack.py FILE.binpack [FILE.binpack ...] [--sample N]
Requires: pip install chess
"""

import struct
import sys

import chess

HEADER_SIZE = 32
MOVE_SIZE = 4

# viriformat piece code (piece_type | colour << 3) -> python-chess piece symbol
PIECE_TYPES = "pnbrqk"

CASTLE_FLAG = 0x8000
EP_FLAG = 0x4000
PROMO_FLAG = 0xC000
PROMO_PIECES = [chess.KNIGHT, chess.BISHOP, chess.ROOK, chess.QUEEN]


def viri_to_chess_square(viri_sq: int) -> int:
    # viriformat orders squares a1=0..h8=63, same as python-chess
    return viri_sq


def decode_header(data: bytes):
    occupancy = struct.unpack_from("<Q", data, 0)[0]
    stm_ep = data[24]
    halfmove = data[25]
    fullmove = struct.unpack_from("<H", data, 26)[0]
    wdl = data[30]

    if wdl not in (0, 1, 2):
        raise ValueError(f"invalid wdl byte {wdl}")
    turn = chess.BLACK if (stm_ep >> 7) != 0 else chess.WHITE
    if (stm_ep & 0x7F) != 64:
        raise ValueError("datagen headers must not have an ep square")

    squares = {}  # chess square -> piece symbol
    unmoved = {chess.WHITE: [], chess.BLACK: []}
    king_sq = {chess.WHITE: None, chess.BLACK: None}

    slot = 0
    for viri_sq in range(64):
        if not (occupancy >> viri_sq) & 1:
            continue
        byte = data[8 + (slot >> 1)]
        code = (byte >> ((slot & 1) * 4)) & 0xF
        sq = viri_to_chess_square(viri_sq)
        colour = chess.BLACK if code & 8 else chess.WHITE
        ptype = code & 7
        if ptype == 6:
            unmoved[colour].append(sq)
            symbol = "R" if colour == chess.WHITE else "r"
        else:
            if ptype >= 6:
                raise ValueError(f"invalid piece code {code} at slot {slot}")
            symbol = PIECE_TYPES[ptype]
            if ptype == 5:
                king_sq[colour] = sq
            if colour == chess.WHITE:
                symbol = symbol.upper()
        squares[sq] = symbol
        slot += 1

    # Derive Shredder-style castling letters from the unmoved rooks.
    letters = ""
    for colour in (chess.WHITE, chess.BLACK):
        rooks = sorted(unmoved[colour])
        for rook_sq in rooks:
            letter = "abcdefgh"[rook_sq % 8]
            letters += letter.upper() if colour == chess.WHITE else letter

    board = chess.Board(None, chess960=True)
    for sq, symbol in squares.items():
        board.set_piece_at(sq, chess.Piece.from_symbol(symbol))
    board.turn = turn
    board.halfmove_clock = halfmove
    board.fullmove_number = fullmove
    board.ep_square = None
    if letters:
        board.set_castling_fen(letters)
    else:
        board.castling_rights = 0
    return board, wdl


def decode_move(raw: int):
    from_sq = raw & 0x3F
    to_sq = (raw >> 6) & 0x3F
    flags = raw & 0xC000
    promo = (raw >> 12) & 0x3

    from_cs = viri_to_chess_square(from_sq)
    to_cs = viri_to_chess_square(to_sq)

    if flags == CASTLE_FLAG:
        # viriformat and python-chess (chess960 mode) both encode castling as
        # king-from -> rook-square.
        return chess.Move(from_cs, to_cs)
    if flags == EP_FLAG:
        return chess.Move(from_cs, to_cs)
    if flags == PROMO_FLAG:
        return chess.Move(from_cs, to_cs, promotion=PROMO_PIECES[promo])
    return chess.Move(from_cs, to_cs)


def parse_game(data: bytes, offset: int):
    board, wdl = decode_header(data[offset:offset + HEADER_SIZE])
    start_fen = board.fen()
    offset += HEADER_SIZE
    moves = []
    while True:
        raw = struct.unpack_from("<H", data, offset)[0]
        eval_cp = struct.unpack_from("<h", data, offset + 2)[0]
        offset += MOVE_SIZE
        if raw == 0:
            break
        move = decode_move(raw)
        if not board.is_legal(move):
            print(f"  start: {start_fen}")
            print(f"  diverged at ply {len(moves)}: {move.uci()} in {board.fen()}")
            for m, e in moves[-12:]:
                print(f"    prev: {m.uci()} eval={e}")
            raise ValueError(
                f"illegal move {board.fen()} {move.uci()} "
                f"(played as {board.piece_at(move.from_square)})"
            )
        board.push(move)
        moves.append((move, eval_cp))
    return board, wdl, moves, offset


class IncompleteGame(Exception):
    pass


def scan_game_bounds(data: bytes, offset: int) -> int:
    """Structural scan only: return the offset just past the null terminator."""
    if len(data) - offset < HEADER_SIZE:
        raise IncompleteGame("truncated header")
    offset += HEADER_SIZE
    while True:
        if len(data) - offset < MOVE_SIZE:
            raise IncompleteGame("truncated move record / missing terminator")
        raw = struct.unpack_from("<H", data, offset)[0]
        offset += MOVE_SIZE
        if raw == 0:
            return offset


def repair(path: str) -> bool:
    with open(path, "rb") as f:
        data = f.read()
    offset, games = 0, 0
    while offset < len(data):
        try:
            offset = scan_game_bounds(data, offset)
            games += 1
        except IncompleteGame as e:
            print(f"{path}: truncated at byte {offset} ({len(data) - offset} trailing bytes): {e}")
            with open(path, "r+b") as f:
                f.truncate(offset)
            print(f"{path}: repaired, kept {games} complete games")
            return True
    print(f"{path}: clean, {games} games")
    return False


def main() -> int:
    args = [a for a in sys.argv[1:] if not a.startswith("--")]
    sample = 0
    repair_mode = any(a == "--repair" for a in sys.argv[1:])
    for a in sys.argv[1:]:
        if a.startswith("--sample"):
            sample = int(a.split("=", 1)[1]) if "=" in a else 1

    if not args:
        print(__doc__)
        return 1

    if repair_mode:
        for path in args:
            repair(path)
        return 0

    total_games = 0
    total_positions = 0
    wdl_counts = {0: 0, 1: 0, 2: 0}

    for path in args:
        with open(path, "rb") as f:
            data = f.read()
        offset = 0
        games = 0
        while offset < len(data):
            board, wdl, moves, offset = parse_game(data, offset)
            games += 1
            total_positions += len(moves)
            wdl_counts[wdl] += 1
            if games <= sample:
                print(f"{path} game {games}: wdl={wdl} plies={len(moves)}")
                print(f"  start: {board.starting_fen if hasattr(board, 'starting_fen') else ''}")
                print(f"  first: {[m.uci() for m, _ in moves[:12]]}")
                print(f"  evals: {[e for _, e in moves[:12]]}")
        total_games += games
        print(f"{path}: {games} games, {offset} bytes, {len(data) - offset} trailing bytes")
        if len(data) - offset:
            print("  ERROR: trailing garbage after last null terminator")
            return 1

    print(
        f"OK: {total_games} games, {total_positions} positions, "
        f"white-win={wdl_counts[2]} draw={wdl_counts[1]} black-win={wdl_counts[0]}"
    )
    return 0


if __name__ == "__main__":
    sys.exit(main())
