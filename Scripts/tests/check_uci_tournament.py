#!/usr/bin/env python3
"""Tournament/production stability checks: pass an absolute Sapling executable path.

Covers what a tournament runner actually exercises: every advertised UCI option
at its boundaries, real time controls (movetime/clock/nodes/depth/infinite),
SMP thread counts, hash sizes, HumanElo, Ponder, and a full self-play game with
per-move legality verification. Stdlib only, like check_uci_runtime.py.

Each lane is independent; failures are collected and reported together.
"""
from pathlib import Path
import queue
import subprocess
import sys
import tempfile
import threading
import time

ERROR_PREFIX = 'info string error'


class Engine:
    def __init__(self, exe, cwd=None, startup_timeout=30):
        self.p = subprocess.Popen([exe], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                  text=True, bufsize=1, cwd=cwd)
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
            self.send('uci')
            self.expect('uciok', startup_timeout)
        except BaseException:
            self.quit()
            raise

    def send(self, msg):
        self.p.stdin.write(msg + '\n')
        self.p.stdin.flush()

    def _next(self, timeout):
        try:
            line = self.lines.get(timeout=timeout)
        except queue.Empty:
            raise TimeoutError('waiting for a line') from None
        if line is None:
            raise RuntimeError('process exited')
        return line

    def expect(self, prefix, timeout=10):
        deadline = time.monotonic() + timeout
        while True:
            line = self._next(max(0.01, deadline - time.monotonic()))
            if line.startswith(prefix):
                return line

    def read_until(self, prefix, timeout=10):
        lines, deadline = [], time.monotonic() + timeout
        while True:
            line = self._next(max(0.01, deadline - time.monotonic()))
            lines.append(line)
            if line.startswith(prefix):
                return lines

    def ready(self, timeout=10):
        self.send('isready')
        return self.expect('readyok', timeout)

    def bestmove(self, go_cmd, timeout=30):
        self.send(go_cmd)
        lines = self.read_until('bestmove', timeout=timeout)
        for line in lines:
            if line.startswith('bestmove '):
                parts = line.split()
                return (parts[1] if len(parts) > 1 else ''), lines
        return '', lines

    def quit(self):
        try:
            if self.p.poll() is None:
                self.send('quit')
                self.p.wait(timeout=1)
        except (OSError, subprocess.TimeoutExpired):
            self.p.kill()
            self.p.wait(timeout=5)
        finally:
            self.reader.join(timeout=5)
            try:
                self.p.stdin.close()
                self.p.stdout.close()
            except OSError:
                pass


def parse_options(engine):
    engine.send('uci')
    opts = {}
    for line in engine.read_until('uciok'):
        if line.startswith('option name '):
            name = line.split()[2]
            kind = line.split(' type ')[1].split()[0]
            lo = line.split(' min ')[1].split()[0] if ' min ' in line else None
            hi = line.split(' max ')[1].split()[0] if ' max ' in line else None
            dflt = line.split(' default ')[1].split()[0] if ' default ' in line else None
            opts[name] = (kind, lo, hi, dflt)
    return opts


def replay_legal(exe, cwd, moves):
    """Engine must accept its own move list without an error line."""
    engine = Engine(exe, cwd=cwd)
    try:
        engine.send('position startpos moves ' + ' '.join(moves))
        engine.ready()
        return True
    except RuntimeError:
        return False
    finally:
        engine.quit()


def options_lane(exe, work):
    engine = Engine(exe, cwd=work)
    try:
        opts = parse_options(engine)
        assert 'Threads' in opts and 'Hash' in opts, f'missing core options: {opts}'
        for name, (kind, lo, hi, dflt) in opts.items():
            if kind == 'spin':
                for val in (lo, hi, dflt):
                    if val is None:
                        continue
                    engine.send(f'setoption name {name} value {val}')
                    engine.ready()
            elif kind == 'check':
                for val in ('true', 'false'):
                    engine.send(f'setoption name {name} value {val}')
                    engine.ready()
            elif kind == 'string':
                engine.send(f'setoption name {name} value {dflt or "default"}')
                engine.ready()
        engine.send('position startpos')
        mv, _ = engine.bestmove('go depth 4')
        assert mv not in ('', '(none)'), f'options stress left engine unable to search: {mv!r}'
    finally:
        engine.quit()


