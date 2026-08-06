# 365 — FdgLab has no terrain lever: LoS work can be cleared but never measured

**Status**: open
**Related**: #363 (the audit below came out of its gate), #364 (path-side sibling, same blind
spot), #194 (FdgLab harness), #191 (Tactician umbrella), #210 (bench nondeterminism)

## Goal

A bench pool whose maps actually contain line-of-sight geometry, so terrain-sensitive AI work has
an instrument that can show a change HELPING rather than only failing to hurt.

## The problem, measured (2026-08-06)

`GameRunner` hardcodes `GameSettings.GetDefault()`, so every bench game plays
`ETerrainPlacementMode.AutoFromLayout` over `DefaultTerrainPool`. That pool has 6
`Blocking | Impassible` pieces, but:

- `PlaceTerrainStage.RepresentativeCenterZ` takes the AVERAGE of a composite's parts' centre-Z
  (not the AABB centre), which puts FIVE of the 6 inside a deployment zone - Rocky outcrop and
  Wreckage at 8.75, Crater rim at 7.33, Tank traps and Collapsed wall at 41.
- `DeploymentZonePlacementChance` = 0.4 then drops each of those 60% of the time, and deployment
  zones sit BEHIND the armies rather than between them.
- So the only piece reliably on the table is the Central building: 6" x 4" = 24 sq in, jittered up
  to 10" from (36,24).

Expected map: **~3.0 blocking pieces, 77.6 sq in = 2.2% of the 72x48 table.** The rest of the
pool (2 forests, 2 sandbag lines, mine field, rubble) is Cover / Difficult / Dangerous, and
`LineOfSightUtilities.HasLineOfSight` reads the Blocking flag only - invisible to any sight work.

Consequence, seen live on #363's gate: the 4 x 640-game pool could certify no-regression and
nothing else, and the follow-on `BlockedThreatShare` tuning (0.2 vs 0.4) moved only 17 of 640
games - a borderline p=0.049 on an instrument that is near-null by construction. More games do
not fix this; only more walls do.

## Sketch

The engine side already exists: `ETerrainPlacementMode.LoadFromFile` + `GameSettings.TerrainLayoutPath`
places a `.fdgterrain` (`TerrainLayoutFile`) VERBATIM - no jitter, no deployment dropout
(`PlaceTerrainStage.PlacePiecesVerbatim`). Missing pieces:

1. `--terrain <file.fdgterrain>` on FdgLab `bench` / `smoke`, threaded into `GameSpec` and set on
   the `GameSettings` in `GameRunner` (mirror how `--weights` is plumbed and echoed into the
   report header, so a report always records the map it was played on).
2. A small set of authored layouts - an urban/dense one around 12-15% blocking coverage
   concentrated MIDFIELD, plus maybe a "lanes" one (long sight corridors) and a "scattered" one.
   Verbatim placement means one fixed map per run, so map diversity comes from running several
   layouts rather than from jitter; the aggregate should be reported per layout, not pooled.
3. Optional: bench-side exposure counters (how often the sight test fires / changes a score per
   game), which turn "delta is ~0" into "delta is ~0 because the code ran N times".

## Open questions

- Does a fixed verbatim map bias deployment/objective placement enough to need 2-3 layouts as a
  minimum rather than a nicety? (The auto layout's jitter is doing real work for map diversity.)
- Worth also fixing the `RepresentativeCenterZ` classification itself? Five of six solid pieces
  being treated as deployment-zone furniture looks unintended rather than designed - but changing
  it changes how every generated map plays, which is exactly what `DefaultTerrainPool`'s comment
  says it does not want to do silently. Separate call from the bench lever; do not conflate.

## Decisions

(none yet)

## Outcome

(open)
