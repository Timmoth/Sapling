// Web UI controller; chess rules and game history come from the engine.

import { eloFromSliderValue, eloLabelForSliderValue, formatEngineEval, formatMoveScore,
  FULL_ENGINE_SLIDER_VALUE } from './ui-model.js';

const INITIAL_FEN = 'rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1';
const PIECE_WORD = { p: 'pawn', n: 'knight', b: 'bishop', r: 'rook', q: 'queen', k: 'king' };
const pieceSrc = (color, type) => `./assets/pieces/${color === 'w' ? 'white' : 'black'}_${PIECE_WORD[type]}.svg`;

const SEARCH_LIMITS = { maxDepth: 120, sliceNodes: 12000, totalNodes: 10000000, timeMs: 1000 };
const AI_MOVE_PAUSE_MS = 650;

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
let restoring = false;
let hinting = false;
let hintMode = false;
let applyingAiMove = false;
let humanColor = 'w';
let orientation = 'w'; // color at the bottom
let selectedSq = null;
let targets = new Map(); // destAlg -> uci (for the selected square)
let requestId = 0;
let restoreId = 0;
let pendingAiMoveTimer = null;
let activeMoveAnimations = [];
let moveAnimationTimer = null;
let redoStack = [];
let hintScores = new Map();
let hintId = 0;
let evalSide = 'w';
let pendingExport = null; // 'copy' | 'download'
let history = []; // committed UCI moves, for the move list
let historyScores = []; // evaluation after each move, normalized to White's perspective
let historyCaptures = []; // captured piece after each move, aligned with history

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

function capturedByMove(board, uci) {
  if (!uci || uci.length < 4) return null;
  const from = fromAlg(uci.slice(0, 2));
  const to = fromAlg(uci.slice(2, 4));
  let captured = board[to];
  // En passant captures the pawn beside the destination square.
  if (!captured && board[from]?.type === 'p' && from % 8 !== to % 8) {
    captured = board[to + (board[from].color === 'w' ? -8 : 8)];
  }
  return captured ? { color: captured.color, type: captured.type } : null;
}

function renderMaterial() {
  const byColor = { w: [], b: [] };
  for (const capture of historyCaptures) {
    if (capture) byColor[capture.color].push(capture.type);
  }
  const opponentColor = humanColor === 'w' ? 'b' : 'w';
  const rows = [
    { id: 'opponent-material', color: opponentColor, pieces: byColor[opponentColor] },
    { id: 'you-material', color: humanColor, pieces: byColor[humanColor] },
  ];
  for (const row of rows) {
    const container = el(row.id);
    container.textContent = '';
    if (!row.pieces.length) {
      const empty = document.createElement('span');
      empty.className = 'material-empty';
      empty.textContent = '—';
      empty.setAttribute('aria-label', 'No pieces lost');
      container.appendChild(empty);
      continue;
    }
    for (const type of row.pieces) {
      const img = document.createElement('img');
      img.className = 'material-piece';
      img.src = pieceSrc(row.color, type);
      img.alt = PIECE_WORD[type];
      img.title = PIECE_WORD[type][0].toUpperCase() + PIECE_WORD[type].slice(1);
      container.appendChild(img);
    }
  }
  const value = (pieces) => pieces.reduce((sum, type) => sum + ({ p: 1, n: 3, b: 3, r: 5, q: 9 }[type] || 0), 0);
  const difference = value(byColor[opponentColor]) - value(byColor[humanColor]);
  el('move-count').textContent = `${history.length} ${history.length === 1 ? 'move' : 'moves'}`;
  const balance = el('material-balance');
  balance.textContent = difference === 0 ? 'Material even'
    : difference > 0 ? `You +${difference}` : `Opponent +${Math.abs(difference)}`;
  balance.dataset.edge = difference === 0 ? 'even' : difference > 0 ? 'you' : 'opponent';
  el('opponent-material-label').textContent = 'Sapling';
  el('you-material-label').textContent = 'You';
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

      if (sq === lastFrom) cell.classList.add('last', 'last-from');
      if (sq === lastTo) cell.classList.add('last', 'last-to');
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
      const destinationMoves = selectedSq === null ? [] : movesFrom(selectedSq).filter((move) => move.slice(2, 4) === alg(sq));
      const hint = destinationMoves.map((move) => hintScores.get(move)).filter(Boolean)
        .sort((a, b) => b.rankScore - a.rankScore)[0];
      if (hint) {
        const badge = document.createElement('span');
        badge.className = 'hint-score';
        badge.textContent = hint.label;
        cell.appendChild(badge);
      }

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
  if (state.lastMove) renderMovePath(lastFrom, lastTo);
  renderMaterial();
  renderMoves();
}

