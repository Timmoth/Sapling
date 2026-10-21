#!/usr/bin/env bash
# Node-hosted browser-wasm qualification. Requires the matching .NET workload.
set -euo pipefail
cd "$(dirname "$0")/.."
case "${1:-}" in
  "") suffix=simd; simd=true ;;
  --no-simd) suffix=nosimd; simd=false ;;
  *) echo 'Usage: Scripts/run_wasm_tests.sh [--no-simd]' >&2; exit 2 ;;
esac
if [[ $# -gt 1 ]]; then echo 'Too many arguments' >&2; exit 2; fi

# Isolate intermediates AND outputs for every referenced project. BaseOutputPath
# alone still shares obj/, which can reuse the other profile's runtime assets.
artifacts="$(pwd)/src/Sapling.WasmRunner/bin/qualification-$suffix"
# Native relink intermediates cannot be reused across SDK/runtime upgrades.
rm -rf "$artifacts"
# Publish creates the runnable AppBundle; retain reflection-discovered test methods.
dotnet publish src/Sapling.WasmRunner -c Release --artifacts-path "$artifacts" \
  -p:WasmEnableSIMD="$simd" -p:PublishTrimmed=false -p:RunAOTCompilation=false
shopt -s nullglob
bundles=("$artifacts/bin/Sapling.WasmRunner/"*/AppBundle)
if [[ ${#bundles[@]} -ne 1 ]]; then
  echo "Expected exactly one AppBundle under $artifacts" >&2
  exit 1
fi
bundle="${bundles[0]}"
cat > "$bundle/run.mjs" <<'JS'
import { dotnet } from "./_framework/dotnet.js";
process.exitCode = await dotnet.run();
JS
# Keep the full log, including loader/runtime errors. pipefail preserves failure.
node "$bundle/run.mjs" 2>&1 | tee "$artifacts/results.txt"
