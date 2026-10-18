import sys
import unittest
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).parents[1]))
from export_onnx import policy_probability_total_variation


class ExportParityTests(unittest.TestCase):
    def test_policy_parity_ignores_shared_logit_offset(self):
        expected = np.array([[16000.0, 15998.0, 15997.0]], dtype=np.float64)
        actual = expected + 1234.0

        self.assertLess(policy_probability_total_variation(expected, actual), 1e-12)

    def test_policy_parity_measures_probability_change(self):
        expected = np.array([[2.0, 1.0, 0.0]], dtype=np.float32)
        actual = np.array([[1.0, 2.0, 0.0]], dtype=np.float32)

        self.assertGreater(policy_probability_total_variation(expected, actual), 0.3)

    def test_policy_parity_requires_matching_candidate_shapes(self):
        with self.assertRaisesRegex(ValueError, r"same \[B,N\] shape"):
            policy_probability_total_variation(np.zeros((1, 2)), np.zeros((1, 3)))


if __name__ == "__main__":
    unittest.main()
