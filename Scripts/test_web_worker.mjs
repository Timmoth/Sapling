import assert from 'node:assert/strict';
const backlog = [];
const waiters = [];
globalThis.self = {
  onmessage: null,
  postMessage(message) {
    const index = waiters.findIndex(({ type, predicate }) =>
      message.type === type && predicate(message));
    if (index >= 0) waiters.splice(index, 1)[0].resolve(message);
    else backlog.push(message);
  },
};

function waitFor(type, predicate = () => true, timeoutMs = 60_000) {
  const index = backlog.findIndex((message) => message.type === type && predicate(message));
  if (index >= 0) return Promise.resolve(backlog.splice(index, 1)[0]);
  return new Promise((resolve, reject) => {
    const timer = setTimeout(() => {
      const index = waiters.findIndex((waiter) => waiter.resolve === done);
      if (index >= 0) waiters.splice(index, 1);
      reject(new Error(`Timed out waiting for worker message '${type}'`));
    }, timeoutMs);
    function done(message) {
      clearTimeout(timer);
      resolve(message);
    }
    waiters.push({ type, predicate, resolve: done });
  });
}

async function send(message, responseType, predicate = () => true) {
  const response = waitFor(responseType, predicate);
  await self.onmessage({ data: message });
  return response;
}

await import('../dist/worker.js');
assert.equal((await waitFor('booted')).ok, true, 'worker boots the published WASM runtime');

const ready = await send({ type: 'init', ttMb: 16 }, 'ready');
assert.equal(ready.ok, true);
const start = await send({ type: 'position', fen: 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1' }, 'position');
assert.equal(start.ok, true);
assert.equal(start.legalMoves.length, 20);

const elo = await send({ type: 'humanelo', elo: 800 }, 'humanelo');
assert.equal(elo.ok, true);
assert.equal(elo.humanElo, 800);

const searchTask = self.onmessage({ data: {
  type: 'search', requestId: 5, maxDepth: 4, sliceNodes: 1800, totalNodes: 8000, timeMs: 1500,
} });
const completed = await waitFor('completed', (message) => message.requestId === 5);
await searchTask;
const finalProgress = await waitFor('progress', (message) => message.requestId === 5 && message.done);
assert.equal(completed.bestMove, finalProgress.bestMove,
  'worker returns the host’s finalized move after Human Elo policy selection');
assert.ok(start.legalMoves.includes(completed.bestMove), 'search picks a legal move');

const moved = await send({
  type: 'move', uci: completed.bestMove, moveSide: 'w',
  scoreType: completed.scoreType, score: completed.score,
}, 'position');
assert.equal(moved.ok, true);
assert.equal(moved.lastMove, completed.bestMove);
assert.equal(moved.sideToMove, 'b');

const restored = await send({ type: 'restore', moves: [completed.bestMove], restoreId: 9 }, 'position',
  (message) => message.restored && message.restoreId === 9);
assert.equal(restored.ok, true);
assert.equal(restored.lastMove, completed.bestMove);

const pgn = await send({ type: 'pgn' }, 'pgn');
assert.equal(pgn.ok, true);
assert.match(pgn.pgn, /1\./);

const hintTask = self.onmessage({ data: {
  type: 'hints', hintId: 12, fen: 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1',
  moves: ['e2e4', 'd2d4'], history: [],
} });
const hintsDone = await waitFor('hints-done', (message) => message.hintId === 12);
await hintTask;
assert.equal(hintsDone.hintId, 12);
const hintUpdates = backlog.filter((message) => message.type === 'hint-progress' && message.hintId === 12);
assert.ok(hintUpdates.some((message) => message.scores.some((score) => score.move === 'e2e4')));
assert.ok(hintUpdates.some((message) => message.scores.some((score) => score.move === 'd2d4')));

const interruptedSearch = self.onmessage({ data: {
  type: 'search', requestId: 20, maxDepth: 120, sliceNodes: 12_000,
  totalNodes: 10_000_000, timeMs: 0,
} });
await waitFor('progress', (message) => message.requestId === 20);
const reset = await send({ type: 'newgame' }, 'position');
assert.equal(reset.ok, true, 'new game cancels an in-flight search');
assert.equal(reset.sideToMove, 'w');
await interruptedSearch;

const resumedSearch = self.onmessage({ data: {
  type: 'search', requestId: 21, maxDepth: 4, sliceNodes: 1800, totalNodes: 8000, timeMs: 1500,
} });
const resumed = await waitFor('completed', (message) => message.requestId === 21);
await resumedSearch;
assert.ok(reset.legalMoves.includes(resumed.bestMove), 'engine accepts a new search after cancellation');

const disabled = await send({ type: 'humanelo', elo: 0 }, 'humanelo');
assert.equal(disabled.enabled, false);
await send({ type: 'dispose' }, 'disposed');
console.log('Web worker integration passed: boot, Elo, search, move, restore, hints, cancellation, PGN, dispose.');
