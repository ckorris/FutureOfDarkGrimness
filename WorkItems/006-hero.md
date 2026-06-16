# 006 — Hero joins unit + takes morale on behalf of unit

**Status**: in-progress
**Related**: #023 (Tough wound-priority — shares the "heroes last" allocation), #031 (Defense/unit rules umbrella, lists Hero), #042 (rule framework), #027 (weapon-scoped rules)

## Goal
Implement the **Hero** special rule (GF Core v3.5.1) as a first-class ability. A 1-model Hero unit joins one friendly multi-model unit (with no other Hero) and fights as part of it for the rest of the game. Slice 1 "done": the merge happens at army setup from an army-file declaration, and the four stat-divergence facets resolve correctly — wounds assigned to the hero **last**, morale tested at the **hero's Quality**, the hero saves at the **unit's** Defense until it is the sole survivor (then its **own**), and the hero's weapons fire at the **hero's** Quality. Eligibility enforced (hero ≤ Tough(6); target multi-model; target has no existing Hero).

Rule text (v3.5.1):
> **Hero:** Heroes with up to Tough(6) may deploy as part of one multi-model unit without another Hero. The hero may take morale tests on behalf of the unit, but must use the unit's Defense until all other models have been killed.
> **Tough(X):** …(heroes must be assigned wounds last, even if already wounded).

## Decisions
- **Representation = merge + hero metadata (Approach A).** The hero's `ModelData` is physically moved into the host `UnitData.Models`; the host carries a small `HeroAttachment { heroModelId, Quality, Defense }`. This reuses ALL existing per-unit machinery (targeting, LoS, save rolls at the unit Defense, coherency, movement, activation) unchanged — the combined unit is one `IUnit` for everything hard to touch. Only the stat-divergence facets need new code, and each hangs off a seam that already exists. Rejected: per-model Quality/Defense on `IModel` (large blast radius — every `unit.Quality`/`unit.Defense` read would need auditing) and linked/attached units (breaks the "enemy shoots one combined unit" model; many special cases). The user explicitly accepted "a little hard-coded engine treatment" here, like Tough/AP.
- **Join is declared in the army file** (not interactive at deployment, not auto-picked). Merge happens at army-setup in `FDGServer.CreateArmyDataFromArmyFile`. Chosen for determinism + testability; no army-builder UI churn for slice 1.
  - **Target reference**: add an authorable `string? Id` to `UnitFileEntry` and a `string? JoinsUnitId` on the hero's entry. `StableID` is a per-process counter (get-only, unserialized) so it can't be the authoring handle. The unit still carries the `Hero` core special rule (marker + Tough-cap eligibility); `JoinsUnitId` names the host.
- **Why the merge is engine code, not a hook Effect**: the merge needs cross-unit info (the host unit), which the single-unit creation hook (`Lifecycle_OnUnitCreated`, used by Tough) can't see. So the merge runs as explicit setup code (the way `UnitCreationRules.Apply` runs after construction), and `Hero` is added to `CoreRuleCatalog` mainly as a recognized marker carrying eligibility.
- **Attacks batch by weapon type** (`CombatActionContext.GetTypeSortedWeapons` via `WeaponComparer`), and `AttackBeatPositions.FiringModels(unit, weaponType)` already maps a batch back to its owning models. So the hero's distinct weapon is already its own hit-roll batch — "hero fires at own Quality" is one conditional in `DetermineHitRollNeededStage`, not a refactor.

## Deferred (recorded — not silently cut)
- **Same-weapon pooling collision**: if the hero carries a weapon *identical* (by `WeaponComparer`) to the rank-and-file, `GetTypeSortedWeapons` pools them into one batch and the hero's Quality can't be peeled out without splitting the batch. Rare (heroes almost always have unique gear). Slice-1 behavior: that pooled batch fires at the unit's Quality. Fix later by splitting the hero out of any shared batch in the attack-builder.
- **Hero's own special rules / activated abilities while joined**: carrying the hero's non-stat rules (auras, Caster, etc.) onto the merged unit needs model-scoped rule carriage, which doesn't exist (rules are unit- or weapon-scoped). Slice 1: the hero contributes its **weapons** and its **baked-in wounds** (Tough already sets the model's max wounds at creation, before the merge), but its unit-level special rules do not ride along. Follow-up slice.
- **Force-org Hero cap** (1 hero / 500pts) is #003, not here.

## Plan (vertical sub-slices — each: implement → integration test mirroring nearest *RuleIntegrationTests → verify → commit → update this ledger)
- **A. Merge primitive + eligibility.** Army-file `Id`/`JoinsUnitId`; `HeroAttachment` on `UnitData`; merge in setup (move hero models into host, drop hero's standalone unit from the army/pool); `Hero` rule def in `CoreRuleCatalog`; eligibility (Tough≤6, host multi-model, host has no hero) → reject+log. Test: `HeroRuleIntegrationTests` — host gains the hero model, hero unit absent from pool, rejections fire.
- **B. Wound-last ordering.** `AssignWoundsResults.AutoFill` (+ assign request/resolvers) assign the hero model last. Intertwines with #023's Tough ordering — scope here is hero-last only.
- **C. Morale at hero's Quality.** `DetermineMoraleSaveNeededStage` / `MoraleUtilities` use the living hero's Quality as the base roll-needed.
- **D. Last-model Defense swap.** `DetermineSaveRollsNeededStage`: when the sole living model is the hero, save at the hero's Defense.
- **E. Hero fires at own Quality.** `DetermineHitRollNeededStage`: if the weapon-batch's owning model is the hero, use the hero's Quality (resolve owner via the `FiringModels` pattern). Honor the deferred collision case.

## Notes
- 2026-06-15: Item opened. Deep analysis of rule + engine done (this turn). Forks resolved with the user: representation = merge+metadata; join = army-file declaration; slice-1 scope = join + all 4 stat facets + eligibility, deferring the same-weapon collision and hero's-own-rules-while-joined. Starting sub-slice A.
