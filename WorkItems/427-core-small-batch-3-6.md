# 427 — Core small batch 3.6: Unstoppable, Aircraft, Flying, Transport contact, Spell Amplifier, Immobile/Strider

**Status**: done 2026-10-29 (engine `7129c9b8`, `05972638`, `9405db7e`, `1a23a57a`, `b6ef0e6f`, `103317de`, `f9f14d95`;
the superproject commit `427: ...` bumps the pointer with `docs/EngineNotes.md` and this ledger). Owner review asked on
D1, D2, D3, D5 and D6.
**Related**: #415, #035/#097 (transport), #429 (casting uses Spell Amplifier), #436 (text parity)
**Branch**: `feature/opr-3.6`. Model/effort: Sonnet 5.5 / medium. Each bullet is its own commit.

## Goal

Small, independent core-rule deltas from Core Rules v3.6.0, each a one-sitting slice:

1. Unstoppable: "Ignores Regeneration, and ignores all negative modifiers to this weapon which are not from
   this model's special rules." (3.5: ignored all negative modifiers.) The catalog comment already notes the
   "ignores all negative modifiers" facet as partial - finish it with the self-exclusion (Hazardous, Imprecise,
   Unwieldy on the same model are NOT ignored).
2. Aircraft: "[...] can't be moved in contact with or charged, and units targeting them get -12" range and -1
   to hit rolls." Add the -1 to hit; confirm the "can't be moved in contact with" guard (pile-in/consolidate
   must not end in contact with an aircraft).
3. Flying: "May move through units and terrain, and ignores the effects of Difficult and Dangerous terrain."
   Confirm the engine's Flying ignores exactly those two (not Impassable - verify current behaviour and the
   patch-note nuance "they do not start their move in"; the core text has no such clause, follow the core text).
4. Transport: "getting in contact to enter" (3.5: within 2"); exit "fully within 6"" unchanged. Update
   `TransportUtilities` embark reach and the embark overlay ring.
5. Spell Amplifier (new core): "Gets +1 to casting rolls." Weapon-scoped (Caster Staffs) and unit-scoped;
   a roll modifier on the cast roll. #429 consumes it; land the catalog entry + test here.
6. Immobile ("May only use Hold actions") and Strider ("May ignore the effects of Difficult terrain"):
   confirm text and behaviour, update descriptions only.

## Acceptance

One integration test per bullet; descriptions match the 3.6 text; suite green; smoke exit 0.

## Code anchors (survey 2026-10-29)

- `CoreRuleCatalog.Unstoppable`, `.Aircraft` (~2018) + `AircraftRules.cs`, `.Flying` (~1984), `.Strider` (~1963),
  `.Immobile` (~2280), `TransportUtilities.cs` (embark distance), casting roll in `CastSpellStage`.
  See the appended survey rows below for exact lines.

### Survey rows (code survey 2026-10-29)

Findings that shape the item: Aircraft ALREADY has the -1 to hit (`CoreRuleCatalog.cs:2026-2030`) and the
can't-be-contacted guard (`MovementUtilities.cs:283`) - bullet 2 becomes confirm + description. Transport
embark ALREADY uses a 1" contact band (`EmbarkContactDistanceInches = 1f`; the engine never had 2") - bullet 4
is confirm + "fully within 6"" vs the current centre-to-centre `IsWithinTransportRange`. Spell Amplifier is
absent everywhere.

- Unstoppable `CoreRuleCatalog.cs:735-746` (weapon scope; `IgnoreRule("Regeneration")` only; doc :730-733);
  variants :834, :888, auras :1759/:1769; "Unstoppable Mark" `GdfRuleSupplement.json:2704`.
  Tests `WoundIgnoreRuleIntegrationTests.cs`.
- Aircraft `CoreRuleCatalog.cs:2018-2048`; `AircraftRules.cs:16-19`; `ChooseActionStage.cs:656/:708/:825/:849`,
  `ChooseMeleeDefenderStage.cs:55`, `MovementUtilities.cs:283`, `DefinePathStage.cs:35`, `ShootStage.cs:33`,
  `ReconcileObjectivesStage.cs:154`, `ChooseUnitToDeployStage.cs:82/:96`. Tests `AircraftRuleIntegrationTests.cs`,
  `ForcedAircraftMoveTests.cs`, `ObjectiveOwnershipTests.cs`, `AiAircraftAdvanceResolverTests.cs`.
