#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$repo_root"
# The local restore is intentionally offline-friendly; NuGet vulnerability audit
# otherwise turns unavailable network access into a build error in generated BDN projects.
export NuGetAudit=false

output_dir="${1:-artifacts/performance/$(date -u +%Y%m%dT%H%M%SZ)}"
if [[ "$output_dir" != /* ]]; then
  output_dir="$repo_root/$output_dir"
fi
mkdir -p "$output_dir"

depth="${SAPLING_PERF_DEPTH:-14}"
repeat="${SAPLING_PERF_REPEAT:-3}"
bench_dll="$repo_root/src/Sapling.Bench/bin/Release/net11.0/Sapling.Bench.dll"
trace_tool="${DOTNET_TRACE_PATH:-}"
if [[ -z "$trace_tool" ]]; then
  trace_tool="$(command -v dotnet-trace || true)"
fi
if [[ -z "$trace_tool" && -x "/private/tmp/sapling-profiler-tools/dotnet-trace" ]]; then
  trace_tool="/private/tmp/sapling-profiler-tools/dotnet-trace"
fi
if [[ -z "$trace_tool" ]]; then
  echo "dotnet-trace is required for this report. Install it with: dotnet tool install --tool-path /private/tmp/sapling-profiler-tools dotnet-trace" >&2
  exit 1
fi

echo "Building the search harness and microbenchmarks..."
dotnet build src/Sapling.Bench/Sapling.Bench.csproj --configuration Release -p:NuGetAudit=false
dotnet build src/Sapling.Benchmarks/Sapling.Benchmarks.csproj --configuration Release -p:NuGetAudit=false

echo "Running the fixed-position search corpus (depth $depth, $repeat repeats)..."
dotnet "$bench_dll" bench \
  --out "$output_dir/search.json" \
  --depth "$depth" \
  --repeat "$repeat" \
  --tt-reset fresh \
  --strict-reset true \
  --engine session

echo "Running BenchmarkDotNet microbenchmarks..."
bdn_args=(
  --artifacts "$output_dir/benchmarkdotnet/"
  --filter '*'
)
dotnet run --project src/Sapling.Benchmarks/Sapling.Benchmarks.csproj \
  --configuration Release --no-build -- \
  "${bdn_args[@]}"

echo "Collecting a separate sampled thread-time profile with $trace_tool..."
"$trace_tool" collect \
  --profile dotnet-sampled-thread-time \
  --format NetTrace \
  --output "$output_dir/search-thread-time.nettrace" \
  -- dotnet "$bench_dll" bench \
    --out "$output_dir/profiled-search.json" \
    --depth "$depth" \
    --repeat 1 \
    --tt-reset fresh \
    --strict-reset true \
    --engine raw
"$trace_tool" report "$output_dir/search-thread-time.nettrace" topN \
  -n 40 --inclusive > "$output_dir/thread-time-hotspots.txt"

python3 Scripts/summarize_performance.py \
  "$output_dir/search.json" \
  "$output_dir/benchmarkdotnet" \
  "$output_dir/performance-report.md" \
  "$output_dir/thread-time-hotspots.txt"

echo "Performance report: $output_dir/performance-report.md"
