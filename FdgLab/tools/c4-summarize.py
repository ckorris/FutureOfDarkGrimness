#!/usr/bin/env python3
"""#191 step 15: fold a c4-slice.sh output tree into one arm-by-pair table.

usage: c4-summarize.py <out-dir> <arm>...   (reads <out-dir>/<arm>/pair*/bench.md)
Score = (wins + 0.5 ties) / games for side A (the Strategist), the bench's own 'A score'.
"""
import re
import sys
from pathlib import Path

out = Path(sys.argv[1])
arms = sys.argv[2:]
ROW = re.compile(r"^\|\s*(?P<match>.+? vs .+?)\s*\|\s*(?P<games>\d+)\s*\|\s*(?P<score>[\d.]+)%\s*\|\s*(?P<w>\d+)\s*\|\s*(?P<l>\d+)\s*\|\s*(?P<t>\d+)\s*\|\s*(?P<f>\d+)")

cells = {}  # (arm, pair) -> dict
for arm in arms:
    for md in sorted((out / arm).glob("pair*/bench.md")):
        for line in md.read_text().splitlines():
            m = ROW.match(line)
            if m:
                cells[(arm, md.parent.name)] = {k: (m.group(k) if k == "match" else int(m.group(k))) for k in ("match", "games", "w", "l", "t", "f")}
                cells[(arm, md.parent.name)]["score"] = float(m.group("score"))

pairs = sorted({p for (_, p) in cells}, key=lambda s: int(s.replace("pair", "")))
print("| Pair (A = Strategist vs Tactician) | " + " | ".join(arms) + " |")
print("|---|" + "---|" * len(arms))
for p in pairs:
    name = next((c["match"] for (a, q), c in cells.items() if q == p), p)
    name = re.sub(r"\s+2k\s+- [^|]*?(?= vs |$)", "", name)  # some army files carry a double space
    print(f"| {name} | " + " | ".join(
        (f"{cells[(a, p)]['score']:.1f} ({cells[(a, p)]['games']}g, {cells[(a, p)]['f']}f)" if (a, p) in cells else "-")
        for a in arms) + " |")
pooled = []
for a in arms:
    w = sum(c["w"] for (x, _), c in cells.items() if x == a)
    t = sum(c["t"] for (x, _), c in cells.items() if x == a)
    g = sum(c["games"] for (x, _), c in cells.items() if x == a)
    f = sum(c["f"] for (x, _), c in cells.items() if x == a)
    pooled.append(f"{100 * (w + 0.5 * t) / g:.1f} ({g}g, {f}f)" if g else "-")
print("| **pooled** | " + " | ".join(f"**{x}**" for x in pooled) + " |")
