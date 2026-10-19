#!/usr/bin/env python3
"""Combine Sapling search, BenchmarkDotNet, and optional dotnet-trace outputs."""

import csv
import json
import pathlib
import re
import sys
import zipfile
from collections import defaultdict


def duration(ms):
    if ms >= 1000:
        return f"{ms / 1000:.2f} s"
    if ms >= 1:
        return f"{ms:.2f} ms"
    return f"{ms * 1000:.1f} µs"


def read_benchmarkdotnet(root):
    rows = []
    invocations = {}
    benchmark_pattern = re.compile(r"^// Benchmark: ([\w.]+):")
    actual_pattern = re.compile(r"^WorkloadActual\s+\d+:\s+([\d,]+) op,")
    for log_path in root.glob("BenchmarkRun-*.log"):
        current = None
        counts = defaultdict(list)
        try:
            for line in log_path.read_text(encoding="utf-8", errors="replace").splitlines():
                benchmark_match = benchmark_pattern.match(line)
                if benchmark_match:
                    current = benchmark_match.group(1).rsplit(":", 1)[0]
                    continue
                actual_match = actual_pattern.match(line)
                if current and actual_match:
                    counts[current].append(int(actual_match.group(1).replace(",", "")))
        except OSError:
            continue
        for key, values in counts.items():
            if values:
                invocations[key] = values

    for path in sorted(root.rglob("*.csv")):
        try:
            with path.open(newline="", encoding="utf-8-sig") as source:
                for row in csv.DictReader(source):
                    method = row.get("Method", "").strip()
                    if method:
                        class_name = path.name.split(".")[-2].removesuffix("-report")
                        rows.append((method, row, path.name, invocations.get(f"{class_name}.{method}", [])))
        except (OSError, csv.Error):
            continue
    # A CLI-specified Dry job can coexist with the benchmark's declared job.
    # Prefer the declared measurement when both appear in a report.
    keys_with_measured_job = {
        (source, method)
        for method, row, source, _ in rows
        if row.get("Job", "").strip().lower() != "dry"
    }
    return [
        entry for entry in rows
        if entry[1].get("Job", "").strip().lower() != "dry"
        or (entry[2], entry[0]) not in keys_with_measured_job
    ]


def compact(value):
    return re.sub(r"\s+", " ", str(value or "")).strip()


