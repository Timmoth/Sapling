#!/usr/bin/env python3
"""Compare two Sapling.Bench single-worker JSON outputs for exact parity.

Matches rows by (rep, fen). Requires every mandatory row field (rep, fen,
nodes, score, depthSearched, bestMove, pv) to be present in every row of both
files; a field missing from both inputs is a failure, not an equal pair.
Fails on missing/extra/duplicate rows and on any difference in nodes, score,
depthSearched, bestMove or full pv. Requires non-empty matching corpus hashes,
identical configuration, and complete repetition/FEN coverage per
configuration.repeat. Requires deterministicAcrossRepeats and independently
compares full PVs across repeats within each file. Elapsed time and nps are
measurement outputs and are never compared.

--mutate-selftest: deliberately mutate B (PV change, field removal, duplicate
row, missing repetition) and require the comparator to reject each mutation.

Exit 0 = parity, 1 = mismatch, 2 = usage/structural error.
"""
import copy
import json
import sys

MANDATORY_FIELDS = ("rep", "fen", "nodes", "score", "depthSearched", "bestMove", "pv")
COMPARED_FIELDS = ("nodes", "score", "depthSearched", "bestMove", "pv")


def load(path):
    with open(path) as f:
        return json.load(f)


def rows_by_key(doc, label, fails):
    rows = doc.get("positions")
    if not rows:
        fails.append(f"{label}: no positions rows")
        return None
    keyed = {}
    for r in rows:
        missing = [f for f in MANDATORY_FIELDS if f not in r]
        if missing:
            fails.append(f"{label}: row missing mandatory fields {missing}: {json.dumps(r)[:120]}")
            continue
        key = (r["rep"], r["fen"])
        if key in keyed:
            fails.append(f"{label}: duplicate row rep={key[0]} fen={key[1]}")
            continue
        keyed[key] = r
    return keyed


def check_config(a, b, fails):
    ca, cb = a.get("configuration", {}), b.get("configuration", {})
    for k in sorted(set(ca) | set(cb)):
        if ca.get(k) != cb.get(k):
            fails.append(f"configuration.{k}: {ca.get(k)!r} != {cb.get(k)!r}")
    ha, hb = a["header"].get("corpusHashes", []), b["header"].get("corpusHashes", [])
    if not ha or not hb:
        fails.append(f"corpusHashes missing/empty: A={ha} B={hb}")
    elif ha != hb:
        fails.append(f"corpusHashes: {ha} != {hb}")


def check_coverage(doc, label, keyed, fails):
    repeat = doc.get("configuration", {}).get("repeat")
    if not isinstance(repeat, int) or repeat <= 0:
        fails.append(f"{label}: configuration.repeat missing or invalid: {repeat!r}")
        return
    expected_reps = set(range(repeat))
    by_fen = {}
    for (rep, fen) in keyed:
        by_fen.setdefault(fen, set()).add(rep)
    for fen, reps in sorted(by_fen.items()):
        if reps != expected_reps:
            missing = sorted(expected_reps - reps)
            extra = sorted(reps - expected_reps)
            fails.append(f"{label}: coverage fen={fen[:50]} missing_reps={missing} extra_reps={extra}")


def check_determinism(doc, label, keyed, fails):
    totals = doc.get("totals", {})
    if not totals.get("deterministicAcrossRepeats", False):
        fails.append(f"{label}: totals.deterministicAcrossRepeats is not true")
    by_fen = {}
    for (rep, fen), r in keyed.items():
        by_fen.setdefault(fen, []).append((rep, r))
    for fen, reps in by_fen.items():
        reps.sort()
        first = reps[0][1]
        for rep, r in reps[1:]:
            for f in COMPARED_FIELDS:
                if r.get(f) != first.get(f):
                    fails.append(
                        f"{label}: intra-file repeat mismatch fen={fen} rep{rep} vs rep{reps[0][0]} field={f}")


def compare(a, b):
    fails = []
    check_config(a, b, fails)
    ka, kb = rows_by_key(a, "A", fails), rows_by_key(b, "B", fails)
    if ka is None or kb is None:
        return fails
    check_coverage(a, "A", ka, fails)
    check_coverage(b, "B", kb, fails)
    check_determinism(a, "A", ka, fails)
    check_determinism(b, "B", kb, fails)
    for key in sorted(set(ka) - set(kb)):
        fails.append(f"row missing in B: rep={key[0]} fen={key[1]}")
    for key in sorted(set(kb) - set(ka)):
        fails.append(f"row missing in A: rep={key[0]} fen={key[1]}")
    for key in sorted(set(ka) & set(kb)):
        for f in COMPARED_FIELDS:
            if ka[key].get(f) != kb[key].get(f):
                fails.append(
                    f"mismatch rep={key[0]} fen={key[1]} field={f}: A={ka[key].get(f)!r} B={kb[key].get(f)!r}")
    return fails


MUTATIONS = ("pv", "missing-field", "duplicate-row", "missing-rep")


def apply_mutation(b, kind):
    row = b["positions"][0]
    if kind == "pv":
        original = row.get("pv", "")
        row["pv"] = (original.split(" ")[0] + " a1a1") if original else "a1a1"
    elif kind == "missing-field":
        del row["pv"]
    elif kind == "duplicate-row":
        b["positions"].append(copy.deepcopy(row))
    elif kind == "missing-rep":
        b["positions"] = [r for r in b["positions"]
                          if not (r["rep"] == 0 and r["fen"] == row["fen"])]


def self_test(path_a, path_b):
    a, b = load(path_a), load(path_b)
    fails = compare(a, b)
    if fails:
        print("self-test precondition failed: A and B are not at parity")
        for f in fails[:5]:
            print("  " + f)
        return 1
    rc = 0
    for kind in MUTATIONS:
        mutated = copy.deepcopy(b)
        apply_mutation(mutated, kind)
        fails = compare(a, mutated)
        if fails:
            print(f"self-test OK: rejected mutation '{kind}' ({len(fails)} failure(s))")
        else:
            print(f"FAIL self-test: comparator accepted mutation '{kind}'")
            rc = 1
    return rc


def main():
    args = sys.argv[1:]
    mutate = "--mutate-selftest" in args
    paths = [p for p in args if not p.startswith("--")]
    if len(paths) != 2:
        print(__doc__)
        return 2
    if mutate:
        return self_test(paths[0], paths[1])
    a, b = load(paths[0]), load(paths[1])
    fails = compare(a, b)
    if fails:
        print(f"FAIL: {len(fails)} parity failure(s)")
        for f in fails:
            print("  " + f)
        return 1
    n = len(a["positions"])
    print(f"PASS: {n} rows match on {', '.join(COMPARED_FIELDS)}; config and corpus identical; "
          f"mandatory fields present; coverage complete; deterministic in both files")
    return 0


if __name__ == "__main__":
    sys.exit(main())