- Flying :1984, Strider :1963, Immobile :2280; `Rules/Definitions/ETerrainIgnoreScope.cs:10-17`.
- Transport `CoreRuleCatalog.cs:1314-1328` (Disembark :1344-1354, Embark :1369-1379);
  `Rules/Dispatch/TransportUtilities.cs:195` (6f), `:208` (1f contact), `:215-216` (centre-to-centre);
  `EmbarkStage.cs:47, :88-110`, `ChooseActionStage.cs:337`, `DisembarkStage.cs:58`, `SpilloutExecutor.cs:58`.
  Tests `TransportEmbarkTests.cs`, `TransportDisembarkTests.cs`, `TransportUtilitiesTests.cs`.
- Cast roll for Spell Amplifier: `CastSpellStage.cs:56` (`CAST_SUCCESS_THRESHOLD = 4`), :207 net modifier
  (boost + assist + granted + relay), :221-223; clamp `Utilities/Probability/DiceUtilities.cs:25`.

## Notes

- 2026-10-29: **Landed**, one engine commit per bullet (hashes in Status). Tests: engine 3679 -> 3725 passed (+46, 1
  skipped as before), app 3066 -> 3066; `--validate-rules` OK 292 definitions; `--rule-coverage` unchanged at
  `Dead: 36 (36 no-definition across 2 names)`; headless smoke exit 0 with a `Game result:` line. Every bullet's key
  test was broken on purpose and failed (Unstoppable: the ownership test, the token test; Aircraft: the pile-in test
  failed before the fix; Flying: the all-terrain scope flipped to Difficult-only; Transport: the 6in constant; Spell
  Amplifier: the `+ rules` term; Immobile: the rule detached).
  - **1 Unstoppable.** The engine could not attribute a modifier's source, so the smallest attribution was added where
    it is knowable: `RuleEvaluator`'s tagged operations already carried the bearer unit, the rule and the weapon, and
    now carry whether the rule reached the bearer as a `RuleGrant` token another unit placed (`Token.OwnerUnitID`
    set). The new marker op is consumed in the evaluator's first pass (`ApplyNegativeModifierImmunity`), so the stage,
    `ShootingForecast`, `CombatMath` and the calculator all see the filtered queue without each re-deriving sources.
    Granted negative hit TOKENS (an enemy "-1 to hit once" spell) never pass through the evaluator, so
    `GrantedRollModifiers.ConsumeNet/PeekNet(ignoreOtherUnitsNegative)` handles them, fed by the marker. The base rule
    and its when-shooting / in-melee variants (and so their Aura grants) all carry both facets. Lint knows the new op.
  - **2 Aircraft.** -1 to hit, the charge guard and the consolidation guard were confirmed (the first had NO test; now
    `ShootingAnAircraft_IsMinusOneToHit_ThroughTheRealHitRollStage`). The pile-in half was a real gap: the defender
    stopped 0.001" from an Aircraft's base, i.e. in contact (the #159 third-party obstacle logic). Fixed (D5).
  - **3 Flying.** Behaviour confirmed and unchanged, against the item's "not Impassable" hint (D3).
  - **4 Transport.** Embark was never 2" (1" contact band) and the exit zone is a 6" circle; nothing numeric changed, so
    no overlay or `ResolverGuide.md` edit (the GUI has no embark ring; the exit ring is the placement zone). Three
    descriptions are now the 3.6 text. See D6 (centre vs edge) and D7 (stale Army Forge text).
  - **5 Spell Amplifier.** New weapon-scoped catalog rule at the previously unwired `Casting_OnSpellCastAttempt`.
  - **6 Immobile / Strider.** Descriptions only, plus two edge tests.
- 2026-10-29: filed with the plan.

## Decisions

- **D1 - what "negative modifiers to this weapon" means (2026-10-29) - OWNER REVIEW.** Read as the two numbers a
  rule can lower on an attack: the to-hit roll (`ApplyRollModifier` of Hit below zero) and the weapon's AP
  (`ReduceArmorPenetration`: Fortified, Primeborn, Guardian, Ossified, Warden, Fortified Growth, and the importer's
  Corrode-style "loses AP(1)" spells). "Not from this model's special rules" is: same unit as the marker bearer (its
  unit rules, its weapon's rules, its models' rules) AND not a `RuleGrant`/`StatModifier` token another unit placed. So
  ignored: the target's Stealth, Evasive, Melee Evasion, Artillery(defensive), Changebound, Machine-Fog, Screened,
  Empyrean Spirit, Aircraft's -1, Fortified's AP-1, an enemy spell's "-1 to hit" / "loses AP". Kept: the bearer's own
  Imprecise, Bad Shot, Indirect-after-moving, Unwieldy-style penalties, and any grant the bearer placed on itself.
  A Fatigued/Shaken melee swing still hits only on a 6: that is a threshold override (#418/#020), not a modifier, and
  Unstoppable does not lift it. A one-shot grant that is ignored is still SPENT by the attack (it was the "next time"),
  the same call as #431 D3 for Pierce. Alternatives: hit rolls only (drop the AP half: delete `ReduceArmorPenetration`
  from `IsNegativeWeaponModifier`), or also range (D2).
