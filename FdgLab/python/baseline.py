"""Score the hand evaluator as a predictor on exported rows (#191 campaign step 12c/13).

This is the bar. `hand_value` is what HandWeightedEvaluator said about the acting side at that
boundary, and `result` is what actually happened to that side. A learned value net is only worth
shipping if it predicts the outcome better than the evaluator it would replace, measured on games
neither of them trained on - so this number is computed BEFORE any training, and step 13's model
report prints the same metrics beside it.

Metrics, and why these:
  brier  - mean squared error against the 1/0.5/0 label. The natural loss for a value head, and it
           handles the tie label without pretending a tie is half a win-event.
  logloss- soft-target cross entropy, the other head's loss. Reported for comparability, not as the
           decision rule.
  auc    - ranking quality on DECISIVE rows only (ties dropped): "does it order wins above losses",
           which is what the search actually consumes - a leaf value is compared, never calibrated.
  base   - the same metrics for a constant predictor (the label mean). Anything that cannot beat
           this is not a signal.

Usage:
    uv run python baseline.py --parquet ../data/parquet/v3.parquet
"""

from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
import pandas as pd


def metrics(pred: np.ndarray, label: np.ndarray) -> dict[str, float]:
    pred = np.clip(pred, 1e-6, 1 - 1e-6)
    brier = float(np.mean((pred - label) ** 2))
    logloss = float(-np.mean(label * np.log(pred) + (1 - label) * np.log(1 - pred)))
    decisive = label != 0.5
    auc = float("nan")
    if decisive.sum() > 0:
        wins = label[decisive] == 1
        if wins.any() and not wins.all():
            order = np.argsort(pred[decisive])
            ranks = np.empty_like(order, dtype=float)
            ranks[order] = np.arange(1, decisive.sum() + 1)
            n_pos, n_neg = wins.sum(), (~wins).sum()
            auc = float((ranks[wins].sum() - n_pos * (n_pos + 1) / 2) / (n_pos * n_neg))
    return {"brier": brier, "logloss": logloss, "auc": auc, "n": int(len(label))}


def line(name: str, m: dict[str, float]) -> str:
    return f"  {name:<26} brier {m['brier']:.4f}  logloss {m['logloss']:.4f}  auc {m['auc']:.4f}  n {m['n']}"


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--parquet", type=Path, required=True)
    parser.add_argument("--split", default="val", help="which split to score (default: val)")
    args = parser.parse_args()

    frame = pd.read_parquet(args.parquet)
    data = frame[frame["split"] == args.split] if args.split != "all" else frame
    label = data["result"].to_numpy(dtype=float)
    hand = data["hand_value"].to_numpy(dtype=float)
    constant = np.full_like(label, frame.loc[frame["split"] == "train", "result"].mean())

    print(f"{args.parquet.name}: split={args.split}, {len(data)} rows, {data['game_id'].nunique()} games")
    print(line("hand evaluator", metrics(hand, label)))
    print(line("constant (train mean)", metrics(constant, label)))

    print("\nby round (hand evaluator):")
    for value, group in data.groupby("round"):
        print(line(f"round {value}", metrics(group["hand_value"].to_numpy(float), group["result"].to_numpy(float))))

    print("\nby level (hand evaluator):")
    for value, group in data.groupby("points_level"):
        print(line(str(value), metrics(group["hand_value"].to_numpy(float), group["result"].to_numpy(float))))

    print("\nby shape (hand evaluator):")
    for value, group in data.groupby("shape"):
        print(line(str(value), metrics(group["hand_value"].to_numpy(float), group["result"].to_numpy(float))))


if __name__ == "__main__":
    main()
