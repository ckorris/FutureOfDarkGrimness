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

- 2026-09-08 (slice 5, saved lists): app **2865/0** (+8), build clean, smoke exits 0. "Load list..."
  opens a `.fdgarmy` through the file dialog and the army then stays in the picker as its own entry for
  the session - shared by BOTH columns, which is the point: picking a second unit out of it never
  reopens the dialog.
  - `ArmySource` is the seam. The distinction that matters is whether the file brought a BOOK: a
    Forge-built army embeds one, so it is adopted fully editable and behaves like a bundled faction; a
    hand-authored or imported list has none, so its units are shown as saved and say why. All 27 lists
    in `armies/` are of that second kind today.
  - A saved unit brings along the hero its own list joined to it - that pairing was the author's intent,
    and re-picking it by hand would be busywork. Its rule definitions, spells and effect-set defaults
    are copied into the compiled army too; without them the units would load with their rule names
    unresolved and quietly do nothing.
  - Units are deep-copied out of the file (ids preserved, so the join link survives), so editing a
    column can never write back to the file or disturb the other column.
  - A file that is missing, empty or malformed reports on the panel and changes nothing; it never
    throws out of Draw (the #307 lesson).

- 2026-09-08 (slice 4, hero joins): app **2857/0** (+7), build clean. A column splits into two rows -
  hero always on top, whichever way the join was made - each row with its own gear and upgrades, and a
  Remove that leaves no dangling link.
  - The join picker is locked to the unit's OWN army and to one role (heroes, or hosts), so it cannot
    offer an illegal partner. A Hero over the Tough join cap is deliberately still LISTED: army creation
    refuses it and the report warns by name, which is more use than a unit that silently is not there.
  - Pinned by the merge itself rather than by the link: a squad of 5 with a joined Hero reports 6 wounds
    to chew through and produces no warning - proof that army creation folded the two into one unit.

- 2026-09-08 (slice 6 taken early - melee; engine `HEAD`): engine **3307/0** (+6), build clean. The
  melee tab already worked through the shared batch loop, so this slice was really about the one thing
  melee has that shooting does not: **charge impact hits**, which now run through the engine's own
  `ResolveImpactHitsStage` (a real `CombatActionContext`, `SetDefender`, `OnImpactResolved` bound to a
  pass-through layer) rather than being approximated. A charger with no Impact rule produces no row at
  all, and the dice are probed with the READ-ONLY evaluation so the stage's own live pass stays the only
  one that logs or spends.
  - Melee pins: melee swings melee weapons only (the guns sit it out), fatigue forces 6s, Impact lands
    before any swing (6 dice on 2+ = 5 hits), and the report SAYS strike-back is not included rather
    than leaving its absence to be discovered.
  - Taken ahead of slices 4/5 because it completes the feature as described (shooting AND melee tabs);
    hero joins and saved lists are additive on top.

- 2026-09-08 (slice 3, the screen - shooting, books only): app suite **2850/0** (+12), engine
  **3301/0**, build clean, headless smoke exits 0. `CombatCalculatorScreen` + `CombatCalc/
  {CalculatorSide, UnitPicker}`, wired into the main menu (Combat Calculator sits below Army Forge;
  Load Game and Quit shift down a row).
  - The screen computes nothing. It re-runs `CombatCalculator` only when a fingerprint of both sides
    plus the situation changes, so editing is live but idle frames cost nothing.
  - `CalculatorSide` holds the unit as the Forge's own `BuilderList`, so upgrades, combining and prices
    are the Forge's rules by construction, not by imitation. There is deliberately no points limit.
  - A stray non-ASCII character (an Arabic letter, from a typo in a numeric literal) got into the
    source during this slice and the compiler caught it. The ASCII test now covers the screen's labels
    AND the engine notes it displays verbatim, so the next one fails a test rather than drawing '?'.

- 2026-09-08 (slice 2, shared Forge pieces; engine `e2a9679`): pure refactor, no behaviour change -
  app suite **2838/0** (2836 before, +2 new glossary tests), engine **3301/0**, build clean, headless
  smoke exits 0.
  - `ArmyBuilding/BuilderListEditing` (engine) now owns the list/upgrade edit rules - add, remove,
    combine, the choice mutations, hero-host candidates - beside `ListCompiler`/`ListValidator`, which
    compile and validate the very lists it produces. `ArmyForgeScreen` keeps every member signature its
    50 tests name, as one-line delegates, so those tests were not touched and still pass: that is what
    makes this refactor safe to believe.
  - `ForgeUnitDetail` (app) owns the third column's DRAWING (header, gear, the upgrade editors). The
    availability MATHS stayed on `ArmyForgeScreen`, where its tests point - moving it would have churned
    dozens of call sites for no gain. `ReplacePool` still needs `YieldsTo`, so that pair stayed put too.
  - `BookLibrary` (app) parses the ~90 bundled books once for both screens instead of once per screen
    (~0.5s of JSON), handing each caller its own list. The Forge's private `LoadLibrary` is deleted
    rather than left as a second copy.
  - `RuleGlossary.Build(IEnumerable<SpecialRuleDefinition>)` gives hovers to an army that carries
    embedded rules but no book - the shape a saved `.fdgarmy` has, which slice 5 opens.

- 2026-09-08 (slice 1, engine `ed9fd10`): `Calculator/{CombatCalculator, CombatReport,
  SandboxGameContext}.cs` + `StateMachine/PassThroughLayer.cs`. Shooting works end to end; the melee
  path already shares the same batch loop, but its Impact stage and its tests are slice 6. Engine suite
  **3301/0** (1 skipped), full `dotnet build` clean. 11 tests in `Tests/CombatCalculatorTests.cs`, every
  expectation hand-computed from the rules (10 dice needing 4+ is 5.0 hits, and so on) rather than
  golden-mastered off a first run.
  - Two of those pins matter more than the arithmetic. **Tough(3) survives army creation** - the guard
    against forgetting `UnitCreationRules.Apply`, which would otherwise give every model one wound and
    be wrong about every tough unit in the game, silently. And **Stealth flips exactly across 9in**
    (5+ at 10in, 4+ at 8in), which pins the placement geometry: the distance the rules measure is the
    distance that was asked for.
  - **The `CombatMath` parity test named in the plan was deliberately not written** (recorded, not
    dropped). It would be circular - `CombatMathPinTests` already pins `CombatMath` against these exact
    stages, so something that runs the stages agrees with it by construction. Hand-computed
    expectations pin the arithmetic harder than a cross-check against another estimator would.

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

The ImGui layout is not unit-testable, so these need eyes. After slice 3 (shooting, bundled books):

1. Main menu shows **Combat Calculator** under Army Forge; Load Game and Quit still work (they moved
   down a row). Back returns to the menu.
2. Both columns open showing "Choose an army" - the search box filters the ~90 books, picking one lists
   its units with their stat lines, picking a unit fills the column.
3. **Choose unit** on a filled column reopens at that army's UNIT list, not the army list; Back inside
   the picker steps out to the armies; Cancel returns to the unit you had.
4. Upgrades behave exactly as in the Army Forge (same widgets, same gray-outs), and the points figure
   at the top of the column moves as you buy things.
5. **Combined Unit** doubles the model count and the price, and doubles the dice in the middle column.
6. The middle column updates as you edit either side - no stale numbers, no visible stutter.
7. Distance past a weapon's range greys that weapon's row and says what it reaches; cover and "moved"
   change the Hit/Save lines, and the bracketed tags match what the in-game to-hit beat says.
8. Hovering a weapon's special rule shows its description.
9. **Swap A <-> B** exchanges the columns, upgrades and all.

## Outcome

_Open._
