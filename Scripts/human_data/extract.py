#!/usr/bin/env python3
"""Mine ranked human move data for HumanPlay calibration (option #2).

For sampled positions from rating-bucketed human games, run Sapling's `rank` command to get the
engine's best-first root ranking WITH scores, and record the move the human actually played. The
stored score vector is what makes the data confidence-aware: the calibrator fits the gap-Boltzmann
temperature so that obvious moves (large gap) are reliably reproduced even at low rating.

SCHEMA (one JSON object per line in --out):
  rating       int    rating of the side that made the move
  bucket       int    nearest bucket centre (see --buckets)
  fen          str    position before the move
  ply          int    move number in the game (0-based)
  depth        int    engine rank depth used
  legal_count  int    number of legal root moves
  moves        [str]  root moves ranked best-first (UCI)
  scores       [int]  engine scores best-first (gaps = scores[0]-scores[i])
  human_move   str    the move the human played (UCI)
  human_rank   int    index of human_move in moves, or -1 if not present

Requires python-chess (pip install python-chess) and a Sapling build that supports `rank`.
Accepts plain .pgn or gzip .cpgn/.gz. Use --max-games to cap parsing on huge Lichess dumps.

Usage:
  python3 extract.py --pgn 2024-01.pgn.gz --engine ./Sapling --depth 8 \
      --buckets 1000,1500,1800,2000,2200 --per-bucket 3000 --workers 12 \
      --max-games 400000 --out ranked.jsonl
"""
import argparse
import gzip
import json
import os
import random
import subprocess
import sys
from multiprocessing import Pool

import chess
import chess.pgn

_G = {}


def parse_buckets(s):
    return [int(x) for x in s.split(",") if x.strip()]


def bucket_for(rating, buckets, tol):
    if rating is None:
        return None
    best = min(buckets, key=lambda b: abs(b - rating))
    return best if abs(best - rating) <= tol else None


def open_pgn(path):
    if path.endswith(".gz") or path.endswith(".cpgn"):
        return gzip.open(path, "rt", encoding="utf-8", errors="replace")
    return open(path, "r", encoding="utf-8", errors="replace")


def _init_worker(engine, depth):
    proc = subprocess.Popen([engine], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                            text=True, bufsize=1)
    _G["proc"] = proc
    _G["depth"] = depth


def _read_rank(proc):
    moves, scores = [], []
    while True:
        line = proc.stdout.readline()
        if not line:
            raise RuntimeError("engine closed")
        line = line.strip()
        if line.startswith("rankmove "):
            _, m, s = line.split()
            moves.append(m)
            scores.append(int(s))
        elif line == "rankdone":
            return moves, scores


def _rank_chunk(items):
    proc = _G["proc"]
    depth = _G["depth"]
    out = []
    for fen, uci, rating, bucket, ply in items:
        proc.stdin.write(f"position fen {fen}\nrank {depth}\n")
        proc.stdin.flush()
        moves, scores = _read_rank(proc)
        if not moves:
            continue
        out.append({
            "rating": rating, "bucket": bucket, "fen": fen, "ply": ply,
            "depth": depth, "legal_count": len(moves),
            "moves": moves, "scores": scores,
            "human_move": uci,
            "human_rank": moves.index(uci) if uci in moves else -1,
        })
    return out


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--pgn", required=True)
    ap.add_argument("--engine", required=True)
    ap.add_argument("--depth", type=int, default=8)
    ap.add_argument("--buckets", default="1000,1500,1800,2000,2200")
    ap.add_argument("--bucket-tol", type=int, default=150)
    ap.add_argument("--per-bucket", type=int, default=3000)
    ap.add_argument("--workers", type=int, default=1)
    ap.add_argument("--max-games", type=int, default=0)
    ap.add_argument("--seed", type=int, default=1234)
    ap.add_argument("--out", default="ranked.jsonl")
    args = ap.parse_args()

    buckets = parse_buckets(args.buckets)
    rng = random.Random(args.seed)

    samples = {b: [] for b in buckets}
    counts = {b: 0 for b in buckets}
    games = 0
    with open_pgn(args.pgn) as fh:
        while True:
            game = chess.pgn.read_game(fh)
            if game is None:
                break
            games += 1
            wr = game.headers.get("WhiteElo") or game.headers.get("WhiteRating")
            br = game.headers.get("BlackElo") or game.headers.get("BlackRating")
            board = game.board()
            ply = 0
            for node in game.mainline_moves():
                mover_white = board.turn
                rating = int(wr) if (wr and mover_white) else (int(br) if (br and not mover_white) else None)
                b = bucket_for(rating, buckets, args.bucket_tol)
                if b is not None:
                    counts[b] += 1
                    item = (board.fen(), node.uci(), rating, b, ply)
                    lst = samples[b]
                    if len(lst) < args.per_bucket:
                        lst.append(item)
                    else:
                        j = rng.randrange(0, counts[b])
                        if j < args.per_bucket:
                            lst[j] = item
                board.push(node)
                ply += 1
            if args.max_games and games >= args.max_games:
                break

    all_items = [it for b in buckets for it in samples[b]]
    print(f"parsed {games} games; sampled {len(all_items)} positions "
          f"{ {b: len(samples[b]) for b in buckets} }", file=sys.stderr)

    if os.path.exists(args.out):
        os.remove(args.out)

    # Chunk for parallel ranking (each worker owns one engine subprocess).
    nchunks = max(1, args.workers * 8)
    chunks = [all_items[i::nchunks] for i in range(nchunks)]
    written = 0
    with Pool(processes=args.workers, initializer=_init_worker,
              initargs=(args.engine, args.depth)) as pool:
        with open(args.out, "w") as out:
            for recs in pool.imap_unordered(_rank_chunk, chunks):
                for r in recs:
                    out.write(json.dumps(r) + "\n")
                    written += 1
                if written and written % 1000 == 0:
                    print(f"ranked {written}/{len(all_items)}", file=sys.stderr)

    print(f"wrote {written} records to {args.out}", file=sys.stderr)


if __name__ == "__main__":
    main()