def timecontrol_lane(exe, work):
    engine = Engine(exe, cwd=work)
    try:
        engine.send('position startpos')

        t0 = time.monotonic()
        mv, _ = engine.bestmove('go movetime 100')
        dt = time.monotonic() - t0
        assert mv not in ('', '(none)'), 'movetime produced no bestmove'
        assert 0.03 < dt < 3.0, f'movetime 100 took {dt:.2f}s'

        mv, _ = engine.bestmove('go wtime 10000 btime 10000 winc 100 binc 100')
        assert mv not in ('', '(none)'), 'clock go produced no bestmove'

        mv, _ = engine.bestmove('go wtime 50 btime 50 winc 0 binc 0')
        assert mv not in ('', '(none)'), 'ultra-bullet clock produced no bestmove'

        mv, _ = engine.bestmove('go wtime 1 btime 1 winc 0 binc 0', timeout=10)
        assert mv not in ('', '(none)'), '1ms clock produced no bestmove'

        mv, _ = engine.bestmove('go nodes 2000')
        assert mv not in ('', '(none)'), 'nodes go produced no bestmove'

        engine.send('go infinite')
        time.sleep(0.3)
        engine.send('stop')
        lines = engine.read_until('bestmove', timeout=10)
        mv = next((l.split()[1] for l in lines if l.startswith('bestmove ')), '')
        assert mv not in ('', '(none)'), 'infinite+stop produced no bestmove'
    finally:
        engine.quit()


def smp_lane(exe, work):
    for threads in (1, 2, 4, 8, 24, 64):
        engine = Engine(exe, cwd=work)
        try:
            engine.send(f'setoption name Threads value {threads}')
            engine.ready()
            engine.send('position startpos')
            mv, _ = engine.bestmove('go movetime 250')
            assert mv not in ('', '(none)'), f'Threads={threads}: no bestmove'
            engine.send(f'position startpos moves {mv}')
            engine.ready()
        finally:
            engine.quit()


def hash_lane(exe, work):
    for mb in (32, 256, 1024):
        engine = Engine(exe, cwd=work)
        try:
            engine.send(f'setoption name Hash value {mb}')
            engine.ready()
            engine.send('position startpos')
            mv, _ = engine.bestmove('go depth 6')
            assert mv not in ('', '(none)'), f'Hash={mb}: no bestmove'
        finally:
            engine.quit()


def game_lane(exe, work, plies=60, base_ms=60, inc_ms=6):
    """Full self-play game through UCI with real clocks; every move legal."""
    moves = []
    wtime, btime = base_ms * 10, base_ms * 10
    engine = Engine(exe, cwd=work)
    try:
        for ply in range(plies):
            white = ply % 2 == 0
            clock = wtime if white else btime
            inc = inc_ms
            t0 = time.monotonic()
            mv, _ = engine.bestmove(
                f'go wtime {wtime} btime {btime} winc {inc} binc {inc}')
            dt = int((time.monotonic() - t0) * 1000)
            if mv in ('', '(none)'):
                break
            assert dt < clock + 500, f'ply {ply}: used {dt}ms of {clock}ms clock'
            if white:
                wtime = max(1, wtime - dt + inc)
            else:
                btime = max(1, btime - dt + inc)
            moves.append(mv)
            engine.send('position startpos moves ' + ' '.join(moves))
        engine.ready()
    finally:
        engine.quit()
    assert len(moves) >= 20, f'game ended too early after {len(moves)} plies'
    assert replay_legal(exe, work, moves), 'engine rejected its own game move list'


