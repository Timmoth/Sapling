<p align="center">
   <div style="width:640;height:320">
       <img style="width: inherit" src="./sapling-banner.png">
</div>
</p>

### A strong dotnet UCI Chess engine - My leaf nodes are growing

Play it here -> https://iblunder.com/

Or challenge it on Lichess -> https://lichess.org/@/sapling-bot


| Rating pool                                                                   | Version | ELO  |
|-------------------------------------------------------------------------------|---------|------|
| [CCRL 40/15](https://www.computerchess.org.uk/ccrl/4040/rating_list_all.html) | 1.1.8   | 3340 |
| [Lichess Bullet](https://lichess.org/@/Sapling-Bot/perf/bullet)               | 1.2.2   | 2890 |
| [Lichess Blitz](https://lichess.org/@/Sapling-Bot/perf/blitz)                 | 1.2.2   | 2786 |
| [Lichess Rapid](https://lichess.org/@/Sapling-Bot/perf/rapid)                 | 1.2.2   | 2797 |
| [Lichess Chess960](https://lichess.org/@/Sapling-Bot/perf/chess960)           | 1.2.2   | 2209 |

| Tournament                                                                                         | Version | Result           |
|----------------------------------------------------------------------------------------------------|---------|------------------|
| [CCRL 112th Amateur Series Division 5](https://talkchess.com/viewtopic.php?p=969661&hilit=sapling) | 1.1.8   | drawed 3rd place |
| [Blitz Tournament 3'+2" (48th Edition)](https://talkchess.com/viewtopic.php?t=84301&hilit=sapling) | 1.2.0   | TBD              |

## Releases
You can browse all windows, linux or mac releases [here](https://github.com/Timmoth/Sapling/releases)

### Latest Release [v1.2.0 10/10/2024](https://github.com/Timmoth/Sapling/releases/tag/Sapling-1.2.0)

## Requirements
- Sapling makes use of hardware intrinsics to improve performance. Currently your CPU must support: `Avx2`, `Bmi1`, `Bmi2`, `Popcnt`, `Sse`. Most modern hardware shipped after 2013 should be supported.
- The releases come with a bundled version of the dotnet runtime, however if you want to run from source you'll need the dotnet 8 SDK installed.

## Running from source
```bash
cd ./scripts

// Windows
./build_windows_avx256.bat
./build_windows_avx512.bat

// Linux
./build_linux_avx256.sh
./build_linux_avx512.sh

// Osx
./build_osx_avx256.sh
./build_osx_avx512.sh
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
- `datagen` : [Dev] starts generating data in the bullet format, used when training a new NNUE network

## Features

<details>
<Summary>General</Summary>
  
- Bitboards 
- NNUE (768 -> 1024)x2 -> 8
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
- Check extensions
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
- Incremental sorting
- Magic bitboards
- PEXT bitboards
</details>

## SPRT
After any changes to the engine a SPRT test must be ran to ensure that the changes have a positive effect.

There is a script `sprt.bat` which contains the command to run a cutechess-cli SPRT test. Ensure that you've configured CuteChess to point to both `dev` and `base` engines before hand, and also update the opening book + endgame table base to point to one on your system.

## NNUE
I'm in the process of training a (768x8->1024)x2-8 network starting from random weights using self play data generation and bullet trainer. Expect the engine to get much stronger as I improve the network. Check [here](https://github.com/Timmoth/Sapling/tree/main/Sapling.Engine/Resources/WeightsHistory) to see the sequence of networks starting from scratch and the training logs.

## Resources:
- [Chess Programming Wiki](https://www.chessprogramming.org/)
- [Talk Chess Forum](https://talkchess.com/)
- [Coding Adventure](https://www.youtube.com/watch?v=U4ogK0MIzqk)
