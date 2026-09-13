#!/usr/bin/env python3
"""Automated TacticianWeights tuning (#191): coordinate descent over the movement-scoring knobs.

Evaluation = mean Tactician score over a fixed cell set (tactician-as-A vs solo-rules), paired
seeds. Cells are the weak row and sub-70 cells from the 2026-07-26 trio gate, so the objective
targets exactly where the policy loses today.

Two-stage design (2026-07-27 overnight round): the 2026-07-26 campaign showed the committed
defaults are locally optimal to +/-30% on the five #191 knobs, and that a screen-only threshold
invites winner's curse (a +2.62 screen combo read -1.12 at 200 games/cell). So this driver
probes WIDE (x0.5 / x2.0) over an expanded knob set, and a screen hit (>= --threshold mean
points at --games/cell, seed --seed-base) is adopted only after a CONFIRM eval at
--confirm-games/cell on a DIFFERENT seed base clears --confirm-threshold against a confirm
baseline. Screen sigma-of-mean is ~2.6-3 at 50 games/cell (binomial + #210 DOP-16 schedule
noise), so +3.0 is ~1.2 sigma and several false screen hits are EXPECTED across ~24 trials;
the confirm stage exists to kill them. Any adoption is still re-confirmed by the caller's full
ordered-pool gate before default changes (this script never edits source).

--deadline-epoch stops STARTING work once projected finish (from the observed games/sec rate)
would pass the deadline; screen hits left unconfirmed by the deadline are reported as such in
result.json rather than silently dropped.

Usage: python3 FdgLab/tools/tune_weights.py --out DIR [--games 50] [--rounds 2]
         [--threshold 3.0] [--confirm-games 150] [--confirm-seed-base 5000]
         [--confirm-threshold 2.0] [--deadline-epoch EPOCH]
Runs from the repo root; expects FdgLab built (bin/Debug/net8.0/FdgLab.dll).
"""
import argparse, json, os, re, subprocess, sys, time

LAB = "FdgLab/bin/Debug/net8.0/FdgLab.dll"
ARMIES = "FdgLab/armies"

# (name, committed default). Candidates are multiples of the CURRENT value each round.
# Ordered most-promising-first so a deadline truncates the tail: the last five were already
# probed at +/-30% on 2026-07-26 (all flat), so the wide multipliers re-probe them last.
# MoveDamage is the scale anchor and stays fixed; MoveReachableBonus is a tie-breaker.
KNOBS = [
    ("MoveScreen", 0.8),
    ("MoveObjective", 0.75),
    ("MoveObjectiveApproach", 0.4),
    ("MoveApproach", 0.75),
    ("ShootThreatFactor", 1.25),
    ("MoraleBreakBonus", 1.3),
    ("ShootingKillBonus", 1.5),
    ("MoveRetaliation", 0.45),
    ("RetaliationShareFloor", 0.25),
    ("MoveProjectedThreat", 0.15),
    ("PostureRetaliationRelief", 0.35),
    ("PostureObjectiveBoost", 0.30),
]
CANDIDATE_MULTIPLIERS = [0.5, 2.0]

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


def eval_key(weights: dict, games: int, seed_base: int) -> str:
    return f"{weights_spec(weights) or '(defaults)'}|g{games}|s{seed_base}"


