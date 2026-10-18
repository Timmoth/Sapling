# Sapling webapp — build, run, deploy

A self-contained static webapp for playing the Sapling Wasm engine. Basic HTML + vanilla
JavaScript modules + a dedicated Web Worker hosting the .NET/Wasm runtime. No backend, no
CDN, no third-party runtime requests.

## Build

```bash
npm --prefix web ci                 # pinned esbuild
dotnet test src/Sapling.Engine.Tests -c Release
dotnet test src/Sapling.WebHost.Tests -c Release
Scripts/build_web.sh                 # SIMD + AOT (default; fastest search)
Scripts/build_web.sh --jit           # SIMD + JIT (faster build)
Scripts/build_web.sh --no-simd       # non-SIMD + AOT (older-browser fallback)
Scripts/build_web.sh --no-simd --jit # non-SIMD + JIT
```

## Tests

```bash
npm --prefix web ci
npm --prefix web run check
dotnet test src/Sapling.Engine.Tests -c Release
dotnet test src/Sapling.WebHost.Tests -c Release
Scripts/run_wasm_tests.sh
Scripts/build_web.sh
node Scripts/test_web_runtime.mjs
node Scripts/test_web_worker.mjs
```

After building `dist/`, run `npm --prefix web run test:all` for the JavaScript checks and
the published-Wasm host and worker integration tests.

The Node tests cover UI formatting, slider bounds, and the HTML/app/worker contracts. The .NET
tests cover engine and WebHost behavior; `Scripts/run_wasm_tests.sh` runs the Wasm test bundle under Node.
After publishing, `test_web_runtime.mjs` exercises the shipped WASM runtime through the same host API
used by the worker: startup, Human Elo, legal-move search, scoring, apply, PGN export and disable.
`test_web_worker.mjs` drives the published worker protocol against that runtime, including undo-style
restore, hint scoring and Elo restoration. Run the built static app in a browser for visual and
interaction checks before release.

The trained policy's weights are embedded in `Sapling.Engine` and its inference runs in the
same C# engine code for native UCI and browser WASM builds. Human Elo values from 800 to 2200
select the policy; zero disables it. The web build ships no separate policy model or inference
runtime.

## Wasm profiles (4.6)

Measured nodes/second for the **published** artifacts (Node-hosted `browser-wasm`, identical
bounded-slice search, 3 positions x 250k nodes, after warm-up):

| Profile | nodes/sec | dist size |
| --- | --- | --- |
| SIMD JIT | ~92k | 14 MB |
| **SIMD AOT (default)** | **~513k** | 20 MB |
| non-SIMD JIT | ~50k | 14 MB |
| non-SIMD AOT | ~272k | 20 MB |

AOT (`RunAOTCompilation=true`) is ~5.5x faster than JIT; SIMD adds ~1.8x on top. AOT grows
the payload ~6 MB (native code in `dotnet.native.wasm`); wasm compresses well, so enable
gzip/brotli on the host. The default build is SIMD + AOT. Ship the non-SIMD AOT artifact as
the fallback for browsers without Wasm SIMD.

These are Node-hosted numbers. Real browser and physical-device throughput are still NOT RUN
(see below).

This publishes `src/Sapling.WebHost` for `browser-wasm` and assembles a static directory at
`dist/`:

```
dist/
  index.html  app.js  ui-model.js  worker.js  styles.css  ASSET-MANIFEST.md
  Sapling.WebHost.runtimeconfig.json  package.json
  _framework/            # .NET runtime, native wasm, engine assemblies, ICU data
```

NNUE and human-policy weights are embedded in `Sapling.Engine`; no model sidecar is fetched.

## Local preview

```bash
python3 -m http.server 8080 --directory dist
# open http://localhost:8080/
```

Any static file server works. `file://` is **not** supported (module workers and Wasm
streaming require HTTP). Serve the files over HTTP.

## Required MIME types

The server must serve:

- `.wasm` → `application/wasm`
- `.js`   → `text/javascript`

`python3 -m http.server` already does both. Verify with, e.g.:
`curl -I http://localhost:8080/_framework/dotnet.native.wasm`.

## Subdirectory deployment

All asset references are relative (`./styles.css`, `./app.js`, `./worker.js`,
`./_framework/...`), and the .NET loader resolves `_framework` from the worker's own URL.
Serve `dist/` at the domain root or under any subpath (e.g. `/sapling/`) with no base tag or
server-side code. No custom isolation headers are required (single worker, no shared
memory).

## What works now (verified)

- Engine builds and runs on `browser-wasm` (SIMD) — the shared suite runs 224 passed /
  0 failed / 5 skipped under Node.
- Host contract verified end-to-end on Wasm: initialize, set position, bounded-slice search
  with live depth/eval/nodes/nps/PV, apply move, obsolete-request rejection, stop returning
  the best completed result, dispose.
- Authoritative SAN/PGN export (replayed through the engine) and Copy FEN.
- Static assembly + correct MIME serving + JS syntax all verified.
- Wasm profiles built + benchmarked under Node: SIMD/non-SIMD x JIT/AOT (see table). AOT is
  the default build (~5x faster search).
