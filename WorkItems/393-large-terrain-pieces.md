# 393 — Large 3-point terrain pieces

**Status**: in-progress (implemented + tested; awaiting GUI hand-verify)
**Related**: #268 (the auto-layout / palette split this extends), #301 (point costs), #394 (the type filter that ships alongside it), #044 (externalize the pool to JSON)

## Goal
Widen the heavy end of the Alternating-mode terrain palette. Owner's report: "I find I always add one of
every existing one each game" — the 3-point tier was seven pieces, small enough to exhaust, so terrain
selection stopped being a choice. Done = ten new palette-only templates, all 3 points, all sized to the
band of the biggest existing templates (Collapsed wall 11", Forest 10" across), spread across all four
filter types so #394's buttons each have a heavy option. The auto layout must not change.

## Notes

- 2026-09-06 (verified + pushed): engine suite **3175/0** (1 skipped, the always-skipped spell probe),
  app suite **2836/0**, `dotnet build` clean, headless smoke exits 0 and plays to a result. Engine
  `e697219`. Two things the tests caught that review had not: (1) the horizontal sight-line sweep failed
  at y = 1.00 because the south wall's top edge and the east wall's bottom edge merely ABUTTED - a line
  laid exactly along that seam grazes both boundaries and counts as hitting neither, a hole in the wall
  one float-epsilon tall. Fixed in the geometry (side walls now run past the end walls) rather than in the
  test. (2) A new `TerrainPointsPlacementIntegrationTests` case at a 3-point turn budget: the existing
  1-point-per-turn test can only ever buy 1-cost pieces, so nothing had ever driven the big end of the
  palette through the real placer.

## HAND-VERIFY (owner)

Host a game with terrain mode **Alternating: Points** (the auto layout is unchanged, so the default mode
shows nothing new), then in the placement picker:

1. Scroll to the 3-point rows - ten new names, thumbnails to scale against the rest of the palette.
2. Place **Cathedral shell**: confirm the two doorways are on opposite corners and a unit can walk into
   the courtyard through either one.
3. Stand a unit in the courtyard and check nothing outside can draw a straight horizontal sight line
   through both doors.
4. Place **Hab block** and walk a two-model front through a street between towers.
5. Place **Rocky ridge** and confirm a unit must go around it but can still shoot over it.
6. Place **Razorwire belt** and confirm a model can halt in a lane without taking a dangerous-terrain test.

- 2026-09-06 (owner, second review): **Cathedral shell** doors staggered to opposite corners - west low
  (y 1 -> 3.5), east high (y 4.5 -> 7) - so no horizontal sight line runs in one door and out the other.
  Their y ranges are disjoint by 1", which is the maximum two 2.5" doors can be separated in the 6" wall
  run between the south and north walls. New test `CathedralShell_HasNoHorizontalSightLineThroughBothDoorways`
  sweeps a BARE sight line (no base inflation - what LoS traces) across the piece at every 0.05" of height
  and requires every one blocked; the two-bases-abreast entry test now walks each doorway's own centre.

- 2026-09-06 (owner review of the candidate sheet): four shape revisions. **Cathedral shell** gains a 2.5"
  doorway in each side wall (west + east), turning the ring from a sealed block into a courtyard models can
  enter. **Hab block** towers spread to 2.5" streets (now 10.5x10, was 10x9). **Rocky ridge** rock centres
  staggered ~1.2" off the line (now 11x4.6, was 11x3.7) - consecutive rocks still overlap, so no hole.
  **Razorwire belt** lanes widened 1.5" -> 2" (now 10x7, was 10x6). New shared `DoorwayInches = 2.5f`
  const: two 28mm bases abreast is 2.205" (`BaseShapeDefaults.CircleDiameterInches` x 2), plus clearance
  for the validators' float-precision margins. Four new geometry tests sweep a real 28mm base through each
  opening / street / lane and across the ridge, using `IZone.DoesPathIntersectZone(a, b, inflationRadius)`
  - the same swept-disc call the movement validator makes against terrain, so the tests exercise the real
  geometry rather than restating the coordinates.

- 2026-09-06: Ten templates added to `DefaultTerrainPool.ExtraTemplates` — Cathedral shell (10x8 Solid,
  h6), Hab block (10x9 Solid, h7, tallest in the palette), Ancient wood (10.5x10 Woods), Sunken mire
  (10.5x10 Difficult|Dangerous), Mine belt (9x7 Dangerous), Trench line (10x3.5 Cover), Crashed hauler
  (11x6 Solid, h4), Rocky ridge (11x3.7 Impassible-not-Blocking, h0.5), Refinery tanks (11.5x6 Solid,
  h6, widest in the palette), Razorwire belt (10x6 Dangerous|Difficult). Three new tests in
  `DefaultTerrainPoolTests`: the heavy tier has >=10 large 3-pointers, every filterable type has a large
  piece, and nothing outgrows a 12" band. Candidate sheet drawn to scale for owner review:
  https://claude.ai/code/artifact/8c35d05b-88b9-452e-87fd-b7fab8b0fdbe

## Decisions

- **Palette only; `Get()` untouched.** The #268 split exists exactly so that widening the player's
  choice does not silently make generated maps denser. `AutoLayout_IsUnchangedBy_ThePaletteExpansion`
  still pins 12 pieces.
- **All ten priced at 3.** Top of the existing scale. `TerrainPieceEntry.Points` is not hard-capped and a
  4 was available, but nothing else in the palette costs 4, so introducing one here would have been a
  balance change smuggled in under a content change.
- **Size band, not "bigger".** `NoPalettePiece_OutgrowsTheSizeBand` caps the longest side at 12".
  "As large as the largest existing ones" is a band; a piece far past 11" would dominate the table and
  leave no room for the other placements.
- **Type spread is deliberate**, not decoration: the palette had no large Dangerous piece at all (Mine
  field, 6x6, was the biggest) and only small Impassible-without-Blocking pieces (Tank traps, Water pool).
- **Not verified this session.** Owner asked for no builds or test runs (cores in use training the bot),
  so nothing is committed. Needs `dotnet test FutureOfDarkGrimness/FutureOfDarkGrimness.csproj` green plus
  a `dotnet build` before commit, then a GUI eyeball of the picker.

## Outcome
_Open._
