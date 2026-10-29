# 430 — Force org and points validation 3.6 (GDF + AoF levels, Unique Hero once per army)

**Status**: todo
**Related**: #415, #003 (force-org warnings), #378 (AoF game-system field), #402 (Army Source setting), #441 (Forge UI shows the warnings)
**Branch**: `feature/opr-3.6`. Model/effort: Sonnet 5.5 / medium.

## Goal

Core Rules v3.6.0 (GF): "Force Org. (optional): Players may only bring up to 1 Hero per 500pts in their army,
and up to 1+X copies of the same unit, where X is 1 per 1000pts in their army (combined units count as one).
No single unit may be worth over 35% of total points, and armies may only have max. 1 unit per 200pts, and
max. 1 model or Tough per 20pts." Suggested sizes 1000pts / 2000pts. AoF Core Rules v3.6.0: 1 Hero per
375pts, X is 1 per 750pts, suggested 750pts / 1500pts (the 35% / 200pts / 20pts clauses: read the AoF text and
record the numbers here). Book rule Unique Hero (replaces Unique): "Counts as having Hero, and this model may
only be taken once per army."

"Done" = the #003 validator (lobby + Forge amber warnings) applies the 3.6 limits per game system
(`ArmyListFile.GameSystem`, absent = GDF), adds the unit-count and model-or-Tough caps, counts Unique Hero
as a Hero, and errors on a second copy of a Unique Hero; the lobby's default points presets follow the
suggested sizes per system.

## What changes

1. Engine: force-org constants become a per-system table; new caps; `Unique Hero` -> Hero semantics in
   `HeroJoinResolver`/hero eligibility (it must be able to join a unit like any Hero) via a catalog entry
   that grants Hero (`UnitAura`-style grant or a plain AddRule) plus the once-per-army validation.
2. App: warning strings (ASCII only); lobby presets.
3. Tests: `ForceOrgValidatorTests` (or nearest) for each cap in both systems; Unique Hero join + duplicate.

## Acceptance

Tests green; Forge shows the new warnings on an over-cap list; hand-verify (Chris) optional.

## Code anchors (survey 2026-10-29)

- Force-org validator from #003 (`FutureOfDarkGrimness/ArmyBuilding/` validator), `GameSystems` class,
  lobby points preset in `FdgRaylib/Rendering/LobbyScreen.cs`. See the appended survey rows below.

### Survey rows (code survey 2026-10-29)

Findings that shape the item: today 1 Hero per 500pts and a FLAT 3 copies per unit (not 1+X per 1000pts);
the 35% cap, 1 unit/200pts and 1 model-or-Tough/20pts are not implemented; nothing anywhere knows AoF points
levels.

- `SaveLoad/ForceOrgValidator.cs` `Validate :24-62` (`POINTS_PER_HERO = 500` :15, `MAX_COPIES_PER_UNIT = 3` :18,
  cap :29, warnings :39/:53/:58-60, `IsHero` :76). `ArmyBuilding/ListValidator.cs:16` wraps it (:123; hero Tough
  cap :191). UI `FdgRaylib/Rendering/ArmyBuilderScreen.cs:279`.
- Game system: `ArmyBuilding/GameSystems.cs:8-49` (slugs, Normalize, DisplayName; enum :61-74);
  `BookModel.cs:41`, `SaveLoad/ArmyListFile.cs:32`, `GameModel/GameSettings.cs`; lobby gate
  `ArmyBuilding/LaunchGate.cs:53`.
- Tests: `ForceOrgValidatorTests.cs`, `ListValidatorTests.cs`.

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
