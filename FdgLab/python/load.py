"""Read self-play jsonl.gz into parquet, checked and split (#191 campaign step 12c).

The exporter's own guarantees are re-asserted here rather than trusted: schema sec 7's checks run
at export time on a sample, this runs them on every row that will ever reach a model. A silent
feature bug is the most expensive failure available in phase C - it is invisible until training and
wastes the box-days that produced the data.

Split discipline (campaign doc sec 5): the pairings in pool.json's heldOut list are NEVER trained
on, at any point level, plus one 2v2 cell; inside the trained pairings a by-GAME split holds out a
validation fraction. By game, never by row: both sides' rows share a game and its outcome label, so
a row-wise split leaks the answer across the boundary.

Usage:
    uv run python load.py --data ../data/2026-09-06-v3 --out ../data/parquet/v3.parquet
    uv run python load.py --data ../data/2026-09-06-v3 --out /tmp/v3.parquet --limit-files 4
"""

from __future__ import annotations

import argparse
import gzip
import hashlib
import json
import sys
from dataclasses import dataclass
from pathlib import Path

import pandas as pd

REPO_ROOT = Path(__file__).resolve().parents[2]
POOL_PATH = REPO_ROOT / "FdgLab" / "armies" / "pool.json"

# Schema v3 (docs/tactician-c1-schema.md): 7 globals + 18 per side x 4 blocks.
GLOBALS = 7
PER_SIDE = 18
WIDTH = GLOBALS + PER_SIDE * 4
BLOCKS = ("self", "ally", "enemy_sum", "enemy_max")
GLOBAL_NAMES = (
    "round_frac", "rounds_left_frac", "objective_count_norm", "players_per_side_norm",
    "points_norm", "activation_frac", "acting_side_is_first",
)
SIDE_NAMES = (
    "health_frac", "value_share", "units_alive_frac", "ranged_share", "melee_share",
    "activations_left_frac", "obj_held_share", "obj_contested_share", "mean_obj_dist_norm",
    "min_obj_dist_norm", "mobility_norm", "threat_coverage", "reserve_frac", "seizer_frac",
    "activation_share", "obj_held_threatened_share", "obj_contest_strength", "obj_open_approach",
)


def feature_names() -> list[str]:
    names = list(GLOBAL_NAMES)
    for block in BLOCKS:
        names += [f"{block}__{n}" for n in SIDE_NAMES]
    assert len(names) == WIDTH, f"{len(names)} != {WIDTH}"
    return names


@dataclass
class Checks:
    files: int = 0
    games: int = 0
    rows: int = 0
    schemas: frozenset = frozenset()
    out_of_range: int = 0
    nan_hand_value: int = 0
    held_out_rows: int = 0
    width_mismatch: int = 0

    def report(self) -> None:
        print(f"  files {self.files} | games {self.games} | rows {self.rows}")
        print(f"  schema versions seen: {sorted(self.schemas)}")
        print(f"  features outside [0,1]: {self.out_of_range}")
        print(f"  rows with a bad feature width: {self.width_mismatch}")
        print(f"  rows with NaN hand_value: {self.nan_hand_value}")
        print(f"  rows from a held-out pairing: {self.held_out_rows}")

    def failed(self) -> list[str]:
        problems = []
        if self.out_of_range:
            problems.append(f"{self.out_of_range} feature values outside [0,1]")
        if self.width_mismatch:
            problems.append(f"{self.width_mismatch} rows with the wrong feature width")
        if self.held_out_rows:
            problems.append(f"{self.held_out_rows} rows from a held-out pairing (the exporter should refuse these)")
        if len(self.schemas) > 1:
            problems.append(f"mixed schema versions in one load: {sorted(self.schemas)}")
        if self.rows == 0:
            problems.append("no rows loaded")
        return problems


def held_out_pairings() -> set[frozenset[str]]:
    """Army-file basenames, as unordered pairs - the same normalization SelfPlay.IsHeldOut uses."""
    pool = json.loads(POOL_PATH.read_text())
    out = set()
    for entry in pool.get("heldOut", []):
        side_a = [Path(p).stem.strip().lower() for p in entry.get("sideA", [])]
        side_b = [Path(p).stem.strip().lower() for p in entry.get("sideB", [])]
        out.add(frozenset(side_a + side_b))
    return out


def army_key(name: str) -> str:
    return Path(name).stem.strip().lower()


