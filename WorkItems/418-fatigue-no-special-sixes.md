# 418 — Fatigue 3.6: fatigued hits on unmodified 6 do not trigger "on a 6" rules

**Status**: done 2026-10-29 - awaiting Chris's GUI hand-verify (dice panel in a fatigued melee)
**Related**: #415, #030 (Furious), #032 (Rending), #202 (Fatigue plumbing), #431 (new 6-triggered weapon rules)
**Branch**: `feature/opr-3.6`. Model/effort: Opus 5.5 / high.

## Goal

Core Rules v3.6.0, Fatigue: "After a melee where they attacked, units hit on unmodified rolls of 6 in melee
until the end of that round, and those hits don't count as rolls of 6 for special rules." The second clause
is new. "Done" = while a unit is Fatigued, its melee hits scored on a natural 6 do not fire any
"unmodified result of 6" effect: Furious, Rending (AP+4 on 6s), Surge, Relentless (shooting only - unaffected,
Fatigue is melee), Devout/Ferocious/Brutal-style extra hits, Fracture (#431), Bloodborn/Predator/Primal
"5-6" variants (#433), and the per-hit save modifier path (`PerHitSaveModifier OnRollValue 6`). Shred
(1s to block) and Bane (re-roll 6 saves) are defender-side rolls and are NOT affected.

Note the 3.6 patch-note phrasing "Unmodified 6s while Fatigued no longer count as 6s for special rules" and
the Furious/Surge/Relentless texts' "only the original hit counts as a 6 for special rules" - the engine must
distinguish "this die is a 6" from "this hit counts as a 6 for special rules", and Fatigue clears the latter.

## What changes

1. Engine: find how Fatigue is represented (token) and where the hit-roll pipeline exposes the natural roll
   to conditions (`Condition.UnmodifiedRollEquals`, `Effect.PerHitSaveModifier(OnRollValue:6)`, the
   `HitRollComplete` context). Add one "counts as 6 for rules" flag per hit that is false when the attacking
   unit is Fatigued and the attack is melee; route every 6-trigger through it rather than patching each rule.
2. Make sure the Shaken-strike-back path ("may strike back counting as Fatigued") sets the same flag.
3. Tests: Furious + Fatigued = no extra hit; Rending + Fatigued = no AP+4; a non-Fatigued control; Shred
   unaffected; a histogram-mode (`ProbabilisticDiceRoller`) run for one rule.
4. Resolver/presentation: the dice panel's "6!" callouts (#245/#238) should not flag special-rule triggers for
   fatigued hits; the #325 forecast tags should drop the trigger when the unit is Fatigued.

## Acceptance

Integration tests above green; the shooting forecast and dice panel agree with the engine in a
hand-verified fatigued melee (Chris).

## Code anchors (survey 2026-10-29)

- `Rules/Dispatch/CoreRuleCatalog.cs` Furious/Rending/Surge/Relentless entries; `RollToHitStage`;
  `Condition.UnmodifiedRollEquals`; `Effect.PerHitSaveModifier`; Fatigue token in `Rules/Tokens/`.
- See the appended survey rows below for exact lines.

### Survey rows (code survey 2026-10-29)

Findings that shape the item: Fatigue is a unit token; `CountsAsFatiguedInMelee` already treats Shaken as
Fatigued; the hit threshold is forced to 6 in `DetermineHitRollStage`, but the `HitRollCompleteContext` the
6-trigger rules read is built from the RAW rolls with no fatigue filter. No test combines Fatigue with a
6-trigger today.

- Token: `Rules/Foundation/TokenType.cs:6, :117` (FATIGUED); definition `Rules/Tokens/TokenDefinitionCatalog.cs:78-80`
  (clears at round end); `MainUnitActionStage/FatigueUtilities.cs:15` `ApplyFatigued`, `:26-27`
  `CountsAsFatiguedInMelee`; applied in `MeleeStage/ApplyFatigueStage/ApplyFatigueStage.cs:19-40`.
- Threshold: `ShootStage/FireStage/DetermineHitRollStage/DetermineHitRollStage.cs:116` (fatiguedInMelee),
  :143-147 (HitRollNeeded = 6), :122/:133 (skips buffs), :172 ("Fatigued: 6s only" tag). Copies:
  `Ai/Tactician/CombatMath.cs:347-348`, `Calculator/CombatCalculator.cs:117-119`.
- The gap: `RollToHitStage/RollToHitStage.cs:84` builds `HitRollCompleteContext` from raw results;
  `Rules/Definitions/Condition.cs:240-244` `UnmodifiedRollEquals` reads `UnmodifiedHitRolls.At(DieValue)`;
  `Effect.cs:154` `PerHitSaveModifier(OnRollValue)`, `:199-203` `AddExtraHit`; `MainUnitActionStage/
  PerHitApSplitter.cs:47-51`; context record `Rules/Dispatch/Contexts/HitRollCompleteContext.cs:30-35`; other
  builders `SyntheticHitResolution.cs:66`, `ShootingForecast.cs:133`, `CombatMath.cs:356`, `RuleFireLint.cs:684-702`.
- 6-trigger catalog rules: Surge `CoreRuleCatalog.cs:398-409`, Relentless :412-423, Furious :426-438, Rending
  :754-769, Crack :776-787 (dropped by 3.6), "Rending in melee" :937. Bane :793 and Lacerate :814 trigger on
  DEFENSE 6s (unaffected).
- Tests: `Tests/FatigueTests.cs`, `CombatCalculatorTests.cs` (AFatiguedAttackerOnlyHitsOnSixes),
  `DiceBeatGlanceMetadataTests.cs`, `ExtraHitRuleIntegrationTests.cs`, `RendingRuleIntegrationTests.cs`.

## Notes

- 2026-10-29: **Landed.** Engine commit `fe5fe52e` (submodule, `feature/opr-3.6`); the superproject commit bumps it with these docs.
  - **Engine, one seam.** `IHasUnmodifiedHitRolls` gained `SpecialRuleHitRolls`: the hit rolls as special
    rules see them. `HitRollCompleteContext` carries `SixesCountForSpecialRules` (default true) and computes
    the view on read (face 6 zeroed when false; raw roll untouched). The decision is
    `FatigueUtilities.SixesCountForSpecialRules(attacker, isMelee)` = not (melee and
    `CountsAsFatiguedInMelee`), so Shaken strikers are covered by the same predicate as the 6+ threshold.
    `RollToHitStage` stamps it on the context it already built (the old :84). Readers switched to the view:
    `Condition.UnmodifiedRollEquals`, `Effect.AddExtraHit`, `Effect.SelfWoundOnUnmodifiedRoll`,
    `Effect.PerHitSaveModifier` (now emits nothing when no die counts at its face, see D3) and
    `PerHitApSplitter.Split` (new optional view argument; never peels a face the view zeroes). No rule
    definition changed: Furious, Surge, Rending, Rending in melee, Crack and every supplement rule on
    `UnmodifiedRollEquals(6)` / `AddExtraHit(6)` / `PerHitSaveModifier(6)` (Predator Fighter, Bloodborn,
    Clan Warrior, Primal, Ferocious, Devout, Brutal Fighter, Destructive, Sergeant, Lucky, Royal Warrior,
    Great Sergeant's 6 entry, ...) inherit it. Shared helper `DiceResultsExtensions.WithoutFaces` (the
    splitter's old private `RemoveFaces`, promoted).
  - **Other builders.** `SyntheticHitResolution` (spells, Breath Attack, Strafing, Surprise Attack: never a
    melee hit roll) keeps the default and passes the view to its split. `ShootingForecast` routes the flag
    through the shared helper for parity (always true: shooting-only, and it prices no face triggers - D6).
    `RuleFireLint` unchanged (default flag; lint still green).
  - **AI / calculator.** `CombatMath.EstimateVolley` builds its hit context with the same helper and passes
    the view to its split. The Combat Calculator runs the real stages, so it needed no code.
  - **Text.** Fatigued token description and the `DetermineHitRollStage` log line now add "and those don't
    count as 6s for special rules". The threshold chip stays "Fatigued: 6s only" (D5).
  - **Rule text check.** 3.6 Rending is "Ignores Regeneration, and for each unmodified result of 6 to hit,
    those hits get AP(+4)": the Regeneration-ignore facet is unconditional and stays on under Fatigue; only
    the AP facet is a 6-trigger.
  - **Tests.** New `FatigueSpecialSixesRuleIntegrationTests` (23, real `DetermineHitRollStage` ->
    `RollToHitStage`): Furious fatigued charge = no extra hits + fresh control; Surge fatigued/fresh; Rending
    and Rending in melee fatigued = one base group saving at 4+ / fresh = AP group saving at 8; Shaken
    strike-back counts as Fatigued (no Surge, no Rending); fatigued unit SHOOTING still fires Relentless;
    to-hit beat has no proc chips under Fatigue (keeps the "Fatigued: 6s only" chip) and names both triggers
    when fresh; `ProbabilisticDiceRoller` Furious fatigued (1 hit) vs fresh (4); Hazardous's natural 1 still
    self-wounds while Surge's 6 adds nothing (histogram mode); an UNGUARDED Always -> AddExtraHit(6) +
    PerHitSaveModifier(6) rule also adds nothing; full chain to `AssignWoundsStage` showing Shred (+1/6) and
    Bane (+1/12) unchanged under Fatigue, plus a no-rule control; the context view and helper directly;
    the splitter's own guard. `CombatMathPinTests`: fatigued/fresh Furious + Surge + Rending charge pinned
    to the engine (fatigued estimate = 0.5 by hand). `CombatCalculatorTests`: fatigued Furious charger gets
    10/6 hits vs 5 + 10/6 fresh. Negative controls run by hand: forcing the flag to true in `RollToHitStage`
    fails 8 of the new tests (the fatigued ones; every control passes), and in `CombatMath` fails the
    fatigued pin. Nothing in `FatigueTests`, `ExtraHitRuleIntegrationTests`, `RendingRuleIntegrationTests`
    or `CombatCalculatorTests` encoded the old behaviour (all stayed green untouched; `FatigueTests` header
    now points at the new file).
  - **Counts.** Engine 3443 -> 3469 passed (1 skipped before and after). FdgRaylib.Tests 3063 -> 3063.
  - **Verification.** Engine suite green; full `dotnet build` 0 errors, no warnings in touched files; headless
    smoke exit 0 ("Game result: outcome=Tie winner=none rounds=4", and the log shows "Heavy Gunners is
    fatigued - hits only on unmodified 6s in melee, and those don't count as 6s for special rules.");
    `--rule-coverage FdgRaylib/Assets/Books` "Total references: 22079 / Dead: 0".
  - **Deferred, named.** (1) The 5-face of the 3.5 Boost rules - see D2, handed to #433. (2) The dice
    panel rings top-face successes whenever ANY proc chip is present, so a Hazardous self-wound chip still
    rings a fatigued 6 - see D4 (pre-existing, not changed). (3) GUI hand-verify of the dice panel in a
    fatigued melee is Chris's (no GUI code changed; the panel reads the engine's proc chips).
- 2026-10-29: filed with the plan.

## Decisions

- **D1 - where the decision lives (2026-10-29).** One flag on the hit context, decided by one helper
  (`FatigueUtilities.SixesCountForSpecialRules`), and one view every trigger reads - not per-rule `Not(Fatigued)`
  conditions. `RollToHitStage` computes the flag from the attacker's tokens with the same
  `CountsAsFatiguedInMelee` predicate `DetermineHitRollStage` uses for the 6+ threshold, rather than carrying
  a new field on `DetermineHitRollResults`: the two stages read the same unit within one attack, so they
  cannot disagree, and the many tests that inject a `DetermineHitRollResults` directly still see Fatigue.
- **D2 - only face 6 is stripped; the 5s of the Boost rules are #433's (2026-10-29).** The rule text strips
  "rolls of 6", so natural 1s still count (Hazardous self-wounds, pinned). A fatigued unit's natural 5 is a
  MISS, but the 3.5 Boost rules still in the supplements (Primal / Clan Warrior / Devout / Ferocious /
  Lucky / Royal Warrior Boost, Great Sergeant's 5 entry: `UnmodifiedRollEquals(5)` + `AddExtraHit(5)`) add
  an extra hit for ANY natural 5, hit or miss - pre-existing, and the same today for a Quality 6+ or a
  -1-to-hit Quality 5+ unit. Stripping 5s too would be reading beyond the text, so it was not done. The 3.6
  replacements say "extra attacks on successful unmodified hit results of 5-6", so #433's Enhanced family
  must gate on SUCCESSFUL hits; that removes the fatigued-5 case along with the Boost rules. Recorded in #433.
- **D3 - `PerHitSaveModifier` reads the view everywhere (2026-10-29).** It emits nothing when the firing
  context has hit rolls and none count at its face - not only under Fatigue. Every shipped user is guarded by
  `UnmodifiedRollEquals(6)` already, so nothing changes in play; for an unguarded authoring it also drops a
  spurious "AP+4 on 6s" chip on a roll with no 6s. Kept a plain `Effect` (not a `CapabilityEffect`) so the
  validator's requirements are unchanged.
- **D4 - dice panel ring (2026-10-29) - OWNER NOTE.** The proc chips are composed from the ops, so a fatigued
  6 yields no chip and, with no chip, `DiceOverlay` rings no 6 (pinned engine-side via the beat's
  `ProcTags`). The ring itself keys on "any proc chip present", so a Hazardous "1 self-wound" chip still rings
  the top-face successes - including a fatigued melee 6 (only melee Hazardous weapon in the books: AoF
  Ratmen Overtuned Halberd). Pre-existing and identical without Fatigue; fixing it needs a "top-face proc"
  flag on `DiceRolledBeat` (a wire-format change) and an overlay change, so it was left alone. Candidate
  small follow-up if the owner wants it (needs a fresh number).
- **D5 - the threshold chip text stays "Fatigued: 6s only" (2026-10-29).** It is pinned by
  `DiceBeatGlanceMetadataTests` and chip width is tight; the token tooltip and the log line carry the new
  clause instead.
- **D6 - the #325 forecast (2026-10-29).** `ShootingForecast` is shooting-only and by design prices no
  face-triggered effects (it evaluates an empty histogram), so "drop the trigger when Fatigued" had nothing to
  drop. It now passes the flag through the shared helper so a future melee forecast inherits the rule.

## Outcome

Done 2026-10-29 on `feature/opr-3.6` (engine + superproject commits; see the Notes entry). Fatigued (or
Shaken) melee hits no longer trigger any "unmodified 6 to hit" special rule, in the engine, the AI's
CombatMath and the Combat Calculator alike; shooting and defender-side rolls are unaffected. Open: Chris's
GUI hand-verify of the dice panel; D2 handed to #433; D4 noted for the owner.

## Rules for the agent (plan section 1, restated)

- Branch `feature/opr-3.6` in BOTH repos; check `git branch --show-current` in each before committing.
  Never commit, merge or push `master`. Submodule-first commit cadence; subjects start with `NNN:`.
- 3.6 rule text: `~/Projects/GDF Armies/Rules 3.6.0/` (core PDFs' text extracts, common-rules JSON) and the
  3.6.0 book snapshots. Never commit anything from that folder. No 3.5 fallbacks or version flags.
- Vertical slice: engine change + CLI resolver + GUI resolver/overlay + AI resolver legality + an
  integration test mirroring the nearest `*RuleIntegrationTests` + the `docs/` line if a number changed.
- Verify per CLAUDE.md (engine suite green, full build, headless smoke exit 0) and
  `FdgRaylib --rule-coverage FdgRaylib/Assets/Books` not regressing. Never commit red.
- Keep this file's Notes/Decisions current and flip the row in `docs/opr-3.6-upgrade-plan.md` section 5.
  A design fork stops for Chris's sign-off with options + a recommendation written here.
