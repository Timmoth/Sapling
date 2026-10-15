// Web UI controller; chess rules and game history come from the engine.

const INITIAL_FEN = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
const PIECE_WORD = { p: 'pawn', n: 'knight', b: 'bishop', r: 'rook', q: 'queen', k: 'king' };
const pieceSrc = (color, type) => `./assets/pieces/${color === 'w' ? 'white' : 'black'}_${PIECE_WORD[type]}.png`;

// Think time -> bounded search budgets. Depth accounts for the engine's inclusive-depth
// off-by-one (requested N completes N-1). These are starting values pending device tuning.
const DIFFICULTY = {
  short:  { maxDepth: 5,  sliceNodes: 4000,  totalNodes: 12000,   timeMs: 500 },
  medium: { maxDepth: 11, sliceNodes: 12000, totalNodes: 150000,  timeMs: 2500 },
  long:   { maxDepth: 19, sliceNodes: 20000, totalNodes: 1500000, timeMs: 8000 },
};

const el = (id) => document.getElementById(id);
const boardEl = el('board');
const statusEl = el('status');
const statusText = el('status-text');
function setStatus(text, state) { statusText.textContent = text; statusEl.dataset.state = state || 'idle'; }

const state = {
  fen: INITIAL_FEN,
  sideToMove: 'w',
  legalMoves: [],
  lastMove: '',
  inCheck: false,
  gameOver: false,
  result: '*',
};

let ready = false;
let thinking = false;
let applyingAiMove = false;
let humanColor = 'w';
let orientation = 'w'; // color at the bottom
let difficulty = 'medium';
let selectedSq = null;
let targets = new Map(); // destAlg -> uci (for the selected square)
let requestId = 0;
let evalSide = 'w';
let pendingExport = null; // 'copy' | 'download'
let history = []; // committed UCI moves, for the move list

const worker = new Worker('./worker.js', { type: 'module' });

// ---- square helpers (engine indexing: a1 = 0, file = sq % 8, rank = sq / 8) ----
const alg = (sq) => String.fromCharCode(97 + (sq % 8)) + String.fromCharCode(49 + Math.floor(sq / 8));
const fromAlg = (a) => (a.charCodeAt(1) - 49) * 8 + (a.charCodeAt(0) - 97);
const movesFrom = (sq) => state.legalMoves.filter((u) => u.slice(0, 2) === alg(sq));

function parseFen(fen) {
  const board = new Array(64).fill(null);
  const rows = fen.split(' ')[0].split('/');
  for (let r = 0; r < 8; r++) {
    let file = 0;
    for (const ch of rows[r]) {
      if (ch >= '1' && ch <= '9') { file += +ch; continue; }
      const color = ch === ch.toUpperCase() ? 'w' : 'b';
      board[(7 - r) * 8 + file] = { type: ch.toLowerCase(), color };
      file++;
    }
  }
  return board;
}

function kingSquare(board, color) {
  const want = color === 'w' ? 'K' : 'k';
  for (let sq = 0; sq < 64; sq++) {
    const p = board[sq];
    if (p && p.type === 'k' && p.color === color) return sq;
  }
  return -1;
}

// ---- rendering ----
function render() {
  const board = parseFen(state.fen);
  boardEl.textContent = '';
  const checkSq = state.inCheck ? kingSquare(board, state.sideToMove) : -1;
  const lastFrom = state.lastMove ? fromAlg(state.lastMove.slice(0, 2)) : -1;
  const lastTo = state.lastMove ? fromAlg(state.lastMove.slice(2, 4)) : -1;

  for (let r = 0; r < 8; r++) {
    for (let c = 0; c < 8; c++) {
      const sq = orientation === 'w' ? (7 - r) * 8 + c : r * 8 + (7 - c);
      const file = sq % 8, rank = Math.floor(sq / 8);
      const cell = document.createElement('button');
      cell.type = 'button';
      cell.className = 'sq ' + ((file + rank) % 2 === 0 ? 'dark' : 'light');
      cell.dataset.sq = String(sq);
      cell.setAttribute('role', 'gridcell');
      cell.setAttribute('aria-label', alg(sq));

      if (sq === lastFrom || sq === lastTo) cell.classList.add('last');
      if (sq === selectedSq) cell.classList.add('selected');
      if (sq === checkSq) cell.classList.add('check');

      const piece = board[sq];
      if (piece) {
        const img = document.createElement('img');
        img.className = 'piece';
        img.src = pieceSrc(piece.color, piece.type);
        img.alt = '';
        img.draggable = false;
        cell.appendChild(img);
      }

      if (targets.has(alg(sq))) cell.classList.add('valid');

      if (c === 0) {
        const rank2 = document.createElement('span');
        rank2.className = 'coord rank';
        rank2.textContent = String(rank + 1);
        cell.appendChild(rank2);
      }
      if (r === 7) {
        const file2 = document.createElement('span');
        file2.className = 'coord file';
        file2.textContent = String.fromCharCode(97 + file);
        cell.appendChild(file2);
      }

      cell.addEventListener('click', () => onSquare(sq));
      boardEl.appendChild(cell);
    }
  }
  renderMoves();
}

