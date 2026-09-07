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
    columns = ["round", "points_level", "shape"]
    if "source" in frame.columns and frame["source"].nunique() > 1:
        columns.append("source")  # #191 step 15: A-play vs B-play rows (tree-reached states)
    for column in columns:
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


def train_mlp(x_train, y_train, aux_train, x_test, epochs: int, threads: int, hidden=(128, 64),
              sample_weight=None):
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
    # #191 step 15: per-row weights (source balancing). Mean of 1 keeps the loss scale comparable.
    wt = torch.tensor(sample_weight if sample_weight is not None else np.ones(len(x_train), np.float32),
                      dtype=torch.float32, device=device)
    bce, mse = nn.BCEWithLogitsLoss(reduction="none"), nn.MSELoss(reduction="none")
    batch = 4096
    for epoch in range(epochs):
        net.train()
        order = torch.randperm(len(xt), device=device)
        total = 0.0
        for start in range(0, len(order), batch):
            index = order[start:start + batch]
            opt.zero_grad()
            logit, aux = net(xt[index])
            w = wt[index]
            loss = ((bce(logit, yt[index]) + 0.2 * mse(aux, at[index])) * w).sum() / w.sum()
            loss.backward()
            opt.step()
            total += float(loss.detach()) * len(index)
        print(f"    epoch {epoch + 1}/{epochs} loss {total / len(order):.5f}")
    net.eval()
    with torch.no_grad():
        xv = torch.tensor(x_test, dtype=torch.float32, device=device)
        pred = torch.sigmoid(net(xv)[0]).cpu().numpy()
    return net, pred



def export_model(net, features: list[str], stem: Path, sample) -> None:
    """weights.json for the C# forward pass, model.onnx as the interchange + parity oracle.

    The C# evaluator ships the JSON, not the ONNX: at this size a dense forward pass is tens of
    microseconds and it keeps a native runtime out of four unsigned platform archives (plan sec 10,
    step 14). The ONNX file exists so a test can assert the two agree - if they ever diverge, the
    C# code is wrong, and without an independent oracle that is silent.
    """
    import numpy as np
    import torch

    layers = []
    modules = list(net.body) + [net.head]
    for module in modules:
        if isinstance(module, torch.nn.Linear):
            layers.append({
                "weight": module.weight.detach().cpu().numpy().tolist(),  # [out][in]
                "bias": module.bias.detach().cpu().numpy().tolist(),
                "activation": "relu",
            })
    layers[-1]["activation"] = "none"  # heads are raw: [result logit, obj_diff_norm]

    payload = {
        "schema": 3,
        "note": "input order is exactly `features`; result = sigmoid(out[0]), obj_diff_norm = out[1]",
        "features": features,
        "layers": layers,
    }
    (stem.parent / f"{stem.name}-weights.json").write_text(json.dumps(payload))

    device = next(net.parameters()).device
    dummy = torch.tensor(sample[:1], dtype=torch.float32, device=device)
    torch.onnx.export(net, dummy, str(stem.parent / f"{stem.name}.onnx"),
                      input_names=["features"], output_names=["result_logit", "obj_diff"],
                      dynamic_axes={"features": {0: "batch"}}, opset_version=17)

    # Parity rows the C# test reads: inputs plus what torch says, so the test needs no python.
    with torch.no_grad():
        xs = torch.tensor(sample, dtype=torch.float32, device=device)
        logit, aux = net(xs)
        value = torch.sigmoid(logit).cpu().numpy()
    (stem.parent / f"{stem.name}-parity.json").write_text(json.dumps({
        "features": features,
        "inputs": np.asarray(sample, dtype=float).tolist(),
        "expected_value": value.astype(float).tolist(),
        "expected_obj_diff": aux.cpu().numpy().astype(float).tolist(),
    }))
    print(f"  exported {stem.name}-weights.json, {stem.name}.onnx, {stem.name}-parity.json "
          f"({len(features)} inputs, {len(layers)} layers)")


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
    parser.add_argument("--drop", default="", help="comma-separated features to exclude (serving-parity ablation)")
    parser.add_argument("--balance-sources", action="store_true",
                        help="#191 step 15: weight B-play rows (search_budget != none) so both sources carry "
                             "equal total weight in the MLP loss (the plan's 'weighted so they are not swamped')")
    parser.add_argument("--export", action="store_true",
                        help="write weights.json + model.onnx for the C# evaluator (mlp only)")
    args = parser.parse_args()

    os.environ["OMP_NUM_THREADS"] = str(args.threads)
    frame = pd.read_parquet(args.parquet)
    if "search_budget" in frame.columns:
        frame["source"] = np.where(frame["search_budget"].astype(str) == "none", "A-play", "B-play")
        counts = frame["source"].value_counts().to_dict()
        print(f"  sources: {counts}")
    features = [c for c in frame.columns if "__" in c or c in
                ("round_frac", "rounds_left_frac", "objective_count_norm", "players_per_side_norm",
                 "points_norm", "activation_frac", "acting_side_is_first")]
    dropped = [d.strip() for d in args.drop.split(",") if d.strip()]
    if dropped:
        missing = [d for d in dropped if d not in features]
        if missing:
            raise SystemExit(f"--drop names features that are not in the parquet: {missing}")
        features = [f for f in features if f not in dropped]
        print(f"  dropped {len(dropped)} features: {', '.join(dropped)} -> {len(features)} inputs")
    train, test, note = split_frames(frame, args.split_mode, args.holdout_pairings, seed=191)
    print(f"{args.parquet.name}: {len(frame)} rows | split={args.split_mode} -> {note}")
    print(f"  train {len(train)} rows / {train['game_id'].nunique()} games | "
          f"test {len(test)} rows / {test['game_id'].nunique()} games")

    x_train = train[features].to_numpy(np.float32)
    y_train = train["result"].to_numpy(np.float32)
    sample_weight = None
    if args.balance_sources:
        if "source" not in train.columns or train["source"].nunique() < 2:
            raise SystemExit("--balance-sources: the training rows do not contain both A-play and B-play sources")
        is_b = (train["source"] == "B-play").to_numpy()
        n_a, n_b = int((~is_b).sum()), int(is_b.sum())
        # B-play rows get n_a/n_b each, A-play rows 1, then rescale to mean 1.
        raw = np.where(is_b, n_a / n_b, 1.0).astype(np.float32)
        sample_weight = raw / raw.mean()
        print(f"  --balance-sources: A-play {n_a} rows x 1.00, B-play {n_b} rows x {n_a / n_b:.2f} "
              f"(equal total weight; per-row weights rescaled to mean 1)")
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
        model, pred = train_mlp(x_train, y_train, aux_train, x_test, args.epochs, args.threads,
                                sample_weight=sample_weight)
        import torch
        torch.save(model.state_dict(), MODELS_DIR / f"{tag}.pt")
        gains = []
        if args.export:
            export_model(model, features, MODELS_DIR / tag, x_test[:1024])

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
