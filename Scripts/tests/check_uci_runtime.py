#!/usr/bin/env python3
"""Process-level regression check: pass an absolute Sapling executable path.

Checks malformed commands, castling, search limits, cancellation and lifecycle behavior.
Uses only the Python standard library and a compiled engine executable.
"""
from pathlib import Path
import queue
import subprocess
import sys
import tempfile
import threading
import time

# every one of these used to end the process (or was silently accepted).
BAD_FENS = [
    '1k5b/5b2/8/8/8/8/3N3K/N4B2 w - - 0 0',
    '4k3/8/8/8/8/8/8/4K3 w -',
    'garbage',
    '4k3/8/8/8/8/8/8/4K3 w - - 0 65536',
    '4k3/8/8/8/8/8/8/4K3 w KQkq e9 0 1',
    '4k3/8/8/8/8/8/8/4K3 w - - 300 1',
]

# malformed go parameters must be rejected, not fatal.
BAD_GO = ['go depth', 'go depth abc', 'go depth 0', 'go depth 121', 'go perft', 'go perft 0',
          'go movetime', 'go see', 'go see e2e5']

ERROR_PREFIX = 'info string error'


class Engine:
    """Minimal UCI client: the match runner needs python-chess, this harness must not."""

    def __init__(self, exe, name='sapling', startup_timeout=20, cwd=None):
        self.p = subprocess.Popen([exe], stdin=subprocess.PIPE, stdout=subprocess.PIPE,
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
            self.send('uci')
            self.expect('uciok', startup_timeout)
            self.send('isready')
            self.expect('readyok', startup_timeout)
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
            raise TimeoutError(f'{self.name} waiting for a line') from None
        if line is None:
            raise RuntimeError(f'{self.name} process exited')
        return line

    def expect(self, prefix, timeout=10):
        """Return the next line starting with prefix, discarding everything before it."""
        deadline = time.monotonic() + timeout
        while True:
            line = self._next(max(0.01, deadline - time.monotonic()))
            if line.startswith(prefix):
                return line

    def read_until(self, prefix, timeout=10):
        """Collect every line up to and including the first one starting with prefix."""
        lines, deadline = [], time.monotonic() + timeout
        while True:
            line = self._next(max(0.01, deadline - time.monotonic()))
            lines.append(line)
            if line.startswith(prefix):
                return lines

    def drain(self, timeout=5):
        """Read every remaining line until the process closes stdout."""
        lines, deadline = [], time.monotonic() + timeout
        while time.monotonic() < deadline:
            try:
                line = self.lines.get(timeout=max(0.01, deadline - time.monotonic()))
            except queue.Empty:
                break
            if line is None:
                break
            lines.append(line)
        return lines

    def quit(self):
        try:
            if self.p.poll() is None:
                self.send('quit')
                self.p.wait(timeout=0.2)
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


def smoke_lane(exe, work):
    engine = Engine(exe, cwd=work)
    try:
        engine.send('position startpos')
        engine.send('go perft 1')
        assert engine.expect('Nodes searched:', 5) == 'Nodes searched: 20'
        engine.send('go movetime 250')
        depths = []
        while True:
            line = engine._next(5)
            if line.startswith('info depth '): depths.append(int(line.split()[2]))
            if line.startswith('bestmove '): break
        assert max(depths, default=0) > 1, depths
        engine.p.stdin.close()
        assert engine.p.wait(timeout=5) == 0, 'EOF must terminate cleanly'
    finally:
        engine.quit()


def protocol_lane(exe, work):
    engine = Engine(exe, cwd=work)
    try:
        # castling is only verified for standard chess, so Chess960 must not be offered.
        engine.send('uci')
        assert not any('UCI_Chess960' in line for line in engine.read_until('uciok'))

        for fen in BAD_FENS:
            engine.send(f'position fen {fen}')
            engine.expect(ERROR_PREFIX, 5)
            engine.send('isready')
            engine.expect('readyok', 5)

        for command in BAD_GO:
            engine.send(command)
            engine.expect(ERROR_PREFIX, 5)
            engine.send('isready')
            engine.expect('readyok', 5)

        engine.send('setoption name Hash')
        engine.expect(ERROR_PREFIX, 5)

        # a command merely containing 'quit' or 'stop' must not trigger either.
        engine.send('quietmode')
        engine.send('stopwatch')
        engine.send('isready')
        engine.expect('readyok', 5)

        # divide lines carry the promotion piece, so parsers cannot collide.
        engine.send('position fen 4k2r/6P1/8/8/8/8/8/4K3 w KQkq - 0 1')
        engine.send('go perft 1')
        divide = engine.read_until('Nodes searched:')
        assert {'g7g8q', 'g7g8r', 'g7g8b', 'g7g8n'} <= {line.split(':')[0] for line in divide}, divide

        # capturing the h8 rook by under-promotion must remove black's castle.
        engine.send('position fen 4k2r/6P1/8/8/8/8/8/4K3 w KQkq - 0 1 moves g7h8b')
        engine.send('go perft 1')
        divide = engine.read_until('Nodes searched:')
        assert divide[-1] == 'Nodes searched: 5', divide
        assert not any(line.startswith('e8g8') for line in divide), divide

        # 'go depth N' must reach depth N, not N-1.
        engine.send('position startpos')
        engine.send('go depth 8')
        depths = [int(line.split()[2]) for line in engine.read_until('bestmove')
                  if line.startswith('info depth ')]
        assert max(depths) == 8, depths

        # A legal high-fullmove FEN stays playable.
        engine.send('position fen 4k3/8/8/8/8/8/8/4K3 w - - 0 65535')
        engine.send('go depth 3')
        engine.expect('bestmove', 10)
    finally:
        engine.quit()


def quit_race_lane(exe, work):
    # commands already queued ahead of quit must still be answered.
    for _ in range(3):
        engine = Engine(exe, cwd=work)
        try:
            engine.send('isready')
            engine.send('quit')
            engine.p.stdin.flush()
            assert any(line.startswith('readyok') for line in engine.drain()), 'quit dropped readyok'
            assert engine.p.wait(timeout=5) == 0, 'quit must terminate cleanly'
        finally:
            engine.quit()

    for _ in range(3):
        engine = Engine(exe, cwd=work)
        try:
            engine.send('position startpos')
            engine.send('go depth 90')
            engine.send('quit')
            engine.p.stdin.flush()
            assert engine.p.wait(timeout=5) == 0, 'Quit racing search start must terminate cleanly'
        finally:
            engine.quit()


def _bestmove_token(lines):
    for line in lines:
        if line.startswith('bestmove '):
            token = line.split()[1] if len(line.split()) > 1 else ''
            return token
    return None


def stability_lane(exe, work):
    # Higher-level end-to-end invariants exercised through the UCI surface.

    # stop mid-search must still yield a bestmove.
    engine = Engine(exe, cwd=work)
    try:
        engine.send('position startpos')
        engine.send('go movetime 5000')
        engine.expect('info depth ', 10)  # search has started and is streaming progress
        engine.send('stop')
        lines = engine.read_until('bestmove', timeout=10)
        assert _bestmove_token(lines) not in (None, ''), f'stop did not produce a bestmove: {lines}'
    finally:
        engine.quit()

    # The engine's own bestmove must be legal: replaying it via 'position moves' must not error.
    engine = Engine(exe, cwd=work)
    try:
        engine.send('position startpos')
        engine.send('go depth 8')
        mv = _bestmove_token(engine.read_until('bestmove', timeout=10))
        assert mv not in (None, ''), 'go must return a bestmove'
        engine.send(f'position startpos moves {mv}')
        engine.send('isready')
        replay = engine.read_until('readyok', timeout=5)
        assert not any(line.startswith(ERROR_PREFIX) for line in replay), \
            f'bestmove {mv} was rejected as illegal: {replay}'
    finally:
        engine.quit()

    # A mate in one must be reported as 'score mate 1' through the protocol.
    engine = Engine(exe, cwd=work)
    try:
        engine.send('position fen 6k1/5ppp/8/8/8/8/8/R6K w - - 0 1')
        engine.send('go depth 6')
        lines = engine.read_until('bestmove', timeout=10)
        assert any('score mate 1' in line for line in lines), f'no mate-1 score reported: {lines}'
    finally:
        engine.quit()

    # Single-thread determinism: two fresh engines on the same position/depth agree on bestmove.
    first = second = None
    for _ in range(2):
        engine = Engine(exe, cwd=work)
        try:
            engine.send('position startpos')
            engine.send('go depth 8')
            token = _bestmove_token(engine.read_until('bestmove', timeout=10))
            if first is None:
                first = token
            else:
                second = token
        finally:
            engine.quit()
    assert first not in (None, '') and first == second, f'non-deterministic bestmove: {first} vs {second}'

    # A Threads option change must still produce a legal, complete search.
    engine = Engine(exe, cwd=work)
    try:
        engine.send('setoption name Threads value 2')
        engine.send('isready')
        engine.expect('readyok', 5)
        engine.send('position startpos')
        engine.send('go depth 8')
        lines = engine.read_until('bestmove', timeout=10)
        assert _bestmove_token(lines) not in (None, ''), f'multi-thread go produced no bestmove: {lines}'
    finally:
        engine.quit()


def main():
    exe = str(Path(sys.argv[1]).resolve())
    with tempfile.TemporaryDirectory(prefix='sapling-uci-check-') as work:
        smoke_lane(exe, work)
        protocol_lane(exe, work)
        quit_race_lane(exe, work)
        stability_lane(exe, work)
    print('PASS: perft depth 1, timed search progress, FEN/go validation, Chess960 not advertised, '
          'divide promotion suffixes, phantom castling, go depth N, EOF exit, quit races, '
          'stop-mid-search, bestmove legality replay, mate score, determinism, Threads option')


if __name__ == '__main__':
    main()