def main():
    if len(sys.argv) != 5:
        raise SystemExit("usage: summarize_performance.py SEARCH_JSON BDN_DIR OUTPUT_MD [CPU_HOTSPOTS]")

    search_path = pathlib.Path(sys.argv[1])
    bdn_root = pathlib.Path(sys.argv[2])
    output_path = pathlib.Path(sys.argv[3])
    hotspots_path = pathlib.Path(sys.argv[4])
    result = json.loads(search_path.read_text(encoding="utf-8"))
    positions = result.get("positions", [])
    totals = result.get("totals", {})
    memory = result.get("memory", {})
    config = result.get("configuration", {})
    grouped = defaultdict(list)
    for item in positions:
        grouped[(item.get("fen", ""), item.get("tag", ""))].append(item)
    slow_positions = sorted(
        grouped.items(),
        key=lambda pair: sum(p.get("elapsedNs", 0) for p in pair[1]) / max(1, len(pair[1])),
        reverse=True,
    )

    lines = [
        "# Sapling local performance report",
        "",
        f"Generated: {result.get('header', {}).get('utcTimestamp', 'unknown')} UTC",
        f"Source: `{result.get('header', {}).get('build', {}).get('source', {}).get('gitCommit', 'unknown')}` "
        f"(dirty: `{result.get('header', {}).get('build', {}).get('source', {}).get('gitDirty', 'unknown')}`)",
        f"Machine: {compact(result.get('header', {}).get('machine', {}).get('os'))}; "
        f"{result.get('header', {}).get('machine', {}).get('cpuCount', '?')} logical CPUs; "
        f".NET {result.get('header', {}).get('runtime', {}).get('framework', '?')}",
        "",
        "## Search workload",
        "",
        f"Fixed corpus: {len(grouped)} positions, depth {config.get('depth', '?')}, "
        f"{config.get('repeat', '?')} repeats; TT reset `{config.get('ttReset', '?')}`, "
        f"engine `{config.get('engine', '?')}`.",
        f"Total: {totals.get('nodes', 0):,} nodes in {duration(totals.get('elapsedNs', 0) / 1_000_000)} "
        f"({totals.get('nps', 0):,} nodes/s). Deterministic across repeats: "
        f"`{totals.get('deterministicAcrossRepeats', 'unknown')}`.",
        "",
        "Slowest positions by average elapsed time (use these to choose focused optimization work):",
        "",
        "| Rank | Corpus tag | FEN | Average time | Average nodes | Best move |",
        "|---:|---|---|---:|---:|---|",
    ]
    for rank, ((_, tag), samples) in enumerate(slow_positions[:15], 1):
        avg_ns = sum(s.get("elapsedNs", 0) for s in samples) / len(samples)
        avg_nodes = sum(s.get("nodes", 0) for s in samples) / len(samples)
        best_move = samples[-1].get("bestMove", "")
        fen = samples[-1].get("fen", "")
        lines.append(f"| {rank} | {compact(tag) or '(untagged)'} | `{fen}` | {duration(avg_ns / 1_000_000)} | {avg_nodes:,.0f} | `{best_move}` |")

    allocations_by_repeat = defaultdict(int)
    nodes_by_repeat = defaultdict(int)
    for item in positions:
        allocations_by_repeat[item.get("rep", 0)] += item.get("managedAllocatedBytes", 0)
        nodes_by_repeat[item.get("rep", 0)] += item.get("nodes", 0)
    first_repeat = min(allocations_by_repeat, default=0)
    steady_allocated = sum(value for rep, value in allocations_by_repeat.items() if rep != first_repeat)
    steady_nodes = sum(value for rep, value in nodes_by_repeat.items() if rep != first_repeat)
    steady_bpn = steady_allocated / steady_nodes if steady_nodes else totals.get("managedBytesPerNode", 0)
    steady_samples = defaultdict(list)
    for item in positions:
        if item.get("rep", 0) != first_repeat or len(allocations_by_repeat) == 1:
            steady_samples[(item.get("fen", ""), item.get("tag", ""))].append(item)
    allocation_positions = sorted(
        steady_samples.items(),
        key=lambda pair: sum(p.get("managedAllocatedBytes", 0) for p in pair[1]) / max(1, len(pair[1])),
        reverse=True,
    )
    cold_positions = sorted(
        (item for item in positions if item.get("rep", 0) == first_repeat),
        key=lambda item: item.get("managedAllocatedBytes", 0),
        reverse=True,
    )
    total_allocated = totals.get("managedAllocatedBytes", 0)
    elapsed_seconds = totals.get("elapsedNs", 0) / 1_000_000_000
    managed_allocation_rate = total_allocated / elapsed_seconds if elapsed_seconds > 0 else 0
    lines += [
        "",
        "## Managed allocations and memory pressure",
        "",
        f"Across all repeats, search calls allocated **{total_allocated:,} managed bytes** on the calling thread "
        f"({totals.get('managedBytesPerNode', 0):.3f} B/node; {managed_allocation_rate:,.0f} B/s). "
        f"The first corpus pass allocated {allocations_by_repeat.get(first_repeat, 0):,} B; later passes "
        f"allocated {steady_allocated:,} B total ({steady_bpn:.4f} B/node), a steadier estimate after "
        f"first-use JIT/runtime work. "
        f"Observed Gen 0/1/2 collections during search: "
        f"{totals.get('gen0CollectionsDuringSearch', 0)}/"
        f"{totals.get('gen1CollectionsDuringSearch', 0)}/"
        f"{totals.get('gen2CollectionsDuringSearch', 0)}.",
        f"Process working set: {memory.get('workingSetBeforeBytes', 0) / 1024 / 1024:.1f} MiB before corpus, "
        f"{memory.get('workingSetAfterBytes', 0) / 1024 / 1024:.1f} MiB after; highest sampled working set "
        f"{memory.get('sampledPeakWorkingSetBytes', 0) / 1024 / 1024:.1f} MiB. The benchmark reserves a "
        f"{config.get('ttMb', '?')} MiB transposition table, allocated outside the managed heap.",
        f"Approximate managed heap (`GC.GetTotalMemory(false)`): "
        f"{memory.get('managedHeapBeforeBytes', 0) / 1024 / 1024:.1f} MiB before, "
        f"{memory.get('managedHeapAfterBytes', 0) / 1024 / 1024:.1f} MiB after.",
        "",
        "Steady-state per-position allocations (first corpus pass excluded):" if len(allocations_by_repeat) > 1
        else "Per-position allocations (single pass; no warmup):",
        "",
        "| Rank | Corpus tag | FEN | Average managed bytes/search | Average bytes/node |",
        "|---:|---|---|---:|---:|",
    ]
    for rank, ((_, tag), samples) in enumerate(allocation_positions[:10], 1):
        avg_bytes = sum(p.get("managedAllocatedBytes", 0) for p in samples) / len(samples)
        avg_nodes = sum(p.get("nodes", 0) for p in samples) / len(samples)
        fen = samples[-1].get("fen", "")
        lines.append(f"| {rank} | {compact(tag) or '(untagged)'} | `{fen}` | {avg_bytes:,.0f} B | {avg_bytes / max(1, avg_nodes):.3f} B/node |")
    if len(allocations_by_repeat) > 1:
        lines += [
            "",
            "First-pass allocation outliers (keep separate from steady-state work):",
            "",
            "| Rank | Corpus tag | FEN | Managed bytes/search | Bytes/node |",
            "|---:|---|---|---:|---:|",
        ]
        for rank, item in enumerate(cold_positions[:5], 1):
            allocated = item.get("managedAllocatedBytes", 0)
            node_count = item.get("nodes", 0)
            fen = item.get("fen", "")
            tag = item.get("tag", "")
            lines.append(f"| {rank} | {compact(tag) or '(untagged)'} | `{fen}` | {allocated:,} B | {allocated / max(1, node_count):.3f} B/node |")
        lines.append(
            "The first position allocated much more than its repeats; treat this as a first-use runtime/JIT or "
            "initialization investigation. This counter does not attribute those bytes to a method or type."
        )
    lines += [
        "",
        f"Collection deltas over full process lifetime: Gen 0 {memory.get('gen0CollectionsBefore', 0)} → "
        f"{memory.get('gen0CollectionsAfter', 0)}, Gen 1 {memory.get('gen1CollectionsBefore', 0)} → "
        f"{memory.get('gen1CollectionsAfter', 0)}, Gen 2 {memory.get('gen2CollectionsBefore', 0)} → "
        f"{memory.get('gen2CollectionsAfter', 0)}.",
        "The managed allocation counter covers the synchronous one-worker search API on its calling thread. "
        "It excludes native allocations and cannot count allocations on additional worker threads. "
        "Working set is a process snapshot, not a per-position peak.",
    ]

    bdn_rows = read_benchmarkdotnet(bdn_root)
    lines += ["", "## Microbenchmarks and allocations", ""]
    if bdn_rows:
        lines += [
            "BenchmarkDotNet measurements are the best place to compare kernel-level improvement. "
            "Methods have different workloads, so compare a method against its own later runs.",
            "",
            "| Benchmark | Mean | Allocated/op | Gen0/1k ops | Invocations/iteration | Iterations | Source report |",
            "|---|---:|---:|---:|---|---:|---|",
        ]
        for method, row, source, invocation_counts in bdn_rows:
            mean = compact(row.get("Mean", "")) or compact(row.get("Mean (ns)", "")) or "—"
            allocated = compact(row.get("Allocated", "")) or compact(row.get("Allocated B", "")) or "—"
            gen0 = compact(row.get("Gen0", "")) or "—"
            if invocation_counts:
                unique_counts = sorted(set(invocation_counts))
                actual_invocations = f"{unique_counts[0]:,}" if len(unique_counts) == 1 else f"{min(unique_counts):,}–{max(unique_counts):,}"
            else:
                actual_invocations = "not logged"
            iterations = compact(row.get("IterationCount", "")) or "—"
            warmups = compact(row.get("WarmupCount", "")) or "—"
            lines.append(f"| `{method}` | {mean} | {allocated} | {gen0} | {actual_invocations} | {iterations} (+{warmups} warmups) | `{source}` |")
    else:
        lines.append("No BenchmarkDotNet CSV files were found. Check the runner output under `benchmarkdotnet/`.")

    lines += [
        "",
        "Benchmark method invocation workload (one BenchmarkDotNet operation is one method call):",
        "",
        "| Method | Known inner work per method invocation |",
        "|---|---|",
        "| `MoveGenBench.LegalAll` | 6 FEN resets and 6 full legal move generations |",
        "| `MoveGenBench.LegalCaptures` | 4 FEN resets and 4 capture-only legal move generations |",
        "| `NnueEvalBench.FullEval` | 6 FEN resets and 6 cold NNUE evaluations |",
        "| `SeeBench.SeeAllCaptures` | 4 tactical positions; SEE call count equals total legal captures across those positions |",
        "| `TtBench.StoreAndProbe` | 4,096 transposition stores and 4,096 probes |",
        "| `SearchBench.SearchFixedPosition` | One depth-10 search of a fixed hard position, after a 64 MiB TT and correction-history reset |",
        "",
        "Search `nodes` are exact recursive search/quiescence node visits for the fixed corpus. Invocation counts "
        "above are parsed from BenchmarkDotNet's measured `WorkloadActual` log lines. The CSV files retain the "
        "job settings and raw statistics.",
        "Fixture-based kernel benchmarks include FEN reset/parsing for every listed position, so their allocation "
        "totals include fixture preparation as well as the named kernel. `SearchBench.SearchFixedPosition` isolates "
        "the production search API more directly.",
    ]

    lines += [
        "",
        "## Code investigation queue",
        "",
        "These are source-inspection candidates, not profiler-confirmed hot spots. Use the dynamic sections "
        "above to decide which to investigate first.",
        "",
        "| Priority | Area | Evidence to inspect | Candidate follow-up |",
        "|---|---|---|---|",
        "| 1 | Per-search managed allocations | `src/Sapling.Engine/Search/ParallelSearcher.cs:176,187`; allocates a worker array and result array per search. `src/Sapling.Engine/Search/Searcher.cs:302,574`; materializes the returned PV as a new `List<uint>`. | Use the per-search allocated-byte measurement to estimate cost at real UCI search frequency; consider reusable storage only if it is material. |",
        "| 2 | Search output work | `src/Sapling.Engine/Search/Searcher.cs:517-534` builds progress text and formats PV moves on completed iterations when progress reporting is enabled. | Compare protocol-visible searches against the silent qualification harness; separate formatting/console time from search time. |",
        "| 3 | Node-level CPU | `src/Sapling.Engine/Search/NegaMaxSearch.cs` and `QuiescenceSearch.cs` use stack-local move/score/SEE buffers and recursive calls; current stack storage avoids managed arrays. | Use a sampled thread-time trace on a longer single-thread corpus and inspect exclusive time in evaluation, move scoring, SEE, and TT paths before changing algorithms. |",
        "| 4 | Non-managed memory | Search state, accumulators, histories, and the transposition table use aligned native allocations; `ttActualBytes` gives the table size but not resident/peak usage. | Compare working set across hash sizes and thread counts; collect OS-level peak/RSS and runtime committed heap for production-like sessions. |",
        "| 5 | Cold initialization | `src/Sapling.Engine/Evaluation/NnueWeights.cs:60-102` loads and transposes managed weight arrays before copying to aligned native storage. | Profile startup separately from steady-state search; avoid mixing one-time setup with per-move allocation rates. |",
    ]

    lines += ["", "## Sampled thread-time hotspots", ""]
    if hotspots_path.is_file():
        lines += [
            "`dotnet-trace`'s `dotnet-sampled-thread-time` profile samples managed thread stacks to estimate thread time. "
            "The trace is a separate one-pass `--engine raw` run at the corpus depth, isolating the single-worker search path. "
            "Percentages are sample shares, not absolute CPU time; idle and waiting threads can dominate. "
            "Hot methods suggest where to inspect; they do not by themselves prove an optimization will help.",
            "",
            "```text",
            *hotspots_path.read_text(encoding="utf-8", errors="replace").strip().splitlines(),
            "```",
        ]
    else:
        lines += [
            "Thread-time sampling was skipped because `dotnet-trace` output was unavailable. Install `dotnet-trace` "
            "to include a sampled profile. The current BenchmarkDotNet 0.14 setup also rejected EventPipe profiling "
            "for this .NET 11 preview target.",
        ]

    lines += [
        "",
        "## Artifacts",
        "",
        f"- Search measurements: `{search_path.name}`",
        "- BenchmarkDotNet reports and raw CSV/JSON: `benchmarkdotnet/`",
        "- Ranked optimization tasks and acceptance criteria: `optimization-opportunities.md`",
        "- Shareable bundle: `performance-report.zip`",
    ]
    if hotspots_path.is_file():
        lines.append("- Sampled thread-time trace: `search-thread-time.nettrace`; text summary: `thread-time-hotspots.txt`")
    lines += [
        "",
        "This report ranks investigation targets. Validate candidate changes with repeated local runs "
        "and the engine's SPRT strength test before adopting them.",
        "",
    ]
    output_path.parent.mkdir(parents=True, exist_ok=True)
    output_path.write_text("\n".join(lines), encoding="utf-8")
    package_path = output_path.with_suffix(".zip")
    package_root = search_path.parent
    with zipfile.ZipFile(package_path, "w", compression=zipfile.ZIP_DEFLATED) as package:
        for path in sorted(package_root.rglob("*")):
            if path.is_file() and path != package_path:
                package.write(path, path.relative_to(package_root))


if __name__ == "__main__":
    main()
