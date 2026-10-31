import unittest
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).parents[1]))
from train import rating_band, rating_balanced_weights


class TrainingRatingBandTests(unittest.TestCase):
    def test_balancing_gives_rare_and_common_bands_equal_total_weight(self):
        weights = rating_balanced_weights([{"rating": 500}] + [{"rating": 1500}] * 9)
        self.assertAlmostEqual(weights[0], sum(weights[1:]))
        self.assertEqual(rating_balanced_weights([]), [])

    def test_inclusive_2200_endpoint_stays_in_last_band(self):
        self.assertEqual(rating_band(500), "400-599")
        self.assertEqual(rating_band(2199), "2000-2199")
        self.assertEqual(rating_band(2200), "2000-2199")


if __name__ == "__main__":
    unittest.main()
