#!/usr/bin/env python3
"""Automated TacticianWeights tuning (#191): coordinate descent over the movement-scoring knobs.

Evaluation = mean Tactician score over a fixed cell set (tactician-as-A vs solo-rules), 50
games/cell, paired seeds. Cells are the weak row and sub-70 cells from the 2026-07-26 trio gate,
so the objective targets exactly where the policy loses today. A candidate is adopted only when
it clears ADOPT_THRESHOLD mean points - per-cell sigma is ~7 binomial at N=50 PLUS the #210
schedule noise (17/20 outcome flips between same-code DOP-16 runs, though cell SCORES stay far
more stable than per-game outcomes), so mean-of-8 sigma is ~2.6-3 and the threshold sits at
~1.2 sigma; every adoption is re-confirmed by the caller's full ordered-pool gate before any
default changes (this script never edits source).

Usage: python3 FdgLab/tools/tune_weights.py --out DIR [--games 50] [--rounds 2] [--threshold 3.0]
Runs from the repo root; expects FdgLab built (bin/Debug/net8.0/FdgLab.dll).
"""
import argparse, json, os, re, subprocess, sys, time

LAB = "FdgLab/bin/Debug/net8.0/FdgLab.dll"
ARMIES = "FdgLab/armies"

# (name, committed default). Candidates are multiples of the CURRENT value each round.
KNOBS = [
    ("MoveRetaliation", 0.45),
    ("RetaliationShareFloor", 0.25),
    ("MoveProjectedThreat", 0.15),
    ("PostureRetaliationRelief", 0.35),
    ("PostureObjectiveBoost", 0.30),
]
CANDIDATE_MULTIPLIERS = [0.7, 1.3]

# Tactician plays army A (profile A) vs the solo bot on army B.
CELLS = [
    ("Robot Legions 2k - Mixed", "Orks 2k - Horde Mixed"),
    ("Robot Legions 2k - Mixed", "Alien Hives 2k - Horde Melee"),
    ("Robot Legions 2k - Mixed", "High Elf Fleets 2k  - Caster-Heavy"),
    ("Human Defense Force 2k - Tough and Vehicle-Heavy", "Alien Hives 2k - Horde Melee"),
    ("Dark Elf Raiders 2k - Transport", "Alien Hives 2k - Horde Melee"),
    ("Dark Elf Raiders 2k - Transport", "Orks 2k - Horde Mixed"),
    ("Battle Brothers 2k - Elite Shooting", "Alien Hives 2k - Horde Melee"),
    ("Dwarf Guilds 2k - Ambush and Scout-Heavy", "Orks 2k - Horde Mixed"),
]

SCORE_ROW = re.compile(r"^\|.*\|\s*\d+\s*\|\s*([0-9.]+)%")


def weights_spec(weights: dict) -> str:
    return ";".join(f"{k}={v:g}" for k, v in sorted(weights.items()))


def eval_key(weights: dict) -> str:
    return weights_spec(weights) or "(defaults)"