def val_split(seed: int, fraction: float) -> bool:
    """Deterministic by GAME seed, so a re-run reproduces the split and a resumed dataset extends it."""
    digest = hashlib.sha256(f"split:{seed}".encode()).digest()
    return (int.from_bytes(digest[:4], "big") % 10_000) < fraction * 10_000


def load_dir(data_dir: Path, checks: Checks, limit_files: int | None) -> pd.DataFrame:
    files = sorted(data_dir.glob("selfplay_*.jsonl.gz"))
    if limit_files:
        files = files[:limit_files]
    if not files:
        sys.exit(f"no selfplay_*.jsonl.gz under {data_dir}")

    held_out = held_out_pairings()
    names = feature_names()
    records: list[dict] = []

    for path in files:
        checks.files += 1
        games: dict[str, dict] = {}
        rows: list[dict] = []
        with gzip.open(path, "rt") as handle:
            for line in handle:
                item = json.loads(line)
                kind = item.get("Kind")
                if kind == "header":
                    checks.schemas = checks.schemas | {item["Schema"]}
                elif kind == "game":
                    games[item["GameId"]] = item
                elif kind == "row":
                    rows.append(item)
                # "entity" lines are the 5% per-unit sample, for a later entity model - not loaded here.

        for row in rows:
            features = row["Features"]
            if len(features) != WIDTH:
                checks.width_mismatch += 1
                continue
            game = games.get(row["GameId"])
            if game is None:
                continue  # a row whose game never finished: no label, drop it
            pair = frozenset({army_key(game["ArmyA"]), army_key(game["ArmyB"])})
            if pair in held_out:
                checks.held_out_rows += 1
                continue
            if any(not (-1e-6 <= value <= 1 + 1e-6) for value in features):
                checks.out_of_range += 1
            hand = row.get("HandValue", float("nan"))
            if hand != hand:  # NaN
                checks.nan_hand_value += 1

            record = dict(zip(names, features))
            record.update(
                game_id=row["GameId"],
                seed=game["Seed"],
                boundary=row["Boundary"],
                round=row["Round"],
                acting_slot=row["ActingSlot"],
                chosen_unit=row["ChosenUnit"],
                chosen_action=row["ChosenAction"],
                chosen_macro=row["ChosenMacro"],
                hand_value=hand,
                result=row["Result"],
                obj_diff_norm=row["ObjDiffNorm"],
                rounds_played=row["RoundsPlayed"],
                points_level=game["PointsLevel"],
                shape=game["Shape"],
                army_a=game["ArmyA"],
                army_b=game["ArmyB"],
                profile_a=game["ProfileA"],
                profile_b=game["ProfileB"],
                # #191 step 12b: absent in v1-v3 A-play files (all "none"/"hand").
                search_budget=game.get("SearchBudget", "none"),
                evaluator=game.get("Evaluator", "hand"),
            )
            records.append(record)
        checks.games += len(games)

    checks.rows = len(records)
    return pd.DataFrame.from_records(records)


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--data", type=Path, action="append", required=True,
                        help="a self-play output directory (repeatable; all must share one schema)")
    parser.add_argument("--out", type=Path, required=True, help="parquet file to write")
    parser.add_argument("--val-fraction", type=float, default=0.1, help="games held out for validation")
    parser.add_argument("--limit-files", type=int, default=None, help="read only the first N files (smoke)")
    args = parser.parse_args()

    checks = Checks()
    frames = [load_dir(directory, checks, args.limit_files) for directory in args.data]
    frame = pd.concat(frames, ignore_index=True)

    frame["split"] = ["val" if val_split(int(s), args.val_fraction) else "train" for s in frame["seed"]]

    print("checks:")
    checks.report()
    problems = checks.failed()
    if problems:
        print("\nFAILED:")
        for problem in problems:
            print(f"  - {problem}")
        sys.exit(1)

    args.out.parent.mkdir(parents=True, exist_ok=True)
    frame.to_parquet(args.out, index=False)
    counts = frame["split"].value_counts().to_dict()
    games = frame.groupby("split")["game_id"].nunique().to_dict()
    print(f"\nwrote {args.out} ({args.out.stat().st_size / 1e6:.1f} MB)")
    print(f"  rows by split: {counts}")
    print(f"  games by split: {games}")
    print(f"  label mean (result): {frame['result'].mean():.4f} | hand_value mean: {frame['hand_value'].mean():.4f}")
    print(f"  levels: {frame['points_level'].value_counts().to_dict()}")
    print(f"  shapes: {frame['shape'].value_counts().to_dict()}")


if __name__ == "__main__":
    main()
