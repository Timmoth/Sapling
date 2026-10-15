#!/usr/bin/env python3
"""Paired-game strength match runner for Sapling qualification (UCI).

Predeclared protocol companion to the acceptance policy: deterministic seeded
openings, each opening played twice with colors swapped, fixed clock TC, fresh
engine processes per game (restart=on equivalent). Reports dev-vs-base score,
a diagnostic paired Elo estimate (not release qualification), and counts crashes/time forfeits
separately (never hidden).

Usage:
  python3 strength_match.py --dev DEV_EXE --base BASE_EXE --out DIR
      [--openings 200] [--seed 20261011] [--base-ms 5000] [--inc-ms 50]
      [--max-plies 160] [--concurrency 6] [--ply-oversample 8]
"""

import argparse
import math
import random
import queue
import statistics
import subprocess
import sys
import threading
import time
from concurrent.futures import ThreadPoolExecutor
from pathlib import Path

try:
    import chess
except ImportError:
    chess = None


def opening_fens(count, seed, ply):
    rng = random.Random(seed)
    fens = []
    while len(fens) < count:
        board = chess.Board()
        for _ in range(ply):
            moves = list(board.legal_moves)
            board.push(rng.choice(moves))
            if board.is_game_over():
                break
        if not board.is_game_over():
            fens.append(board.fen())
    return fens


class Engine:
    def __init__(self, exe, name, startup_timeout=10, cwd=None):
        command = [exe] if isinstance(exe, str) else exe
        self.p = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                  text=True, bufsize=1, cwd=cwd)
        self.name = name
        self.lines = queue.Queue()
        def read_lines():
            try:
                for line in self.p.stdout:
                    self.lines.put(line.strip())
            finally:
                self.lines.put(None)
        self.reader = threading.Thread(target=read_lines, daemon=True)
        self.reader.start()
        try:
            self._send("uci")
            self._expect("uciok", startup_timeout)
            self._send("isready")
            self._expect("readyok", startup_timeout)
        except BaseException:
            self.quit()
            raise

    def _send(self, msg):
        self.p.stdin.write(msg + "\n")
        self.p.stdin.flush()

    def _expect(self, token, timeout=10):
        deadline = time.monotonic() + timeout
        while True:
            remaining = deadline - time.monotonic()
            if remaining <= 0:
                raise TimeoutError(f"{self.name} waiting for {token}")
            try:
                line = self.lines.get(timeout=remaining)
            except queue.Empty:
                raise TimeoutError(f"{self.name} waiting for {token}") from None
            if line is None:
                raise RuntimeError(f"{self.name} process exited")
            if line == token or line.startswith(token + " "):
                return line

    def new_game(self):
        self._send("ucinewgame")
        self._send("isready")
        self._expect("readyok")

    def move(self, position, wtime, btime, winc, binc, timeout):
        self._send(position)
        self._send(f"go wtime {wtime} btime {btime} winc {winc} binc {binc}")
        return self._expect("bestmove", timeout).split()[1]

    def quit(self):
        try:
            if self.p.poll() is None:
                self._send("quit")
                self.p.wait(timeout=0.2)
        except (OSError, subprocess.TimeoutExpired):
            self.p.kill()
            self.p.wait(timeout=5)
        finally:
            self.reader.join(timeout=5)
            self.p.stdin.close()
            self.p.stdout.close()


def play_game(dev_exe, base_exe, fen, dev_white, args):
    dev, base = None, None
    plies = 0
    board = None
    try:
        # Assign separately so a failed second constructor cannot leak the first engine.
        dev = Engine(dev_exe, "dev")
        base = Engine(base_exe, "base")
        for e in (dev, base):
            e.new_game()
        white, black = (dev, base) if dev_white else (base, dev)
        board = chess.Board(fen)
        wtime, btime = args.base_ms, args.base_ms
        moves = []
        while plies < args.max_plies and not board.is_game_over(claim_draw=True):
            t0 = time.monotonic()
            clock = wtime if board.turn else btime
            # Preserve the complete game history for engine repetition detection.
            position = f"position fen {fen}"
            if moves:
                position += " moves " + " ".join(moves)
            mv = (white if board.turn else black).move(
                position, wtime, btime, args.inc_ms, args.inc_ms,
                timeout=max(0.001, clock / 1000.0))
            used = (time.monotonic() - t0) * 1000
            if used > clock:
                return ("0-1" if board.turn else "1-0", "forfeit", plies + 1)
            try:
                move = chess.Move.from_uci(mv)
            except ValueError:
                return ("0-1" if board.turn else "1-0", "illegal", plies + 1)
            if move not in board.legal_moves:
                return ("0-1" if board.turn else "1-0", "illegal", plies + 1)
            if board.turn:
                wtime = int(clock - used + args.inc_ms)
            else:
                btime = int(clock - used + args.inc_ms)
            board.push(move)
            moves.append(mv)
            plies += 1
        if board.is_game_over(claim_draw=True):
            return (board.result(claim_draw=True), "normal", plies)
        return ("*", "maxplies-unadjudicated", plies)
    except TimeoutError:
        return (("0-1" if board.turn else "1-0") if board is not None else "*",
                "forfeit" if board is not None else "startup-timeout", plies)
    except Exception as ex:
        return (("0-1" if board.turn else "1-0") if board is not None else "*",
                f"crash:{type(ex).__name__}", plies)
    finally:
        for e in (dev, base):
            if e:
                e.quit()


