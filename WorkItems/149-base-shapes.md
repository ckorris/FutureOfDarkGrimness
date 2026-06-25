# 149 — Configurable model base size + shapes

**Status**: in-progress
**Related**: #150 (collision paths that stay bounding-radius — the deferred half of this), #050 (movement base-radius / swept-disc), #002 (zone shapes — the abstraction precedent)
**Branch** (both repos): `149-base-shapes` — submodule + superproject branched from master.

## Goal
Make a model's base **configurable in the army creator** (per unit) and support **non-circular shapes**.
- Base size is settable in the army builder; it defaults to the current value (28mm circle) for existing armies.
- Changing it affects **both** geometry (distance / movement / collision) **and** rendering (the drawn shape grows).
- A **dropdown** chooses the shape; a **circle** takes a diameter, a **rectangle** takes width × height (inches). Reasonable defaults assigned (circle 28mm; rectangle 25×50mm cavalry base).
- The representation is **abstract** (`IBaseShape`) so more shapes can be added later.

## Decisions (settled with the user 2026-06-25)
- **Collision fidelity = shape-aware model-to-model now; bounding-circle for the hard paths (→ #150).** Base-to-base distance/spacing/melee/charge reach between two models uses exact per-shape geometry (circle / axis-aligned rectangle / circle-rect). The swept-path-vs-terrain, pile-in swept collision, LoS blockers, and objective seizure keep using the model's **bounding circle** for this slice; those are catalogued and carried in **#150**.
- **Scope = per-unit.** One base shape per `UnitFileEntry`, applied to all the unit's models (matches today's uniform behavior and the builder's per-unit editing). Not per-model.
- **Input units = inches.** Circle = diameter in inches; rectangle = width × height in inches (consistent with the app's inch-based ranges). The engine stores a circle as a radius internally.
- **No base facing/rotation yet.** Models have no facing in the engine; rectangles are axis-aligned (width→X, height→Z). Facing is deferred (would be needed for full exact terrain geometry — see #150).
- **`BaseRadiusInches` is retained as the bounding-circle seam.** `IModel.BaseRadiusInches => BaseShape.BoundingRadiusInches`, so every existing radius-based call site keeps compiling and a bigger base (any shape) still means a bigger footprint everywhere. New geometry reads `IModel.BaseShape`.

## Design
- `IBaseShape` (engine, namespace `FDG`): `BoundingRadiusInches` + `ContainsLocalPoint(dx,dz)`. Implementations `CircleBase(RadiusInches)`, `RectangleBase(WidthInches, HeightInches)`.
- `BaseShapeGeometry.SurfaceGap2D(a, posA, b, posB)`: exact pairwise gap for circle-circle / rect-rect / circle-rect (axis-aligned); **bounding-circle fallback for any unhandled shape pair** (the extension seam + the #150 hook).
- Live model (`ModelData`, Newtonsoft + `TypeNameHandling.Auto`): polymorphic `BaseShape` field round-trips via `$type`; legacy saves with no base default to the 28mm circle.
- Army file (`.fdgarmy`, STJ): a plain `BaseFileEntry { Shape (enum), DiameterInches, WidthInches, HeightInches }` on `UnitFileEntry` — primitives + a string enum (no polymorphic converter), missing → default circle.

## Plan (slices; each: implement → test → verify → commit)
- **A. Engine foundation.** `IBaseShape`/`CircleBase`/`RectangleBase`/`BaseShapeGeometry`/`BaseShapeDefaults`; `DistanceUtilities` shape-aware overloads; `IModel.BaseShape` + computed `BaseRadiusInches`; `ModelData`/`ModelTemplate` carry the shape; model-to-model extension methods route through shapes. `BaseShapeTests`.
- **B. Army file → models.** `BaseFileEntry` on `UnitFileEntry`; `UnitData` builds the shape (default circle for old files). Army-load tests.
- **C. Shape-aware model-to-model collision.** Route melee range / coherency / cohesion packing / AI placement spacing through shape-aware distance; catalogue the bounding-radius remnants into #150.
- **D. Army builder UI.** Shape dropdown + dimension inputs (inches) in `ArmyBuilderScreen`.
- **E. Render + hit-test.** Draw the true shape (mirror `ZoneRenderer` dispatch) in the renderer + resolver overlays; hit-test via shape containment.

## Notes
- 2026-06-25: **Slices A–E built.** Engine (submodule branch `149-base-shapes`, commits `31c1294`/`62c61b0`/`f8b0b8b`): A — `IBaseShape`/`CircleBase`/`RectangleBase`/`BaseShapeGeometry`/`BaseShapeDefaults`, `DistanceUtilities` shape-aware overloads, `IModel.BaseShape` + computed `BaseRadiusInches`, polymorphic round-trip; B — `BaseFileEntry` on `UnitFileEntry`, `UnitData` builds the shape (default circle for old files); C — melee/coherency/pile-in/ranged/spell model-to-model distance routed through `BaseShape`. App (superproject branch): D — army-builder shape dropdown + diameter / W×H inputs; E — `ModelBaseRenderer` (Raylib + ImGui dispatch) drives the main table render, `TableHitTester`, and the movement / placement / consolidation overlay ghosts + hit-tests, all shape-aware. Engine suite **834/0**, full build clean, headless smoke exit 0.
- **Deferred (recorded):** (1) the *collision* bounding-radius remnants (terrain swept-paths, pile-in swept, move-through-enemy, LoS blockers, objective seizure, placement spacing) → **#150**. (2) Minor *decorative* overlay rings still drawn as the bounding circle: the assign-wounds dim/highlight ring (`GuiAssignWoundsResolver`), the ranged target ring (`GuiChooseRangedAttackResolver`), and the tooltip label offset (`TableTooltipOverlay`) — these emphasize a model, not its footprint, so a circumscribing ring reads fine; convert opportunistically. (3) Base facing/rotation (rectangles are axis-aligned) — also a #150 prerequisite.
- 2026-06-25: Opened at the user's request. Design forks resolved with the user (fidelity / scope / input units — see Decisions). Branch created in both repos.

## Outcome
**Slices A–E complete on branch `149-base-shapes` (both repos) 2026-06-25.** A model's base is now an abstract `IBaseShape` (circle / rectangle), authored per-unit in the army builder (shape dropdown + inches), defaulting to the pre-existing 28mm circle. The shape drives the table render + click hit-testing and the movement/placement/consolidation ghosts, and shape-aware model-to-model geometry (melee/charge reach, coherency, ranged/spell range). `BaseRadiusInches` is retained as the bounding-circle seam for the collision paths deferred to **#150**. Engine suite 834/0; build clean; headless exit 0. (Not yet GUI hand-verified in the running window; not merged.)
