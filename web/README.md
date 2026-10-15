# Sapling webapp — build, run, deploy

A self-contained static webapp for playing the Sapling Wasm engine. Basic HTML + vanilla
JavaScript modules + a dedicated Web Worker hosting the .NET/Wasm runtime. No backend, no
CDN, no third-party runtime requests.

## Build

```bash
Scripts/build_web.sh                 # SIMD + AOT (default; fastest search)
Scripts/build_web.sh --jit           # SIMD + JIT (faster build)
Scripts/build_web.sh --no-simd       # non-SIMD + AOT (older-browser fallback)
Scripts/build_web.sh --no-simd --jit # non-SIMD + JIT
```

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
  index.html  app.js  worker.js  styles.css  ASSET-MANIFEST.md
  Sapling.WebHost.runtimeconfig.json  package.json
  _framework/            # .NET runtime, native wasm, engine assemblies, ICU data
```

NNUE weights are embedded in `Sapling.Engine.wasm`, so there is no separate weights file.

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