function renderMovePath(from, to) {
  const point = (sq) => {
    const file = sq % 8, rank = Math.floor(sq / 8);
    const col = orientation === 'w' ? file : 7 - file;
    const row = orientation === 'w' ? 7 - rank : rank;
    return { x: (col + 0.5) * 100, y: (row + 0.5) * 100 };
  };
  const a = point(from), b = point(to);
  const svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
  svg.classList.add('move-path');
  svg.setAttribute('viewBox', '0 0 800 800');
  svg.setAttribute('aria-hidden', 'true');
  const fromAlgSquare = state.lastMove.slice(0, 2);
  const toAlgSquare = state.lastMove.slice(2, 4);
  const fileChange = Math.abs(fromAlgSquare.charCodeAt(0) - toAlgSquare.charCodeAt(0));
  const rankChange = Math.abs(Number(fromAlgSquare[1]) - Number(toAlgSquare[1]));
  const isKnightMove = (fileChange === 1 && rankChange === 2) || (fileChange === 2 && rankChange === 1);
  const path = document.createElementNS('http://www.w3.org/2000/svg', isKnightMove ? 'polyline' : 'line');
  if (isKnightMove) {
    path.setAttribute('points', `${a.x},${a.y} ${a.x},${b.y} ${b.x},${b.y}`);
  } else {
    path.setAttribute('x1', a.x); path.setAttribute('y1', a.y);
    path.setAttribute('x2', b.x); path.setAttribute('y2', b.y);
  }
  svg.appendChild(path);
  boardEl.appendChild(svg);
}

function clearMoveAnimations() {
  clearTimeout(moveAnimationTimer);
  for (const { image, animation, destination } of activeMoveAnimations) {
    animation.cancel();
    image.remove();
    destination.classList.remove('animating');
  }
  activeMoveAnimations = [];
}

function animateMove(before, from, to) {
  const piece = before[from];
  const source = boardEl.querySelector(`[data-sq="${from}"]`);
  const destination = boardEl.querySelector(`[data-sq="${to}"]`);
  if (!piece || !source || !destination || matchMedia('(prefers-reduced-motion: reduce)').matches) return;

  const boardRect = boardEl.getBoundingClientRect();
  const sourceRect = source.getBoundingClientRect();
  const destinationRect = destination.getBoundingClientRect();
  const size = sourceRect.width * 0.8;
  const startX = sourceRect.left - boardRect.left - boardEl.clientLeft + sourceRect.width * 0.1;
  const startY = sourceRect.top - boardRect.top - boardEl.clientTop + sourceRect.height * 0.1;
  const dx = destinationRect.left - sourceRect.left;
  const dy = destinationRect.top - sourceRect.top;
  const image = document.createElement('img');
  image.className = 'move-animation';
  image.src = pieceSrc(piece.color, piece.type);
  image.alt = '';
  image.draggable = false;
  image.style.left = `${startX}px`;
  image.style.top = `${startY}px`;
  image.style.width = `${size}px`;
  image.style.height = `${size}px`;
  boardEl.appendChild(image);
  destination.classList.add('animating');

  const fromAlgSquare = alg(from), toAlgSquare = alg(to);
  const fileChange = Math.abs(fromAlgSquare.charCodeAt(0) - toAlgSquare.charCodeAt(0));
  const rankChange = Math.abs(Number(fromAlgSquare[1]) - Number(toAlgSquare[1]));
  const isKnightMove = (fileChange === 1 && rankChange === 2) || (fileChange === 2 && rankChange === 1);
  const frames = [{ transform: 'translate(0, 0)' }];
  if (isKnightMove) frames.push({ transform: `translate(0, ${dy}px)`, offset: 0.67 });
  frames.push({ transform: `translate(${dx}px, ${dy}px)` });
  const animation = image.animate(frames, { duration: 360, easing: 'ease-in-out', fill: 'forwards' });
  activeMoveAnimations.push({ image, animation, destination });
}