def run_cell(a, b, weights, games, seed_base, out_dir, log):
    cmd = ["dotnet", LAB, "bench",
           "--a", f"{ARMIES}/{a}.fdgarmy", "--b", f"{ARMIES}/{b}.fdgarmy",
           "--profile-a", "tactician", "--profile-b", "solorules",
           "--games", str(games), "--seed-base", str(seed_base), "--out", out_dir]
    spec = weights_spec(weights)
    if spec:
        cmd += ["--weights", spec]
    # Retry native crashes (one DOP-16 bench segfaulted mid-campaign 2026-07-26, rc=-11, after
    # nine identical invocations ran clean - transient, likely the #210 race under load). A
    # persistent failure is still fatal: a silently skipped cell would skew the eval mean.
    for attempt in range(3):
        proc = subprocess.run(cmd, capture_output=True, text=True)
        if proc.returncode == 0:
            break
        log(f"bench rc={proc.returncode} for {a} vs {b} "
            f"(attempt {attempt + 1}/3): {proc.stderr.strip()[:300]}")
    else:
        log(f"FATAL bench kept failing for {a} vs {b}")
        sys.exit(1)
    with open(os.path.join(out_dir, "bench.md")) as f:
        for line in f:
            m = SCORE_ROW.match(line)
            if m and " vs " in line:
                return float(m.group(1))
    log(f"FATAL no score row in {out_dir}/bench.md")
    sys.exit(1)


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument("--out", required=True)
    ap.add_argument("--games", type=int, default=50)
    ap.add_argument("--rounds", type=int, default=2)
    ap.add_argument("--threshold", type=float, default=3.0)
    ap.add_argument("--seed-base", type=int, default=3000)
    args = ap.parse_args()

    os.makedirs(args.out, exist_ok=True)
    log_path = os.path.join(args.out, "campaign.log")

    def log(msg):
        stamp = time.strftime("%H:%M:%S")
        line = f"[{stamp}] {msg}"
        print(line, flush=True)
        with open(log_path, "a") as f:
            f.write(line + "\n")

    eval_cache = {}
    evals = 0

    # Resume: completed evals replay from evals.jsonl instead of re-running ~10 minutes each.
    evals_path = os.path.join(args.out, "evals.jsonl")
    if os.path.exists(evals_path):
        with open(evals_path) as f:
            for line in f:
                rec = json.loads(line)
                eval_cache[eval_key(rec["weights"])] = (rec["mean"], rec["cells"])
                evals = max(evals, rec["eval"])
        log(f"resumed {len(eval_cache)} cached evals from evals.jsonl")

    def evaluate(weights, label):
        nonlocal evals
        key = eval_key(weights)
        if key in eval_cache:
            return eval_cache[key]
        evals += 1
        cell_dir_root = os.path.join(args.out, f"eval-{evals:03d}")
        scores = {}
        for i, (a, b) in enumerate(CELLS):
            d = os.path.join(cell_dir_root, f"cell-{i}")
            os.makedirs(d, exist_ok=True)
            scores[f"{a} vs {b}"] = run_cell(a, b, weights, args.games, args.seed_base, d, log)
        mean = sum(scores.values()) / len(scores)
        eval_cache[key] = (mean, scores)
        log(f"eval {evals:03d} [{label}] mean={mean:.2f} :: {key}")
        for cell, s in scores.items():
            log(f"    {s:5.1f}  {cell}")
        with open(os.path.join(args.out, "evals.jsonl"), "a") as f:
            f.write(json.dumps({"eval": evals, "label": label, "weights": weights,
                                "mean": mean, "cells": scores}) + "\n")
        return eval_cache[key]

    current = {}  # overrides on top of committed defaults
    values = {name: default for name, default in KNOBS}
    base_mean, _ = evaluate(current, "baseline")
    log(f"baseline mean {base_mean:.2f} over {len(CELLS)} cells x {args.games} games")

    for rnd in range(1, args.rounds + 1):
        adopted_this_round = 0
        for name, _default in KNOBS:
            trials = []
            for mult in CANDIDATE_MULTIPLIERS:
                cand_value = round(values[name] * mult, 3)
                cand = dict(current)
                cand[name] = cand_value
                mean, _ = evaluate(cand, f"r{rnd} {name}={cand_value}")
                trials.append((mean, cand_value, cand))
            best_mean, best_value, best = max(trials, key=lambda t: t[0])
            if best_mean - base_mean >= args.threshold:
                log(f"ADOPT r{rnd} {name}: {values[name]:g} -> {best_value:g} "
                    f"(mean {base_mean:.2f} -> {best_mean:.2f})")
                current = best
                values[name] = best_value
                base_mean = best_mean
                adopted_this_round += 1
            else:
                log(f"keep  r{rnd} {name}={values[name]:g} "
                    f"(best cand {best_value:g} at {best_mean:.2f} vs {base_mean:.2f})")
        if adopted_this_round == 0:
            log(f"round {rnd}: nothing adopted - stopping early")
            break

    log(f"DONE evals={evals} final mean={base_mean:.2f} overrides={eval_key(current)}")
    with open(os.path.join(args.out, "result.json"), "w") as f:
        json.dump({"final_mean": base_mean, "overrides": current, "evals": evals}, f, indent=2)


if __name__ == "__main__":
    main()