- **D2 - what Unstoppable does NOT touch (2026-10-29) - OWNER REVIEW.** Shooting-range reductions (Aircraft's -12",
  Shrouded/Darkborn/Wild Veil's -4") and Defense modifiers (cover, Shielded: that is Pierce's job, not Unstoppable's).
  Range is a reach limit on who may be targeted, not a modifier to the attack's rolls; making Unstoppable ignore it
  would let an Unstoppable cannon shoot an Aircraft from 24" and strip Shrouded wholesale, which no rule text says.
  Cost to cover it: one `HookEntry` at `Shooting_OnRangeCheck` on the three Unstoppable definitions, an
  `ApplyRangeModifier { Delta: < 0 }` case in `IsNegativeWeaponModifier`, and a combat-kind-free condition for the
  variants (`RangeModifierContext` has no `IHasCombatKind`, so "when shooting" can use `Always` there and "in melee"
  gets none).
- **D3 - Flying keeps ignoring Impassable terrain (2026-10-29) - OWNER REVIEW.** The item asked to confirm Flying
  ignores "exactly Difficult and Dangerous (not Impassable)". It does not, and I left it. The core text is "May move
  through units and terrain, and ignores the effects of Difficult and Dangerous terrain": two clauses, the first is
  the permission to cross terrain, and the Beginner's Guide's Impassable entry says units "may not move through
  Impassable terrain, unless they have any rules that allow them to ignore it" - Flying's first clause (and
  Aircraft's "ignore all units and terrain") is that rule. Making it Difficult+Dangerous only would stop flyers
  crossing buildings and mountains. The patch-note nuance ("they do not start their move in") is not in any source I
  was given and not in the core text; not implemented. If Chris wants Impassable to block flyers: a third
  `ETerrainIgnoreScope` (difficult + dangerous only) for Flying, and the `IgnoresAllTerrain` readers split in two
  (`DefinePathStage`, `ConsolidateStage`, `GameOperationServices`, `MovementExecutor`, the planner).
- **D4 - Spell Amplifier scope (2026-10-29).** One definition, `Weapon` scope: the books print it on a weapon
  ("Psy-Hacker Gear (A2, Spell Amplifier)"), a definition has a single scope, and a unit-level mention is already
  re-homed onto every weapon at army load (`GameBootstrap.AttachToEveryWeapon`) while a token-granted copy is read off
  the unit - so "weapon-scoped AND unit-scoped" is one entry plus the existing re-homing, both tested. The engine does
  not track which model of a unit casts, so ANY living model's weapon counts (a dead bearer's staff does not). The
  rule has no argument, so two staffs are one +1. The picker's `ChooseSpellRequest.BaseThreshold` now starts from the
  caster's own threshold (4+ less the bonus), so the GUI's "Roll needed", the CLI cap and the AI's boost sizing read
  3+ with no resolver change; the cast banner lists "rules +1".
- **D5 - pile-in keeps a 1" standoff from an Aircraft (2026-10-29) - OWNER REVIEW.** Behaviour change in the one
  place the guard was missing. An Aircraft's footprint is grown by `ENEMY_STANDOFF_DISTANCE_INCHES` before pile-in's
  obstacle checks, so a defender stops up to 1" short of it instead of at its base. Aircraft still BLOCK the lane (the
  Beginner's Guide says units may move under one; pile-in treating it as an obstacle predates this item and is not
  changed). A pile-in that cannot reach its charger because an Aircraft hovers in the lane now ends further away;
  consolidation, charge and plain moves were already guarded (the `EndedTooCloseToEnemy` standoff).
