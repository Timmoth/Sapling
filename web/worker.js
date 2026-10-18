// Browser worker for the Wasm engine. Sliced searches yield so stop and new-game messages
// can run; interrupted iterations restart from the root with a warm transposition table.
let host = null;
const stopFlags = new Set();
let currentRequest = 0;
let activeHintId = 0;
let currentHumanElo = 0;

function post(msg) { self.postMessage(msg); }
function parse(s) {
  try { return JSON.parse(s); }
  catch (e) { return { ok: false, error: 'bad-json', message: String(e) }; }
}

async function boot() {
  const { dotnet } = await import('./_framework/dotnet.js');
  const runtime = await dotnet.withDiagnosticTracing(false).create();
  const config = runtime.getConfig();
  const exports = await runtime.getAssemblyExports(config.mainAssemblyName);
  host = exports.Sapling.WebHost.EngineHost;
}

try {
  await boot();
  post({ type: 'booted', ok: true });
} catch (e) {
  post({ type: 'failed', error: 'boot', message: String(e && e.message || e) });
}

self.onmessage = async (event) => {
  const m = event.data || {};
  if (!host && m.type !== 'dispose') {
    post({ type: 'failed', error: 'not-booted', message: 'Engine runtime is not available.' });
    return;
  }
  switch (m.type) {
    case 'init': {
      const r = parse(host.Initialize(m.ttMb | 0));
      post({ type: 'ready', ...r });
      break;
    }
    case 'position': {
      post({ type: 'position', ...parse(host.SetPosition(String(m.fen || ''))) });
      break;
    }
    case 'move': {
      let moveScore;
      if (m.scoreType && Number.isFinite(m.score)) {
        moveScore = { scoreType: m.scoreType, score: m.score, side: m.moveSide || 'w' };
      } else {
        const scored = parse(host.ScoreMove(String(m.uci || '')));
        if (scored.ok) moveScore = { scoreType: scored.scoreType, score: scored.score, side: m.moveSide || 'w' };
      }
      post({ type: 'position', ...parse(host.ApplyMove(String(m.uci || ''))), moveScore });
      break;
    }
    case 'pgn': {
      post({ type: 'pgn', ...parse(host.Pgn()) });
      break;
    }
    case 'humanelo': {
      const result = parse(host.SetHumanElo(m.elo | 0));
      if (result.ok) currentHumanElo = result.humanElo | 0;
      post({ type: 'humanelo', ...result });
      break;
    }
    case 'newgame': {
      activeHintId = 0;
      stopFlags.add(currentRequest);
      post({ type: 'position', ...parse(host.NewGame()) });
      break;
    }
    case 'restore': {
      activeHintId = 0;
      stopFlags.add(currentRequest);
      let result = parse(host.NewGame());
      if (result.ok) {
        for (const uci of m.moves || []) {
          result = parse(host.ApplyMove(String(uci)));
          if (!result.ok) break;
        }
      }
      if (result.ok) {
        post({ type: 'position', ...result, restored: true, restoreId: m.restoreId });
      } else {
        post({ type: 'failed', error: 'restore', message: result.message || result.error, restoreId: m.restoreId });
      }
      break;
    }
    case 'hints': {
      if (activeHintId) {
        post({ type: 'hint-failed', hintId: m.hintId, message: 'A hint search is already running.' });
      } else {
        runHints(m);
      }
      break;
    }
    case 'stop': {
      stopFlags.add(m.requestId | 0);
      post({ type: 'stopped', ...parse(host.Stop(m.requestId | 0)) });
      break;
    }
    case 'search': {
      await runSearch(m);
      break;
    }
    case 'dispose': {
      if (host) post({ type: 'disposed', ...parse(host.Dispose()) });
      host = null;
      break;
    }
    default:
      post({ type: 'failed', error: 'unknown-message', message: m.type });
  }
};