def run_cell(a, b, weights, games, seed_base, out_dir, log):
    cmd = ["dotnet", LAB, "bench",
           "--a", f"{ARMIES}/{a}.fdgarmy", "--b", f"{ARMIES}/{b}.fdgarmy",
           "--profile-a", "tactician", "--profile-b", "solorules",
           "--games", str(games), "--seed-base", str(seed_base), "--out", out_dir]
    spec = weights_spec(weights)
    if spec:
        cmd += ["--weights", spec]
    # Retry native crashes (one DOP-16 bench segfaulted mid-campaign 2026-07-26, rc=-11, after
    # nine identical invocations ran clean - transient, likely the #210 race under load) and
    # hangs (per-bench timeout ~8x the expected wall time; an unattended overnight run must
    # never wedge silently). A persistent failure is still fatal: a silently skipped cell
    # would skew the eval mean.
    for attempt in range(3):
        try:
            proc = subprocess.run(cmd, capture_output=True, text=True,
                                  timeout=max(600, games * 12))
        except subprocess.TimeoutExpired:
            log(f"bench TIMEOUT for {a} vs {b} (attempt {attempt + 1}/3)")
            continue
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
    ap.add_argument("--confirm-games", type=int, default=150)
    ap.add_argument("--confirm-seed-base", type=int, default=5000)
    ap.add_argument("--confirm-threshold", type=float, default=2.0)
    ap.add_argument("--deadline-epoch", type=float, default=None)
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
    stats = {"games": 0, "secs": 0.0}

    def rate():
        # Conservative default until an hour of data exists; observed 2026-07-26: ~0.66 games/s.
        return stats["games"] / stats["secs"] if stats["secs"] > 300 else 0.55

    def time_for(games_per_cell):
        if args.deadline_epoch is None:
            return True
        est = games_per_cell * len(CELLS) / rate()
        return time.time() + est <= args.deadline_epoch

    # Resume: completed evals replay from evals.jsonl instead of re-running.
    evals_path = os.path.join(args.out, "evals.jsonl")
    if os.path.exists(evals_path):
        with open(evals_path) as f:
            for line in f:
                rec = json.loads(line)
                key = eval_key(rec["weights"], rec.get("games", args.games),
                               rec.get("seed_base", args.seed_base))
                eval_cache[key] = (rec["mean"], rec["cells"])
                evals = max(evals, rec["eval"])
        log(f"resumed {len(eval_cache)} cached evals from evals.jsonl")

    def evaluate(weights, label, games, seed_base):
        nonlocal evals
        key = eval_key(weights, games, seed_base)
        if key in eval_cache:
            return eval_cache[key]
        evals += 1
        started = time.time()
        cell_dir_root = os.path.join(args.out, f"eval-{evals:03d}")
        scores = {}
        for i, (a, b) in enumerate(CELLS):
            d = os.path.join(cell_dir_root, f"cell-{i}")
            os.makedirs(d, exist_ok=True)
            scores[f"{a} vs {b}"] = run_cell(a, b, weights, games, seed_base, d, log)
        stats["games"] += games * len(CELLS)
        stats["secs"] += time.time() - started
        mean = sum(scores.values()) / len(scores)
        eval_cache[key] = (mean, scores)
        log(f"eval {evals:03d} [{label}] mean={mean:.2f} :: {key}")
        for cell, s in scores.items():
            log(f"    {s:5.1f}  {cell}")
        with open(evals_path, "a") as f:
            f.write(json.dumps({"eval": evals, "label": label, "weights": weights,
                                "games": games, "seed_base": seed_base,
                                "mean": mean, "cells": scores}) + "\n")
        return eval_cache[key]

    current = {}  # overrides on top of committed defaults
    values = {name: default for name, default in KNOBS}
    base_mean, _ = evaluate(current, "baseline", args.games, args.seed_base)
    log(f"baseline mean {base_mean:.2f} over {len(CELLS)} cells x {args.games} games")
    confirm_base = None  # mean of CURRENT config at confirm games/seeds, evaluated lazily
    adoptions, refuted, unconfirmed = [], [], []
    stopped = False

    for rnd in range(1, args.rounds + 1):
        adopted_this_round = 0
        for name, _default in KNOBS:
            trials = []
            for mult in CANDIDATE_MULTIPLIERS:
                if not time_for(args.games):
                    log(f"deadline: stopping before r{rnd} {name} x{mult}")
                    stopped = True
                    break
                cand_value = round(values[name] * mult, 3)
                cand = dict(current)
                cand[name] = cand_value
                mean, _ = evaluate(cand, f"r{rnd} {name}={cand_value}",
                                   args.games, args.seed_base)
                trials.append((mean, cand_value, cand))
            if not trials:
                break
            best_mean, best_value, best = max(trials, key=lambda t: t[0])
            if best_mean - base_mean >= args.threshold:
                hit = {"knob": name, "value": best_value, "screen_mean": best_mean,
                       "screen_base": base_mean, "weights": best}
                if confirm_base is None and time_for(args.confirm_games):
                    confirm_base, _ = evaluate(current, "confirm-base",
                                               args.confirm_games, args.confirm_seed_base)
                if confirm_base is not None and time_for(args.confirm_games):
                    cmean, _ = evaluate(best, f"confirm {name}={best_value}",
                                        args.confirm_games, args.confirm_seed_base)
                    hit["confirm_mean"] = cmean
                    hit["confirm_base"] = confirm_base
                    if cmean - confirm_base >= args.confirm_threshold:
                        log(f"ADOPT r{rnd} {name}: {values[name]:g} -> {best_value:g} "
                            f"(screen {base_mean:.2f} -> {best_mean:.2f}, "
                            f"confirm {confirm_base:.2f} -> {cmean:.2f})")
                        current = best
                        values[name] = best_value
                        base_mean = best_mean
                        confirm_base = cmean
                        adoptions.append(hit)
                        adopted_this_round += 1
                    else:
                        log(f"REFUTED r{rnd} {name}={best_value:g}: screen +"
                            f"{best_mean - hit['screen_base']:.2f} but confirm "
                            f"{cmean:.2f} vs {confirm_base:.2f}")
                        refuted.append(hit)
                else:
                    log(f"UNCONFIRMED (deadline) r{rnd} {name}={best_value:g} "
                        f"screen {base_mean:.2f} -> {best_mean:.2f}")
                    unconfirmed.append(hit)
            else:
                log(f"keep  r{rnd} {name}={values[name]:g} "
                    f"(best cand {best_value:g} at {best_mean:.2f} vs {base_mean:.2f})")
            if stopped:
                break
        if stopped:
            break
        if adopted_this_round == 0:
            log(f"round {rnd}: nothing adopted - stopping early")
            break

    log(f"DONE evals={evals} final screen mean={base_mean:.2f} "
        f"overrides={weights_spec(current) or '(defaults)'} adopted={len(adoptions)} "
        f"refuted={len(refuted)} unconfirmed={len(unconfirmed)} "
        f"rate={rate():.2f} games/s deadline_stop={stopped}")
    with open(os.path.join(args.out, "result.json"), "w") as f:
        json.dump({"final_screen_mean": base_mean, "overrides": current,
                   "adoptions": adoptions, "refuted": refuted, "unconfirmed": unconfirmed,
                   "evals": evals, "rate_games_per_sec": round(rate(), 3),
                   "stopped_at_deadline": stopped}, f, indent=2)


if __name__ == "__main__":
    main()
