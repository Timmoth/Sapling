import assert from 'node:assert/strict';
import { dotnet } from '../dist/_framework/dotnet.js';

const runtime = await dotnet.withDiagnosticTracing(false).create();
const config = runtime.getConfig();
const assemblies = await runtime.getAssemblyExports(config.mainAssemblyName);
const host = assemblies.Sapling.WebHost.EngineHost;
const parse = (value) => JSON.parse(value);

const initialized = parse(host.Initialize(16));
assert.equal(initialized.ok, true, 'WASM engine initializes');

const enabled = parse(host.SetHumanElo(1500));
assert.equal(enabled.ok, true);
assert.equal(enabled.enabled, true);
assert.equal(enabled.humanElo, 1500);

const position = parse(host.SetPosition('rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1'));
assert.equal(position.ok, true);
assert.equal(position.legalMoves.length, 20);

const started = parse(host.StartSearch(1, 4, 1000, 5000, 1000));
assert.equal(started.ok, true);
let result;
for (let slice = 0; slice < 100; slice++) {
  result = parse(host.SearchSlice(1));
  if (!result.ok || result.done) break;
}
assert.equal(result?.ok, true, 'WASM search returns successfully');
assert.equal(result?.done, true, 'bounded search completes');
assert.ok(position.legalMoves.includes(result.bestMove), 'policy selects a legal move');

const scored = parse(host.ScoreMove(result.bestMove));
assert.equal(scored.ok, true, 'move scoring succeeds for the selected legal move');
assert.equal(scored.move, result.bestMove);

const applied = parse(host.ApplyMove(result.bestMove));
assert.equal(applied.ok, true, 'selected move applies');
assert.equal(applied.sideToMove, 'b');
assert.match(parse(host.Pgn()).pgn, /1\./, 'game exports a move-numbered PGN');

const disabled = parse(host.SetHumanElo(0));
assert.equal(disabled.ok, true);
assert.equal(disabled.enabled, false);

console.log(`WebAssembly runtime smoke passed: HumanElo=${enabled.humanElo}, move=${result.bestMove}, PGN export verified.`);