async function runSearch(req) {
  const id = req.requestId | 0;
  currentRequest = id;
  stopFlags.delete(id);

  const start = parse(host.StartSearch(id, req.maxDepth | 0, req.sliceNodes | 0, req.totalNodes | 0, req.timeMs | 0));
  if (!start.ok) { post({ type: 'failed', requestId: id, ...start }); return; }

  let best = null;
  let lastPost = 0;
  while (true) {
    const p = parse(host.SearchSlice(id));
    if (!p.ok) {
      if (p.state === 'obsolete') return; // superseded by a newer request
      post({ type: 'failed', requestId: id, ...p });
      return;
    }
    if ((p.bestDepth ?? -1) > (best ? best.bestDepth : -1)) best = p;

    const now = performance.now();
    if (p.done || now - lastPost > 120) { post({ type: 'progress', ...p }); lastPost = now; }

    if (p.done || stopFlags.has(id)) break;
    // Yield to the worker event loop so stop/new-game postMessages are processed.
    await new Promise((resolve) => setTimeout(resolve, 0));
  }

  const fin = parse(host.Stop(id));
  // The host applies the Human Elo override when the search finalizes. Its terminal
  // response is authoritative; an earlier progress message may still contain the
  // unmodified full-engine move.
  const chosen = fin.bestMove ? fin : best || fin;
  post({
    type: 'completed',
    requestId: id,
    stopped: stopFlags.has(id),
    bestMove: chosen.bestMove || '',
    bestDepth: chosen.bestDepth || 0,
    scoreType: chosen.scoreType,
    score: chosen.score,
    pv: chosen.pv || [],
    nodesTotal: chosen.nodesTotal || 0,
  });
}

async function runHints(req) {
  const id = req.hintId | 0;
  activeHintId = id;
  // Hint scores are evaluations, so bypass the Human Elo move-selection override.
  const previousHumanElo = currentHumanElo;
  const disabled = parse(host.SetHumanElo(0));
  if (!disabled.ok) {
    activeHintId = 0;
    post({ type: 'hint-failed', hintId: id, message: disabled.message || disabled.error || 'Could not disable Human Elo for move scoring.' });
    return;
  }
  currentHumanElo = 0;

  try {
    const scores = [];
    let error = '';

    for (const move of req.moves || []) {
      if (activeHintId !== id) return;
      let result = parse(host.SetPosition(String(req.fen || '')));
      if (result.ok) result = parse(host.ApplyMove(String(move)));
      if (!result.ok) { error = result.message || result.error || 'Could not apply move.'; break; }

      const searchId = ++currentRequest;
      stopFlags.delete(searchId);
      result = parse(host.StartSearch(searchId, 5, 1800, 5000, 45));
      if (!result.ok) { error = result.message || result.error || 'Could not start analysis.'; break; }

      while (true) {
        if (activeHintId !== id) return;
        result = parse(host.SearchSlice(searchId));
        if (!result.ok || result.done) break;
        await new Promise((resolve) => setTimeout(resolve, 0));
      }
      if (!result.ok) { error = result.message || result.error || 'Move analysis failed.'; break; }

      const score = -(result.score || 0);
      const scoreType = result.scoreType || 'cp';
      const rankScore = scoreType === 'mate'
        ? score > 0 ? 100000 - Math.abs(score) : -100000 + Math.abs(score)
        : score;
      scores.push({ move, score, scoreType, rankScore });
      scores.sort((a, b) => b.rankScore - a.rankScore);
      post({ type: 'hint-progress', hintId: id, scores });
    }

    if (activeHintId !== id) return;
    let restored = parse(host.NewGame());
    if (restored.ok) {
      for (const move of req.history || []) {
        restored = parse(host.ApplyMove(String(move)));
        if (!restored.ok) break;
      }
    }
    activeHintId = 0;
    if (error || !restored.ok) {
      post({ type: 'hint-failed', hintId: id, message: error || restored.message || restored.error || 'Could not restore game.' });
      return;
    }
    post({ type: 'hints-done', hintId: id });
  } finally {
    if (host) {
      const restoredElo = parse(host.SetHumanElo(previousHumanElo));
      if (restoredElo.ok) currentHumanElo = restoredElo.humanElo | 0;
    }
  }
}