function renderMoves() {
  const list = el('moves');
  list.textContent = '';
  for (let i = 0; i < history.length; i += 2) {
    const li = document.createElement('li');
    const no = document.createElement('span'); no.className = 'no'; no.textContent = (i / 2 + 1) + '.';
    const w = document.createElement('span'); w.className = 'san'; w.textContent = history[i];
    li.append(no, w);
    if (history[i + 1]) { const b = document.createElement('span'); b.className = 'san'; b.textContent = history[i + 1]; li.append(b); }
    list.appendChild(li);
  }
  list.scrollTop = list.scrollHeight;
}

function updateStatusLine() {
  if (state.gameOver) {
    const map = { '1-0': 'White wins', '0-1': 'Black wins', '1/2-1/2': 'Draw', '*': 'Game over' };
    setStatus((map[state.result] || 'Game over') + ' (' + state.result + ')', 'over');
  } else if (thinking) {
    setStatus('Sapling is thinking\u2026', 'thinking');
  } else if (state.inCheck) {
    setStatus((state.sideToMove === humanColor ? 'Your move' : 'Sapling to move') + ' \u00b7 check', 'check');
  } else {
    setStatus(state.sideToMove === humanColor ? 'Your move' : 'Sapling to move',
      state.sideToMove === humanColor ? 'yourmove' : 'idle');
  }
}

// ---- input ----
function onSquare(sq) {
  if (!ready || thinking || state.gameOver || state.sideToMove !== humanColor) return;

  if (selectedSq === null) {
    if (movesFrom(sq).length) select(sq);
    return;
  }
  if (sq === selectedSq) { clearSelection(); return; }

  const dest = alg(sq);
  const cands = movesFrom(selectedSq).filter((u) => u.slice(2, 4) === dest);
  if (cands.length === 0) {
    if (movesFrom(sq).length) select(sq);
    else clearSelection();
    return;
  }
  if (cands.length === 1) { commit(cands[0]); return; }
  showPromotion(cands);
}

function select(sq) {
  selectedSq = sq;
  targets = new Map();
  for (const u of movesFrom(sq)) targets.set(u.slice(2, 4), u);
  render();
}

function clearSelection() { selectedSq = null; targets = new Map(); render(); }

function showPromotion(cands) {
  const box = el('promotion');
  box.textContent = '';
  for (const u of cands) {
    const btn = document.createElement('button');
    btn.type = 'button';
    const type = u.slice(4) || 'q';
    const img = document.createElement('img');
    img.className = 'piece';
    img.src = pieceSrc(humanColor, type);
    img.alt = '';
    img.draggable = false;
    btn.appendChild(img);
    btn.setAttribute('aria-label', 'Promote to ' + type);
    btn.addEventListener('click', () => { box.hidden = true; commit(u); });
    box.appendChild(btn);
  }
  box.hidden = false;
}

function commit(uci) {
  clearSelection();
  history.push(uci);
  worker.postMessage({ type: 'move', uci });
}

// ---- engine flow ----
function startAi() {
  const preset = DIFFICULTY[difficulty] || DIFFICULTY.medium;
  requestId += 1;
  thinking = true;
  evalSide = state.sideToMove;
  el('stop').hidden = false;
  updateStatusLine();
  worker.postMessage({ type: 'search', requestId, ...preset });
}

function onCompleted(msg) {
  if (msg.requestId !== requestId) return; // obsolete
  thinking = false;
  el('stop').hidden = true;
  if (msg.bestMove) {
    applyingAiMove = true;
    history.push(msg.bestMove);
    worker.postMessage({ type: 'move', uci: msg.bestMove });
  } else {
    updateStatusLine();
  }
}

function onProgress(msg) {
  if (msg.requestId !== requestId) return;
  setStat('st-depth', msg.bestDepth ?? '\u2013');
  setStat('st-eval', formatEval(msg.scoreType, msg.score, evalSide));
  setStat('st-time', ((msg.timeMs || 0) / 1000).toFixed(1) + 's');
  setStat('st-nodes', (msg.nodesTotal || 0).toLocaleString());
  setStat('st-nps', (msg.nps || 0).toLocaleString());
  el('st-pv').textContent = (msg.pv && msg.pv.length) ? msg.pv.join(' ') : '\u2013';
}

function formatEval(type, score, side) {
  if (score === undefined || score === null) return '\u2013';
  const whiteScore = side === 'w' ? score : -score;
  if (type === 'mate') return (whiteScore > 0 ? 'M' : 'M-') + Math.abs(whiteScore);
  return (whiteScore / 100).toFixed(2);
}