function animateLastMove(before, uci) {
  if (!uci || uci.length < 4) return;
  const from = fromAlg(uci.slice(0, 2)), to = fromAlg(uci.slice(2, 4));
  animateMove(before, from, to);

  const king = before[from];
  if (king?.type === 'k' && Math.abs((from % 8) - (to % 8)) === 2) {
    const rank = Math.floor(from / 8);
    const kingSide = to > from;
    const rookFrom = rank * 8 + (kingSide ? 7 : 0);
    const rookTo = rank * 8 + (kingSide ? 5 : 3);
    animateMove(before, rookFrom, rookTo);
  }
  if (activeMoveAnimations.length) {
    moveAnimationTimer = setTimeout(clearMoveAnimations, 380);
  }
}

function renderMoves() {
  const list = el('moves');
  list.textContent = '';
  for (let i = 0; i < history.length; i += 2) {
    const li = document.createElement('li');
    const no = document.createElement('span'); no.className = 'no'; no.textContent = (i / 2 + 1) + '.';
    li.append(no, renderMoveEntry(history[i], historyScores[i]));
    if (history[i + 1]) li.append(renderMoveEntry(history[i + 1], historyScores[i + 1]));
    list.appendChild(li);
  }
  list.scrollTop = list.scrollHeight;
  renderMoveChart();
  updateUndoControls();
}

function renderMoveEntry(move, score) {
  const entry = document.createElement('span'); entry.className = 'move-entry';
  const san = document.createElement('span'); san.className = 'san'; san.textContent = move;
  const evalBadge = document.createElement('span');
  evalBadge.className = 'move-eval';
  evalBadge.textContent = score ? formatMoveScore(score) : '\u2013';
  evalBadge.title = score ? `Position after this move; ${score.scoreType === 'mate' ? 'mate' : 'evaluation'} from White's perspective` : 'Move evaluation unavailable';
  evalBadge.setAttribute('aria-label', evalBadge.title + ': ' + evalBadge.textContent);
  entry.append(san, evalBadge);
  return entry;
}

function renderMoveChart() {
  const path = el('move-eval-path');
  const marker = el('move-eval-current');
  const chart = el('move-eval-chart');
  const width = 280, height = 64, padding = 7, mid = height / 2, amplitude = 24;
  const available = historyScores
    .map((score, index) => score ? { score, index } : null)
    .filter(Boolean);
  if (!available.length) {
    path.setAttribute('d', '');
    marker.setAttribute('cx', String(padding));
    marker.setAttribute('cy', String(mid));
    marker.setAttribute('visibility', 'hidden');
    chart.setAttribute('aria-label', 'Move evaluation graph. Evaluations appear after moves are analyzed.');
    return;
  }

  const xFor = (index) => history.length <= 1
    ? width - padding
    : padding + index / (history.length - 1) * (width - 2 * padding);
  const yFor = (score) => {
    const pawns = score.scoreType === 'mate' ? Math.sign(score.score || 1) * 5 : score.score / 100;
    return mid - Math.max(-5, Math.min(5, pawns)) / 5 * amplitude;
  };
  const points = available.map(({ score, index }) => [xFor(index), yFor(score)]);
  path.setAttribute('d', points.map(([x, y], i) => `${i ? 'L' : 'M'}${x.toFixed(1)},${y.toFixed(1)}`).join(' '));
  const [lastX, lastY] = points[points.length - 1];
  marker.setAttribute('cx', lastX.toFixed(1));
  marker.setAttribute('cy', lastY.toFixed(1));
  marker.setAttribute('visibility', 'visible');
  const lastScore = available[available.length - 1].score;
  const label = formatMoveScore(lastScore);
  chart.setAttribute('aria-label', `Move evaluation graph. White's perspective, range plus or minus 5 pawns. Latest evaluation ${label}.`);
}

function updateUndoControls() {
  el('undo').disabled = !ready || restoring || hinting || history.length === 0;
  el('redo').disabled = !ready || restoring || hinting || redoStack.length === 0;
  el('hint-mode').disabled = !ready || restoring || hinting || thinking || state.gameOver || state.sideToMove !== humanColor;
  el('human-elo').disabled = !ready || restoring || hinting || thinking;
}

