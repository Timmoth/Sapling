"""Runner regressions independent of python-chess or a real engine."""
import importlib.util
from pathlib import Path
import sys
import time
import unittest

spec = importlib.util.spec_from_file_location("strength", Path(__file__).parents[1] / "strength_match.py")
strength = importlib.util.module_from_spec(spec)
spec.loader.exec_module(strength)

HANDSHAKE = '''import sys,time
for line in sys.stdin:
 cmd=line.strip()
 if cmd=='uci': print('uciok',flush=True)
 elif cmd=='isready': print('readyok',flush=True)
 elif cmd=='quit': break
 elif cmd.startswith('go'): time.sleep(10)
'''

class RunnerTests(unittest.TestCase):
    def test_silent_engine_cannot_block_past_move_timeout(self):
        engine = strength.Engine([sys.executable, '-u', '-c', HANDSHAKE], 'fake')
        try:
            engine.new_game()
            start = time.monotonic()
            with self.assertRaises(TimeoutError):
                engine.move('position startpos', 100, 100, 0, 0, timeout=0.05)
            self.assertLess(time.monotonic() - start, 2)
        finally:
            engine.quit()
        self.assertIsNotNone(engine.p.poll())

    def test_startup_timeout_is_bounded(self):
        start = time.monotonic()
        with self.assertRaises(TimeoutError):
            strength.Engine([sys.executable, '-u', '-c', 'import time; time.sleep(10)'], 'silent', startup_timeout=0.05)
        self.assertLess(time.monotonic() - start, 2)

    def test_unfinished_pair_has_no_rating(self):
        lines, complete = strength.paired_summary([(0, 'white', '*', 'maxplies-unadjudicated', 160), (0, 'black', '1-0', 'normal', 40)])
        self.assertFalse(complete)
        self.assertIn('complete_pairs=0 estimate=UNAVAILABLE', lines)

    def test_forfeit_is_loss_and_invalidates_clean_run(self):
        lines, complete = strength.paired_summary([(0, 'white', '0-1', 'forfeit', 2), (0, 'black', '1/2-1/2', 'normal', 40)])
        self.assertFalse(complete)
        self.assertIn('dev_score=0.2500', lines[0])
        self.assertIn('abnormal_or_unfinished=1', lines)

    def test_paired_result_is_from_candidate_perspective(self):
        lines, complete = strength.paired_summary([(0, 'white', '1-0', 'normal', 30), (0, 'black', '0-1', 'normal', 40)])
        self.assertTrue(complete)
        self.assertIn('dev_score=1.0000', lines[0])
        self.assertTrue(any('ci95=UNAVAILABLE' in line for line in lines))

if __name__ == '__main__':
    unittest.main()
