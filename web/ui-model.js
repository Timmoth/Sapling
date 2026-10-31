export const FULL_ENGINE_SLIDER_VALUE = 171;

export function eloFromSliderValue(value) {
  const step = Number(value);
  if (!Number.isInteger(step) || step < 0 || step > FULL_ENGINE_SLIDER_VALUE) {
    throw new RangeError('Human-style slider value must be an integer from 0 to 171.');
  }
  return step === FULL_ENGINE_SLIDER_VALUE ? 0 : 500 + step * 10;
}

export function eloLabelForSliderValue(value) {
  const elo = eloFromSliderValue(value);
  return elo === 0 ? '3000+ · Full engine' : `≈${elo} Elo`;
}

export function formatMoveScore(score) {
  if (score.scoreType === 'mate') return score.score > 0 ? `M${Math.abs(score.score)}` : `-M${Math.abs(score.score)}`;
  return (score.score >= 0 ? '+' : '') + (score.score / 100).toFixed(1);
}

export function formatEngineEval(type, score, side) {
  if (score === undefined || score === null) return '–';
  const whiteScore = side === 'w' ? score : -score;
  if (type === 'mate') return (whiteScore > 0 ? 'M' : 'M-') + Math.abs(whiteScore);
  return (whiteScore / 100).toFixed(2);
}

export function formatHintScore(score) {
  if (score.scoreType === 'mate') return score.score > 0 ? `M${Math.abs(score.score)}` : `-M${Math.abs(score.score)}`;
  if (score.lossCp === 0) return 'Best';
  const loss = Math.max(0, score.lossCp) / 100;
  return `−${loss.toFixed(loss < 0.1 ? 2 : 1)}`;
}
