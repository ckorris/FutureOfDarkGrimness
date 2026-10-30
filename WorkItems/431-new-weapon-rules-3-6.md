# 431 — New weapon-rule family: Anti-Cover ... Crossing Strike

**Status**: todo
**Related**: #415, #418 (Fatigue 6s), #032 (weapon rules), #100 (primitives), #164 (DealHits.WithRules), #434/#435 (consumers)
**Branch**: `feature/opr-3.6`. Model/effort: Opus 5.5 / high.

## Goal

Every 3.6 weapon-scoped rule below has a validated definition (catalog or `GdfRuleSupplement.json` - the
AoF supplement inherits GDF names) and an integration test. Texts are the 3.6.0 book definitions (identical
across books; the census has them all):

- Anti-Cover: "This weapon ignores Cover." (45 GDF / 14 AoF books; replaces Ignores Cover)
- Anti-Regen: "This weapon ignores Regeneration." (replaces Ignores Regeneration)
- Smash: "When attacking units over half of this weapon's range away, the target must re-roll unmodified
  Defense results of 6."
- Pierce: "Ignores all positive Defense modifiers on the target, and when attacking units over half of this
  weapon's range away, this weapon gets AP(+1)."
- Fracture: "For each unmodified result of 6 to hit, those hits get AP(+2)." (Rending-shaped; #418 applies)
- Fragment: "When attacking units where most models have Defense 5+ to Defense 6+, the target must re-roll
  successful unmodified Defense results of 5-6."
- Puncture: "Ignores Regeneration, and when attacking units where most models have Defense 2+ to Defense 3+,
  this weapon gets AP(+2)."
- Obliterate: "Ignores Regeneration, and when attacking units where most models have Tough(3) or higher, the
  target must re-roll successful unmodified Defense results of 5-6."
- Decimate: "When attacking units with more than half of their total size or Tough value (for single-model
  units), the target must re-roll unmodified Defense results of 6."
- Destructive (re-authored): "When attacking units with more than half of their total size or Tough value
  (for single-model units), those hits get AP(+1)."
- Disintegrate (re-authored): "Ignores Regeneration, and when attacking units where most models have Tough(3)
  or higher, this weapon gets AP(+2)."
- Demolish: "Ignores Cover, and when attacking units with most models within 1" of terrain, this weapon gets
  AP(+1)."
- Purge: "When attacking units that are Shaken or Fatigued, this weapon gets AP(+2)."
- Reap: "When attacking units that are Shaken or Fatigued, the target must re-roll successful unmodified
  Defense results of 5-6."
- Scourge: "For each unmodified result of 1 to block hits, enemies count as having taken 2 extra wounds when
  checking who won melee, or if shooting wounds would trigger Morale Tests." (feeds #419's taken-count and the
  ranged morale trigger; no real wounds)
- Unwieldy: read the 3.6 text (two GDF + two AoF books) - likely "-1 to hit" or a move penalty; record it.
- Imprecise: "Gets -1 to hit when attacking."
- Crossing Strike: "Once per activation, when this model moves through enemy units, pick one of them and
  attack it with this weapon as if it was shooting. This weapon may only be used in this way." (needs the
  Flying move-through path; a mid-move attack - nearest precedent is Strafing, which 3.6 dropped; reuse its
  stage shape)
- Cumbersome is #425; Assault is #424; Parry is #423; Brace is #422.

New engine conditions needed (write them once, name them for #432-#435): target-over-half-weapon-range,
target-most-models-Defense-in-range, target-most-models-Tough-at-least, target-above-half-strength,
target-is-Shaken-or-Fatigued, target-most-models-within-1"-of-terrain, re-roll-successful-5-6-saves (effect).

## Acceptance

Each rule has a `*RuleIntegrationTests` case and a control; `--validate-rules` OK; `--rule-coverage` on a
scratch 3.6 import drops every name above to 0.

## Code anchors (survey 2026-10-29)

- `CoreRuleCatalog.Rending` / `.Bane` / `.Lacerate` (re-roll-6 saves) / `.Blast` (ignores cover) as templates;
  `Effect.PerHitSaveModifier`, `RerollSink`, `Condition` catalogue in `Rules/Definitions/`; `StrafingStage`
  for Crossing Strike's shape. See the appended survey rows below.

### Survey rows (code survey 2026-10-29)

- Templates: Rending `CoreRuleCatalog.cs:754-769` (per-hit AP on 6s), Bane :793 / Lacerate :814 (re-roll
  defense 6s), Shred :1222 (`AddExtraWound(OnRollValue: 1)` on `Shooting_OnSaveRollComplete`), Unstoppable :735
  (`IgnoreRule("Regeneration")`), Blast (ignores cover via `SightRuleQueries.IgnoresCover :35`);
  `Rules/Dispatch/RerollSink.cs`, `MainUnitActionStage/PerHitApSplitter.cs`; Strafing (`StrafingRules.cs:18`,
  `StrafingStage`) as the mid-move attack shape for Crossing Strike. Predecessor supplement definitions:
  "Ignores Cover", "Ignores Regeneration" (+ variants) in `GdfRuleSupplement.json`.

## Notes

- 2026-10-29 (from #418): Fatigue's "those hits don't count as rolls of 6 for special rules" is now enforced
  once, on the hit context: `UnmodifiedRollEquals`, `AddExtraHit`, `SelfWoundOnUnmodifiedRoll`,
  `PerHitSaveModifier` and `PerHitApSplitter` read `IHasUnmodifiedHitRolls.SpecialRuleHitRolls`. Fracture
  (and any other hit-face rule) built on those primitives inherits it with no extra work. A NEW primitive that
  reads hit-roll faces must read `SpecialRuleHitRolls`, never `UnmodifiedHitRolls`. Defense-roll rules (the
  5-6 re-roll family, Scourge) read the save histogram and are unaffected by Fatigue.
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
