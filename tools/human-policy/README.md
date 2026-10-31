# Sapling human move policy

The shared engine embeds `src/Sapling.Engine/Resources/human_policy_v1.weights`; native and browser
builds require no external model file or inference service. Human Elo supports 500–2200; zero disables it.

## Current model

The model uses 1,025,387 July 2026 Lichess rated rapid positions. Its latest two-epoch fine-tune
added 15,623 game-disjoint positions below 800 and used rating-balanced sampling.
Resource SHA-256: `b075580ede7c87f7bb1375189d0d894d969e02579ed346efd3f0b116bc9aee87`.

On 9,000 separate September games, compared with the preceding runtime:

| Metric | Previous | Current |
|---|---:|---:|
| NLL | 2.00581 | 1.97440 |
| Brier score | 0.74130 | 0.73299 |
| Top-choice agreement | 41.33% | 41.32% |
| Sampled exact-match | 29.73% | 28.47% |
| Direct-capture risk mass | 5.98% | 8.36% |
| >=300 cp regret mass | 12.12% | 15.26% |

NLL and Brier improve with game-cluster 95% confidence intervals excluding zero. Recorded human
risk and large-regret rates were 8.80% and 17.22%, respectively. The broader policy fits these human
frequencies better, with lower sampled exact-match as an explicit tradeoff. These signals do not
establish that every individual mistake looks human.

Maia calibration matches gave score-equivalent gaps of approximately −74 to +92 Elo across nine
settings, versus +63 to +288 previously. Each point used 100 games and 50 color-swapped opening pairs
on a reused book; these are calibration estimates, not independently verified playing ratings.
Archives, checkpoints, and detailed experiment records remain on the evaluation host outside this repository.

## Runtime calibration

[`HumanPlayCalibration.cs`](../../src/Sapling.Engine/Tuning/HumanPlayCalibration.cs) contains the
model-bound rating, temperature and risk-penalty anchors. Settings interpolate at each exact rating.
A checksum mismatch bypasses calibration. [`DirectCaptureRisk.cs`](../../src/Sapling.Engine/Tuning/DirectCaptureRisk.cs)
flags an immediate legal capture of a minor-or-better piece without an immediate recapture; it does
not assess full material compensation or justified sacrifices.

## Evaluating changes

[`compare_checkpoints.py`](compare_checkpoints.py) compares two checkpoints on scored validation or
test positions using exact-rating interpolation from [`runtime_calibration.py`](runtime_calibration.py).
It reports likelihood, Brier score, top-k and sampled agreement, engine-regret tails, observed human
risk rates, and game-cluster bootstrap intervals by rating band. Run `--help` for checkpoint, dataset,
calibration and risk-map options. A calibration JSON contains `calibration_anchors` with
`human_rating_band`, `display_rating`, `model_rating`, and `temperature`; a risk-map JSON contains
`penalties` with `human_rating_band` and `risk_penalty`.

Select changes on validation data and retain a separate game-disjoint evaluation window. Compare
actual human error frequencies instead of minimizing every mistake. September was excluded from
training; previously consulted test windows should not be treated as untouched for future selection.
Current training data used the earlier both-player rating restriction; the extractor now retains
in-range movers against out-of-range opponents.

## Repeatable training workflow

Use Python 3.11 or newer. Create an isolated environment, install a PyTorch build compatible with the available CUDA runtime using the official PyTorch selector, then install the project dependencies:

```sh
python3 -m venv .venv
. .venv/bin/activate
python -m pip install --upgrade pip
python -m pip install -r tools/human-policy/requirements.txt
```

Before training, verify the available device with `python -c 'import torch; print(torch.__version__, torch.cuda.is_available())'`. The trainer supports CPU and CUDA; the extractor and dataset tests can run on CPU.

1. Download a Lichess monthly standard PGN archive from <https://database.lichess.org/standard/> and record its checksum. Store it outside the repository, for example as `data/raw/month.pgn.zst`.
2. Extract rated rapid games to deterministic game-level splits. `pzstd` parallelizes decompression; `--workers` and `--batch-size` control parsing. Omit `--max-games` or set it to `0` to scan the full archive. Use a nonzero value for a bounded pilot. To evaluate a later archive-order window, set `--skip-games N` with a nonzero `--max-games`; the manifest records both bounds and the sampled positions' game-date range.

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
- Ratings are mover-side Lichess rapid ratings in `[500,2200]`, mapped by `(rating - 1500) / 700`. Out-of-range values are rejected by the encoder. The checked-in candidate is calibrated across 500–2200, with an additional 15,623 game-disjoint low-rating positions and rating-balanced sampling in its latest fine-tune.
- Schema version is `1`; Chess960 is excluded. `tests/golden_cases.json` covers castling, en-passant, promotions and score normalization for both Python and C# encoders.

`extract_pgn.py` filters standard, rated rapid games, samples positions by 200-point mover-rating band and splits by game. `analyze_positions.py` scores every legal move through Sapling's `rank` command. `dataset.py` validates score coverage and encodes inputs; `HumanPolicyEncoding` is the matching C# implementation. `train.py` writes the best validation checkpoint and metrics; `export_engine_weights.py` produces the compact resource embedded by the engine.