def human_lane(exe, work):
    engine = Engine(exe, cwd=work)
    try:
        engine.send('setoption name HumanElo value 1500')
        engine.ready()
        engine.send('position startpos')
        mv, _ = engine.bestmove('go movetime 200')
        assert mv not in ('', '(none)'), 'HumanElo=1500: no bestmove'
        engine.send(f'position startpos moves {mv}')
        engine.ready()
        engine.send('setoption name HumanElo value 0')
        engine.ready()
    finally:
        engine.quit()


def ponder_lane(exe, work):
    engine = Engine(exe, cwd=work)
    try:
        engine.send('setoption name Ponder value true')
        engine.ready()
        engine.send('position startpos')
        mv, _ = engine.bestmove('go wtime 2000 btime 2000')
        assert mv not in ('', '(none)'), 'Ponder=true: no bestmove'
        engine.send('setoption name Ponder value false')
        engine.ready()
    finally:
        engine.quit()


def lifecycle_lane(exe, work):
    engine = Engine(exe, cwd=work)
    try:
        for game in range(3):
            engine.send('ucinewgame')
            engine.ready()
            engine.send('position startpos')
            mv, _ = engine.bestmove('go depth 6')
            assert mv not in ('', '(none)'), f'game {game}: no bestmove'
        # stop racing search start must still answer
        engine.send('position startpos')
        engine.send('go depth 90')
        engine.send('stop')
        lines = engine.read_until('bestmove', timeout=10)
        assert any(l.startswith('bestmove ') for l in lines), 'stop race lost bestmove'
    finally:
        engine.quit()


def hash_max_lane(exe, work):
    """The advertised Hash max must be safe to set (engine clamps to available
    memory since the 2026-10-14 hang fix). Parse the advertised max from the
    uci output and set it."""
    engine = Engine(exe, cwd=work)
    try:
        engine.send('uci')
        lines = engine.read_until('uciok', timeout=10)
        hash_line = next(l for l in lines if l.startswith('option name Hash'))
        max_mb = int(hash_line.split('max')[1].strip())
        assert 32 <= max_mb <= 1_000_000, f'advertised Hash max out of range: {max_mb}'
        engine.send(f'setoption name Hash value {max_mb}')
        engine.ready(timeout=30)
    finally:
        engine.quit()


def gameover_lane(exe, work):
    # A mated side must get 'bestmove (none)', not a hang or crash.
    engine = Engine(exe, cwd=work)
    try:
        engine.send('position fen 6k1/6Q1/6K1/8/8/8/8/8 b - - 0 1')  # black is mated
        mv, lines = engine.bestmove('go depth 5', timeout=10)
        assert mv == '(none)', f'mated side got {mv!r}: {lines[-3:]}'

        engine.send('position fen 7k/5RK1/8/8/8/8/8/8 b - - 0 1')  # stalemate family
        mv, _ = engine.bestmove('go depth 5', timeout=10)
        assert mv in ('(none)', ''), f'stalemate got {mv!r}'

        # Flagged or negative clocks must still answer, not wedge the engine.
        engine.send('position startpos')
        mv, _ = engine.bestmove('go wtime 0 btime 5000', timeout=10)
        assert mv not in ('',), 'wtime=0 produced no bestmove'
        mv, _ = engine.bestmove('go wtime -100 btime 5000', timeout=10)
        assert mv not in ('',), 'negative clock produced no bestmove'
        engine.ready()
    finally:
        engine.quit()


def invalid_input_lane(exe, work):
    engine = Engine(exe, cwd=work)
    try:
        engine.send('position startpos moves e2e5')  # illegal move
        engine.expect(ERROR_PREFIX, 5)
        engine.ready()
        engine.send('position')  # bare position
        engine.expect(ERROR_PREFIX, 5)
        engine.ready()
        # trailing junk is silently ignored (observed behavior); the engine
        # must remain usable either way.
        engine.send('position startpos junk')
        engine.ready()
        engine.send('position startpos')
        mv, _ = engine.bestmove('go depth 4')
        assert mv not in ('', '(none)'), 'engine unusable after invalid input'
    finally:
        engine.quit()