function updateStatusLine() {
  if (restoring) {
    setStatus('Restoring position', 'thinking');
  } else if (hinting) {
    setStatus('Scoring moves', 'thinking');
  } else if (state.gameOver) {
    const map = { '1-0': 'White wins', '0-1': 'Black wins', '1/2-1/2': 'Draw', '*': 'Game over' };
    setStatus((map[state.result] || 'Game over') + ' (' + state.result + ')', 'over');
  } else if (thinking) {
    setStatus('Sapling is thinking', 'thinking');
  } else if (state.inCheck) {
    setStatus((state.sideToMove === humanColor ? 'Your move' : 'Sapling to move') + ' \u00b7 check', 'check');
  } else {
    setStatus(state.sideToMove === humanColor ? 'Your move' : 'Sapling to move',
      state.sideToMove === humanColor ? 'yourmove' : 'idle');
  }
}

// ---- input ----
function onSquare(sq) {
  if (!ready || thinking || restoring || hinting || state.gameOver || state.sideToMove !== humanColor) return;

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
  hintScores.clear();
  render();
  if (hintMode && targets.size) requestHintScores();
}

function clearSelection() { selectedSq = null; targets = new Map(); hintScores.clear(); render(); }

function requestHintScores() {
  const moves = movesFrom(selectedSq);
  if (!moves.length) return;
  hinting = true;
  const id = ++hintId;
  updateUndoControls();
  updateStatusLine();
  worker.postMessage({ type: 'hints', hintId: id, fen: state.fen, moves, history: [...history] });
}

function updateHintScores(msg) {
  if (msg.hintId !== hintId) return;
  const ranked = [...(msg.scores || [])].sort((a, b) => b.rankScore - a.rankScore);
  hintScores = new Map(ranked.map((score) => [score.move, { ...score, label: formatHintScore(score) }]));
  render();
}

function formatHintScore(score) {
  if (score.scoreType === 'mate') return score.score > 0 ? `M${Math.abs(score.score)}` : `-M${Math.abs(score.score)}`;
  return (score.score >= 0 ? '+' : '') + (score.score / 100).toFixed(1);
}

function toggleHintMode() {
  if (hinting || restoring) return;
  hintMode = !hintMode;
  const button = el('hint-mode');
  el('hint-mode-state').textContent = hintMode ? 'On' : 'Off';
  button.setAttribute('aria-label', hintMode ? 'Move hints on. Turn off' : 'Move hints off. Turn on');
  button.setAttribute('aria-pressed', String(hintMode));
  button.classList.toggle('selected', hintMode);
  hintScores.clear();
  render();
  if (hintMode && selectedSq !== null && targets.size) requestHintScores();
}

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
  historyCaptures.push(capturedByMove(parseFen(state.fen), uci));
  clearSelection();
  redoStack = [];
  history.push(uci);
  historyScores.push(null);
  worker.postMessage({ type: 'move', uci, moveSide: state.sideToMove });
}

// ---- engine flow ----
function startAi() {
  requestId += 1;
  thinking = true;
  updateUndoControls();
  evalSide = state.sideToMove;
  updateStatusLine();
  worker.postMessage({ type: 'search', requestId, ...SEARCH_LIMITS });
}

function onCompleted(msg) {
  if (msg.requestId !== requestId) return; // obsolete
  if (msg.bestMove) {
    pendingAiMoveTimer = setTimeout(() => {
      pendingAiMoveTimer = null;
      if (msg.requestId !== requestId) return;
      thinking = false;
      applyingAiMove = true;
      redoStack = [];
      history.push(msg.bestMove);
      historyCaptures.push(capturedByMove(parseFen(state.fen), msg.bestMove));
      historyScores.push(null);
      worker.postMessage({ type: 'move', uci: msg.bestMove, moveSide: evalSide, scoreType: msg.scoreType, score: msg.score });
    }, AI_MOVE_PAUSE_MS);
  } else {
    thinking = false;
    updateUndoControls();
    updateStatusLine();
  }
}