function setStat(id, value) { el(id).textContent = String(value); }

// ---- exports ----
function legacyCopy(text) {
  const ta = document.createElement('textarea');
  ta.value = text;
  ta.setAttribute('readonly', '');
  ta.style.position = 'fixed';
  ta.style.top = '-1000px';
  ta.style.opacity = '0';
  document.body.appendChild(ta);
  ta.select();
  let ok = false;
  try { ok = document.execCommand('copy'); } catch (e) { ok = false; }
  ta.remove();
  return ok;
}

async function copyText(text) {
  try {
    await navigator.clipboard.writeText(text);
    setStatus('Copied to clipboard.', 'copied');
  } catch (e) {
    const ok = legacyCopy(text);
    setStatus(ok ? 'Copied to clipboard.' : 'Copy failed \u2014 your browser blocked clipboard access.',
      ok ? 'copied' : 'error');
  }
}

function downloadText(text, name) {
  const blob = new Blob([text], { type: 'text/plain' });
  const url = URL.createObjectURL(blob);
  const a = document.createElement('a');
  a.href = url; a.download = name;
  document.body.appendChild(a); a.click(); a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 1000);
}

// ---- worker messages ----
worker.addEventListener('message', (event) => {
  const m = event.data || {};
  switch (m.type) {
    case 'booted':
      worker.postMessage({ type: 'init', ttMb: 16 });
      break;
    case 'ready':
      if (!m.ok) { setStatus('Engine init failed: ' + (m.message || m.error), 'error'); return; }
      ready = true;
      worker.postMessage({ type: 'position', fen: INITIAL_FEN });
      break;
    case 'position': {
      const wasAiApply = applyingAiMove; applyingAiMove = false;
      if (!m.ok) { setStatus(m.message || 'Position error', 'error'); return; }
      Object.assign(state, {
        fen: m.fen, sideToMove: m.sideToMove, legalMoves: m.legalMoves || [],
        lastMove: m.lastMove || '', inCheck: !!m.inCheck, gameOver: !!m.gameOver, result: m.result || '*',
      });
      render();
      updateStatusLine();
      if (!wasAiApply && !state.gameOver && state.sideToMove !== humanColor && !thinking) startAi();
      break;
    }
    case 'progress': onProgress(m); break;
    case 'completed': onCompleted(m); break;
    case 'stopped': break;
    case 'pgn':
      if (!m.ok) { setStatus('PGN failed: ' + (m.message || m.error), 'error'); break; }
      if (pendingExport === 'download') downloadText(m.pgn, 'sapling-game.pgn');
      else copyText(m.pgn);
      pendingExport = null;
      break;
    case 'failed':
      thinking = false; el('stop').hidden = true;
      setStatus('Engine error: ' + (m.message || m.error), 'error');
      break;
  }
});

// ---- controls ----
function newGame() {
  requestId += 1;
  thinking = false; applyingAiMove = false;
  el('stop').hidden = true;
  selectedSq = null; targets = new Map();
  history = [];
  el('promotion').hidden = true;
  ['st-depth', 'st-eval', 'st-time', 'st-nodes', 'st-nps'].forEach((id) => setStat(id, '\u2013'));
  el('st-pv').textContent = '\u2013';
  worker.postMessage({ type: 'newgame' });
}

function segmented(id, onChange) {
  const group = el(id);
  group.addEventListener('click', (e) => {
    const btn = e.target.closest('.pill');
    if (!btn) return;
    for (const p of group.querySelectorAll('.pill')) {
      const on = p === btn;
      p.classList.toggle('selected', on);
      p.setAttribute('aria-checked', on ? 'true' : 'false');
    }
    onChange(btn.dataset.value);
  });
}

el('new-game').addEventListener('click', newGame);
segmented('side', (v) => { humanColor = v; orientation = v; newGame(); });
segmented('difficulty', (v) => { difficulty = v; });

const eloSlider = el('human-elo');
const eloLabel = el('human-elo-label');
const eloLabelFor = (v) => (v <= 0 ? 'Off' : '\u2248' + v + ' Elo');
eloSlider.addEventListener('input', () => {
  const v = +eloSlider.value;
  eloLabel.textContent = eloLabelFor(v);
  worker.postMessage({ type: 'humanelo', elo: v });
});
el('flip').addEventListener('click', () => { orientation = orientation === 'w' ? 'b' : 'w'; render(); });
el('stop').addEventListener('click', () => worker.postMessage({ type: 'stop', requestId }));
el('copy-fen').addEventListener('click', () => copyText(state.fen));
el('copy-pgn').addEventListener('click', () => { pendingExport = 'copy'; worker.postMessage({ type: 'pgn' }); });
el('download-pgn').addEventListener('click', () => { pendingExport = 'download'; worker.postMessage({ type: 'pgn' }); });

render();
if (ready) updateStatusLine();
