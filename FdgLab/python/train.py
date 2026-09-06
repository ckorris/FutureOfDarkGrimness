"""Train a value model on exported self-play rows and score it against the hand evaluator (#191 step 13).

The promotion question is never "did the loss go down". It is: on games this model has not seen,
does it predict the outcome better than `HandWeightedEvaluator`, the thing it would replace - and
does it do so where B actually needs help. `baseline.py` measured that the hand evaluator is nearly
saturated late (auc 0.94 in round 4) and nearly blind early (0.58 in round 1), so a headline average
hides the only interesting number. Every report here is per round, per level and per shape, with the
hand evaluator scored on the same rows.

Two split modes, because they answer different questions:
  game    - hold out whole GAMES (the parquet's own `split` column). Measures "can it read a
            position it has not seen" on the same army pairings it trained on.
  pairing - hold out whole ARMY PAIRINGS. Measures the generalization the C-gate actually tests
            (held-out pairs within ~5 points of trained pairs). The gate's own held-out pairs never
            appear in exported data at all - the exporter refuses to write them - so this is the
            only way to see that gap offline, before spending ~30 h of bench time to find out.

Usage:
    uv run python train.py --parquet ../data/parquet/v3.parquet --model lgbm
    uv run python train.py --parquet ../data/parquet/v3.parquet --model lgbm --split-mode pairing
    uv run python train.py --parquet ../data/parquet/v3.parquet --model mlp --epochs 12
"""

from __future__ import annotations

import argparse
import json
import os
from pathlib import Path

import numpy as np
import pandas as pd

from baseline import line, metrics

MODELS_DIR = Path(__file__).resolve().parent / "models"


def report(name: str, pred: np.ndarray, hand: np.ndarray, label: np.ndarray, frame: pd.DataFrame) -> dict:
    """Model vs hand evaluator on identical rows, overall and sliced."""
    out = {"overall": {"model": metrics(pred, label), "hand": metrics(hand, label)}}
    print(f"\n=== {name} ===")
    print(line("model", out["overall"]["model"]))
    print(line("hand evaluator", out["overall"]["hand"]))
    for column in ("round", "points_level", "shape"):
        print(f"\n  by {column}:")
        slice_out = {}
        for value, group in frame.groupby(column):
            index = group.index
            m = metrics(pred[index], label[index])
            h = metrics(hand[index], label[index])
            slice_out[str(value)] = {"model": m, "hand": h}
            delta = m["auc"] - h["auc"]
            print(f"    {str(value):<14} model auc {m['auc']:.4f}  hand auc {h['auc']:.4f}  "
                  f"delta {delta:+.4f}  brier {m['brier']:.4f} vs {h['brier']:.4f}  n {m['n']}")
        out[column] = slice_out
    return out


def split_frames(frame: pd.DataFrame, mode: str, holdout_pairings: int, seed: int):
    if mode == "game":
        train = frame[frame["split"] == "train"]
        test = frame[frame["split"] == "val"]
        note = "held-out GAMES (same pairings)"
    else:
        pairs = sorted({tuple(sorted(p)) for p in zip(frame["army_a"], frame["army_b"])})
        rng = np.random.default_rng(seed)
        chosen = {pairs[i] for i in rng.choice(len(pairs), size=min(holdout_pairings, len(pairs) - 1), replace=False)}
        key = [tuple(sorted(p)) in chosen for p in zip(frame["army_a"], frame["army_b"])]
        test = frame[pd.Series(key, index=frame.index)]
        train = frame[~pd.Series(key, index=frame.index)]
        note = f"held-out PAIRINGS ({len(chosen)} of {len(pairs)}): " + "; ".join(
            f"{a} vs {b}" for a, b in sorted(chosen))
    return train.reset_index(drop=True), test.reset_index(drop=True), note


def train_lgbm(x_train, y_train, x_test, y_test, threads: int, rounds: int):
    import lightgbm as lgb
    train_set = lgb.Dataset(x_train, label=y_train)
    valid_set = lgb.Dataset(x_test, label=y_test, reference=train_set)
    params = dict(objective="regression", metric="l2", learning_rate=0.05, num_leaves=63,
                  min_data_in_leaf=200, feature_fraction=0.9, bagging_fraction=0.8, bagging_freq=1,
                  num_threads=threads, verbosity=-1)
    model = lgb.train(params, train_set, num_boost_round=rounds, valid_sets=[valid_set],
                      callbacks=[lgb.early_stopping(50, verbose=False), lgb.log_evaluation(0)])
    return model, np.clip(model.predict(x_test), 0.0, 1.0)