function onProgress(msg) {
  if (msg.requestId !== requestId) return;
  setStat('st-depth', msg.bestDepth ?? '\u2013');
  setStat('st-eval', formatEngineEval(msg.scoreType, msg.score, evalSide));
  setStat('st-time', ((msg.timeMs || 0) / 1000).toFixed(1) + 's');
  setStat('st-nodes', (msg.nodesTotal || 0).toLocaleString());
  setStat('st-nps', (msg.nps || 0).toLocaleString());
  el('st-pv').textContent = (msg.pv && msg.pv.length) ? msg.pv.join(' ') : '\u2013';
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
      updateUndoControls();
      worker.postMessage({ type: 'position', fen: INITIAL_FEN });
      break;
    case 'position': {
      if (m.restored && m.restoreId !== restoreId) break;
      if (m.restored) restoring = false;
      const wasAiApply = applyingAiMove; applyingAiMove = false;
      if (!m.ok) { setStatus(m.message || 'Position error', 'error'); return; }
      clearMoveAnimations();
      const previousBoard = parseFen(state.fen);
      Object.assign(state, {
        fen: m.fen, sideToMove: m.sideToMove, legalMoves: m.legalMoves || [],
        lastMove: m.lastMove || '', inCheck: !!m.inCheck, gameOver: !!m.gameOver, result: m.result || '*',
      });
      if (m.moveScore && historyScores.length === history.length && historyScores.length) {
        const sign = m.moveScore.side === 'b' ? -1 : 1;
        historyScores[historyScores.length - 1] = {
          scoreType: m.moveScore.scoreType || 'cp',
          score: sign * (m.moveScore.score || 0),
        };
      }
      render();
      if (!m.restored) animateLastMove(previousBoard, state.lastMove);
      updateStatusLine();
      if (!wasAiApply && !state.gameOver && state.sideToMove !== humanColor && !thinking) startAi();
      break;
    }
    case 'humanelo':
      if (!m.ok) setStatus('Could not change Human-style level: ' + (m.message || m.error), 'error');
      break;
    case 'progress': onProgress(m); break;
    case 'hint-progress': updateHintScores(m); break;
    case 'hints-done':
      if (m.hintId !== hintId) break;
      hinting = false;
      updateUndoControls();
      updateStatusLine();
      break;
    case 'hint-failed':
      if (m.hintId !== hintId) break;
      hinting = false;
      updateUndoControls();
      setStatus('Hint failed: ' + (m.message || 'engine error'), 'error');
      break;
    case 'completed': onCompleted(m); break;
    case 'stopped': break;
    case 'disposed': break;
    case 'pgn':
      if (!m.ok) { setStatus('PGN failed: ' + (m.message || m.error), 'error'); break; }
      if (pendingExport === 'download') downloadText(m.pgn, 'sapling-game.pgn');
      else copyText(m.pgn);
      pendingExport = null;
      break;
    case 'failed':
      if (m.restoreId !== undefined && m.restoreId !== restoreId) break;
      clearTimeout(pendingAiMoveTimer);
      pendingAiMoveTimer = null;
      thinking = false; restoring = false;
      updateUndoControls();
      setStatus('Engine error: ' + (m.message || m.error), 'error');
      break;
  }
});

// ---- controls ----
function newGame() {
  clearTimeout(pendingAiMoveTimer);
  pendingAiMoveTimer = null;
  clearMoveAnimations();
  restoreId += 1;
  requestId += 1;
  thinking = false; restoring = false; hinting = false; applyingAiMove = false;
  hintId += 1;
  hintScores.clear();
  selectedSq = null; targets = new Map();
  history = [];
  historyCaptures = [];
  historyScores = [];
  el('promotion').hidden = true;
  redoStack = [];
  renderMoves();
  ['st-depth', 'st-eval', 'st-time', 'st-nodes', 'st-nps'].forEach((id) => setStat(id, '\u2013'));
  el('st-pv').textContent = '\u2013';
  worker.postMessage({ type: 'newgame' });
}

function restoreHistory() {
  clearTimeout(pendingAiMoveTimer);
  pendingAiMoveTimer = null;
  clearMoveAnimations();
  requestId += 1;
  restoring = true;
  thinking = false;
  applyingAiMove = false;
  selectedSq = null; targets = new Map();
  el('promotion').hidden = true;
  renderMoves();
  updateStatusLine();
  const id = ++restoreId;
  worker.postMessage({ type: 'restore', moves: [...history], restoreId: id });
}

