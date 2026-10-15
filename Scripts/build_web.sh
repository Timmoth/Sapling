#!/usr/bin/env bash
# Build a static Sapling web app in ./dist. Options: --jit and --no-simd.
set -euo pipefail
cd "$(dirname "$0")/.."

WSIMD=true
WAOT=true
for a in "$@"; do
  case "$a" in
    --no-simd) WSIMD=false ;;
    --jit)     WAOT=false ;;
    *) echo "Usage: Scripts/build_web.sh [--no-simd] [--jit]" >&2; exit 2 ;;
  esac
done

suffix="simd"; [[ "$WSIMD" == "false" ]] && suffix="nosimd"
[[ "$WAOT" == "true" ]] && suffix="$suffix-aot" || suffix="$suffix-jit"

artifacts="$PWD/src/Sapling.WebHost/bin/webdist-$suffix"
echo "Publishing Sapling.WebHost (WasmEnableSIMD=$WSIMD, RunAOTCompilation=$WAOT)..."
dotnet publish src/Sapling.WebHost -c Release --artifacts-path "$artifacts" \
  -p:WasmEnableSIMD="$WSIMD" -p:RunAOTCompilation="$WAOT" -v quiet

appbundle="$(find "$artifacts" -type d -name AppBundle | head -1)"
if [[ -z "$appbundle" ]]; then echo "AppBundle not found under $artifacts" >&2; exit 1; fi

dist="dist"
rm -rf "$dist"
mkdir -p "$dist"

# Runtime, Wasm modules, assemblies, ICU data, runtimeconfig (self-contained).
cp -R "$appbundle/." "$dist/"
rm -f "$dist/smoke.mjs"
# Drop debug-only source maps from the served payload. (dotnet.native.js.symbols is
# referenced by the runtime asset manifest and must be kept.)
find "$dist" -type f -name '*.map' -delete

# Application assets.
cp web/index.html web/app.js web/worker.js web/styles.css "$dist/"
[[ -d web/assets ]] && cp -R web/assets "$dist/assets"
[[ -f web/ASSET-MANIFEST.md ]] && cp web/ASSET-MANIFEST.md "$dist/"

echo
echo "Static site assembled at: $dist/  (profile: $suffix)"
echo "Preview (root):   python3 -m http.server 8080 --directory $dist"
echo "Then open:        http://localhost:8080/"
