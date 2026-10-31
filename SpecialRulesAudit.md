# Special Rules Audit

Date: 2026-07-06
Scope: engine special-rules system (`FutureOfDarkGrimness/Rules/`), its stage integration points, the test suite, and rule data in army files / `GdfRuleSupplement.json`.

Status: **COMPLETE (audit + fix pass).** Contents: (1) system flaws — 3 confirmed gameplay bugs, 8 latent bugs, 7 systemic weaknesses; (1a) 15 fixes applied across two sessions; (2) test-suite gaps; (3) human-testing workflow + tooling proposal; (4) robustness advice; (5) implementation plan with DONE markers. All fixes verified: 1151/1151 engine tests green (11 new tests), full build clean, rule supplement validates, headless smoke exits 0. Remaining roadmap: plan section 5 status note.

---

## 1. Special Rules System — Flaws Found

Method: four parallel read audits (dispatch/lifecycle, effects/conditions + stage consumption, data coverage, tests), with every bug below spot-verified against the actual code before being listed. CONFIRMED = full path traced; LATENT = real defect, not reachable from today's shipped rule corpus.

### 1.1 Gameplay-visible bugs (confirmed)

**BUG-1 — "Breath Attack" (GdfRuleSupplement.json) silently deals zero hits. CONFIRMED + FIXED (session 2; see 1a.4 — WithRules still deferred).**
The rule is an ActivatedAbility at `Activation_OnPreAttack` whose effect is `Effect.DealHits(1, [Blast(3)], AP:1)`. `Effect.DealHits.Apply` queues a `RuleOperation.InvokeDealHits` — but the pre-attack path (`PreAttackStage.cs:102-103`) only runs `OperationApplier.ApplyTokenOperations(ops)`, whose switch handles Grant/Consume/InvokeHeal and **silently drops InvokeDealHits** (`Rules/Dispatch/OperationApplier.cs:23-47`). The only two places in the engine that actually execute `InvokeDealHits` are `StrafingStage.cs:89` (hand-rolled) and `CastSpellStage.cs` (pattern-matches the Effect before it becomes an op). Net: the player is offered Breath Attack, pays the once-per-activation cost, sees "used Breath Attack before attacking" in the log — and no hits are ever dealt.
*Fix (session 2):* `PreAttackStage` was restructured into a `ParentStage` with the StrafingStage save->wound child pipeline; DealHits abilities now deal real damage (see 1a.4). Still open: the `WithRules` resolver seam (Breath Attack's Blast(3) is warned about and skipped, hits resolve at ability AP), and the generic trap remains for any DealHits ability on hooks other than the three now wired (pre-attack, strafing, spells).

**BUG-2 — "Destructive" (GdfRuleSupplement.json) over-applies its AP bonus to the whole attack. CONFIRMED + FIXED (data).**
Rulebook wording (same as Rending): "an unmodified 6 to hit gives the attack AP(+4)". Rending implements this with `Effect.PerHitSaveModifier(OnRollValue:6, Delta:-4)` (`CoreRuleCatalog.cs:696-699`), which peels only the natural-6 hits into their own save group. Destructive instead used a whole-attack `rollModifier(Save, -4)` gated on `unmodifiedRollEquals(6)` — and `Condition.UnmodifiedRollEquals` is a boolean "did any die show a 6" gate, while `RollToHitStage` folds whole-attack Save modifiers into **every** hit. Net: one lucky 6 in a 20-shot volley gave all 20 hits AP(4). **Fixed by re-authoring Destructive's effect as `perHitSaveModifier` (mirroring Rending); see 1a.**

**BUG-3 — Fortified never reduces AP against spell damage (melee/ranged vs spell asymmetry). CONFIRMED + FIXED (session 2; see 1a.5).**
`ResolveSpellDamageStage.cs:93-95` evaluates `HitRollCompleteContext` with **only the caster** as participant; the defender is never passed at `ERuleSeat.Subject`, unlike `RollToHitStage.cs:77-92` which evaluates both sides. Fortified (`CoreRuleCatalog.cs:990-1001`) is a Subject-seat `ReduceArmorPenetration(1)` on exactly this hook, so it works against bullets and blades but silently does nothing against spells. The stage also never folds `ReduceArmorPenetration` ops into `RollToHitResults.ArmorPenetrationReduction`.
*Fix (session 2):* the design fork was resolved exactly as recommended — `IsSpell` capability on the context + `Condition.IsNotSpell` on Shielded (explicit exemption), defender evaluated at Subject seat, AP reduction folded. See 1a.5.

### 1.2 Latent bugs (real defects, not yet reachable from shipped rules)

**LAT-1 — Granting an argumented rule crashes dispatch. FIXED (1a.11).** `Effect.AddRule`/`Effect.Aura`/`Effect.MarkTarget` grant rules by name only (`TokenPayload.RuleGrant` has no argument slot); `RuleEvaluator.CollectGrantedRules` attaches `Array.Empty<RuleArgument>()`. If the granted name is an (X) rule whose effect reads `Arg(0)`, `ValueSource.Arg.Resolve` (`ValueSource.cs:37-42`) indexes the empty array — unhandled `IndexOutOfRangeException`. The arity safety net (`RuleArgumentArity`) runs only at army-load attach, never on the grant path. The next "Tough Buff"/"Deadly Buff" copy-pasted from the FuriousBuff template ("a representative 'X Buff' rule", per its own doc) crashes the first time it fires.

**LAT-2 — Token merge can give a granted stat-modifier the wrong duration. FIXED (1a.6).** `TokenContainer.AddToken` (`TokenContainer.cs:23-41`) merges tokens on `(Type, OwnerUnitID, Payload)` — `ClearTrigger` is not part of the key, and the merged entry keeps the *existing* entry's trigger. `TokenPayload.StatModifier` carries only `Delta` (unlike `RuleGrant`, which deliberately bakes `Lifetime` into the payload). Two same-delta hit buffs with different durations ("next attack" + "this round") merge into one entry with whichever trigger arrived first: the round-long buff dies after one roll, or the one-shot buff lasts all round.

**LAT-3 — `GrantedRollModifiers.ConsumeNet` can drain the wrong grant. FIXED (1a.7).** Known and recorded in-code as #033 (`GrantedRollModifiers.cs:19-45`): consuming spent one-shot grants removes by token *type* only, FIFO — a duration grant of the same roll kind can be cleared early while the spent one-shot keeps reapplying.

**LAT-4 — `PostCombatMoveGate` early-return drops unrelated token ops. FIXED (1a.8).** When the once-per-round move budget is already spent (`PostCombatMoveGate.cs:46-50`) the method returns without calling `ApplyTokenOperations(operations)`, unlike every other branch. All `Shooting_OnPostShoot`/`Melee_OnPostMelee` ops route through this gate, so a future non-move rule on those hooks would silently lose its tokens whenever the move budget was spent. (Only the triggered-move family uses those hooks today.)

**LAT-5 — `TokenClearTrigger.OwnerDestroyed` is never enforced in a real game. FIXED (1a.9; dangerous-terrain deaths still outside the seam).** `TokenClearService.ClearForDestroyedOwner` (`TokenClearService.cs:84-99`) has exactly one caller: the test harness. No stage calls it on unit death, so any future "mark clears when its placer dies" token persists forever in live play while its tests pass.

**LAT-6 — `EHookID.Shooting_OnUnitDestroyed` is never fired by any stage. FIXED (1a.9; fires for attacker-caused deaths).** `UnitDestroyedContext` exists and is documented, but no production code constructs it. A "when this unit destroys an enemy" rule passes validation and tests, then never triggers in a game.

**LAT-7 — `Effect.ConsumeToken` is authorable but not implemented. FIXED (1a.10).** It has a JSON kind (`"consumeToken"`) but no `Apply()` override, so it falls through to the base `throw new NotImplementedException` (`Effect.cs:66-71, 325`). Zero current users; crash-on-first-use for authors.

**LAT-8 — `RollModifierTokens.TypeFor` silently defaults unknown roll kinds to Hit** (`RollModifierTokens.cs:14-20`). Dead arm today (only Hit/Save/Morale exist), but a future roll kind added without updating the switch would silently buff the wrong roll. **FIXED — now throws; see 1a.**

### 1.3 Systemic weaknesses (design-level; these are why manual testing keeps hurting)

**SYS-1 — Unknown/mis-scoped/under-argumented rule names are dropped silently, with no player-visible signal. FIXED engine-side (1a.12); UI surfacing still open.** All three skip paths in `ArmyListRuleResolution.ResolveForScope` (`ArmyListRuleResolution.cs:63-94`) report via `Debug.WriteLine` — `[Conditional("DEBUG")]`, i.e. compiled out of Release builds and invisible outside a debugger even in Debug. A misspelled rule, an OPR-imported rule the engine lacks, or a numeric rule missing its value all vanish identically. A player fielding an army with 10 unimplemented rules gets zero indication. (`RuleEvaluator.CollectGrantedRules:436-439` has the same silent-skip for granted names — not even a Debug line.) **This is the single highest-leverage robustness fix available**: route these through a real warn channel (game log + lobby/army-builder surface).

**SYS-2 — 14 of 27 `EHookID` values are dead, and `RuleValidator` passes rules authored against them. FIXED: validator now warns (1a.13).** `RuleValidator.Validate` treats "no registered context for this hook" as "nothing to validate" (`RuleValidator.cs:24-29`). The dead set includes all three `Casting_On*` hooks (the real spell flow bypasses rule dispatch entirely), `Activation_OnActivationStart`, `Movement_OnMoveResolved`, `Shooting_OnPostApplyWound`, `Morale_OnShakenApplied/Cleared`, and more. A supplement/embedded rule authored against any of them registers cleanly and never fires. The validator should at minimum warn on hooks with no context type.

**SYS-3 — `HookEntry.Lifetime` is decorative. DOCUMENTED honestly (1a.15); wiring it remains future work.** Every one of the 100+ `HookEntry`s in `CoreRuleCatalog` declares an `ELifetime`, and the doc says the effect "fires with the given Lifetime scope" — but nothing reads the property. Per-attack reset actually works because stages instantiate sinks fresh per attack. Any author who trusts `ELifetime.ThisRound` on a HookEntry to change expiry is wrong. Either wire it or rename/doc it honestly.

**SYS-4 — `Effect.DealHits` claims to be "the universal offensive-spell delivery mechanism" but only works in hand-wired stages. PARTIALLY FIXED: pre-attack path wired (1a.4), doc rewritten to list the three supported paths honestly.** (see BUG-1). Doc/design mismatch that will keep manufacturing Breath-Attack-class bugs.

**SYS-5 — OPR import has no rule-name reconciliation report.** `OprBookImporter.MapRule` deliberately imports every rule name verbatim ("the engine already skips rules it doesn't implement"), and combined with SYS-1 there is no import-time or load-time list of "these N rules on your units do nothing". A one-page import summary (implemented / partial / ignored) would set player expectations and double as a coverage worklist.

**SYS-6 — Multiplier sinks compound instead of taking-best, unlike every sibling sink. FIXED: take-best (1a.14).** `HitMultiplierSink`/`WoundModifierSink` do `_net *= factor` while `MaxWoundsSink`/`QualityFloorSink`/`WoundIgnoreSink` all keep-best with documented intent. Single-source today (Blast, Deadly), but a second multiplier source would silently compound (Deadly(3) x spell x2 = x6). Decide the semantics before a second source ships.

**SYS-7 — Corpus-approximation facets are documented only in code comments.** `CoreRuleCatalog` notes several deliberate approximations (Artillery's Hold-only restriction deferred; Unstoppable's "ignores all negative modifiers" facet partial; Harassing-family "once per round" is actually once per shoot + once per melee; Mend's per-model heal approximated at unit scope; several "where all models have X" rules gated at unit level pending #093). None of this is visible to players or testers — a tester "finding a bug" in Harassing may be finding a documented approximation. These belong in a visible ledger (this file can seed it).

### 1a. Bugs fixed during this audit

All fixes below verified together: engine suite 1151/1151 green (11 new tests), full `dotnet build` clean, headless smoke exits 0.

**Session 1 (audit day):**
1. **Destructive (BUG-2)** — `FdgRaylib/Assets/Books/GdfRuleSupplement.json`: effect re-authored from whole-attack `rollModifier(Save,-4)` + `unmodifiedRollEquals(6)` to `perHitSaveModifier(onRollValue:6, delta:-4)`, exactly mirroring core Rending. Validated with `--validate-rules` (OK: 14 definitions). The stale definition was also baked into three `.fdgbook` files; re-ran `--apply-rules` on `RobotLegions`, `Jackals`, and `AlienHives` so the shipped books carry the fix.
2. **RollModifierTokens.TypeFor (LAT-8)** — silent `_ => HitRollModifier` default replaced with a throw, so an unmapped future roll kind fails loudly at the source instead of buffing the wrong roll.
3. **Stale HeroStatRules docs** — see section 2.3 (test-side + prod-side comments claimed the feature was stubbed/[Ignore]d; it is implemented and green).

**Session 2 (remaining fixes):**
4. **BUG-1 (Breath Attack / DealHits on the pre-attack path)** — `PreAttackStage` is now a `ParentStage` with the same save->wound child pipeline `StrafingStage` uses: a `DealHits` ability seeds a synthetic weapon (ability AP) with its hits and resolves real damage, then finishes. Pinned by `PreAttackRuleIntegrationTests.DealHitsAbility_ResolvesHitsThroughSaveAndWoundPipeline` (which fails against the old code with the enemy untouched). **Two deliberate scope notes, recorded loudly:** (a) `DealHits.WithRules` (Breath Attack's `Blast(3)`) is still not applied on this path — no rule resolver is reachable at stage runtime (the identical pre-existing gap Strafing documents); the stage warns via `RuleDiagnostics` and the hits resolve at the ability's AP only. Breath Attack now deals 1 real hit at AP(1) instead of nothing; the Blast multiplication needs the resolver seam (plan 2.2 follow-up). (b) Like Strafing, at most one DealHits ability resolves per pre-attack entry (the menu can't safely resume after a child pipeline in this engine's await-chained transitions).
5. **BUG-3 (Fortified vs spells)** — `ResolveSpellDamageStage` now evaluates the defender at Subject seat (mirroring `RollToHitStage`) and folds `ReduceArmorPenetration` into the spell's AP. The design fork was resolved by adding an `IsSpell` flag to `HitRollCompleteContext` (new `IHasIsSpell` capability) and a `Condition.IsNotSpell` (JSON kind `isNotSpell`): **Shielded** now carries `IsNotSpell`, making its corpus spell-exemption explicit instead of accidental. Two new tests: Fortified saves a unit from an AP(1) spell; Shielded provably does not leak into spell saves.
6. **LAT-2 (token merge drops duration)** — `TokenContainer.AddToken`'s merge key now includes `ClearTrigger`; same-delta buffs with different durations stay separate entries.
7. **LAT-3 / #033 (ConsumeNet drains wrong grant)** — new `RemoveFirstTriggerTokens` on `ITokenContainer`; `GrantedRollModifiers.ConsumeNet` now drains only one-shot entries, never a coexisting duration grant of the same roll kind.
8. **LAT-4 (PostCombatMoveGate drops ops)** — the budget-spent early return now applies unrelated token ops before returning.
9. **LAT-5 (OwnerDestroyed never enforced) + LAT-6 (unit-destroyed hook never fires)** — new `UnitDestructionNotifier.NotifyUnitDestroyed` seam: clears the dead unit's cross-unit `OwnerDestroyed` marks and fires `Shooting_OnUnitDestroyed` with the killer. Wired into `ApplyWoundsStage` (the choke point for shooting, melee swings, impact hits, spell damage, and strafing — killer = attacker) and `MoraleUtilities.RoutWithPresentation` (cleanup only, no killer — whether a rout counts as "destroyed by" the melee winner is an open rules question). **Remaining gap, recorded:** dangerous-terrain deaths don't reach the seam yet — `ApplyDangerousTerrainEffects` has no unit binding in scope and widening it cascades through `TryMove`; see plan 2.3.
10. **LAT-7 (ConsumeToken unimplemented)** — `Effect.ConsumeToken.Apply` now emits `ConsumeTokensFromUnit` for the bearer.
11. **LAT-1 (grant-arity crash)** — `RuleEvaluator.CollectGrantedRules` screens out argumented rules arriving via grants (warn + skip, mirroring army-load's arity gate), and `ValueSource.Arg.Resolve` bounds-checks with a descriptive message.
12. **SYS-1 (silent rule drops), engine side** — new `RuleDiagnostics` warn channel (event + stdout fallback; `WarnOnce` for dispatch-frequency sites). All three `ArmyListRuleResolution` skip paths and `CollectGrantedRules`' silent skip now route through it — visible in Release builds. **Remaining: app-side surfacing** (game log at launch, army-builder pane) — plan 1.1.
13. **SYS-2 (validator passes dead hooks)** — `RuleValidator` now warns (via `RuleDiagnostics.WarnOnce`) for any passive entry on a hook with no registered context: "that entry will never trigger in a game."
14. **SYS-6 (multiplier sinks compound)** — `HitMultiplierSink`/`WoundModifierSink` now take-the-best like the sibling sinks, with tests.
15. **SYS-3 (decorative Lifetime)** — `HookEntry` doc now states plainly that `Lifetime` is declarative-only and what actually controls expiry.

New tests: `Tests/AuditHardeningTests.cs` (8 tests pinning items 6-10, 13-14), two spell-defense tests in `CasterRuleIntegrationTests`, one DealHits pipeline test in `PreAttackRuleIntegrationTests`.

## 2. Test Suite — Gaps & Strategic Shortcomings

Baseline: 1140 tests, all green, ~2s. Audited 2026-07-06 (Sonnet sweep + spot verification).

### 2.1 What the harness pattern can and cannot catch

`Tests/RulesHarness/TestRuleHarness.cs` and the ~34 `*RuleIntegrationTests.cs` files share one shape: hand-build a `GameDataStore` + units, attach a `ResolvedRule` from `CoreRuleCatalog` directly (bypassing army-list load/compile), then drive **one real production stage** with `FixedDiceRoller`. This is genuinely strong for isolated single-rule/single-stage correctness — real dispatch, real token/aura plumbing, real rehydration — and negative "rule does NOT fire" cases are well covered (23 of 34 files have explicit control tests). What it structurally cannot catch is anything that only appears when **multiple stages, rounds, rules, or layers (network/GUI) compose**. That is exactly where manual play keeps finding bugs.

### 2.2 Ranked strategic gaps (most likely to explain "manual play finds bugs the suite misses")

1. **No rule-interaction tests on a shared unit/weapon.** Essentially every test attaches exactly one rule to a purpose-built unit. No test has e.g. Rending + Deadly on one weapon, or Furious + Fatigue/Shaken on one unit, landing on the same roll resolution. Real armies constantly combine 2-4 rules per model. **This is the #1 gap.**
2. **No multi-round game-loop test for rule lifetime.** Nothing proves an `UntilEndOfGame` grant survives rounds 1->3, or that once-per-game vs once-per-activation gates behave across a live loop. `TokenLifecycleTests` calls `TokenClearService.ClearForHook` directly instead of driving real rounds.
3. **Probabilistic (histogram) dice mode is near-vestigial in tests.** Only 6 of 168 test files use `ProbabilisticDiceRoller`; 55 use `FixedDiceRoller`. ~29 of 34 rule suites never run their rule under histogram mode, despite it being a project invariant.
4. **No rule-triggered request crosses the real network wire in any test.** All rule tests resolve requests in-process; the serialization boundary (where real multiplayer bugs live) is only tested with synthetic requests (`RequestSystemTests`).
5. **GUI resolver paths for rule-driven choices are essentially untested.** `FdgRaylib.Tests` has ~12 files; only `GuiChooseRangedAttackResolverTests` is rule-adjacent. Ability offers, spell targeting, triggered-move choices — the surface a human clicks — have no tests.
6. **Save/load round-trips never capture mid-effect state.** Rehydration tests attach rules statically, serialize, then run a fresh stage. Saving while a request is pending / a wound assignment is half-resolved (the realistic save moment) is unverified.
7. **Book-supplement rules are validated structurally, never behaviorally.** The 14 rules in `GdfRuleSupplement.json` are proven to parse/merge, but none is driven through a live stage. Five (Breath Attack, Destructive, Infiltrate, Precision Debuff, Predator Fighter) have zero test presence of any kind.
8. **Six catalog rules have no test at all**, covered only by "shares this exact definition" comments on sibling tests: Protected, Very Fast, Rending-in-melee, Shred-in-melee, Unstoppable-in-melee, Guerrilla Boost (`WoundIgnoreRuleIntegrationTests.cs:100`, `BaneRuleIntegrationTests.cs:104`).

### 2.3 Rotten/stale test artifacts

- `Tests/HeroStatRulesTests.cs:11-14` — header claimed the tests are `[Ignore]`d pending implementation, but none are ignored and the production code (`Rules/Dispatch/HeroStatRules.cs`) is fully implemented and wired into 5 stages. Both the test header and the `HeroStatRules.cs` class doc were stale ("deliberately the pre-#006 behavior"). **FIXED during this audit: both comments rewritten to describe the implemented behavior.**
- `Tests/ToughWoundOrderingRuleIntegrationTests.cs` — despite the name, never attaches the Tough rule; calls `model.SetMaxWounds(...)` directly (line 230). Valid test of wound ordering, but gives false confidence that Tough's *dispatch path* is covered in this scenario.

## 3. Human Testing Workflow Proposal

### 3.0 Existing tooling inventory (what a workflow can build on)

Verified 2026-07-06:

- **Test baseline**: `dotnet test` on the engine = 1140 tests, all green, ~2s runtime. Cheap to run constantly.
- **Headless mode**: `--headless`, plus `--army <path>` (both players load the same army file non-interactively, EOF defaults drive the rest), `--slow [ms]`.
- **Rule/book pipeline** (#153): `--import-opr`, `--apply-rules`, `--validate-rules` (strict-parse + validate a rule supplement), `--book-to-army` (compile every unit of a book into an army file).
- **Save/load** (#052): `.fdgsave` via `GameSaveSerializer`; main menu "Load Game" resumes a save as host. Rule attachments are rehydrated on resume (#094). **This is the natural backbone for a scenario library** — a save positioned one click before the rule fires.
- **AI players**: AI controllers exist (`AiController`, per-request AI resolvers) — a human can test one side while the AI drives the other.
- **Dice**: `GameSettings.RandomnessType` selects `ProbabilisticDiceRoller` (histogram dice — deterministic expected values) or `RealisticDiceRoller`. `RealisticDiceRoller` uses an **unseeded** `new Random()` — there is currently no way to seed or force dice for a repeatable manual test. Probabilistic mode is the closest thing to deterministic testing today.

### 3.1 The core idea: a scenario library of saves, not fresh games

Setting up a game per rule is slow because you pay for lobby + army load + deployment + maneuvering every time. Saves already skip all of that: a `.fdgsave` resumes as host, slots can be re-crewed as AI, and rule attachments rehydrate (#094). So the workflow is:

1. **One save per rule-scenario, positioned one decision before the rule fires.** `Scenarios/Rending-shoot-vs-D5.fdgsave`, `Scenarios/Counter-charged.fdgsave`, ... Load Game -> take the single decision -> observe -> quit. A test that took 10 minutes of setup becomes ~20 seconds, and it is *identical* every repetition.
2. **Batch by mechanism, not by rule.** Rules sharing a primitive (all the "X in melee" variants; the aura twins; the rapid-move family) reuse one scenario with the army file swapped. Army files are JSON — cloning a scenario for a sibling rule is a one-word edit.
3. **Test in Probabilistic dice mode first.** Histogram dice make modifier arithmetic *visible and deterministic*: an AP change shifts the save fraction every single time, no rerolling to see through variance. Then spot-check in Realistic mode only for things that genuinely branch on discrete outcomes.
4. **Armies designed for signal, not realism.** Extreme stats so any modifier flips an outcome unmistakably (Quality 2+ attackers vs Defense 6+ targets, 1-wound models, weapon counts of 10 so fractional shifts are visible in probabilistic mode). The repo already does this informally (`WeaponTests.fdgarmy`, `CounterFastTest.fdgarmy`) — formalize it as `Scenarios/armies/`.

### 3.2 Tools to build (in leverage order)

**T1 — Scenario compiler (`--make-scenario <scenario.json> <out.fdgsave>`).** The missing piece: authoring a save currently means playing to that point. A compact scenario JSON — two army file paths, explicit model positions, round number, whose activation it is, optional pre-applied wounds/tokens — compiled straight to a `.fdgsave`. Implementation reuses `GameSaveSerializer` plus the same `GameDataStore` bootstrap `FDGServer` runs at launch; the state-machine cursor is the only subtle part. Recommendation: always position at the target unit's Choose Action (that single anchor point covers nearly every rule test — shoot, charge, abilities, movement rules all branch from there). This turns scenario authoring from "play 10 minutes" into "edit 20 lines of JSON".

**T2 — Rule-trace log channel.** A verbosity switch that logs every hook evaluation that produced (or was suppressed from producing) an operation: `[rule] Rending fired at Shooting_OnHitRollComplete: PerHitSaveModifier(6, -4)`. Without it, manual testing is black-box ("the save numbers look... right?"); with it, every scenario check is "did the expected line appear". It also directly kills the worst current failure mode — rules that silently don't fire (BUG-1, SYS-1, SYS-2 class). Probably the best effort-to-value tool on this list.

**T3 — Manual-test ledger generator.** A small tool that walks `CoreRuleCatalog` + the supplement and emits/refreshes `ManualTestLedger.md`: one row per rule x standard scenario column set (fires when expected / negative case / stacking-dedup / save-load mid-effect / networked client / AI handles it), with pass-date cells. ~128 rules x 6 checks sounds huge, but mechanism-batching (3.1.2) collapses it to ~40 distinct sessions. The ledger also inherits section 1's SYS-7 job: documented approximations get a footnote so testers stop re-finding them.

**T4 — Seeded dice.** Add an optional seed to `GameSettings` -> `new Random(seed)` in `RealisticDiceRoller`. Cheap, makes Realistic-mode repro cases shareable ("seed 42, scenario X, the bug appears on the second volley"). Networked play needs the seed only host-side (rolls happen on the host).

### 3.3 Suggested session cadence

- Before a session: `dotnet test` (2s) + headless smoke with the scenario's armies (`--headless --army Scenarios/armies/X.fdgarmy`) to catch load-time breakage before spending human time.
- Per rule: happy path in probabilistic mode -> negative case (rule must NOT fire) -> one interaction partner (the rule's most common real pairing) -> mark the ledger.
- End of session: any bug found gets its scenario saved into `Scenarios/` *before* fixing — that save is both the repro and, after the fix, the regression check.

## 4. General Robustness Advice

Ordered by leverage:

1. **Make rule drops loud (SYS-1) before anything else.** Every skip path in `ArmyListRuleResolution.ResolveForScope` should hit a real warn channel: game log + a one-time army-load summary surfaced in lobby/army builder ("3 rules on this list are not implemented: ..."). This one change converts a whole class of "why didn't X work" play-session mysteries into instant answers, and it's an afternoon of plumbing. Same treatment for `RuleEvaluator.CollectGrantedRules`'s silent skip.
2. **Add a catalog-wide "every rule must do something" lint test.** A single parameterized engine test that, for each catalog + supplement rule, attaches it to a generic unit, synthesizes its trigger context (by hook), and asserts the rule produced at least one operation or modifier. This is the automated version of BUG-1's lesson: Breath Attack passed validation, registration, and serialization tests while being a complete no-op. A does-it-fire lint would have caught it, and catches every future one, for every data-driven rule, at zero marginal cost.
3. **Harden the validator at the seams the audit exposed.** (a) `RuleValidator`: warn/error on hooks with no registered context (SYS-2 — 14 dead hook IDs currently validate clean); (b) arity-check granted rule names at validation time (LAT-1's crash is statically detectable: any `AddRule`/`Aura`/`MarkTarget` payload naming a rule whose effects read `Arg(n)`); (c) make `ELifetime` on `HookEntry` either real or explicitly documented as reserved (SYS-3).
4. **Test the pairs players actually field.** Don't chase full combinatorics; add one `RuleInteractionTests.cs` covering the ~10 pairings that co-occur in real lists (Rending+Deadly on one weapon, Furious+Fatigue, Tough+Regeneration wound flow, Blast+Cover, hero-joined unit + per-model rule, aura + its own base rule). Section 2.2's #1 gap; each test is cheap in the existing harness.
5. **Fold save/load round-trips into existing rule tests.** A harness helper (`SaveLoadRoundTrip(store)`) called mid-test between "rule attached/token granted" and "stage runs" would make every rule integration test double as a rehydration test for near-zero cost — closing gap 2.2.6 without a new suite.
6. **Keep new rules on proven primitives; when a primitive is new, land it with the rule and its integration test in one slice.** The corpus's healthiest rules are one-line reuses of `AddExtraHit`/`RollModifier`/`TriggeredMove`. Both confirmed data bugs (Destructive, Breath Attack) came from a rule using a primitive in a context it wasn't actually wired for. A supplement-authoring rule of thumb: if the effect kind + hook combination doesn't already appear in `CoreRuleCatalog`, treat it as engine work, not data work.
7. **Decide multiplier-stacking semantics now** (SYS-6) — one line of intent (take-best vs compound) while there's still only one source per sink; retrofitting after a second source ships is a balance-visible change.
8. **Adopt the golden-game smoke once dice are seedable** (T4): one scripted headless game per test-army pair, asserting on the final-state summary. Cheap cross-stage regression net that runs in CI alongside the 2s unit suite.

---

## 5. Implementation Plan

Ordered into phases so each lands as an independent, testable, committable slice (per the repo's one-vertical-slice convention). Sizes: S = under an hour, M = half-day, L = 1-2 days. Items marked **[DONE]** were completed during the audit sessions (see 1a for what shipped); the rest are the roadmap.

**Status after session 2:** all of Phase 0 is done; 1.1 is done engine-side (UI surfacing open); 1.2 done; 2.1 done; 2.2 done except the WithRules resolver seam; 2.3 done except dangerous-terrain deaths. The remaining roadmap = 1.1 UI surfacing, 1.3 rule-trace channel, 2.2/2.3 residuals, Phase 3 (test-suite upgrades), Phase 4 (workflow tools), Phase 5 (docs/data hygiene).

### Phase 0 — Contained hardening fixes (all S, engine submodule)

| # | Item | Files | Approach | Test |
|---|------|-------|----------|------|
| 0.1 | LAT-7: implement `Effect.ConsumeToken.Apply` **[DONE]** | `Rules/Definitions/Effect.cs` | Mirror `GrantToken.Apply`: emit `RuleOperation.ConsumeTokensFromUnit` for the invocation's subject unit. | New case in `TokenLifecycleTests`: attach a rule with ConsumeToken, fire hook, assert count decrement. |
| 0.2 | LAT-2: token merge respects duration **[DONE]** | `Rules/Tokens/TokenContainer.cs` (`AddToken`) | Add `ClearTrigger` equality to the merge key `(Type, OwnerUnitID, Payload)` so same-delta buffs with different durations stay separate entries. | New `TokenContainerTests` case: add two same-payload tokens with different `ClearTrigger`s, assert two entries; save/load round-trip still green (`TokenRoundTripTests`). |
| 0.3 | LAT-3 / #033: `ConsumeNet` drains the exact spent entries **[DONE]** | `Rules/Dispatch/GrantedRollModifiers.cs`, `Rules/Tokens/TokenContainer.cs` | Add a trigger-aware removal (`RemoveTokens(type, count, clearTrigger)` or predicate overload); `ConsumeNet` removes specifically from `FirstTrigger` entries it counted. | New test: unit holds ThisRound +2 and NextTrigger +1 on Hit; consume once; assert ThisRound entry intact, NextTrigger gone. |
| 0.4 | LAT-4: `PostCombatMoveGate` applies non-move ops on the budget-spent path **[DONE]** | `StateMachine/.../PostCombatMoveGate.cs` | Apply token operations before (or on) the early return, excluding double-application of the move op itself — verify exact op flow when reading the file. | Extend `TriggeredMoveRuleIntegrationTests`: second move offer in one round still applies an unrelated granted token. |
| 0.5 | LAT-1: grant-path arity guards **[DONE]** | `Rules/Dispatch/RuleEvaluator.cs` (`CollectGrantedRules`), `Rules/Definitions/ValueSource.cs` (`Arg.Resolve`) | (a) In `CollectGrantedRules`, check `RuleArgumentArity.MaxReferencedArgIndex` before attaching a granted rule; skip + warn through the SYS-1 channel if under-supplied. (b) `Arg.Resolve` bounds-check with a descriptive exception naming the rule. | New `RuleArgumentArityTests` case: grant "Tough" (argumented) via a test AddRule payload; assert skip-with-warning, no crash. |
| 0.6 | SYS-6: multiplier sinks take-best **[DONE]** | `Rules/Dispatch/HitMultiplierSink.cs`, `WoundModifierSink.cs` | Replace `_net *= factor` with keep-max + the same "if several land, keep the best" comment the sibling sinks carry. No behavior change for today's single-source corpus. | Existing Blast/Deadly suites stay green; new unit test: two multipliers -> max wins. |
| 0.7 | SYS-3: `HookEntry.Lifetime` honesty **[DONE]** | `Rules/Definitions/HookEntry.cs` | Doc fix now (declare the field reserved/unread; per-attack reset actually comes from per-attack sink instantiation). Actually wiring lifetimes is future work, tracked separately. | None (doc). |
| 0.8 | Stale HeroStatRules docs **[DONE]** | `Tests/HeroStatRulesTests.cs`, `Rules/Dispatch/HeroStatRules.cs` | Rewrote both stale headers. | n/a |
| 0.9 | LAT-8: `TypeFor` throws on unmapped roll kind **[DONE]** | `Rules/Definitions/RollModifierTokens.cs` | Silent default -> `ArgumentOutOfRangeException`. | Suite green. |
| 0.10 | BUG-2: Destructive per-hit AP **[DONE]** | `FdgRaylib/Assets/Books/GdfRuleSupplement.json` + 3 `.fdgbook` re-bakes | `rollModifier(Save,-4)` -> `perHitSaveModifier(6,-4)`. | `--validate-rules` OK; suite green. |

### Phase 1 — Diagnostics: make silent failures loud (prerequisite for productive manual testing)

| # | Item | Size | Files | Approach |
|---|------|------|-------|----------|
| 1.1 | SYS-1: real warn channel for rule drops **[DONE engine-side; UI surfacing open]** | M | `SaveLoad/ArmyListRuleResolution.cs`, `Rules/Dispatch/RuleEvaluator.cs`, `GameModel/FDGServer.cs`, app-side lobby/army-builder | Engine: introduce a static `RuleDiagnostics.Warn(string)` event (default: `Console.WriteLine`) replacing the three `Debug.WriteLine`s and the silent `CollectGrantedRules` skip; FDGServer collects warnings per army load into a list on the game context. App: surface that list once in the game log at launch and in the army-builder validation pane ("N rules on this list are not implemented: ..."). |
| 1.2 | SYS-2: validator flags dead hooks **[DONE]** | S/M | `Rules/Dispatch/RuleValidator.cs`, `Program.cs` (`--validate-rules`) | `Validate` returns a warnings list alongside its hard errors; "no registered IHookContext for hook X" becomes a warning (not an error, to avoid breaking legit reserved data). `--validate-rules` prints warnings; embedded-rule registration routes them through the SYS-1 channel. |
| 1.3 | T2: rule-trace log channel | M | `Rules/Dispatch/RuleEvaluator.cs`, `IGameContext`/`GameContext` (log verbosity), app `GameLog` | At the single dispatch choke point (`RuleEvaluator.EvaluateAll` / `ResolveAbility`), when trace is on, emit `[rule] <name> fired at <hook>: <op summary>` (and optionally `<name> suppressed by <rule>`). Gate behind a `GameSettings` or env toggle so normal play is unaffected. This is the tool that turns manual testing into "did the expected line appear". |

### Phase 2 — Confirmed-bug fixes with design decisions (engine)

| # | Item | Size | Design decision + approach |
|---|------|------|---------------------------|
| 2.1 | BUG-3: Fortified vs spells **[DONE]** | M | Add an is-spell capability to the hit-roll-complete evaluation: extend `HitRollCompleteContext` with `IsSpell` (new `IHasIsSpell` capability interface), add `Condition.IsNotSpell` (JSON kind `"isNotSpell"`). `ResolveSpellDamageStage` then evaluates the **defender** at `ERuleSeat.Subject` (mirroring `RollToHitStage.cs:77-92`) and folds `ReduceArmorPenetration` ops into the spell AP. Shielded's HookEntry gains `IsNotSpell` (preserving its intentional spell exemption); Fortified stays unconditioned and starts working against spells. Tests: `FortifiedRuleIntegrationTests` new case (spell AP reduced); `PerHitSaveEffectIntegrationTests`-style control proving Shielded still ignores spells. |
| 2.2 | BUG-1: Breath Attack / generic `DealHits` on the pre-attack path **[DONE except WithRules seam - see 1a.4]** | L | Adopt the StrafingStage pattern: make `PreAttackStage` a parent stage owning the same `DetermineSaveRollsNeeded -> RollToSave -> AssignWounds` child pipeline, entered only when a resolved ability emitted `InvokeDealHits` (synthetic weapon carrying the effect's AP + WithRules, hits seeded exactly like `StrafingStage.GetNewChildContext`). The ability loop then resumes (multiple abilities may fire per entry). Alternative considered: extracting a shared `SyntheticHitsSubStage` used by both Strafing and PreAttack — better long-term, more churn; decide at implementation time after reading Strafing's parent class. Tests: mirror `StrafingRuleIntegrationTests` for a pre-attack DealHits ability (hits dealt, saves rolled, once-per-activation gate closed, `WithRules` [Blast] applied). Update `Effect.DealHits`'s "universal delivery mechanism" doc to match reality (SYS-4). |
| 2.3 | LAT-5 + LAT-6: wire the unit-death path **[DONE except dangerous-terrain deaths - see 1a.9]** | M | Find the single authoritative "unit destroyed" moment (wound application / table-state removal — investigate `OnWoundsDealt` consumers and wherever units leave `ITableState.Units`). There, (a) call `TokenClearService.ClearForDestroyedOwner(deadUnit)` (LAT-5) and (b) fire `Shooting_OnUnitDestroyed` dispatch with the killer as actor when the kill came from an attack (LAT-6). If no clean killer attribution exists at that point, land LAT-5 alone and record LAT-6 as blocked-on-attribution. Tests: token with `OwnerDestroyed` trigger clears when placer dies in a real melee/shoot flow; a test rule on `Shooting_OnUnitDestroyed` fires on a lethal volley. |

### Phase 3 — Test-suite strategic upgrades (engine tests only)

| # | Item | Size | Approach |
|---|------|------|----------|
| 3.1 | Catalog-wide "every rule fires" lint test | M/L | Parameterized test over `CoreRuleCatalog.All` + supplement: for each passive HookEntry, synthesize the matching hook context from `HookContextCatalog` with a generic attacker/defender pair satisfying common conditions (melee for IsMelee rules, etc.), evaluate, and assert >=1 operation. Rules whose conditions can't be satisfied generically get an explicit allowlist entry with a reason — the allowlist IS the documented not-covered ledger. Would have caught Breath Attack (activated abilities: assert `ResolveAbility` ops contain at least one op a production applier actually handles — i.e. reject ops only `StrafingStage`/`CastSpellStage` consume unless the hook is theirs). |
| 3.2 | `RuleInteractionTests.cs` for real pairings | M | ~10 cases in the existing harness: Rending+Deadly one weapon; Furious+Fatigued; Tough+Regeneration wound flow; Blast+Cover; hero-joined + per-model rule; aura + own base rule; Counter+Fear melee; Stealth+IncreasedShootingRange range math; two auras same token; FirstTrigger + duration modifier same roll (locks in fix 0.3). |
| 3.3 | Save/load round-trip helper | S | `TestRuleHarness.SaveLoadRoundTrip(store)` (serialize via `GameSaveSerializer`, deserialize, rebind); call it mid-test in 5-6 representative rule suites between grant and stage run — closes gap 2.2.6 at near-zero cost. |
| 3.4 | Probabilistic-mode sweeps | M | Add a probabilistic variant to the top-10 rule suites (or parameterize the harness's roller). Priority: anything with per-hit splitting or extra-hit math (Rending done, add Deadly, Blast, Furious, Impact, per-hit save groups). |
| 3.5 | One wire-crossing rule test | M | Serialize a real rule-triggered request (`StringSelectionRequest` from PreAttackStage, `CancellableSelectionRequest<UnitData>` targeting) through `MessageRegistrar` round-trip in `NetworkProtocolTests` style — closes gap 2.2.4 for the highest-traffic request types. |
| 3.6 | Rename `ToughWoundOrderingRuleIntegrationTests` or attach real Tough | S | Attach `CoreRuleCatalog.Tough` via the evaluator in at least one case so the name stops overpromising. |

### Phase 4 — Human-testing workflow tools (app side)

| # | Item | Size | Approach |
|---|------|------|----------|
| 4.1 | T4: seeded dice | S | `GameSettings.DiceSeed` (nullable int) -> `RealisticDiceRoller(new Random(seed))`. Host-side only (all rolls happen on host). Expose in lobby settings + scenario JSON (4.2). |
| 4.2 | T1: scenario compiler `--make-scenario <scenario.json> <out.fdgsave>` | L | Scenario JSON: `{armies: [pathA, pathB], round, activePlayer, placements: [{unit, models: [[x,z],...]}], wounds: [...], tokens: [...], diceSeed}`. Build the `GameDataStore` exactly as `FDGServer` does at launch (army load + rule attach), apply placements/wounds/tokens, set the state cursor to the target unit's Choose Action (the single anchor covering nearly all rule tests), write via `GameSaveSerializer`. Ship 3-4 example scenarios in `Scenarios/` as templates. |
| 4.3 | T3: manual-test ledger generator | M | `--gen-ledger <out.md>`: walk `CoreRuleCatalog.All` + supplement, group by shared mechanism (same effect kind + hook), emit `ManualTestLedger.md` with per-rule rows (fires / negative / stacking / save-load / networked / AI) and footnotes for the SYS-7 documented approximations. Hand-edited pass dates survive regeneration (merge on rule name). |
| 4.4 | SYS-5: OPR import reconciliation report | S/M | At `--import-opr` end, diff every imported rule name against resolver registrations; print implemented / partial (via SYS-7 list) / ignored table. Doubles as the coverage worklist. |

### Phase 5 — Documentation & data hygiene

- **SYS-7 ledger**: move the corpus-approximation notes out of `CoreRuleCatalog` comments into a visible `RuleApproximations.md` (or the 4.3 ledger's footnotes), so testers stop re-finding documented approximations. Tracked under #167(c). (S)
- **Breath Attack data**: RESOLVED differently — 2.2 landed, so the ability now deals real damage (1 hit at AP(1)); the missing Blast(3) multiplication is tracked as #164. (S)
- **WorkItems**: **[DONE 2026-07-07]** residuals filed as #162 (SYS-1 UI surfacing), #163 (rule-trace channel), #164 (WithRules seam), #165 (terrain deaths -> destruction seam), #166 (Phase 3 test upgrades, umbrella), #167 (Phase 4 workflow tools, umbrella) in WorkItemsList.md.

### Suggested execution order

Phase 0 (one sitting, one commit per item or one batch commit) -> 1.1 + 1.2 (diagnostics unblock everything else) -> 2.1 -> 2.3 -> 1.3 -> 3.2 + 3.3 (lock in the fixes) -> 2.2 (biggest engine change, benefits from trace channel existing) -> 3.1 -> Phase 4 tools -> rest of Phase 3/5.

---

## Appendix: audit provenance

- Four parallel read audits (dispatch/lifecycle; effects/conditions + stage consumption; data coverage; test suite), findings spot-verified in source before listing. Line numbers refer to the 2026-07-06 master (superproject `9c447be`, submodule `cf83a51`).
- Verification after fixes: `--validate-rules` OK (14 definitions); engine tests 1140/1140 green; full `dotnet build` 0 errors.
- Areas audited and found solid (for balance): roll clamping (natural 6 always succeeds / 1 always fails, applied after modifier folding in all roll paths); melee/ranged share one generic hit->save->wound stage chain (no asymmetry there — the asymmetry was in spells, BUG-3); serialization derived-type coverage complete for Effect/Condition/TokenPayload/ClearTrigger; anti-stacking machinery (keep-best sinks, per-bearer dedup, suppression pass) consistent for the shipped corpus; distance conditions uniformly base-edge via `UnitCompareUtilities.MinDistanceBetweenUnits`; numeric-arg plumbing round-trips correctly for every shipped (X) rule.