function undo() {
  if (!history.length || restoring) return;
  const count = thinking ? 1 : Math.min(2, history.length);
  redoStack.push({
    moves: history.splice(history.length - count, count),
    scores: historyScores.splice(historyScores.length - count, count),
    captures: historyCaptures.splice(historyCaptures.length - count, count),
  });
  restoreHistory();
}

function redo() {
  if (!redoStack.length || restoring) return;
  const restored = redoStack.pop();
  history.push(...restored.moves);
  historyScores.push(...restored.scores);
  historyCaptures.push(...restored.captures);
  restoreHistory();
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

const mobileMenuToggle = el('mobile-menu-toggle');
const mobileMenuBackdrop = el('mobile-menu-backdrop');
const menuPanel = el('menu-panel');
let mobileMenuOpen = false;

function setMobileMenuOpen(open, returnFocus = true) {
  mobileMenuOpen = open;
  menuPanel.classList.toggle('mobile-menu-open', open);
  mobileMenuBackdrop.hidden = !open;
  mobileMenuToggle.setAttribute('aria-expanded', String(open));
  mobileMenuToggle.setAttribute('aria-label', open ? 'Close game menu' : 'Open game menu');
  if (open) {
    const firstControl = menuPanel.querySelector('button:not(:disabled), input:not(:disabled), summary');
    firstControl?.focus();
  } else if (returnFocus) {
    mobileMenuToggle.focus();
  }
}

mobileMenuToggle.addEventListener('click', () => setMobileMenuOpen(!mobileMenuOpen));
mobileMenuBackdrop.addEventListener('click', () => setMobileMenuOpen(false));
menuPanel.addEventListener('click', (event) => {
  if (mobileMenuOpen && event.target.closest('button')) setMobileMenuOpen(false);
});
document.addEventListener('keydown', (event) => {
  if (!mobileMenuOpen) return;
  if (event.key === 'Escape') {
    event.preventDefault();
    setMobileMenuOpen(false);
    return;
  }
  if (event.key !== 'Tab') return;
  const controls = [...menuPanel.querySelectorAll('button:not(:disabled), input:not(:disabled), summary')]
    .filter((control) => control.getClientRects().length > 0);
  if (!controls.length) return;
  const first = controls[0], last = controls[controls.length - 1];
  if (event.shiftKey && document.activeElement === first) {
    event.preventDefault();
    last.focus();
  } else if (!event.shiftKey && document.activeElement === last) {
    event.preventDefault();
    first.focus();
  }
});
window.matchMedia('(min-width: 900px)').addEventListener('change', (event) => {
  if (event.matches && mobileMenuOpen) setMobileMenuOpen(false, false);
});

el('new-game').addEventListener('click', newGame);
el('undo').addEventListener('click', undo);
el('redo').addEventListener('click', redo);
el('hint-mode').addEventListener('click', toggleHintMode);
segmented('side', (v) => { humanColor = v; orientation = v; newGame(); });

const eloSlider = el('human-elo');
const eloLabel = el('human-elo-label');
eloSlider.addEventListener('input', () => {
  const sliderValue = +eloSlider.value;
  const elo = eloFromSliderValue(sliderValue);
  const label = eloLabelForSliderValue(sliderValue);
  eloLabel.textContent = label;
  eloSlider.setAttribute('aria-valuetext', sliderValue === FULL_ENGINE_SLIDER_VALUE
    ? '3000+ (full engine strength)'
    : `${elo} Elo`);
  worker.postMessage({ type: 'humanelo', elo });
});
el('flip').addEventListener('click', () => {
  clearMoveAnimations();
  orientation = orientation === 'w' ? 'b' : 'w';
  render();
});
el('copy-fen').addEventListener('click', () => copyText(state.fen));
el('copy-pgn').addEventListener('click', () => { pendingExport = 'copy'; worker.postMessage({ type: 'pgn' }); });
el('download-pgn').addEventListener('click', () => { pendingExport = 'download'; worker.postMessage({ type: 'pgn' }); });

render();
if (ready) updateStatusLine();
