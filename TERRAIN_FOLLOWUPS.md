# Terrain follow-ups

Cross-cutting tweaks and refactors uncovered while implementing terrain rules.
Not blocking the current phase, but worth doing as the system matures.

## Stage-level

- **`ChooseRangedAttackStage` LoS check vs. `OcclusionCheckStage`.** The former
  filters the target-selection menu (which weapons can hit which units); the
  latter does the authoritative LoS check during fire resolution. Today both
  ask the same question. Once `OcclusionCheckStage` is wired up, consider:
  - Either making the menu filter call a cheaper "is there *any* possible
    line" version, or
  - Caching the per-(attacker, defender) sight result somewhere the resolution
    stage can also read, so we don't recompute.
  The current per-stage snapshot pattern is fine for correctness — this is an
  optimisation note.

- **`OcclusionCheckResults` and `CoverCheckResults` are empty structs.** They
  need real fields once stages fill in:
  - `OcclusionCheckResults`: which (attacker, defender) pairs have LoS, or a
    per-defender bitmap, so downstream stages can drop unreachable models.
  - `CoverCheckResults`: at minimum `int DefenseRollBonus` (or per-defender
    granularity), consumed by the defense-roll stage.

- **`Indirect.OnPreExecute(... OcclusionCheckResults ...)` etc. all throw
  `NotImplementedException`.** Same for `Blast`. Wire these up when the
  occlusion / cover stages do real work — Indirect should treat any `Blocking`
  result as `Clear`; Blast should zero out the cover defense bonus.

- **`ChooseRangedAttackStage`'s LoS cache is keyed on defender model only**,
  not on weapon. Weapon-specific bypass behavior (Indirect) needs to
  short-circuit *before* the cache lookup, not after. Today the cache is moot
  because LoS is hardcoded `true`, but it'll bite as soon as real terrain math
  goes in if we don't update the key or the call order.

## API-level

- **`ITerrain` will accumulate rule-query methods** (`EvaluateSightLine`,
  later `ProvidesCoverFor`, possibly movement-related queries). Watch for the
  point where this stops feeling cohesive and consider splitting into smaller
  interfaces (e.g., `IBlocksSight`, `IMovementHazard`) that a terrain opts
  into.

- **Per-defender granularity for the segment query.** `EvaluateSightLine`
  currently takes two `Position` values. Per the rulebook, LoS and cover are
  per-defender-model checks aggregated to the unit. The helper should iterate
  defender models; consider whether the per-pair API needs a richer shape
  (e.g., bundling base radius) once we move past center-to-center to model
  silhouettes.

## Deferred phases (not bugs, just reminders)

- **Unit perimeters block LoS** — Phase 9. Same algorithm as terrain blocking,
  different shape source (model bases as circles).
- **Height-aware sight lines** — once heightmaps land. The `ITerrain.HeightInches`
  field is already there; the segment query becomes 3D.
- **Difficult terrain caps unit move at 6"** — Phase 4. Touches
  `ChooseActionStage`'s `MaxAdvanceDistance` calculation.
- **Dangerous terrain test** — Phase 5. Lives in
  `ApplyNonMovementTerrainEffectsStage`.
- **`PathTemplate.ValidateAll()` doesn't pass terrain** — currently calls the
  no-terrain `ValidatePaths` overload. PathTemplate isn't on any production
  path right now (only its own tests/samples), so this is harmless until it
  becomes used.
