#!/usr/bin/env python3
"""Paired timing analysis for Sapling perf experiments.

Usage: analyze_timing.py [--allow-backend-diff] A-01.json B-01.json ...
Pairs are consecutive A,B files. Before any ratio is calculated, every file is
validated: mandatory row fields, duplicate/missing rows rejected, elapsedNs
must be positive, and configuration must match across all files (corpus name,
corpus hashes, depth, repetition set, tt-reset, strict-reset, workers, timed
region). A and B must declare different engine binaries (engineSha256); the
harness may be identical. Backend (nnueBackend) must match unless
--allow-backend-diff is given for a backend comparison.

For each invocation, sums elapsedNs over repetitions 1..max (rep 0 is warmup,
excluded) and all positions. Per pair: speedup = baseline_elapsed /
candidate_elapsed - 1. Reports every pair, median, spread, 95% bootstrap CI
resampling whole pairs, per-pair startup (rep 0) trend, and per-position
aggregates across ALL pairs.
"""
import json
import statistics
import sys

WARMUP_REP = 0
MANDATORY_ROW = ("rep", "fen", "elapsedNs", "nodes")
CONFIG_KEYS = ("corpus", "depth", "ttReset", "strictReset", "workers", "timedRegion")


def load_validated(path):
    d = json.load(open(path))
    rows = d.get("positions")
    if not rows:
        raise SystemExit(f"{path}: no positions rows")
    seen = set()
    for r in rows:
        missing = [f for f in MANDATORY_ROW if f not in r]
        if missing:
            raise SystemExit(f"{path}: row missing fields {missing}")
        key = (r["rep"], r["fen"])
        if key in seen:
            raise SystemExit(f"{path}: duplicate row rep={key[0]} fen={key[1]}")
        seen.add(key)
        if not isinstance(r["elapsedNs"], int) or r["elapsedNs"] <= 0:
            raise SystemExit(f"{path}: nonpositive elapsedNs rep={key[0]} fen={key[1]}")
    reps = {r["rep"] for r in rows}
    fens = {r["fen"] for r in rows}
    for fen in fens:
        if {(r["rep"], r["fen"]) for r in rows if r["fen"] == fen} != {(x, fen) for x in reps}:
            raise SystemExit(f"{path}: inconsistent repetition coverage for fen={fen[:50]}")
    return d


def config_of(d):
    c = d.get("configuration", {})
    return {k: c.get(k) for k in CONFIG_KEYS}, tuple(d["header"].get("corpusHashes", []))


def invocation_stats(d):
    rows = d["positions"]
    warm = sum(r["elapsedNs"] for r in rows if r["rep"] == WARMUP_REP)
    timed = sum(r["elapsedNs"] for r in rows if r["rep"] > WARMUP_REP)
    per_pos = {}
    for r in rows:
        if r["rep"] > WARMUP_REP:
            per_pos[r["fen"]] = per_pos.get(r["fen"], 0) + r["elapsedNs"]
    return timed, warm, per_pos


def bootstrap_ci(values, n=20000, seed=20261012):
    import random
    rng = random.Random(seed)
    meds = sorted(statistics.median([rng.choice(values) for _ in values]) for _ in range(n))
    return meds[int(0.025 * n)], meds[int(0.975 * n)]


def main():
    args = sys.argv[1:]
    allow_backend = "--allow-backend-diff" in args
    paths = [p for p in args if not p.startswith("--")]
    if len(paths) % 2 or len(paths) < 4:
        print(__doc__)
        return 2
    pairs = list(zip(paths[0::2], paths[1::2]))

    docs = {}
    for p in paths:
        docs[p] = load_validated(p)

    ref_cfg = None
    for p in paths:
        cfg, hashes = config_of(docs[p])
        if any(v is None for v in cfg.values()) or not hashes:
            raise SystemExit(f"{p}: missing configuration fields or corpus hashes: {cfg}")
        if ref_cfg is None:
            ref_cfg = (cfg, hashes)
        elif (cfg, hashes) != ref_cfg:
            raise SystemExit(f"{p}: configuration mismatch vs first file: {cfg} vs {ref_cfg[0]}")
    for a, b in pairs:
        ea = docs[a]["header"]["build"]["engineSha256"]
        eb = docs[b]["header"]["build"]["engineSha256"]
        if ea == eb:
            raise SystemExit(f"pair {a}/{b}: identical engineSha256 — baseline and candidate "
                             f"identities must differ as declared")
        ba, bb = docs[a]["header"]["build"]["nnueBackend"], docs[b]["header"]["build"]["nnueBackend"]
        if ba != bb and not allow_backend:
            raise SystemExit(f"pair {a}/{b}: backend differs ({ba} vs {bb}); "
                             f"backend comparisons require --allow-backend-diff")

    speedups = []
    print(f"{'pair':>4} {'base_s':>9} {'cand_s':>9} {'speedup':>9} {'warmA_s':>8} {'warmB_s':>8}")
    pos_acc = {}
    warm_a, warm_b = [], []
    for i, (a, b) in enumerate(pairs, 1):
        ta, wa, pa = invocation_stats(docs[a])
        tb, wb, pb = invocation_stats(docs[b])
        s = ta / tb - 1
        speedups.append(s)
        warm_a.append(wa)
        warm_b.append(wb)
        for fen in pa:
            acc = pos_acc.setdefault(fen, [0, 0])
            acc[0] += pa[fen]
            acc[1] += pb[fen]
        print(f"{i:>4} {ta/1e9:9.3f} {tb/1e9:9.3f} {100*s:8.3f}% {wa/1e9:8.3f} {wb/1e9:8.3f}")
    med = statistics.median(speedups)
    lo, hi = bootstrap_ci(speedups)
    print(f"\npairs={len(pairs)} median speedup={100*med:.3f}% "
          f"min={100*min(speedups):.3f}% max={100*max(speedups):.3f}% "
          f"95% bootstrap CI=[{100*lo:.3f}%, {100*hi:.3f}%]")
    print(f"startup (rep0) trend: A median={statistics.median(warm_a)/1e9:.3f}s "
          f"B median={statistics.median(warm_b)/1e9:.3f}s "
          f"(A first={warm_a[0]/1e9:.3f}s last={warm_a[-1]/1e9:.3f}s; "
          f"B first={warm_b[0]/1e9:.3f}s last={warm_b[-1]/1e9:.3f}s)")
    regs = [(fen, 100 * (acc[1] / acc[0] - 1)) for fen, acc in pos_acc.items()
            if acc[1] / acc[0] > 1.02]
    if regs:
        print(f"\nper-position regressions >2% aggregated over all pairs: {len(regs)}")
        for fen, pct in sorted(regs, key=lambda x: -x[1])[:10]:
            print(f"  {fen[:60]}: +{pct:.1f}%")
    else:
        print("\nno per-position regression >2% aggregated over all pairs")
    return 0


if __name__ == "__main__":
    sys.exit(main())
