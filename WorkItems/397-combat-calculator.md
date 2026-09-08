# 397 - Combat Calculator

**Status**: in-progress (filed 2026-09-08; implementation starting)
**Related**: #153 (Army Forge - the column-3 unit detail this reuses), #191 (`CombatMath`, the AI
estimator this deliberately does NOT compute from), #325/#345 (`ShootingForecast` - the
preview-and-resolution-share-one-implementation doctrine this follows), #167 (`ScenarioCompiler` - the
existing "real engine world outside a live game" precedent), #107 (combined squads), #006 (hero join),
#042 (special rules), #209 (weapon-order nondeterminism - why rows sort by `WeaponProfileKey`)

## Goal

A main-menu screen that simulates one unit attacking another and shows expected hits and wounds -
overall and per weapon - with the situational factors (range, cover, moved, charging, fatigue)
adjustable. Three columns: unit A, results/variables, unit B. Each side is built the way the Army Forge
builds a unit (upgrades under the book's constraints, combined squads, a joined hero), sourced from a
bundled faction book or a saved `.fdgarmy` list, with no points limit. Shooting and Melee tabs.

Done = the screen ships, its numbers come from the real combat stages (never a hand-written mirror),
and the deferred facets below are recorded rather than dropped.

## Notes

- 2026-09-08: filed. `git fetch origin` before filing put `origin/master`'s index high-water mark at
  **395**, archive at **396**, detail files at **396**; no `WorkItems/397*` on any remote branch.
  **397 = Combat Calculator.** No collision. Superproject synced to `6fb3162`, engine to `883b676`.
- 2026-09-08: owner sign-offs on the three design forks, before any code:
  1. **Numbers come from running the real stage chain in a throwaway sandbox**, not from `CombatMath`
     and not from a new mirror.
  2. **Unit sources: bundled books AND saved `.fdgarmy` lists** - a list is opened through a file
     dialog and then becomes a quick-pick button, so picking a second unit from the same army does not
     re-open the dialog.
  3. **Name: "Combat Calculator"** (renamed from the working title "Battle Calculator" at plan review).

## Decisions

- **The engine is the calculator; the screen is a view.** A new `FDG.Calculator.CombatCalculator`
  builds a throwaway `GameDataStore`, runs `GameBootstrap.CreateArmy` for both sides (so hero joins,
  combined units and rule attachment are the real ones), and runs the actual
  `BuildTargetList -> DetermineHitRoll -> RollToHit -> DetermineSaveRollsNeeded -> RollToSave ->
  AssignWounds -> ApplyWounds` chain per weapon batch under the `ProbabilisticDiceRoller`. This is the
  `CombatMathPinTests.AssertVolleyPinned` harness promoted to production. Rationale: the project's
  standing rule is that a preview and its resolution share ONE implementation (#325); a second
  estimator would be a second thing to keep honest, and `CombatMath`'s own doc concedes it is the one
  that is wrong when the two disagree.
- **Not `CombatMath`.** It is state-free and already models a melee exchange, but it returns wounds
  only (no expected hits, no threshold chips) and carries documented gaps - no casualty carry-over
  within a unit's shooting, no terrain-gated saves, no marks/one-shots. Running the stages gets all of
  that for free, and strike-back later needs no new math.
- **Wounds are APPLIED between weapon batches**, so the second weapon fires into the casualties the
  first one caused. This is what a live volley does and what `CombatMath` explicitly does not do.
- **Models are placed stacked, not in lines.** Every model of A at one point, every model of B at
  another, separated so the edge-to-edge distance is exactly the requested D. Base-to-base distance is
  edge-to-edge (`DistanceUtilities.GetBaseToBaseDistanceInches_3D`), so every carrier measures the same
  D and casualties never shift it. Nothing in the attack chain reads intra-unit geometry (no occlusion
  blockers, no cover geometry; wound allocation is by wound state), so the overlap is inert. A line
  layout would have made the far models drift out of range as the near ones died.
- **`UnitCreationRules.Apply` must be called explicitly.** `GameBootstrap.CreateArmy` does NOT apply
  Tough / Armor(X) / joined-hero wounds / creation auras; `FDGServer.BuildContextAndLaunch` and
  `ScenarioCompiler.Compile` each run that loop themselves. Without it every model has 1 wound and the
  whole calculator is silently wrong.
- **The player-request answerer is the production AI resolver registry**, not a hand-rolled stub. The
  chain can emit five request types (`AssignWoundsRequest`, `YesNoRequest`, `StringSelectionRequest`,
  `SelectionRequest<ModelData>`, and `PlaceObjectsRequest<ModelData>` when a dying unit Splits);
  `AiResolverRegistryFactory.BuildSoloRules` already answers all five the way the EOF/AI paths do.
- **Forge editing logic is extracted, not duplicated.** The list mutations move to
  `ArmyBuilding/BuilderListEditing` (engine) and the column-3 drawing to `ForgeUnitDetail` (app), with
  `ArmyForgeScreen` delegating - so the Forge and the calculator can never diverge on what an upgrade
  costs or which options are legal.

## Deferred (recorded, not silently cut)

- **Probability bell curve** (owner asked for it explicitly; deferred by agreement). Recommended route
  when picked up: Monte Carlo through the same sandbox with a seeded `RealisticDiceRoller` (~2000 runs,
  milliseconds each) - exact for every rule with zero new math, versus an analytic convolution that is
  exact only for plain profiles. Needs `CombatCalculator.Run(..., IDiceRoller)` and a `Distribution`
  record.
- **Strike-back** (owner: "keep in mind that we want to"). The sandbox already holds the wounded
  defender after A's swings, so it is one more loop with the roles reversed - the hard part is
  presentation, not math. Wound allocation would use the engine's own `AutoFill` order (already-wounded
  first, hero last) and the row must SAY so, because a real defender may choose differently; that
  divergence from real play is exactly why this is deferred until the owner picks the UI.
- **Expected models killed**: fractional wounds do not kill whole models under the probabilistic
  roller. Falls out of the Monte Carlo work; `CombatMath.ExpectedKillsFrom` exists in the meantime.
- **Not priced in v1** (surfaced as report Notes so the screen never lies about them): spendable marks
  and one-shot granted buffs, Unpredictable's per-action die, terrain-gated rules (no terrain in the
  sandbox), line of sight / occlusion (assumed clear), charge-origin distance ("over 9in" rules),
  Takedown's model pick (AI picks first), Regenerative Strength (auto-accepted), defender Shaken,
  spells, Ambush/Aircraft, melee in-range trimming (perfect pile-in assumed), Counter / strike order.
- **Later variables** (owner: "later we can consider things like temporary buffs from special rules"):
  charge-origin distance, defender Shaken, a pre-wounded defender, granted-rule buffs.
- **Freeform Army Builder lists are read-only** in the calculator - no book means no upgrade
  constraints to edit under.
- **Weapon firing order** is the stable `WeaponProfileKey` order, not a player choice. Only matters
  when the defender is nearly dead; capped rows say so.

## HAND-VERIFY (owner)

_Written as the slices land; the ImGui layout itself is not unit-testable._

## Outcome

_Open._
