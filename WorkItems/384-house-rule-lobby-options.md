# 384 — House-rule lobby options: see-through allies + unlimited split fire

**Status**: done (GUI hand-verified 2026-08-23; archived)
**Related**: #044 (ally LoS exclusion, becomes the house rule), #201 (settings-toggle pattern), #363 (AI lane approximation), #371 (shooting mode setting)

## Goal
The 2026-08-22 tester feedback ("issues with pathfinding and LOS between friendly units") traced to
deliberate deviations from OPR RAW. Chris's ruling: the see-through-allies LoS behavior was an
unintentional house rule — keep it, but as a **lobby option, default OFF**:

1. **See-Through Allies** (`GameSettings.SeeThroughFriendlyUnits`, default false).
   - ON (today's behavior): no same-team model ever blocks shooting LoS.
   - OFF (official rules, new default): only the shooter's OWN unit and the target unit are
     transparent; every other unit's models — friendly or enemy — block.
   - The AI must be aware of it when planning movement (lane scoring / clear-lane goals), per Chris.
2. **Unlimited Split Fire** (`GameSettings.UnlimitedSplitFire`, default false).
   - ON: a shooting unit may split fire across any number of enemy units.
   - OFF (today's behavior, default): at most `MAX_TARGETED_UNITS_PER_SHOOT_ACTION` (2) distinct
     units per shoot action.
   - AI awareness explicitly NOT required (Chris); the AI picks from stage-gated options anyway.

Movement-through-friendlies (the "pathfinding" half of the feedback) is NOT in scope — no ruling to
change it yet.

## Plan (slices)
- S1 (engine): settings fields; `BuildModelBlockers(..., bool seeThroughFriendlyUnits)` + all engine
  call sites (ShotEligibility, Cover/Occlusion/ChooseRangedAttack stages, SpellTargeting,
  SpellValuation, AttackBeatPositions); ModelBlockerTests coverage.
- S2 (engine): split-fire limit gated on the setting in ChooseRangedAttackStage + integration test.
- S3 (engine): AI awareness — TacticianOptions/AiProfileFactory carry the flag; TacticianPlanner
  Score lane test + MacroActionGenerator.ClearLaneGoal use terrain+friendly-blockers when OFF
  (enemy bases stay out of the approximation, #363 unchanged).
- S4 (engine): lobby viewmodels (host/client/interface) — subjects, setters, settings broadcast.
- S5 (app): LobbyScreen checkboxes, UserConfig persistence, GameGuiWiring/ResolverRegistryFactory/
  GuiResolverOverlay stamps, GuiDefineMovementResolver + GuiChooseRangedAttackResolver +
  RulesProbe/TacticalOverlayController previews, CliApp/ScenarioLauncher/Program launch paths.

## Decisions
- **Plain bools, not the #201 nullable pattern**: Chris wants default OFF = official rules. A
  pre-#384 save (field absent) therefore resumes under official rules, NOT the house rule it may
  have been played with — deviates from the #371 save-compat principle, chosen for simplicity and
  because "default = official" was the explicit instruction. Flagged in the report.
- The LoS option applies to ALL sight tests that used team exclusion (shooting, cover, occlusion,
  spell targeting, AI spell valuation, attack-beat visuals) — one uniform LoS rule, no
  shooting-only carve-out.
- Client preview parameter defaults are `false` (match the new GameSettings default), unlike #201's
  default-true, so a forgotten stamp fails toward the engine default rather than silently diverging.
- AI approximation: when OFF, lane checks add blockers for same-team units other than the active
  unit (helper `LineOfSightUtilities.BuildFriendlySightBlockers`); third-party ENEMY bases remain
  ignored in the AI lane approximation (#363, unchanged).

## Notes
- 2026-08-23: implemented, all slices, engine commit `493cc70` + superproject app commit.
  - Engine: `GameSettings.SeeThroughFriendlyUnits` / `UnlimitedSplitFire` (plain bools, default off);
    `BuildModelBlockers(..., bool seeThroughFriendlyUnits)` (required param, off = only own unit +
    target transparent) + new `BuildFriendlySightBlockers` AI helper; threaded through
    ChooseRangedAttack/Cover/Occlusion stages, ShotEligibility, SpellTargeting, SpellValuation,
    AttackBeatPositions; split-fire cap gated in ChooseRangedAttackStage; Tactician
    `SightSnapshot()` (Score lane test) + `sightTerrain` (ClearLaneGoal) + TacticianOptions/
    AiProfileFactory plumbing; lobby viewmodels (host/client/interface) sync both settings.
  - App: two lobby checkboxes (host-gated, synced, tooltips ASCII); HostGameSettings persistence;
    stamps through GameGuiWiring -> ResolverRegistryFactory -> GuiResolverOverlay ->
    GuiDefineMovementResolver + GuiChooseRangedAttackResolver; RaylibRenderer ->
    TacticalOverlayController -> RulesProbe; scenario launch (Program.cs GUI path +
    ScenarioLauncher AI registries). CliApp needs no change: it plays GetDefault() = official rules,
    matching the AiProfileFactory default.
  - Tests: engine 2998 green (+6: ally blocks under official rules, same-player other unit blocks,
    FriendlySightBlockers, split-fire cap lifted, ally-screen stage integration x2, resume policy
    pins); app 1319 green (FakeLobby + config round-trip extended). Headless smoke exit 0.
  - Awaiting GUI hand-verify: lobby checkboxes render/sync, fire-line previews + tactical overlay
    under both settings, split fire past 2 units in a real shoot action.
