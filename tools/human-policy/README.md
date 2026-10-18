# Sapling human move policy

This directory contains the versioned data, training and export pipeline for Sapling's rating-conditioned move policy. Keep large source archives, prepared datasets, checkpoints and intermediate exports outside Git. The checked-in `src/Sapling.Engine/Resources/human_policy_v1.weights` is the compact model used at runtime.

## Current embedded model

The embedded resource is the candidate trained on the 10 GiB Lichess sample. It was evaluated on 36,549 held-out positions and exported from the selected checkpoint with SHA-256 `af76696a6ee68a4cd87919283df143a6c3b513aa3c6a56b614489e6810b0c80f` (1,175,142 bytes). Held-out metrics are NLL 1.8880, top-1 43.75%, and top-3 70.22%; the 1 GiB baseline on the same test split scored NLL 2.1599, top-1 38.81%, and top-3 65.30%. `tests/inference_golden.json` contains reference logits regenerated from that checkpoint and is checked against the embedded runtime by xUnit.

## Repeatable training workflow

Use Python 3.11 or newer. Create an isolated environment, install a PyTorch build compatible with the available CUDA runtime using the official PyTorch selector, then install the project dependencies:

```sh
python3 -m venv .venv
. .venv/bin/activate
python -m pip install --upgrade pip
python -m pip install -r tools/human-policy/requirements.txt
```

Before training, verify CUDA with `python -c 'import torch; print(torch.__version__, torch.cuda.is_available())'`. The trainer requires CUDA. The extractor and dataset tests can run on CPU.

1. Download a Lichess monthly standard PGN archive from <https://database.lichess.org/standard/> and record its checksum. Store it outside the repository, for example as `data/raw/month.pgn.zst`.
2. Extract rated rapid games to deterministic game-level splits. `pzstd` parallelizes decompression; `--workers` and `--batch-size` control parsing. Omit `--max-games` or set it to `0` to scan the full archive. Use a nonzero value for a bounded pilot.

   ```sh
   mkdir -p data/positions
   sha256sum data/raw/month.pgn.zst
   pzstd -d -p 6 -c data/raw/month.pgn.zst \
     | python tools/human-policy/extract_pgn.py \
         --input - --archive-sha256 "$(sha256sum data/raw/month.pgn.zst | cut -d' ' -f1)" \
         --workers 12 --batch-size 64 --seed 1729 --max-games 0 \
         --output data/positions
   ```

   The manifest records the archive name and checksum, filters, sampling limits, split seed and encoder schema. It does not store the local archive path.
3. Attach Sapling's fixed-depth score for every legal move. Set `SAPLING_BIN` to a Sapling build that supports the `rank` command; adjust worker count and depth to available compute.

   ```sh
   python tools/human-policy/analyze_positions.py \
     --input data/positions --engine "$SAPLING_BIN" --depth 8 --workers 12 \
     --output data/engine_scored
   ```

4. Set `dataset_dir` and `output_dir` in `tools/human-policy/config.yaml` to the scored data and desired artifact directory. Train and evaluate on the held-out test split:

   ```sh
   python tools/human-policy/train.py --config tools/human-policy/config.yaml
   python tools/human-policy/evaluate_checkpoint.py \
     --checkpoint artifacts/human-policy/best.pt \
     --dataset-dir data/engine_scored --split test \
     --output artifacts/human-policy/test-report.json
   ```

5. Export the selected checkpoint to the engine's embedded binary format, then run the .NET tests and the Python pipeline tests before updating the checked-in weights.

   ```sh
   python tools/human-policy/export_engine_weights.py \
     --checkpoint artifacts/human-policy/best.pt \
     --output src/Sapling.Engine/Resources/human_policy_v1.weights
   python -m unittest discover -s tools/human-policy/tests -v
   dotnet test src/Sapling.Engine.Tests/Sapling.Engine.Tests.csproj
   ```

Keep the archive checksum, preprocessing commit, extraction options, engine build and analysis depth, training config, test metrics and exported-weight checksum with each run's report. Do not commit raw PGNs, scored datasets or training checkpoints.

## Schema v1

- Squares use `a1=0`, `b1=1`, through `h8=63`; board tensor flattening is `channel * 64 + square`, with row `square // 8` and column `square % 8`.
- Board planes are white P/N/B/R/Q/K, black P/N/B/R/Q/K, side-to-move, white K/Q rights, black K/Q rights, then en-passant target: 18 planes total. En-passant uses the FEN target square even when no legal capture exists.
- The first 132 move features are `from[64] + to[64] + promotion[Q,R,B,N]`. Features 132 and 133 are `tanh(root_score / 1000)` and `tanh(max(0, best_score - root_score) / 1000)`. Scores are fixed-depth root-ranking integers from the side-to-move perspective. Preserve legal candidate order through scoring and selection.
- Ratings are mover-side Lichess rapid ratings in `[800,2200]`, mapped by `(rating - 1500) / 700`. Out-of-range values are rejected by the encoder.
- Schema version is `1`; Chess960 is excluded. `tests/golden_cases.json` covers castling, en-passant, promotions and score normalization for both Python and C# encoders.

`extract_pgn.py` filters standard, rated rapid games, samples positions by 200-point mover-rating band and splits by game. `analyze_positions.py` scores every legal move through Sapling's `rank` command. `dataset.py` validates score coverage and encodes inputs; `HumanPolicyEncoding` is the matching C# implementation. `train.py` writes the best validation checkpoint and metrics; `export_engine_weights.py` produces the compact resource embedded by the engine.
