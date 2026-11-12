# 048 — Block deployment of models into impassible terrain

**Status**: implemented, awaiting GUI hand-verification
**Related**: #002 (terrain placement), #050 (shares the swept-disc base-radius primitive)

## Goal
When a unit is being deployed (auto-placement by AI, or manual placement via the GUI/CLI resolvers), models must not be placed inside impassible terrain. Currently `DeployAllUnitsStage` / the AI auto-placement has no intersection check against `Impassible`-flagged terrain pieces, so a model can be placed inside or overlapping a building. Observed in the wild: a terrain piece placed flush against the deployment zone boundary caused the AI to place a model directly on top of it.

Done when:
- Auto-placement (AI and CLI EOF fallback) rejects candidate positions that intersect any `Impassible` terrain piece.
- The GUI placement resolver similarly blocks the player from confirming a position that overlaps impassible terrain (or at minimum warns visually).
- The engine test suite has a case covering auto-placement with an impassible piece in the deployment zone.

## Notes

### 2026-06-13
- Implemented across all three place resolvers on branch `050-movement-base-radius` (rode along with the
  #050 swept-disc work it depends on). Shared engine helper `FDG.PlacementUtilities.OverlapsImpassibleTerrain`
  (`Helpers/PlacementUtilities.cs`) reuses the #050 `IZone.DoesPathIntersectZone(p, p, radius)` overload with
  a zero-length path so the model's base *disc* is tested, not just its center point.
  - **AI** (`AiPlaceObjectsResolver`): snapshots impassible terrain per `Resolve`; rejects in `FindValidPosition`
    (covers normal deployment *and* the Ambush row scan, which routes through it).
  - **CLI** (`PlaceObjectsResolver`): manual entry prints "On impassible terrain…" and re-prompts; auto-placement
    (EOF / scan fallback) skips overlapping candidates. Guards null `_tableState`.
  - **GUI** (`GuiPlaceObjectsResolver`): `notOnTerrain` joins the click-validity gate (ghost turns red, click
    shows an error toast), and "Auto-place rest" skips terrain.
- New engine test `AiPlaceObjectsResolverTests.DoesNotPlaceModelsOnImpassibleTerrain` (wall splits the zone;
  asserts no placed model's base overlaps it). Suite 424/0; full build clean; headless smoke exits 0.
- **Not changed:** `DeployUnitStage` still applies placements without engine-side validation (unlike
  `DefinePathStage`). Kept parity with existing deployment design — resolvers block at the source, so no
  bad data is submitted; a throwing engine gate would crash on a GUI misclick. The AI's last-resort
  zone-center fallback (when no legal spot exists in a scanned row) is still best-effort and could land on
  terrain in a fully-blocked zone — noted, not addressed (rare; would need a "give up / skip unit" path).

## Decisions
- **Disc-vs-zone via the #050 overload, not a new interface method.** A model at rest occupies its base disc;
  `DoesPathIntersectZone(p, p, radius)` (zero-length swept disc) answers the overlap exactly, so #048 needed
  no new `IZone` surface — just the shared helper. Base-radius aware for the same reason #050 is.
- **Resolver-side blocking, no engine throw** — see Notes; matches how deployment already works.

## Outcome
Engine + AI + CLI implemented and unit-tested; GUI block implemented (parked for hand-verification).
