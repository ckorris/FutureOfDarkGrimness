# 442 — Tactician AI + Combat Calculator parity with 3.6

**Status**: todo
**Related**: #415, #191 (Tactician umbrella), #364 (melee threat reach), #360 (Indirect pricing), #066 (AI resolver legality tests), #325 (forecast), the phase-2 items it mirrors (#417, #422, #423, #424, #425)
**Branch**: `feature/opr-3.6`. Model/effort: Opus 5.5 / high. Run after phase 2 lands; read `docs/ai-agent-plan.md`.

## Goal

The AI and the Combat Calculator model the 3.6 game, not the 3.5 one. Both re-implement parts of the rules
for estimation, so each phase-2 change needs a mirror here:

1. Melee threat reach uses the 3" strike range (#417) and includes Assault weapons as melee attacks (#424);
   shooting threat includes Assault weapons at their range.
2. Expected damage: Impact resolves first and is cancelled by Fatigue (#422); Brace(X) reduces incoming
   Impact; Parry(X) reduces expected melee hits by the 6+ fraction (#423); Fatigue removes 6-triggers (#418);
   melee-loser prediction uses wounds taken (#419) incl. Fear and Scourge.
3. Action legality: Cumbersome units never plan Rush/Charge or advance-and-shoot; Indirect pricing is
   Hold-only for the LoS bypass (#425); Scout/Vanguard placement respects the 9" enemy clauses (#426).
4. Range-conditional army rules (#432): the AI's "attack from over 9"/6"" preference where it is cheap to add
   (at least do not price a Devout unit's 6-trigger when charging from 5").
5. New AI resolver for the Parry pick (#423 lands the EOF default; this item adds a legality test per #066's
   pattern) and for Versatile Expert/Warrior activation picks (#434: default AP(+1) vs Tough targets, +1 to
   hit otherwise).
6. The `--ai-profile` bench (#191's gate) re-run with 3.6 armies to confirm no regression in win rate vs the
   random/A baseline; record numbers here.

"Done" = the above with unit tests on the estimators and the bench numbers recorded.

## Acceptance

Estimator tests green; bench numbers within the #191 gate; a headless `--all-ai` game with 3.6 armies exits 0
with no illegal-action retries in the log.

## Code anchors (survey 2026-10-29)

- `FdgRaylib/` AI controllers and resolvers (`AiController`, per-request AI resolvers), `MeleeThreatReach`,
  `FdgRaylib/Rendering/CombatCalc/`. See the appended survey rows below.

### Survey rows (code survey 2026-10-29)

- `Ai/Tactician/CombatMath.cs` (:21/:119 Indirect, ~:240 Impact, :244-253 Counter, :253/:571/:596 weapon split,
  :347-348 Fatigue, :356 hit context, :402 cover, :658 rule scope); `TacticalAnalysis.cs:96` (`MeleeThreatReach`),
  :131/:350/:372; `TacticianPlanner.cs:808/1124/1363/1370`; `Search/TacticianActionSpace.cs:148`;
  `TacticianWeights.cs:220`; `AiUnitClassifier.cs:14-15`; `MacroActionGenerator.cs:198`; `SpellValuation.cs:166-184`;
  `Learning/PositionEncoder.cs:491`. AI resolvers: `Ai/Resolvers/AiConsolidationMoveResolver.cs:57`,
  `AiStringSelectionResolver.cs:185`, `Resolvers/TacticianPlaceObjectsResolver.cs:24`, `AiAircraftAdvanceResolver`.
- `BuiltInAssets/StrategistLeafV1.json` holds AI weights trained on 3.5 armies - decide re-train vs accept and
  record it.
- Calculator: `Calculator/CombatCalculator.cs:117-119` (Fatigue), :374 (weapon split); `FdgRaylib/Rendering/
  CombatCalc/`; tests `CombatCalculatorTests.cs`, `CombatCalculatorScreenTests.cs`.

## Notes

- 2026-10-29 (from #417): goal item 1's strike-range half is done - `TacticalAnalysis.MeleeThreatReach`,
  `TacticianPlanner.MeleeReachOf` and `PositionEncoder.CheapThreatReach` read `MELEE_RANGE_INCHES_HORIZONTAL`,
  now 3". Open for this item: the shipped Strategist leaf net (`MlpPositionEvaluator`) was trained when
  `CheapThreatReach` added 2"; it now adds 3" for melee units (same schema, inputs drifted) - re-train or at
  least re-measure it in the goal-6 bench (#417 D5). One Tactician pin moved (#417 D3, the corridor scene).
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
