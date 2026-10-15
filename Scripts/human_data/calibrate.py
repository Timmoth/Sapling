#!/usr/bin/env python3
"""Fit the HumanPlay gap-Boltzmann constants from ranked human data (extract.py output).

For each rating bucket, fit the temperature T that maximizes the log-likelihood of the move the
human actually played under weight_k = exp(-min(gap_k, GapCap)/T). Pick the GapCap that maximizes
total log-likelihood, then fit the logistic T(Elo) = TempMax / (1 + exp((Elo-EloCentre)/EloWidth))
through the bucket temperatures. Prints constants to paste into HumanPlay.cs, plus agreement
diagnostics (empirical vs model P(best move)).

Usage: python3 calibrate.py --in ranked.jsonl [--caps 800,1200,1500,2000,3000]
"""
import argparse
import json
import math
from collections import defaultdict


def load(path):
    by = defaultdict(list)
    for line in open(path):
        r = json.loads(line)
        if r.get("human_rank", -1) >= 0 and len(r["scores"]) > 1:
            by[r["bucket"]].append(r)
    return by


def logweights(scores, T, cap):
    best = scores[0]
    return [-min(best - s, cap) / T for s in scores]


def logsumexp(ws):
    m = max(ws)
    return m + math.log(sum(math.exp(w - m) for w in ws))


def loglik(recs, T, cap):
    s = 0.0
    for r in recs:
        lw = logweights(r["scores"], T, cap)
        s += lw[r["human_rank"]] - logsumexp(lw)
    return s


def fit_T(recs, cap):
    bestT, bestL = 1.0, -1e30
    T = 1.0
    while T <= 1200.0:
        L = loglik(recs, T, cap)
        if L > bestL:
            bestL, bestT = L, T
        T *= 1.12
    return bestT, bestL


def model_p0(recs, T, cap):
    tot = 0.0
    for r in recs:
        lw = logweights(r["scores"], T, cap)
        tot += math.exp(lw[0] - logsumexp(lw))
    return tot / len(recs)


def emp_p0(recs):
    return sum(1 for r in recs if r["human_rank"] == 0) / len(recs)


def fit_logistic(points):
    # points: [(elo, T)] with 0 < T < TempMax. Fit logit(T/TempMax) = (EloCentre-elo)/EloWidth.
    tmax = max(t for _, t in points) * 1.25
    xs, ys = [], []
    for elo, t in points:
        if t <= 0 or t >= tmax:
            continue
        xs.append(elo)
        ys.append(math.log(t / (tmax - t)))
    n = len(xs)
    if n < 2:
        return tmax, 1200.0, 250.0
    mx = sum(xs) / n
    my = sum(ys) / n
    num = sum((x - mx) * (y - my) for x, y in zip(xs, ys))
    den = sum((x - mx) ** 2 for x in xs)
    slope = num / den if den else 0.0
    width = -1.0 / slope if slope else 250.0
    centre = mx + my * width
    return tmax, centre, width


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--in", dest="inp", required=True)
    ap.add_argument("--caps", default="800,1200,1500,2000,3000")
    args = ap.parse_args()

    by = load(args.inp)
    caps = [int(c) for c in args.caps.split(",")]

    best = None
    for cap in caps:
        total = 0.0
        fits = {}
        for b, recs in by.items():
            T, L = fit_T(recs, cap)
            fits[b] = T
            total += L
        if best is None or total > best[0]:
            best = (total, cap, fits)
    total, cap, fits = best

    print(f"best GapCap = {cap}   (total loglik {total:.1f})\n")
    for b in sorted(by):
        recs = by[b]
        T = fits[b]
        print(f"  bucket {b:5d}  n={len(recs):6d}  T={T:7.1f}  "
              f"empP0={emp_p0(recs):.3f}  modelP0={model_p0(recs, T, cap):.3f}")

    tmax, centre, width = fit_logistic([(b, fits[b]) for b in sorted(fits)])
    print("\nSuggested HumanPlay.cs constants:")
    print(f"  private const double TempMax = {tmax:.0f};")
    print(f"  private const double EloCentre = {centre:.0f};")
    print(f"  private const double EloWidth = {width:.0f};")
    print(f"  public const int GapCap = {cap};")


if __name__ == "__main__":
    main()
