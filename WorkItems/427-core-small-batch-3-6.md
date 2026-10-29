# 427 — Core small batch 3.6: Unstoppable, Aircraft, Flying, Transport contact, Spell Amplifier, Immobile/Strider

**Status**: todo
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

- 2026-10-29: filed with the plan.

## Decisions

(none yet)

## Outcome

(open)

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