def train_mlp(x_train, y_train, aux_train, x_test, epochs: int, threads: int, hidden=(128, 64)):
    import torch
    from torch import nn
    torch.set_num_threads(threads)
    device = "cuda" if torch.cuda.is_available() else "cpu"
    torch.manual_seed(191)

    layers, width = [], x_train.shape[1]
    for size in hidden:
        layers += [nn.Linear(width, size), nn.ReLU()]
        width = size
    body = nn.Sequential(*layers)

    class Net(nn.Module):
        def __init__(self):
            super().__init__()
            self.body = body
            self.head = nn.Linear(width, 2)  # [result logit, obj_diff_norm]

        def forward(self, x):
            h = self.body(x)
            out = self.head(h)
            return out[:, 0], out[:, 1]

    net = Net().to(device)
    opt = torch.optim.AdamW(net.parameters(), lr=1e-3, weight_decay=1e-4)
    xt = torch.tensor(x_train, dtype=torch.float32, device=device)
    yt = torch.tensor(y_train, dtype=torch.float32, device=device)
    at = torch.tensor(aux_train, dtype=torch.float32, device=device)
    bce, mse = nn.BCEWithLogitsLoss(), nn.MSELoss()
    batch = 4096
    for epoch in range(epochs):
        net.train()
        order = torch.randperm(len(xt), device=device)
        total = 0.0
        for start in range(0, len(order), batch):
            index = order[start:start + batch]
            opt.zero_grad()
            logit, aux = net(xt[index])
            loss = bce(logit, yt[index]) + 0.2 * mse(aux, at[index])
            loss.backward()
            opt.step()
            total += float(loss) * len(index)
        print(f"    epoch {epoch + 1}/{epochs} loss {total / len(order):.5f}")
    net.eval()
    with torch.no_grad():
        xv = torch.tensor(x_test, dtype=torch.float32, device=device)
        pred = torch.sigmoid(net(xv)[0]).cpu().numpy()
    return net, pred


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--parquet", type=Path, required=True)
    parser.add_argument("--model", choices=("lgbm", "mlp"), default="lgbm")
    parser.add_argument("--split-mode", choices=("game", "pairing"), default="game")
    parser.add_argument("--holdout-pairings", type=int, default=3)
    parser.add_argument("--threads", type=int, default=8, help="cap CPU threads (self-play owns the rest)")
    parser.add_argument("--rounds", type=int, default=2000, help="lgbm boosting rounds (early stopped)")
    parser.add_argument("--epochs", type=int, default=12)
    parser.add_argument("--tag", default="", help="suffix for the saved report/model")
    args = parser.parse_args()

    os.environ["OMP_NUM_THREADS"] = str(args.threads)
    frame = pd.read_parquet(args.parquet)
    features = [c for c in frame.columns if "__" in c or c in
                ("round_frac", "rounds_left_frac", "objective_count_norm", "players_per_side_norm",
                 "points_norm", "activation_frac", "acting_side_is_first")]
    train, test, note = split_frames(frame, args.split_mode, args.holdout_pairings, seed=191)
    print(f"{args.parquet.name}: {len(frame)} rows | split={args.split_mode} -> {note}")
    print(f"  train {len(train)} rows / {train['game_id'].nunique()} games | "
          f"test {len(test)} rows / {test['game_id'].nunique()} games")

    x_train = train[features].to_numpy(np.float32)
    y_train = train["result"].to_numpy(np.float32)
    x_test = test[features].to_numpy(np.float32)
    y_test = test["result"].to_numpy(np.float32)
    hand = test["hand_value"].to_numpy(np.float64)

    MODELS_DIR.mkdir(exist_ok=True)
    tag = args.tag or f"{args.model}-{args.split_mode}"
    if args.model == "lgbm":
        model, pred = train_lgbm(x_train, y_train, x_test, y_test, args.threads, args.rounds)
        model.save_model(str(MODELS_DIR / f"{tag}.txt"))
        gains = sorted(zip(features, model.feature_importance("gain")), key=lambda t: -t[1])[:12]
    else:
        aux_train = train["obj_diff_norm"].to_numpy(np.float32)
        model, pred = train_mlp(x_train, y_train, aux_train, x_test, args.epochs, args.threads)
        import torch
        torch.save(model.state_dict(), MODELS_DIR / f"{tag}.pt")
        gains = []

    out = report(f"{args.model} / {note}", np.asarray(pred, dtype=np.float64), hand,
                 y_test.astype(np.float64), test)
    if gains:
        print("\n  top features by gain:")
        for name, gain in gains:
            print(f"    {name:<34} {gain:,.0f}")
    (MODELS_DIR / f"{tag}-report.json").write_text(json.dumps(out, indent=2))
    print(f"\nsaved {MODELS_DIR / tag}.* ")


if __name__ == "__main__":
    main()
