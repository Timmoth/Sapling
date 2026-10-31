"""Mirror the runtime's interpolation at each position's exact display rating."""
from bisect import bisect_left


def settings_for_rating(rating, calibration, risk_map):
    if not calibration:
        band = f"{min(int(rating), 2199) // 200 * 200}-{min(int(rating), 2199) // 200 * 200 + 199}"
        return int(rating), 1.0, risk_map.get(band, 0.0)
    anchors = sorted(calibration.values(), key=lambda row: row['display_rating'])
    value = max(500, min(2200, int(rating)))
    right = bisect_left([row['display_rating'] for row in anchors], value)
    if right == 0:
        left = end = anchors[0]
    elif right == len(anchors):
        left = end = anchors[-1]
    else:
        left, end = anchors[right - 1], anchors[right]
    fraction = 0.0 if left is end else (value - left['display_rating']) / (end['display_rating'] - left['display_rating'])
    def interpolate(key, default):
        return float(left.get(key, default)) + fraction * (float(end.get(key, default)) - float(left.get(key, default)))
    penalty = risk_map.get(left['human_rating_band'], 0.0)
    penalty += fraction * (risk_map.get(end['human_rating_band'], 0.0) - penalty)
    return round(interpolate('model_rating', value)), interpolate('temperature', 1.0), penalty