- **D6 - the exit is measured from the transport's CENTRE; left as is (2026-10-29) - OWNER REVIEW.** The item asked
  to check `IsWithinTransportRange` against "fully within 6"". That method has no production caller (two unit tests
  only). The live exit is `DisembarkStage` / `SpilloutExecutor`: a 6" `CircularZone` centred on the transport's first
  living model, enforced by the GUI, CLI and AI placement resolvers through `PlacementUtilities.IsBaseWithinZone` -
  the model's whole base must fit the circle's bounding square and its centre must lie in the circle, so a model is
  "fully within 6"" of the transport's centre POINT. OPR's "fully within 6" of the transport" reads as measured from
  its hull (the usual edge-to-edge convention), which is looser by about the transport's own radius - roughly 1-2" for
  a vehicle base, nothing for a small one. The 3.6 text is unchanged from 3.5, so this is not a 3.6 delta and I did not
  change it; recorded rather than dropped. A fix is a zone radius of 6" + the transport's circumscribed radius in both
  `DisembarkStage` and `SpilloutExecutor` (the resolvers need no change; the GUI ring is the zone). Not filed as its
  own number (that needs `git fetch origin` and the index); Chris to say if wanted.
- **D7 - the Army Forge common-rules JSON still says "within 2" to enter" (2026-10-29).** Its Transport entry is the
  3.5 wording; the core PDF and the Beginner's Guide both say "getting in contact to enter". I followed the PDF
  (source priority a over b). The engine's 1" contact band is unchanged.
- **D8 - Immobile and Strider are description-only (2026-10-29).** Both behaved as the 3.6 text says. Strider's "may
  ignore" is always taken: no case exists where a unit would choose to be capped. Immobile still allows Hold-and-shoot;
  it also loses Charge when an enemy is in strike range (pinned).
- **D9 - the variants (2026-10-29).** "Unstoppable when shooting" and "Unstoppable in melee" (and their Aura grants)
  carry the modifier facet too, gated to their combat kind. Before they were Regeneration-only, which was already
  narrower than the rule they stand for.

## Ledger

| Bullet | Code anchor | Test |
|--------|-------------|------|
| 1 Unstoppable | `CoreRuleCatalog.Unstoppable` (+ `UnstoppableWhenShooting`, `UnstoppableInMelee`); `Effect.IgnoreNegativeWeaponModifiers`; `RuleOperation.IgnoreNegativeWeaponModifiers`; `RuleEvaluator.ApplyNegativeModifierImmunity` / `IsNegativeWeaponModifier`; `GrantedRollModifiers` `ignoreOtherUnitsNegative`; `DetermineHitRollStage`, `ShootingForecast` | `UnstoppableRuleIntegrationTests` (14), `CombatMathPinTests.UnstoppableWeapon_*` (3) |
| 2 Aircraft | `CoreRuleCatalog.Aircraft`; `PileInUtilities.WithAircraftStandoff`, `EnemyModelFootprint.Inflated`; guards `MovementUtilities.GetEnemyModelFootprints` + `ValidateMovingThroughEnemyUnits` | `AircraftContactGuardTests` (8) |
| 3 Flying | `CoreRuleCatalog.Flying` (`ETerrainIgnoreScope.AllTerrain`) | `FlyingRuleIntegrationTests` (5) |
| 4 Transport | `CoreRuleCatalog.Transport` / `Disembark` / `Embark`; `TransportUtilities.EmbarkContactDistanceInches`, `MaxTransportRangeInches` | `TransportDisembarkTests.DisembarkStage_PlacementZone_*`, `.TheTransportRules_CarryTheCore360Wording`; `TransportEmbarkTests` (contact band, pre-existing) |
| 5 Spell Amplifier | `CoreRuleCatalog.SpellAmplifier`; `EHookID.Casting_OnSpellCastAttempt`; `SpellCastAttemptContext`; `CastRollRules.Net`; `CastSpellStage` (`int rules`), `ChooseSpellRequest.BaseThreshold` | `SpellAmplifierRuleIntegrationTests` (10) |
| 6 Immobile / Strider | `CoreRuleCatalog.Immobile`, `.Strider` | `StriderImmobile36RuleTests` (3) + the existing `RestrictActionsRuleIntegrationTests`, `MovementStriderValidationTests` |

## Outcome

Done 2026-10-29 on `feature/opr-3.6`. Unstoppable now implements the full 3.6 text: Regeneration, plus the
modifier immunity with the own-rules exception, via a new evaluator-level marker and source attribution (D1/D2).
Aircraft's -1 to hit and charge/consolidation guards were confirmed and given tests, and the one gap found - pile-in
ending in contact with an Aircraft - was fixed (D5). Flying, Transport, Immobile and Strider behave as the 3.6 text
says and now carry it (D3, D6, D7, D8). Spell Amplifier exists, weapon-scoped, wired into the cast roll and the
picker's odds on a hook #429 can build on. Open for the owner: D1, D2, D3, D5, D6. Hand-offs recorded in #429, #436,
#442.

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
