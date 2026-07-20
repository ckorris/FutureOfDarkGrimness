# 225 — Audit army lists for wrong base shape/size

**Status**: todo
**Related**: #149 (base shapes), #150 (base-shape geometry everywhere)

## Goal
User has spotted a number of units with default (probably unintended) base shapes/sizes, and some rectangular bases that are wider than they are long when the reverse should be true. Scope: sweep the bundled army/book data (`FdgLab/armies/*.fdgarmy`, `FdgRaylib/Assets/Books/*.fdgbook`) for base entries that are still default-valued or have width > length where that looks backwards, cross-check against real OPR base sizes, and correct them.

## Notes

- 2026-07-19: **Audit done — two systematic defects, not scattered bad data.** Swept all 47 bundled
  `.fdgbook` files (1058 units) + 8 `.fdgarmy` files (237 unit entries). Only 13 distinct base tuples
  exist, and every mm value is a clean integer, so nothing is individually mistranscribed. Both defects
  are single-point importer bugs:
  - **Defect A — every rectangular base was rotated 90 degrees (294 units).** OPR writes its base spec
    LENGTH-first ("60x35" = a 60mm-long, 35mm-wide bike base); `OprBookImporter.TryParseBase` mapped it
    positionally, so length landed on `WidthInches`. But `RectangleBase` runs its local +Z
    (`HeightInches`) along the facing (`IBaseShape.cs` `Footprint`/`ToZone`), so every rectangular model
    presented its LONG axis as frontage. Not cosmetic - #150 made that footprint feed LoS, overlap and
    coherency. Corpus proof: all 294 rectangles were wider than long, which is physically impossible for
    a real base. FIXED (see below).
  - **Defect B — 102 units carry a silent 28mm default.** `MapBase` falls through to a bare
    `new BaseFileEntry()` when OPR's spec is `"none"`, and OPR emits `"none"` for vehicles - so the
    fallback lands on the LARGEST models in the game (Beast Titan T24, Dread Titan T24, Battle Tank T12,
    every APC, Prime Drop Pod). A Tough(24) titan currently collides as a 28mm dot. The corpus contains
    no other 28mm circles, so `Shape == Circle && abs(d - 1.1023622) < 1e-4` is a reliable marker.
    STILL OPEN - see Decisions.
  - Inert-field noise (confirmed, harmless): every Circle carries leftover default `width`/`height`, every
    Rectangle a leftover default `diameter`. `ToBaseShape()` reads only the relevant fields. Consequence
    worth remembering: a Rectangle with `d == 1.1024` is NOT a defaulted base, just the dead field -
    default-detection must gate on `Shape == Circle` first.

- 2026-07-19: **Defect A shipped.** Engine `97b7da3`, superproject `737e0a1`.
  - `OprBookImporter.TryParseBase` now maps length -> `HeightInches`, width -> `WidthInches`, so future
    imports are born correct.
  - New `BaseOrientationRetrofit` (engine) + `--retrofit-bases <fileOrDir>...` CLI (mirrors #239's
    `--retrofit-effects`, including the #236 `BuiltArmyFile` round-trip so a forge army's selections and
    embedded book snapshot survive). Migrates a forge army's compiled units AND its embedded book copy -
    fixing only the former would let re-opening the list in the builder reintroduce the swap.
  - Idempotent by construction: only swaps when `Width > Height`, and a corrected base never matches
    again. That guard doubles as the correctness rule (a real base is never wider than it is long along
    the facing axis). Verified: second run reported 0 patched / 55 unchanged.
  - Ran over the bundled data: **54/55 files patched**, all **294** rectangles now `Height >= Width`,
    0 remaining. Spot-checked bikes read 60mm long x 35mm wide.
  - Verification: engine suite **1739 passed / 0 failed** (+5 new tests), full `dotnet build` clean,
    headless smoke exits 0.

## Decisions

- 2026-07-19: Defect B's 28mm fallback will be replaced with a **heuristic keyed off `Tough`** (user
  call), rather than a hand-authored per-unit override table or a warn-only change. OPR genuinely
  declares no base for vehicles, so there is nothing to import and a size must be invented; the heuristic
  is self-maintaining as books change. Pair it with an import warning either way so these stop passing
  unnoticed. NOT YET IMPLEMENTED.

## Deferred / still open

- **Defect B (the 28mm default on 102 units)** — decided but not built; see Decisions above.
- **Base sizes are coarse in OPR's own data** and were left alone: 105x70 covers both `Artillery Gun`
  and `Paladin Light Titan`; 120x92 covers both `Jeep` and `Super Heavy Battle Tank`. That coarseness
  comes from upstream, not from our importer - out of scope unless hand-tuning is wanted.
- **Optional**: suppress serialization of the inert unused field (emit `diameter` only for circles,
  `width`/`height` only for rectangles) so defaulted entries are obvious in a diff and the 47 books
  shrink. Nice-to-have, not correctness.

## Outcome

Defect A complete and verified; Defect B decided and outstanding. Item stays open until B lands.