def paired_summary(rows):
    """Diagnostic paired estimate. Incomplete games never become artificial draws."""
    pairs = {}
    abnormal = sum(term != "normal" for _, _, _, term, _ in rows)
    for opening, color, result, _, _ in rows:
        if result not in ("1-0", "0-1", "1/2-1/2"):
            continue
        score = 0.5 if result == "1/2-1/2" else float((result == "1-0") == (color == "white"))
        pairs.setdefault(opening, {})[color] = score
    scores = [(p["white"] + p["black"]) / 2 for p in pairs.values() if set(p) == {"white", "black"}]
    if not scores:
        return ["complete_pairs=0 estimate=UNAVAILABLE", f"abnormal_or_unfinished={abnormal}"], False
    score = statistics.fmean(scores)
    def elo(p):
        return -math.inf if p <= 0 else math.inf if p >= 1 else 400 * math.log10(p / (1-p))
    lines = [f"complete_pairs={len(scores)} dev_score={score:.4f} elo={elo(score):+.1f}",
             f"abnormal_or_unfinished={abnormal}"]
    # Treat each color-swapped opening as one observation. Transform score bounds
    # to Elo; an individual-game binomial formula understates paired uncertainty.
    if len(scores) >= 2 and statistics.variance(scores) > 0:
        margin = 1.96 * statistics.stdev(scores) / math.sqrt(len(scores))
        lines.append(f"diagnostic_normal_ci95=[{elo(score-margin):+.1f},{elo(score+margin):+.1f}]")
    else:
        lines.append("ci95=UNAVAILABLE (insufficient independent variation)")
    lines.append("qualification=NOT_QUALIFIED (diagnostic fixed-sample estimate; not SPRT)")
    complete = len(scores) * 2 == len(rows) and abnormal == 0
    return lines, complete


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--dev", required=True)
    ap.add_argument("--base", required=True)
    ap.add_argument("--out", required=True)
    ap.add_argument("--openings", type=int, default=200)
    ap.add_argument("--seed", type=int, default=20261011)
    ap.add_argument("--base-ms", type=int, default=5000)
    ap.add_argument("--inc-ms", type=int, default=50)
    ap.add_argument("--max-plies", type=int, default=160)
    ap.add_argument("--concurrency", type=int, default=6)
    ap.add_argument("--ply-oversample", type=int, default=8)
    args = ap.parse_args()
    if chess is None:
        ap.error("python-chess is required; install it in an isolated Python environment")
    if min(args.openings, args.base_ms, args.max_plies, args.concurrency) <= 0 or min(args.inc_ms, args.ply_oversample) < 0:
        ap.error("counts/time must be positive; increment/opening plies must be nonnegative")

    out = Path(args.out)
    out.mkdir(parents=True, exist_ok=True)

    fens = opening_fens(args.openings, args.seed, args.ply_oversample)
    (out / "openings.txt").write_text("\n".join(fens) + "\n")

    jobs = []
    for i, fen in enumerate(fens):
        jobs.append((i, fen, True))
        jobs.append((i, fen, False))

    rows = []
    lock = threading.Lock()
    done = [0]

    def run(job):
        i, fen, dev_white = job
        result, term, plies = play_game(args.dev, args.base, fen, dev_white, args)
        with lock:
            rows.append((i, "white" if dev_white else "black", result, term, plies))
            done[0] += 1
            if done[0] % 20 == 0:
                print(f"progress {done[0]}/{len(jobs)}", flush=True)

    t0 = time.monotonic()
    with ThreadPoolExecutor(max_workers=args.concurrency) as ex:
        list(ex.map(run, jobs))
    elapsed = time.monotonic() - t0

    lines = ["opening,dev_color,result,termination,plies"]
    lines += [",".join(map(str, r)) for r in sorted(rows)]
    (out / "results.csv").write_text("\n".join(lines) + "\n")

    estimates, complete = paired_summary(rows)
    summary = [
        f"games={len(rows)} openings={args.openings} seed={args.seed} tc={args.base_ms}+{args.inc_ms}",
        *estimates,
        f"elapsed_s={elapsed:.0f}",
    ]
    (out / "summary.txt").write_text("\n".join(summary) + "\n")
    print("\n".join(summary))
    return 0 if complete else 1


if __name__ == "__main__":
    sys.exit(main())
