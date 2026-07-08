# FutureOfDarkGrimness — New-Subsystems Audit

**Date:** 2026-07-06
**Scope:** Seven subsystems built in the `FutureOfDarkGrimness` engine (+ paired `FdgRaylib` app code where inseparable) **since** the 2026-06-10 audit (`Audit-6-10-2026.md`): Army Forge, base-shape geometry & facing, per-model rule dispatch & primitive growth, Transport(X), Caster(X)/spell-casting, Morale & Fatigue, and the client/GUI feature cluster (chat, token display, health bars, movement-hazard overlay, shooting fixes). This is a companion document to `Audit-6-10-2026-Followup-2026-07-06.md` (which diffed the *original* audit's findings against current state) — this one covers ground the original audit never touched, because the code didn't exist yet.
**Method:** Seven independent deep-dive passes (one per subsystem), each reading the relevant source in full, hunting for concrete defects (not style opinions), and scoring on the same scale as the original audit: a 6 is a "solid hobby-engine" baseline, 7 is genuinely good, 10 would be reference-implementation quality. Findings below are as reported by those passes, lightly edited for consistency; every "Fixed" bug listed in §8 was independently verified against the working tree by re-reading the actual diff before this document was finalized.

Bugs are in **§8 — Bug log**. Of 22 distinct defects found, **13 were unambiguous, small enough to fix directly, and were applied to the working tree** (uncommitted — review the diff before committing); the rest are logged with proposed solutions or explicit design forks that need your sign-off per this repo's "surface design forks before building anything non-trivial" convention. After the fixes: engine suite **1140/1140**, app suite **88/88**, full build clean both projects, headless smoke exit 0 with the expected "Game ended" line.

## Contents

1. [Army Forge (army builder)](#1-army-forge-army-builder) — **8/10**
2. [Base-shape geometry & facing](#2-base-shape-geometry--facing) — **7/10**
3. [Per-model rule dispatch & primitive growth](#3-per-model-rule-dispatch--primitive-growth) — **7/10**
4. [Transport(X)](#4-transportx) — **6/10**
5. [Caster(X) / spell-casting](#5-casterx--spell-casting) — **7/10**
6. [Morale & Fatigue](#6-morale--fatigue) — **8/10**
7. [Client/GUI additions](#7-clientgui-additions) — **7/10**
8. [Bug log](#8-bug-log)
9. [Summary & recommendations](#9-summary--recommendations)

---

## 1. Army Forge (army builder)

**Overview:** A catalog-driven list builder — `BookFile`/`RosterUnit`/`UpgradeSection` describe a faction's roster, `BuilderList` is the editable work-in-progress, `ListCompiler` turns the two into a playable `.fdgarmy` (`BuiltArmyFile : ArmyListFile`), and `ListValidator`/`LaunchGate` gate legality before the engine ever sees it. `OprBookImporter` is a one-time offline converter from OnePageRules JSON into a `.fdgbook` snapshot.

**Pros:**
- Cost math is careful and heavily edge-case-tested: `Applications()` (`ArmyBuilding/ListCompiler.cs:180-202`) correctly clamps Replace-One/Any/All against actual available targets (min-across-targets for combined targets, max-across for All), and `ArmyForgeCompilerTests.cs` exercises no-target no-op, plural-target matching, combined-target-requires-every-part, and section-order-not-click-order — real defensive tests, not just happy paths.
- Combined-squad cost accounting is correct and tested: `MergeCombinedUnits` (`ListCompiler.cs:53-93`) sums cost/model-count/weapons from each copy's *independently* compiled cost, matching GDF's "pay for both individually" rule.
- Rejection paths are actually tested, unlike most of this codebase per the original audit: `ListValidatorTests.cs` has dedicated tests for over-points, over-model-count, Unique-duplicate, dangling/cross-roster/triple combine links, hero-join ineligibility, and pick-cap-exceeded.
- The ASCII-fold discipline (CLAUDE.md's hard rule) holds in shipped content — all 19 bundled `.fdgbook` files under `FdgRaylib/Assets/Books/` were scanned byte-for-byte with zero non-ASCII bytes found, confirming `OprBookImporter.AsciiFoldJsonValues` works end-to-end on the real corpus, not just synthetic test strings.
- `BuiltArmyFile : ArmyListFile` needing "NO engine change" is a genuinely clean trick — System.Text.Json's default unmapped-member handling means the engine reads a Forge file as a plain army for free.

**Cons -> recommendations:**
- `UpgradeSection.MinPicks` (`BookModel.cs:136`) is declared and documented ("Pick between MinPicks and MaxPicks") but **never read anywhere** — not by the importer, not by the compiler, not by the validator's pick-cap check (only flags `picks > max`). A mandatory "choose at least one" section can be skipped entirely with zero warning. No shipped book currently uses `MinPicks > 0`, so this is dormant. → Have the importer set `MinPicks` for OPR `"exactly"` selects, and add a `picks < MinPicks` Error alongside the existing check.
- `MergeCombinedUnits`'s rule fold (`ListCompiler.cs:79-81`) dedupes by exact record equality, so two combined copies carrying the *same named* rule with *different* numeric values (e.g. `Tough(3)` on one, `Tough(6)` on the other from an asymmetric upgrade) both survive and only the first is ever read. Needs a design call (max? warn? forbid?), not a one-line fix.

**Novelty:** Nothing architecturally novel — catalog + selections + compiler + validator is the standard army-builder shape. The distinctive move is making the compiled file *be* an `ArmyListFile` subtype so the engine needs zero changes to consume it, and freezing a full book snapshot inside every save for reproducibility.

**Test coverage notes:** Unusually strong for this codebase — cost math, all four upgrade variants, combine/hero-join happy *and* rejection paths, and OPR corpus-verbatim shapes are all covered. Highest-risk gap: no test fed the importer malformed/garbage JSON before this pass (now guarded — see §8), and no test exercises the `MinPicks` lower bound (consistent with it being dead code).

**Score: 8/10** — cost/validation logic is careful and (unusually for this repo) tested on its rejection paths, and the "no engine change needed" file design is genuinely clean; held back by a real dead validation field and a narrow but real combined-unit rule-conflict gap.

---

## 2. Base-shape geometry & facing

**Overview:** Replaces the old single-radius approximation with a unified `IBaseShape` -> `BaseFootprint` (rounded convex hull) abstraction that a single SAT-based routine (`BaseShapeGeometry.SurfaceGap2D`) measures for any shape pair at any facing, plus per-model yaw threaded through movement, pile-in, LoS, and consolidation/deploy formation packing.

This subsystem went through a real, well-documented bug saga (work item #159) involving four independent root causes (a SAT overlap sign error, PileInStage not obstacle-checking third-party enemies, `CohesiveFormation` packing by the largest base size instead of per-model size, and rigid-delta consolidation unable to re-form a casualty gap). This section verified those fixes and hunted for sibling bugs of the same shape.

**Pros:**
- The footprint seam genuinely eliminates shape-pair branching: every shape implements one method, and collides against every existing shape for free, with circle-vs-circle proven byte-identical to the old formula.
- The #159 SAT fix is correct and minimal — hand-verified the boundary math: `overlap == 0` legitimately arises both for touching bases and for a zero-width projection strictly inside a rectangle's slab, and the `<= 0f` -> `< 0f` change at `BaseShapeGeometry.cs:229` fixes the containment case while leaving touching and circle-vs-circle untouched.
- The pile-in fix is real and correctly scoped: `PileInStage.cs:20-30` builds the obstacle set as every enemy of the *defender* excluding the *charging* unit; every other caller of the underlying footprint helper correctly uses the "all enemies" default.
- Facing convention is consistent where it matters most — the collision seam and the LoS-blocker/swept-path seam agree on rectangle corner placement for a given facing.
- `CohesiveFormation.PackGrid` correctly fixes the "pack by largest base" bug: per-row height from the tallest model, per-model width, with a bump so no row strands a single model.
- Float-margin discipline is followed at the new boundaries that need it (`-0.001f` on both the closed-form circle path and the binary-searched rectangle path in `PileInUtilities`).

**Cons -> recommendations:**
- **The exact "pack by largest base" bug is still live in the deploy path**, in a sibling file that never got the `CohesiveFormation.PackGrid` fix. `AiPlaceObjectsResolver.Resolve` computes one uniform spacing from the *maximum* half-extent across **all** models in the unit, then applies that single spacing to every grid cell — there is no per-row sizing. A unit mixing one large-based model (e.g. a hero join) with several small-based models will deploy the small models spaced apart by the large model's footprint, stranding them past the 1" cohesion rule from turn 0. → Route `AiPlaceObjectsResolver`'s grid layout through `CohesiveFormation.PackGrid` (or port its per-row logic).
- Duplicated SAT implementations: `BaseShapeGeometry`'s hull-vs-hull SAT and `SweptBaseGeometry`'s polygon-vs-primitive SAT are two independently written, near-identical separating-axis routines — precisely the shape of risk that produced four independent bugs for one symptom in #159. A future boundary-condition fix to one has no mechanism to propagate to the other.
- The SAT penetration estimate is, by design, only a magnitude *estimate* for genuine interpenetration — for a small shape fully contained in a large one, the per-axis overlap equals the small shape's own projected width, not true penetration depth. No live caller currently consumes the depth numerically, so this is a documentation note, not a functional bug.

**Novelty:** The rounded-convex-hull-as-universal-shape seam is a clean, somewhat uncommon simplification of standard SAT/GJK collision for a 2-shape-type domain; the four-root-cause forensic bisection recorded in #159 (snapshot-before/after per stage) is a genuinely good debugging technique.

**Test coverage notes:** Core geometry is thoroughly covered with regression tests naming #159 by scenario. The highest-risk gap is exactly the finding above: `AiPlaceObjectsResolverTests.cs` has a uniform-rectangle packing test but nothing exercising a mixed-base-size unit through the deploy resolver, unlike the equivalent (and fixed) test for movement/consolidation.

**Score: 7/10** — the collision seam design and the #159 fixes themselves are genuinely good, but the identical spacing defect the ledger explicitly fixed once was left unfixed in a sibling file with no test to catch it.

---

## 3. Per-model rule dispatch & primitive growth

**Overview:** Work item #093 added a genuine per-model composition axis (`EModelRuleScope.AnyOwner`/`AllOwners`) to the hook-dispatch pipeline, plus a small imperative-operation seam (`ExecutableOperation`/`IOperationServices`/`OperationExecutor`) that mirrors the original `SinkOperation` pattern for effects that must call back into engine subsystems. Both are well-documented and correctly wired where the code plumbs models through — but per-model dispatch is opt-in per call site, and it was never extended to the Subject (defensive) seat, which creates a real attribution gap for joined heroes.

**Pros:**
- `EModelRuleScope` is precisely specified (union vs. intersection), and `RuleEvaluator`'s model-aware overloads implement both correctly, with targeted tests proving no-stack behavior across shared owners and correct suppression when only some batch owners carry the rule.
- Per-model movement budgets are wired end-to-end, not just computed and ignored: `MovementActionContext` builds each living model's own cap, and `DefinePathStage` threads the same lookup through both the resolver request and the authoritative post-resolve validation, so preview and enforcement can't drift.
- The imperative seam avoids the async-void hazard the original audit flagged in the stage machine — `OperationExecutor.Execute` is a real `async Task`, awaited by its callers.
- The original audit's three "never applied" operations are now genuinely consumed, and 2 of the original 3 stub Effects are now implemented. The stub-bomb list shrank, not grew.

**Cons -> recommendations:**
- **A hero-join rule-attribution gap (high severity — see §8 item 17). — FIXED 2026-07-08 (#183).** `HeroJoinResolver` relocated every non-Hero rule from a joining hero onto the hero model unconditionally, but per-model dispatch only existed at the Actor seat; every Subject-seat call site passed `models: null`. Any hero-carried defensive rule that wasn't one of the 3 rules special-cased via `Condition.AllModelsHaveThisRule` (Stealth/Fearless/Regeneration) silently stopped firing the moment the hero joined a unit. Now every unit-scoped defensive rule carries the gate (validator-enforced) and every Subject-seat site threads the defender's living models.
- Two independent "does every model share this rule" mechanisms now coexist (the per-model-dispatch `AllOwners` path, and the self-walking `Condition.AllModelsHaveThisRule` hardcoded to exactly 3 rules) with nothing signaling to a future author which to reach for.
- The sink/operation vocabulary kept growing in the same additive style the original audit predicted would eventually want consolidation: sink interfaces 8->11, operation records 30+->34, and a **third** shape was added alongside the original two rather than folding into anything generic.
- `OperationExecutor.Execute` was omitted at 6 call sites that apply operation queues — harmless today (no catalog rule currently needs it there), but undocumented. **5 of 6 fixed in this pass (§8); the 6th (`UnitCreationRules.cs`) deferred** — its `Apply` method is synchronous and sits at the bottom of a private, non-async launch-sequence call chain (`FDGServer.BuildContextAndLaunch`), so closing this one properly means converting that chain to async, not a one-line addition.

**Novelty:** Per-model batch-scoped rule composition (union vs. intersection, chosen per call site) is a genuinely distinctive extension of the hook-dispatch idea; the imperative-operation seam is a clean but standard command-pattern device, well-executed rather than novel.

**Test coverage notes:** Strong for the paths that are wired — weapon-batch composition, hero attribution for the *attacker* side across 4 scenarios, and movement budgets. The highest-risk gap was exactly where the headline bug lived: no test exercised a hero-carried **Subject-seat** rule post-merge. **Closed 2026-07-08 (#183):** `HeroSubjectRuleIntegrationTests` (dormant / sole-survivor / host-lacks / dedup / trace) + `AllModelsRuleGateIntegrationTests` (one per effect class) now cover it.

**Score: 7/10** — the wired slices are careful, tested, and well-documented; it loses points for a real, currently-unguarded attribution bug in the headline hero-join feature and continued unconsolidated vocabulary growth.

---

## 4. Transport(X)

**Overview:** A Transport unit carries friendly units aboard via a cross-unit `EmbarkedIn` token (no bespoke state field); occupants are represented as off-table, loaded at deployment or mid-game via dedicated Embark/Disembark move actions, and spilled onto the table with a dangerous-terrain test + Shaken when the transport dies in combat.

**Pros:**
- Token-derived occupancy is a clean, zero-footprint state model — occupancy queries the token container live rather than caching a roster, so there's no dual-source-of-truth to desync.
- Off-table representation gets targeting/activation/objective exclusion for free via the existing on-battlefield filter, and correctly returns false for a fully-destroyed transport.
- Hero-aware space cost is right and tested: a Hero costs 1 space even at high Tough, with dedicated mixed-unit tests.
- Save/load resume is solid: the `EmbarkedIn` token (including its cross-unit owner reference) round-trips via the normal token container, and the #095 rule-rehydration fix additionally restores the Transport rule's capacity and abilities on resume.
- Good separation between deterministic rules-layer effects and stage-driven presentation: the spillout effects function returns per-model roll data rather than presenting directly.

**Cons -> recommendations:**
- **A destruction-path gap (high severity — see §8 Bug 1).** Spillout only fires from the shooting and melee-swing pipelines, both of which finish before `AssignMeleeMoralePenaltyStage`'s Rout path runs. A Transport that survives a melee exchange but Routs from losing at half strength is killed directly with zero knowledge of its occupants — they remain permanently embarked, off-table, and un-spilled ("ghost" state), since `EmbarkedIn` is deliberately never auto-swept.
- Spillout drops the placement facing that disembark preserves (fixed, §8).
- Disembark/spillout placement requests pass every model binding, dead included, forcing the player to place a corpse in the cramped 6" zone (fixed, §8).

**Novelty:** Nothing exotic — reusing the token/off-table-reserve primitive from Ambush for a structurally different mechanic (cargo, not deployment delay) is a tidy, low-cost reuse rather than a new pattern.

**Test coverage notes:** The deterministic core is well covered (51+ cases across the Transport test files). Gaps: no test drives a transport to destruction via any path other than the direct attack that damages it — exactly where the headline bug lives.

**Score: 6/10** — the foundation (token occupancy, capacity math, save/load) is genuinely solid, but the destruction-handling story has a real silent-failure mode and a real placement-fidelity bug, both previously untested.

---

## 5. Caster(X) / spell-casting

**Overview:** `CastSpellStage` and its supporting cast (`SpellTargeting`, `ResolveSpellDamageStage`, `CastAssistRequest`) implement Caster(X) as a full vertical slice — token economy, three effect archetypes (damage/buff/conditional), and a #103 cast-assist mini-game — reusing the established rule-dispatch/token/request machinery rather than inventing new plumbing.

**Pros:**
- Token grant is correctly round-scoped, not activation-scoped, with the 6-token cap clamped at grant time.
- The cast roll is a genuine threshold shift on a single decisive roll, not a post-roll fudge — matching this codebase's own established decisive-roll convention, with a correct `[1,6]` clamp.
- Assist tokens are spent unconditionally and can't be over-spent — clamped and deducted before the roll happens.
- Off-battlefield/embarked exclusion is correct on both the assist and target sides.
- RuleGrant buffs don't silently double-stack when recast on the same target.
- 17 integration tests cover the token economy, all three effect archetypes, per-hit AP folding, and both assist directions.

**Cons -> recommendations:**
- **An embarked-Caster loophole (medium severity — see §8, fixed in this pass).** Every sibling action gate (`GetCanMove`/`GetCanCharge`/`GetCanShoot`) checks `TransportUtilities.IsEmbarked` and blocks with "Embarked; disembark first." — `GetCanCast` did not, and embarked units remain in the activation pool (to disembark on their own turn). Since embarking sets a unit's models to the world-origin sentinel, and targeting has no on-battlefield filter on the caster's own position, an embarked Caster could be offered — and complete — a Cast action from inside its transport if any real unit happened to sit within spell range of world origin.
- `TargetSelector.RequiredToken`/`RequiredRule` are documented as part of the targeting contract and implemented for pre-attack abilities, but not for spells — dormant today (no shipped spell uses them), same latent-bomb pattern as elsewhere in the Rules system.
- `SingleModel` targeting has no enforcement of its documented "pair with MaxCount = 1" contract; a mis-authored multi-target spell could misattribute wounds to one model across every looped target. Not currently exercised by any shipped spell.

**Novelty:** Nothing architecturally new — it's the established hook/token/request machinery applied to a new action — but the cast-assist mini-game (sequential friendly-then-enemy token bidding with a single shared threshold-shift roll) is a clean, minimal design.

**Test coverage notes:** Token economy, both effect archetypes, per-hit AP folding, and both assist directions are all covered. Highest-risk gap: every damage-spell test disables line-of-sight checking, so there is no integration test proving spell targeting actually respects blocking terrain.

**Score: 7/10** — a well-tested, correctly-modeled economy and roll mechanic, pulled down by a genuinely reachable embarked-Caster loophole (now fixed) that broke an otherwise-consistent per-file gating pattern, plus two dormant contract gaps inherited from the same "latent bomb" pattern seen elsewhere.

---

## 6. Morale & Fatigue

**Overview:** Since the original audit documented these as no-ops, morale and fatigue have gone from stubs to a real, rule-aware, well-tested subsystem: a single morale-test primitive drives every morale path, one half-strength predicate decides Shaken-vs-Rout everywhere, and Fatigue correctly scopes "hits only on unmodified 6s" to the rest of the round via a round-clearing token.

**Pros:**
- Half-strength has exactly one implementation, and both the ranged and melee morale paths call the same extension method, so they can't disagree on the threshold.
- Shaken activation-skip and recovery match CLAUDE.md exactly, and both Shaken and Fatigued tokens no-op on re-application (idempotent), pinned by tests.
- Fatigue timing is correct — round-scoped, not activation-scoped, verified by a test proving a unit fighting twice in one round (charge, then counter-swap) fatigues correctly without losing the flag across the role swap.
- Fear vs. Fear cancels correctly — the wound-equivalent bonus is added symmetrically to each side's own total before comparing, so equal Fear nets to no change.
- The Rout mechanic (lethal-wounds-to-all in lieu of a removal primitive) doesn't cascade into re-entrant morale/fatigue checks.
- The `RollDecisive` threshold-shift invariant holds everywhere checked in this subsystem — no post-roll overrides, no parallel re-roll mechanisms.
- A self-caught rules bug (dangerous terrain wrongly triggering a morale test) was found and fixed mid-development with regression tests, a good sign of the team's own review discipline.

**Cons -> recommendations:**
- Fear gates on `Condition.Always()`, while Fearless/Stealth/Regeneration use the whole-unit-aware `AllModelsHaveThisRule` specifically so a joined hero lacking the rule breaks it for the host unit. A unit with Fear(X) joined by a non-Fear hero still gets the full bonus — an asymmetry that needs a rulebook check before fixing (not applied in this pass; logged).
- **Fixed in this pass (§8):** a Fatigued melee attacker's one-shot "+1 to hit" grant token was being consumed (spent) by the modifier-folding step even though Fatigue immediately overrides the threshold to a flat 6, wasting the buff for no effect instead of carrying it to the attacker's next non-fatigued attack.

**Novelty:** Nothing exotic by design — a shared "decisive roll + threshold clamp" primitive feeding one morale-test function is the correct, unsurprising shape for this problem, applied consistently rather than reinvented per stage.

**Test coverage notes:** Strong — half-strength boundaries, double-jeopardy guards, the Fatigue charge/strike-back/counter-swap matrix, Shaken's auto-fail short-circuit, and the decisive-roll primitive itself are all directly tested. Highest-risk gap: no test either confirms or denies the Fear/joined-hero asymmetry noted above.

**Score: 8/10** — the strongest evidence in this section is that this subsystem replaced a documented no-op with something genuinely good: one shared roll primitive, one half-strength formula, idempotent tokens, and a self-caught rules bug with regression tests.

---

## 7. Client/GUI additions

**Overview:** This cluster (in-game chat + log console, token display chips + tooltips, unit health bars, movement terrain-hazard flagging, dead-model shooting fixes) is app-side rendering and resolver work built on top of already-engine-validated data. The code is more disciplined than a typical hobby-project increment: locking, epsilon conventions, and dead-model filtering are handled correctly almost everywhere checked, including in places the shooting fixes didn't directly touch.

**Pros:**
- The chat/log console's arrival-order merge is built on a small, mostly-correct thread-safe log with a globally-monotonic sequence shared across both log instances, which is what lets the two-pointer merge interleave two independently-produced streams correctly.
- The dead-model filtering discipline from the #157/#158 shooting fixes generalizes well beyond the two files it patched — the movement targeting overlay, tooltip overlay, and unit-selection resolvers all independently filter live/placed models, a consistently-applied convention rather than a one-off patch.
- The new terrain-hazard preview reuses the engine's actual float-precision epsilon constant rather than inventing its own approximation — exactly the class of preview/validator mismatch CLAUDE.md warns about, done correctly.
- The token-chip renderer deliberately uses a stable hash (FNV-1a) instead of `string.GetHashCode()`, which is randomized per process — a subtle correctness point that's called out and handled.
- Went looking for an Army-Forge-style stale-index bug in the deploy/placement resolver's drag/undo state and didn't find one — it's defensively cleared on every relevant transition.
- Work item #161's already-tracked "resolver UI consistency" complaints (stats shown inconsistently, no right-click undo on one resolver, heavy duplication across canvas resolvers) were independently confirmed accurate on direct inspection.

**Cons -> recommendations:**
- **Fixed in this pass (§8):** `GameLog.Add` assigned its sequence number outside the lock protecting the message list, so two concurrent callers (the engine thread and the main-thread crash handler both call `Add`) could append entries out of order within one log instance, breaking the merge's sortedness assumption.
- **Fixed in this pass (§8):** a duplicate-suppression field in the log-forwarding class was read/written without a lock across the same two threads.
- `GuiModelSelectionResolver`'s ring/highlight drawing never checks whether a model is alive (only the unplaced-position sentinel), unlike its sibling unit-selector. Both current callers pre-filter to living models before building the request, so this is a latent defense-in-depth gap, not a live bug — flagged as an addendum to #161's proposed shared canvas-selector base rather than patched independently here.
- **Fixed opportunistically in this pass (§8):** a pre-existing (predates 2026-06-10, outside this cluster's own scope) ASCII-rule violation was found in the terrain-placement resolver — a literal degree sign rendered in-game as `?` per CLAUDE.md's documented font-atlas limitation.

**Novelty:** Nothing architecturally new — chip/badge overlays, health bars, and a merged log/chat console are standard UI patterns. The interesting engineering is in the discipline: reusing the engine's exact epsilon and alive-filtering conventions rather than re-deriving looser app-side approximations, which is where this kind of app/engine boundary usually rots.

**Test coverage notes:** Pure-logic helpers are well covered. The highest-risk gap was the console/chat merge itself — no test existed for the log's sequence-ordering guarantee (exactly where the race lived, now fixed).

**Score: 7/10** — genuinely good: the dead-model and float-epsilon discipline generalizes past the two commits that introduced it, and the resolver-consistency problems are already tracked accurately in #161. Held back by the (now-fixed) concurrency bug in the log merge and thin test coverage at the render/resolver integration layer.

---

## 8. Bug log

### Fixed during this audit (in working tree, uncommitted — review with `git diff`)

All 13 were judged unambiguous, small-enough-to-fix-directly defects; engine suite 1140/1140, app suite 88/88, full build clean, headless smoke exit 0 after the batch.

1. **`ArmyBuilding/OprBookImporter.cs`** — malformed OPR JSON threw an uncaught `JsonException` through the whole `--import-opr` CLI path. Wrapped the parse/fold calls in try/catch, rethrown as a readable `InvalidOperationException`.
2. **`FdgRaylib/Rendering/ArmyForgeScreen.cs` (`AdoptLoaded`)** — the toolbar's book dropdown index was never updated when loading a saved army, so it could show a stale/mismatched book name. Now resolves and sets `_bookIndex` from the loaded book's name.
3. **`FdgRaylib/Rendering/ArmyForgeScreen.cs` (`SetCombined`)** — un-combining a unit while viewing the *spawned copy* row left the selection pointing at an unrelated later unit instead of the surviving base partner, because the generic index clamp didn't know which row survived. Now explicitly resolves and adjusts the surviving partner's post-removal index.
4. **`.../PreAttackStage/PreAttackStage.cs`** — added the missing `OperationExecutor.Execute` call alongside the existing token-operation applier (harmless today, closes a latent gap for future rules using imperative effects at this hook).
5. **`.../CustomActionStage/CustomActionStage.cs`** — same fix as #4.
6. **`.../MovementStage/StrafingStage/StrafingStage.cs`** — same fix as #4.
7. **`.../DeterminePlayerTurnStage/DeterminePlayerTurnStage.cs`** — same fix as #4; required converting the private `ApplyReactivationOps` helper from synchronous to `async Task` and awaiting it at its one call site.
8. **`.../StartOfTurnExtraActionStage/StartOfRoundExtraActionStage.cs`** — same fix as #4; required converting `GrantSpellTokens` to `async Task` and awaiting it at its one call site.
9. **`.../SpilloutOccupantsStage/SpilloutOccupantsStage.cs`** — spillout placement never applied the player-chosen facing (only position), unlike the sibling Disembark stage. Added the matching `SetFacing` call.
10. **`.../SpilloutOccupantsStage/SpilloutOccupantsStage.cs`** — placement requests included dead models, forcing the player to place a corpse. Filtered to living models before building the request.
11. **`.../DisembarkStage/DisembarkStage.cs`** — same dead-model fix as #10.
12. **`.../ChooseActionStage/ChooseActionStage.cs` (`GetCanCast`)** — missing the `TransportUtilities.IsEmbarked` gate present on every sibling action-availability check (`GetCanMove`/`GetCanCharge`/`GetCanShoot`), letting an embarked Caster be offered a Cast action. Added the matching gate.
13. **`.../DetermineHitRollStage/DetermineHitRollStage.cs`** — a Fatigued melee attacker's one-shot "+1 to hit" grant token was consumed by the modifier-folding step even though the very next lines override the threshold to a flat 6 (Fatigue "ignores all modifiers"), wasting the buff. Reordered so the fatigue check runs first and the grant-consumption step is skipped when fatigued in melee, so the buff carries to the attacker's next non-fatigued attack instead.
14. **`FdgRaylib/Rendering/GameLog.cs` (`Add`)** — the sequence number was assigned via `Interlocked.Increment` *outside* the lock guarding the message list, so two concurrent callers (engine thread + main-thread crash handler) could append entries out of sequence order within one log instance, breaking the chat/log console's arrival-order merge invariant. Moved the increment inside the lock so assignment and append are atomic.
15. **`FdgRaylib/Rendering/GuiLogMessageUI.cs`** — the consecutive-duplicate-suppression field was read/written with no lock across the same two threads as #14. Added a dedicated lock around the compare-and-set.
16. **`FdgRaylib/Rendering/Resolvers/GuiPlaceOneTerrainResolver.cs`** — three rendered strings used a literal degree sign (`°`), a non-ASCII character that renders as `?` in-game per CLAUDE.md's font-atlas rule. Pre-existing (predates 2026-06-10, outside this cluster's own scope) and caught opportunistically. Replaced with `deg`.

### Logged, not fixed (design decisions or deliberately deferred)

17. **Hero-join Subject-seat rule attribution (§3, high severity). — FIXED 2026-07-08 (#183, Option C).** A hero carrying a plain defensive (Subject-seat) rule — e.g. Evasive — silently stopped working the moment it joined a unit, because `HeroJoinResolver` relocates the rule onto the hero model, but per-model dispatch was never extended past the 3 special-cased defensive gates (Stealth/Fearless/Regeneration) to every other Subject-seat call site. Fixed both directions with one mechanism: the all-models gate (`Condition.AllModelsHaveThisRule`) now covers all 12 unit-scoped defensive rules (enforced by a new `RuleValidator` check), and every Subject-seat dispatch site threads the defender's living models so the hero's relocated rule is collected, evaluated, and traceable (gate then decides whether it applies — including the sole-survivor case). See `WorkItems/Archive.md` / `WorkItems/183`.
18. **Rout doesn't trigger Transport spillout (§4, high severity).** Spillout only fires from the shooting/melee-swing pipelines; a Transport that Routs from a melee-morale loss is killed directly with its occupants permanently stranded off-table (still embarked, never Shaken, never dangerous-terrain-tested, invisible to activation/targeting/objectives). Fixing this requires either extracting the interactive placement flow into a helper callable from a second stage, or restructuring around a single choke point — while preserving the team's existing, deliberate rejection of an automatic token-sweep (to keep spillout ordering deterministic). **Design decision needed**, not a one-liner.
19. **`AiPlaceObjectsResolver` packs mixed-base-size units by the largest model's footprint (§2, medium severity).** The exact bug `CohesiveFormation.PackGrid` fixed for movement/consolidation was never ported to the deploy resolver's independent grid-packing code, so a hero-joined unit can deploy with small models stranded past the cohesion cap from turn 0. Fixing it well means routing deploy through the same `PackGrid` logic rather than patching a parallel implementation — worth a dedicated pass rather than a blind port.
20. **Army Forge: `UpgradeSection.MinPicks` never enforced (§1).** A mandatory "pick at least one" upgrade section can be silently skipped. Dormant (no shipped book currently sets `MinPicks > 0`) but a real validation gap if one ever does.
21. **Army Forge: asymmetric numeric rule conflict on Combined units (§1).** Two combined copies with different values of the same named rule (e.g. `Tough(3)` vs `Tough(6)`) both survive the merge; only the first is ever read. Needs a ruling on which value should win, or whether the combination should be forbidden.
22. **Caster: `RequiredToken`/`RequiredRule` unchecked in spell targeting (§5).** Implemented for pre-attack abilities but not spells; dormant (no shipped spell uses either field yet).
23. **Caster: `SingleModel` + `MaxCount > 1` misattribution (§5).** A mis-authored multi-target spell with `SingleModel: true` could dump every target's wounds onto one model. Not currently exercised. Needs either army-load validation or a per-target re-pick, not a blind fix.
24. **Morale: Fear vs. Fearless gating asymmetry (§6).** Fear doesn't use the same whole-unit-aware condition as Fearless/Stealth/Regeneration, so a Fear(X) unit joined by a non-Fear hero keeps its full bonus where an equivalent Fearless unit would lose it. Needs a rulebook check before deciding which behavior is correct.
25. **`UnitCreationRules.cs` missing `OperationExecutor.Execute` (§3).** The one remaining call site from the "6 call sites" finding; deferred because its `Apply` method is synchronous, called from a private, non-async method deep in `FDGServer`'s launch sequence — closing it requires an async-signature change cascading through that chain, not a one-line addition.
26. **`GuiModelSelectionResolver` missing a `GetIsAlive` filter (§7).** Latent, not currently exploitable (both callers pre-filter). Best folded into #161's proposed shared canvas-selector base rather than patched as a third copy-pasted guard.

---

## 9. Summary & recommendations

### Overall

The four subsystems built since the last audit that carry the most player-facing weight — Army Forge, the base-shape/facing geometry, per-model rule dispatch, and morale/fatigue — are all solid-to-good (6-8/10), and share a pattern with the codebase's existing strengths: real test discipline on rejection/edge paths, not just happy paths, and a willingness to self-catch and document rules bugs during development (the #159 geometry saga, the dangerous-terrain morale fix). Transport is the outlier at 6/10, not because its foundation is weak, but because its destruction-handling story has a real gap that nothing exercised until this pass.

The most interesting pattern across sections is **the sibling-bug shape**: three separate sections (base-shape geometry, per-model rules, Transport) each contain a bug that is structurally identical to a bug the team already found and fixed once elsewhere in the same subsystem, but in a sibling code path that didn't get the same fix. This is worth naming as a process note: when a bug is found and fixed in one code path, it's worth a quick grep for structurally similar code paths (same helper pattern, same "only some call sites do X" shape) before considering the class of bug closed.

Informal overall for this cluster: **~7/10** — slightly above the original audit's 6.5/10 snapshot of the whole engine, consistent with a team that has kept its test-first, self-documenting habits while shipping a large amount of new functionality (suite went 326 -> 1140 tests over the same period covered by both audits).

### Recommendations, ordered by effort vs. reward

**Already applied this pass** — see §8's "Fixed" list (13 items, all small and mechanical).

**Needs your decision before building (design forks, per CLAUDE.md convention)**
1. Hero-join Subject-seat rule attribution (§8 item 17) — ~~the highest-severity open finding in this audit~~ **RESOLVED 2026-07-08 (#183, Option C — all-models gate + validator + Subject-seat model visibility).**
2. Transport Rout/spillout gap (§8 item 18) — second-highest severity; affects any game where a loaded Transport loses a melee it doesn't die directly from.
3. Fear vs. Fearless gating asymmetry (§8 item 24) — needs a rulebook read, not engineering judgment.

**Worth a dedicated pass (a few hours each)**
4. Port `CohesiveFormation.PackGrid`'s per-row sizing into `AiPlaceObjectsResolver` (§8 item 19).
5. `UnitCreationRules.cs`'s deferred `OperationExecutor.Execute` call (§8 item 25) — low urgency (no rule needs it yet) but worth doing before the async gap becomes load-bearing.

**Low priority / dormant (fine to leave until a real rule exercises them)**
6. Army Forge `MinPicks` enforcement, Combined-unit rule-value conflict, Caster `RequiredToken`/`RequiredRule`, `SingleModel` pairing validation, `GuiModelSelectionResolver`'s missing alive-filter (§8 items 20-23, 26).

### A note on what's already good

Army Forge's cost/validation math, the base-shape collision seam's design (even with the deploy sibling-bug), the per-model movement-budget wiring, and the whole Morale/Fatigue subsystem are all pulling their weight and don't need architectural rework — the recommendations above are about closing specific gaps, not questioning the shape of any of these systems.