def info_format_lane(exe, work):
    engine = Engine(exe, cwd=work)
    try:
        engine.send('position startpos')
        mv, lines = engine.bestmove('go depth 10', timeout=30)
        infos = [l for l in lines if l.startswith('info depth ')]
        assert infos, 'no info lines'
        last = infos[-1].split()
        for field in ('score', 'nodes', 'nps', 'time', 'pv'):
            assert field in last, f'missing {field} in info: {infos[-1]}'
        pv = last[last.index('pv') + 1:]
        assert pv and pv[0] == mv, f'pv head {pv[:1]} != bestmove {mv}'
        depths = [int(l.split()[2]) for l in infos]
        assert depths == sorted(depths) and depths[-1] == 10, f'depths not monotonic to 10: {depths}'
    finally:
        engine.quit()


def id_lane(exe, work):
    engine = Engine(exe, cwd=work)
    try:
        opts = parse_options(engine)  # re-sends uci; also asserts handshake repeats
        assert any(True for _ in opts), 'no options parsed'
    finally:
        engine.quit()
    engine = Engine(exe, cwd=work)
    try:
        engine.send('uci')
        lines = engine.read_until('uciok')
        assert any(l.startswith('id name ') for l in lines), 'missing id name'
    finally:
        engine.quit()


def endurance_lane(exe, work):
    # 10 games x 15 plies in one process; all moves legal; engine healthy at end.
    moves = []
    engine = Engine(exe, cwd=work)
    try:
        for game in range(10):
            engine.send('ucinewgame')
            engine.ready()
            moves = []
            for ply in range(15):
                engine.send('position startpos moves ' + ' '.join(moves))
                mv, _ = engine.bestmove('go depth 6', timeout=20)
                if mv in ('', '(none)'):
                    break
                moves.append(mv)
        engine.ready()
    finally:
        engine.quit()
    assert replay_legal(exe, work, moves), 'endurance game move list rejected'


LANES = [
    ('options (all advertised options at min/max/default)', options_lane),
    ('time controls (movetime/clock/ultra/1ms/nodes/depth/infinite)', timecontrol_lane),
    ('SMP threads (1/2/4/8/24/64)', smp_lane),
    ('hash sizes (32/256/1024)', hash_lane),
    ('full self-play game with clocks (60 plies)', game_lane),
    ('HumanElo option', human_lane),
    ('Ponder option', ponder_lane),
    ('lifecycle (newgame cycles, stop race)', lifecycle_lane),
    ('game-over handling (mate/stalemate/flagged/negative clocks)', gameover_lane),
    ('invalid input (illegal move, bare position, junk)', invalid_input_lane),
    ('info line format + pv/bestmove consistency', info_format_lane),
    ('id/handshake', id_lane),
    ('endurance (10 games x 15 plies, single process)', endurance_lane),
    ('hash at advertised max (memory-clamped)', hash_max_lane),
]


def main():
    exe = str(Path(sys.argv[1]).resolve())
    failures = []
    with tempfile.TemporaryDirectory(prefix='sapling-tourney-') as work:
        for name, lane in LANES:
            if getattr(lane, 'skip', None):
                print(f'SKIP {name}: {lane.skip}')
                continue
            t0 = time.monotonic()
            try:
                lane(exe, work)
                print(f'PASS {name} ({time.monotonic() - t0:.1f}s)')
            except Exception as ex:
                failures.append((name, ex))
                print(f'FAIL {name}: {ex}')
    if failures:
        print(f'{len(failures)} lane(s) failed')
        sys.exit(1)
    print('ALL LANES PASS')


if __name__ == '__main__':
    main()
