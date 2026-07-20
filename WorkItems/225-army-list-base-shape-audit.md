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

- 2026-07-19: **Defect B shipped.** New engine `DefaultBaseEstimator` replaces the silent 28mm fallback.
  - **The heuristic gained a second axis beyond `Tough`.** Profiling the 102 affected units showed every
    one carries a Tough rule, in six discrete buckets (3/6/9/12/18/24 - no interpolation needed), and
    that `Hero` cleanly separates two populations that must not share a size curve: all six Tough(3)
    units are Hero+Unique named CHARACTERS, and four larger Heroes are monstrous creatures (a flying
    Tough(12) hive lord, Tough(6) named beasts). Sizing those as vehicles would have put a named
    character on a 90x52mm tank hull. So: **Hero -> circle, otherwise rectangle, sized by Tough.**
  - Sizes deliberately reuse tuples ALREADY present in the corpus, so an estimated base never looks
    alien beside an imported one. Vehicles: T<=6 90x52, T<=9 105x70, T<=12 120x92, T<=18 160x122,
    else 175x125. Heroes: T<=3 40mm, T<=6 50mm, else 60mm. Both tables are single constants, tunable
    by editing them and re-running the retrofit.
  - `MapBase` now takes the unit's rules + name + the `warn` callback; **every estimate emits a warning**
    (`no base declared for 'X' - estimated ...`), so an invented base is never silent. `MapUnit` maps
    rules up front to feed it.
  - `BaseOrientationRetrofit` handles both defects, in order: an unsized default is REPLACED outright
    (leaving no orientation to fix), otherwise a mis-oriented rectangle is swapped. `--retrofit-bases`
    reports an estimate count per file and `--verbose` names every one.
  - Convergence is a tested invariant: an estimate is never itself `IsUnsizedDefault`, and no estimated
    rectangle is wider than long - so the retrofit cannot re-estimate or re-swap on a second pass.
    Verified: second run reported 0 patched.
  - `IsUnsizedDefault` gates on `Shape == Circle` FIRST. Every Rectangle also carries a leftover 28mm
    `DiameterInches` in its dead field, so a diameter-only test would have flagged correctly-sized
    rectangles. Pinned by a test.
  - Ran over the corpus: **38/56 files patched, 138 bases estimated** (more than 102 because armies carry
    both compiled units and an embedded book snapshot). Corpus now has **0 unsized defaults** (was 102)
    and **0 wider-than-long rectangles**; the resulting distribution is 12 tuples, all pre-existing.
  - Verification: engine **1756 passed / 0 failed** (+17 new), app **393 passed / 0 failed**,
    `dotnet build` clean, headless smoke exits 0.
  - **Expected side effect**: the headless smoke's outcome changed (Tie -> a Win). Vehicles went from
    28mm dots to real footprints, so collision, LoS and coherency geometry genuinely changed. Not a
    regression - it is the point of the fix.

## Decisions

- 2026-07-19: Defect B's 28mm fallback replaced with a heuristic rather than a hand-authored per-unit
  override table or a warn-only change (user call). OPR genuinely declares no base for vehicles, so
  there is nothing to import and a size must be invented; a heuristic is self-maintaining as books
  change. Implemented with the `Hero` axis added on top of `Tough` - see the note above for why.
- 2026-07-19: Estimated sizes are constrained to tuples already in the corpus. An invented base should
  be indistinguishable in KIND from a real one, even though its value is a guess.

## Deferred / still open

- **The estimated sizes are guesses.** They are plausible and internally consistent, but they will not
  match anyone's actual models. If a specific unit looks wrong on the table, hand-tune the tables in
  `DefaultBaseEstimator` and re-run `--retrofit-bases`.
- **Base sizes are coarse in OPR's own data** and were left alone: 105x70 covers both `Artillery Gun`
  and `Paladin Light Titan`; 120x92 covers both `Jeep` and `Super Heavy Battle Tank`. That coarseness
  comes from upstream, not from our importer - out of scope unless hand-tuning is wanted.
- **Optional**: suppress serialization of the inert unused field (emit `diameter` only for circles,
  `width`/`height` only for rectangles) so defaulted entries are obvious in a diff and the 47 books
  shrink. Nice-to-have, not correctness.

## Outcome

Both defects complete and verified; corpus is clean (0 unsized defaults, 0 mis-oriented rectangles).
Open only until hand-verified in the GUI.

**Verify by hand:**
1. Play an army with vehicles (Human Defense Force, Orks) - tanks and titans occupy real footprints
   rather than infantry-sized dots; they block line of sight and cannot slip through gaps they used to.
2. A named character (e.g. Jackals' Ranjo "Swiftsnare") is on a normal round base, NOT a tank hull.
3. Re-import a book with `--import-opr` and confirm the console warns once per undeclared base.
4. Spot-check that estimated vehicle sizes look sane on the table; tune `DefaultBaseEstimator` if not.
