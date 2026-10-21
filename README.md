<p align="center">
   <div style="width:640;height:320">
       <img style="width: inherit" src="./sapling-banner.png">
</div>
</p>

### Sapling v2.0.0 — a .NET UCI chess engine

Play it here -> https://iblunder.com/

Or challenge it on Lichess -> https://lichess.org/@/sapling-bot


Historical results below apply to the listed versions, not v2.0.0.

| Rating pool                                                                   | Version | ELO  |
|-------------------------------------------------------------------------------|---------|------|
| [CCRL 40/15](https://www.computerchess.org.uk/ccrl/4040/rating_list_all.html) | 1.1.2   | 3378 |
| [Lichess Bullet](https://lichess.org/@/Sapling-Bot/perf/bullet)               | 1.2.2   | 2890 |
| [Lichess Blitz](https://lichess.org/@/Sapling-Bot/perf/blitz)                 | 1.2.2   | 2786 |
| [Lichess Rapid](https://lichess.org/@/Sapling-Bot/perf/rapid)                 | 1.2.2   | 2797 |
| [Lichess Chess960](https://lichess.org/@/Sapling-Bot/perf/chess960)           | 1.2.2   | 2209 |

| Tournament                                                                                         | Version | Result           |
|----------------------------------------------------------------------------------------------------|---------|------------------|
| [CCRL 112th Amateur Series Division 5](https://talkchess.com/viewtopic.php?p=969661&hilit=sapling) | 1.1.8   | drawed 3rd place |
| [Blitz Tournament 3'+2" (48th Edition)](https://talkchess.com/viewtopic.php?t=84301&hilit=sapling) | 1.2.0   | Division 4 6th place              |
| [FRC Tournament (35th Edition)](https://talkchess.com/viewtopic.php?p=970724&hilit=sapling#p970724) | 1.2.2   | TBD              |

## Releases
You can browse all windows, linux or mac releases [here](https://github.com/Timmoth/Sapling/releases)

### v2.0.0 (release preparation)

The release tag will be `Sapling-2.0.0`. Version metadata is shared in [`src/Directory.Build.props`](src/Directory.Build.props); builds and publication are defined by the [release workflow](.github/workflows/release-pipeline.yaml). Published downloads are available on the [releases page](https://github.com/Timmoth/Sapling/releases).

The combined v2 build is being measured against v1.2.7 in a fixed 40,000-game match. The Elo difference will be reported with its confidence interval after that match completes.

## Requirements
- Use an executable matching your OS and architecture. The normal build selects AVX2, Vector128 or scalar NNUE at runtime and uses magic bitboards when BMI2/PEXT is unavailable. Dedicated AVX512 builds select that backend when supported; explicitly forcing `SAPLING_BACKEND=Avx512` requires AVX512F and AVX512BW.
- Self-contained release binaries include the .NET runtime. Source builds currently require .NET SDK **11.0.100-rc.2.26504.105**, pinned in `global.json`. This is a release candidate SDK; CI installs its full version number.

## Running from source

From the repository root:

```sh
dotnet restore src/Sapling.sln
dotnet run --project src/Sapling/Sapling.csproj --configuration Release
```

For self-contained Windows, Linux, Intel macOS and Apple Silicon builds, see the [release workflow](.github/workflows/release-pipeline.yaml). Existing platform helper scripts are in `Scripts/`.

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

## NNUE
v2.0.0 embeds the **sapling35-600** network, trained with Bullet. It uses SCReLU, four king-input buckets, a 1024-wide accumulator per perspective and eight output buckets. Tiled accumulator refreshes reduce repeated memory traffic. The network is embedded in the executable, so a new network requires rebuilding the engine.

## Resources:
- [Chess Programming Wiki](https://www.chessprogramming.org/)
- [Talk Chess Forum](https://talkchess.com/)
- [Coding Adventure](https://www.youtube.com/watch?v=U4ogK0MIzqk)
