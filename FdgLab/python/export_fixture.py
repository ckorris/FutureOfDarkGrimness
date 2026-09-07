"""Write the C# forward-pass parity fixture (#191 step 14).

A small net with FIXED random weights, plus real feature rows and what torch computes for them.
Random rather than trained on purpose: this fixture tests arithmetic - layer order, row-major
weights, ReLU, the sigmoid on head 0 - not a model. It stays valid when the real weights are
retrained, and it is small enough to live in the repo (the shipping model's weights are ~400 KB and
are committed only when step 15 promotes one).

    uv run python export_fixture.py --parquet ../data/parquet/v3.parquet
"""

from __future__ import annotations

import argparse
import json
from pathlib import Path

import numpy as np
import pandas as pd
import torch
from torch import nn

FIXTURE_DIR = Path(__file__).resolve().parents[2] / "FutureOfDarkGrimness" / "Tests" / "Fixtures"
SERVING_DROP = ("activation_frac", "acting_side_is_first")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--parquet", type=Path, required=True)
    parser.add_argument("--rows", type=int, default=24)
    parser.add_argument("--hidden", type=int, nargs=2, default=(8, 4))
    args = parser.parse_args()

    frame = pd.read_parquet(args.parquet)
    features = [c for c in frame.columns if "__" in c or c in
                ("round_frac", "rounds_left_frac", "objective_count_norm", "players_per_side_norm",
                 "points_norm", "activation_frac", "acting_side_is_first")]
    features = [f for f in features if f not in SERVING_DROP]
    sample = frame[features].to_numpy(np.float32)[:args.rows]

    torch.manual_seed(191)
    sizes = [len(features), *args.hidden, 2]
    linears = [nn.Linear(sizes[i], sizes[i + 1]) for i in range(len(sizes) - 1)]
    # Wide enough that the 24 cases SPREAD across [0,1]. With small weights every output sits near
    # 0.5 and a broken implementation returning a constant would pass the parity test unnoticed; the
    # test asserts the spread too, so this cannot silently regress into a weak fixture.
    for index, linear in enumerate(linears):
        last = index == len(linears) - 1
        nn.init.uniform_(linear.weight, -8.0 if last else -1.0, 8.0 if last else 1.0)
        nn.init.uniform_(linear.bias, -6.0 if last else -0.2, 6.0 if last else 0.2)
    modules: list[nn.Module] = []
    for index, linear in enumerate(linears):
        modules.append(linear)
        if index < len(linears) - 1:
            modules.append(nn.ReLU())
    net = nn.Sequential(*modules).eval()

    with torch.no_grad():
        out = net(torch.tensor(sample, dtype=torch.float32))
        value = torch.sigmoid(out[:, 0]).numpy()

    layers = []
    for index, linear in enumerate(linears):
        layers.append({
            "weight": linear.weight.detach().numpy().tolist(),
            "bias": linear.bias.detach().numpy().tolist(),
            "activation": "relu" if index < len(linears) - 1 else "none",
        })

    FIXTURE_DIR.mkdir(parents=True, exist_ok=True)
    (FIXTURE_DIR / "mlp-parity-weights.json").write_text(json.dumps({
        "schema": 3,
        "note": "fixed-seed random weights; a parity fixture for the C# forward pass, not a trained model",
        "features": features,
        "layers": layers,
    }, indent=1))
    (FIXTURE_DIR / "mlp-parity-cases.json").write_text(json.dumps({
        "note": "torch reference outputs for the weights beside this file",
        "inputs": sample.astype(float).tolist(),
        "expected_value": value.astype(float).tolist(),
    }, indent=1))
    print(f"wrote {FIXTURE_DIR}/mlp-parity-{{weights,cases}}.json "
          f"({len(features)} inputs, {len(layers)} layers, {len(sample)} cases)")
    print(f"  value range {value.min():.4f}..{value.max():.4f}")


if __name__ == "__main__":
    main()
