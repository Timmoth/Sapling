import assert from 'node:assert/strict';
import { readFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import path from 'node:path';
import test from 'node:test';
import {
  eloFromSliderValue,
  eloLabelForSliderValue,
  formatEngineEval,
  formatMoveScore,
  FULL_ENGINE_SLIDER_VALUE,
} from '../ui-model.js';

const webRoot = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const read = (name) => readFile(path.join(webRoot, name), 'utf8');

test('Human Elo slider maps endpoints and all configured ratings', () => {
  assert.equal(eloFromSliderValue(0), 800);
  assert.equal(eloFromSliderValue(140), 2200);
  assert.equal(eloFromSliderValue(FULL_ENGINE_SLIDER_VALUE), 0);
  assert.deepEqual(Array.from({ length: 141 }, (_, step) => eloFromSliderValue(step)),
    Array.from({ length: 141 }, (_, step) => 800 + step * 10));
  assert.throws(() => eloFromSliderValue(-1), RangeError);
  assert.throws(() => eloFromSliderValue(142), RangeError);
  assert.throws(() => eloFromSliderValue(1.5), RangeError);
});

test('Human Elo labels distinguish trained ratings from full engine', () => {
  assert.equal(eloLabelForSliderValue(0), '≈800 Elo');
  assert.equal(eloLabelForSliderValue(140), '≈2200 Elo');
  assert.equal(eloLabelForSliderValue(FULL_ENGINE_SLIDER_VALUE), '3000+ · Full engine');
});

test('move evaluations use White perspective and keep mate notation readable', () => {
  assert.equal(formatMoveScore({ scoreType: 'cp', score: 34 }), '+0.3');
  assert.equal(formatMoveScore({ scoreType: 'cp', score: -125 }), '-1.3');
  assert.equal(formatMoveScore({ scoreType: 'mate', score: -3 }), '-M3');
  assert.equal(formatEngineEval('cp', 120, 'w'), '1.20');
  assert.equal(formatEngineEval('cp', 120, 'b'), '-1.20');
  assert.equal(formatEngineEval('mate', 4, 'b'), 'M-4');
  assert.equal(formatEngineEval('cp', null, 'w'), '–');
});

test('every app DOM reference exists once in the page', async () => {
  const [html, app] = await Promise.all([read('index.html'), read('app.js')]);
  const ids = [...html.matchAll(/\bid="([^"]+)"/g)].map((match) => match[1]);
  assert.equal(ids.length, new Set(ids).size, 'HTML element IDs must be unique');
  const appIds = [...app.matchAll(/\bel\('([^']+)'\)/g)].map((match) => match[1]);
  for (const id of new Set(appIds)) assert.ok(ids.includes(id), `app.js references missing #${id}`);
});

test('app and worker message types have matching handlers', async () => {
  const [app, worker] = await Promise.all([read('app.js'), read('worker.js')]);
  const sent = [...app.matchAll(/postMessage\(\{\s*type:\s*'([^']+)'/g)].map((match) => match[1]);
  const handled = [...worker.matchAll(/case '([^']+)':/g)].map((match) => match[1]);
  for (const type of new Set(sent)) assert.ok(handled.includes(type), `worker.js does not handle '${type}'`);

  const emitted = [...worker.matchAll(/type:\s*'([^']+)'/g)].map((match) => match[1]);
  const received = [...app.matchAll(/case '([^']+)':/g)].map((match) => match[1]);
  for (const type of new Set(emitted)) assert.ok(received.includes(type), `app.js does not handle '${type}'`);
});

test('all piece image paths used by the board are present', async () => {
  const words = { p: 'pawn', n: 'knight', b: 'bishop', r: 'rook', q: 'queen', k: 'king' };
  const colors = ['white', 'black'];
  for (const color of colors) {
    for (const piece of Object.values(words)) {
      await readFile(path.join(webRoot, 'assets', 'pieces', `${color}_${piece}.svg`));
    }
  }
});
