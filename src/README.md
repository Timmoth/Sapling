### Sapling v2.0.0 — a .NET UCI chess engine

Play it here -> https://iblunder.com/

Or challenge it on Lichess -> https://lichess.org/@/sapling-bot

## Releases
You can browse all windows, linux or mac releases [here](https://github.com/Timmoth/Sapling/releases)

## v2.0.0

The planned release tag is `Sapling-2.0.0`. Version metadata is shared in [`Directory.Build.props`](Directory.Build.props); see the [release workflow](../.github/workflows/release-pipeline.yaml).

## Requirements
- Use an executable matching your OS and architecture. The normal build selects AVX2, Vector128 or scalar NNUE at runtime and uses magic bitboards when BMI2/PEXT is unavailable. Dedicated AVX512 builds select that backend when supported; explicitly forcing `SAPLING_BACKEND=Avx512` requires AVX512F and AVX512BW.
- Self-contained release binaries include the .NET runtime. Source builds currently require .NET SDK **11.0.100-rc.2.26504.105**, pinned in `global.json`. This is a release candidate SDK; CI installs its full version number.

## Running from source
```bash
dotnet run --project .\Sapling\Sapling.csproj --configuration Release
```

## Commands
- `quit` : exit the program
- `setoption name threads value 8` : sets the number of threads to use
- `ucinewgame` : initializes a new game
- `position startpos` : sets the engine to the starting chess position
- `position startpos moves a2a3 a7a6` : sets the engine to the starting position then applies a set of moves
- `position fen rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1` : sets the engine to the position given by the fen string
- `d` : outputs a diagram of the current position
- `go perft 6` : Runs a pert test to a specific depth
- `go depth 10` : Returns the best move after searching for the given depth
- `go wtime 10000 btime 10000 winc 100 binc 100` : returns the best move after searching with the given time control
- `go see a2a3` : [Dev] returns a the result of static exchange evaluation for a given move
- `go eval` : [Dev] returns the static evaluation of the current position
- `datagen` : [Dev] starts generating data as viriformat binpacks (`out-worker<N>.binpack`), used when training a new NNUE network

## Features

<details>
<Summary>General</Summary>
  
- Bitboards 
- SCReLU NNUE: (768 × 4 input buckets → 1024) × 2 perspectives → 8 output buckets
- Horizontal mirroring
- Output buckets x8
- Transposition table
- Lazy SMP
- Pondering
</details>

<details>
<Summary>Search</Summary>
  
- Negamax
- Quiescence
- Alpha-Beta pruning
- Iterative Deepening
- Asperation windows
- Null move pruning
- Late Move Pruning
- Futility Pruning
- Razoring
- Principal Variation Search
- Check, singular, double and negative extensions
- Internal Iterative Reduction
- Late Move Reductions
- Cuckoo filter repetition detection
</details>

<details>
<Summary>Move generation / ordering</Summary>

- Pseudo-legal movegen
- Static exchange evaluation
- Killer move heuristic
- Counter move heuristic
- History heuristic with malus
- Threat history and compact continuation history
- Incremental sorting
- Magic bitboards
- PEXT bitboards
</details>

## SPRT
After any changes to the engine a SPRT test must be ran to ensure that the changes have a positive effect.

Run playing-strength SPRTs with CuteChess CLI using fixed engine builds, time control, and opening set. Record the command and result with the experiment.

## Local performance report
Run `../Scripts/performance_report.sh` from `src/` or `Scripts/performance_report.sh` from the repository root to build and run the fixed-position search corpus and BenchmarkDotNet microbenchmarks. The command writes a Markdown report plus raw JSON, CSV, and BenchmarkDotNet artifacts under `artifacts/performance/`; pass a directory path to choose a different output location.

The report ranks the slowest corpus positions, measures managed allocations and GC counts around each single-worker search, snapshots process working set, and shows microbenchmark timing, allocation, and invocation settings. Each run also creates a ZIP bundle with the report and raw artifacts for sharing. Install [`dotnet-trace`](https://learn.microsoft.com/dotnet/core/diagnostics/dotnet-trace) (`dotnet tool install --global dotnet-trace`) to collect a sampled thread-time profile and top-method summary from a separate single-worker search. The profile is directional evidence, not an absolute CPU-time measurement. Set `SAPLING_PERF_DEPTH` and `SAPLING_PERF_REPEAT` to adjust search depth and repeat count. These are local investigation tools and are not part of the CI runners.

## NNUE
v2.0.0 embeds the **sapling35-600** network, trained with Bullet. It uses SCReLU, four king-input buckets, a 1024-wide accumulator per perspective and eight output buckets. Tiled accumulator refreshes reduce repeated memory traffic. The network is embedded in the executable, so a new network requires rebuilding the engine.

## Resources:
- [Chess Programming Wiki](https://www.chessprogramming.org/)
- [Talk Chess Forum](https://talkchess.com/)
- [Coding Adventure](https://www.youtube.com/watch?v=U4ogK0MIzqk)

## Optional build configurations

The default build uses the production search settings. Pass
`-p:SaplingBuildConstants=OpenBench` for the mutable tuning interface or
`-p:SaplingBuildConstants=AVX512` for the x86 AVX512 backend. Separate multiple
symbols with `%3B`. Use separate build directories for different configurations.
An AVX512 artifact requires compatible hardware when explicitly selected with
`SAPLING_BACKEND=Avx512`; keep the default artifact for other machines.
