import sys
import unittest
from pathlib import Path
sys.path.insert(0, str(Path(__file__).parents[1]))
from runtime_calibration import settings_for_rating

class RuntimeCalibrationTests(unittest.TestCase):
    def test_interpolates_model_temperature_and_risk_at_exact_rating(self):
        anchors = {
            '600-799': dict(human_rating_band='600-799', display_rating=700, model_rating=1000, temperature=1),
            '800-999': dict(human_rating_band='800-999', display_rating=900, model_rating=1200, temperature=1.2),
        }
        self.assertEqual(settings_for_rating(800, anchors, {'600-799': 1, '800-999': 1.5}), (1100, 1.1, 1.25))
        self.assertEqual(settings_for_rating(2200, anchors, {}), (1200, 1.2, 0))
        self.assertEqual(settings_for_rating(500, anchors, {}), (1000, 1, 0))

    def test_identity_without_map(self):
        self.assertEqual(settings_for_rating(2200, None, {'2000-2199': .5}), (2200, 1, .5))
