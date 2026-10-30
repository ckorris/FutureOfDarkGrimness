# 436 — Remove dropped 3.5 definitions; bring every kept description to 3.6 wording

**Status**: todo
**Related**: #415, #434/#435 (run after them), #259/#292/#336 (rule hover glossary shows these descriptions), #168 (not-implemented diagnostics), #440 (legacy army loading)
**Branch**: `feature/opr-3.6`. Model/effort: Sonnet 5.5 / medium.

## Goal

The catalog and supplements contain only 3.6 rules, and every description string the glossary shows matches
the 3.6 text (common-rules JSON for core rules; the 3.6.0 book `specialRules` text for faction rules).

Drop list (census "dropped" + "REMOVED names"; verify each against a scratch 3.6 import before deleting):
Counter-Attack (+Aura) [catalog `CounterAttack`], Courage Aura / Courage Buff [catalog `Courage`?], Crack
[catalog], Tear, Versatile Attack (+Buff, + "(Piercing)/(Precision)" variants), Versatile Defense/Reach
variants (check - "Versatile Defense" and "Versatile Reach" still exist with changed text), Resistance (+Aura),
Strafing [catalog + `StrafingStage`/`StrafingRules` - keep the stage if #431's Crossing Strike reuses it],
Breath Attack, Mobile Artillery, Re-Position Artillery, Storm of Change/Lust/Plague/War, Unique, every
`X Boost` / `X Boost Aura` / `X Boost Buff` (#433 replaces), Ignores Cover (+Aura, when Shooting, when
Shooting Aura), Ignores Regeneration (+in Melee), Piercing Assault (+Aura/Buff), Point-Blank Piercing (+Aura),
Point-Blank Surge, Quick Readjustment, Grounded Stealth, Grounded Reinforcement (+Aura), Casting Debuff,
Brutal Fighter, Predator Fighter, Melee Evasion (GDF only; AoF keeps it - keep the definition in the AoF
supplement), Shred in Melee, Indirect when Shooting, Courageous, Reach Hunt, Reinforced, Precision Hunter,
"+1 to Defense" / "Artillery" book-level duplicates.

Keep list sanity: AP, Lacerate, Ravage, Shatter, Slayer, Takedown Shot/Strike, Targeting Visor, Vengeance,
Versatile Defense/Reach, Protection Feat, Speed Feat, Self-Repair, Steadfast, Reinforcement, Spawn, Split,
Shielded (still exists; Combat Shields now give Armor instead - data), Armor, Protected, Screened, Scurry,
Sturdy, Sergeant, Musician, Banner - all have changed text; update descriptions and re-check hooks.

Legacy: a 3.5 `.fdgarmy` embeds its definitions, so deleting supplement entries does not break it; but a 3.5
army referencing a deleted CATALOG name by name (e.g. `Counter-Attack`) must degrade to the #168 "not
implemented" diagnostic, not an exception - add a test with a captured 3.5 army file (#440 keeps fixtures).

"Done" = nothing in the drop list resolves; every kept definition's `description` equals the 3.6 text
(script-checked against the common-rules JSON and the snapshots, report in this file); `--validate-rules`
OK; tests referencing deleted catalog members updated or deleted with a note here.

## Acceptance

A script in the scratchpad (not committed) compares descriptions and reports 0 mismatches; suite green;
`--rule-coverage` on the bundled 3.6 books (after #438) is unchanged by this item.

## Code anchors (survey 2026-10-29)

- `CoreRuleCatalog.cs` (CounterAttack ~line 600s, Crack, Lacerate, Strafing-related), `StrafingRules.cs`,
  `StrafingStage`, both supplements, `RuleTextFlow.cs` / `RuleHover` (consumers of `description`).
  See the appended survey rows below.

### Survey rows (code survey 2026-10-29)

- Catalog list `CoreRuleCatalog.All :23-46`; Counter-Attack :624-635; Crack :776-787; Resistance :1106-1111;
  Melee Evasion :202 (AoF keeps the name - keep); Strafing `Rules/Dispatch/StrafingRules.cs` + `StrafingStage`
  (#431 may reuse the stage for Crossing Strike). Phrase rules: see #434's rows.
- Where descriptions are shown: `FdgRaylib/Rendering/RuleGlossary.cs:51-59` (core then book, book wins; :22
  "not enforced"), `RuleHoverText.cs:10-32`, `ArmyListOverlay.cs:374-467, :693-767`, `UnitStatBlockRenderer.cs:56-60`,
  `DisplayRuleSet.cs:22-57` (saved armies show their embedded text). OPR prose is never imported (DTO
  `OprBookImporter.cs:864-872` has no field) - descriptions are hand-written in C#/JSON.
- #168 surfaces: `SaveLoad/ArmyRuleAudit.cs:50`, `FdgRaylib/RuleLoadWarnings.cs:63-85`,
  `Import/BundledBookRulebook.cs:62-69`, `ArmyBuilderScreen.cs:286`; tests `ArmyRuleAuditParityTests.cs`.
- Tests to retire with the drops: `QuickReadjustmentRuleIntegrationTests.cs`, `StrafingShippedDataTests.cs:119`,
  `AllModelsRuleGateIntegrationTests.cs` (CounterAttack_*), `VersatileDefenseRuleIntegrationTests.cs` (check -
  Versatile Defense still exists in 3.6 with new text).

## Notes

- 2026-10-29 (from #422): Counter-Attack and Counter-Attack Aura are already DELETED from the catalog, with
  `CounterRuleIntegrationTests`' Counter-Attack case and `MostModelsRuleGateIntegrationTests.CounterAttack_*`
  retired; a list naming them loads with the #168 Unimplemented drop (`CounterLegacyDataTests`). Counter's 3.5
  impact facet is gone too (now Brace(X)); its JSON kind `reduceImpactDicePerModel` parses as a no-op for old
  files. Impact, Counter, Brace, Heavy Impact and the Counter/Brace in Melee grants already carry 3.6 text.
- 2026-10-29 (from #428): two hand-offs. (1) #428 switched every gate to "most models" but deliberately left
  the rule DESCRIPTIONS alone; twelve supplement descriptions still read "all models" and now contradict the
  engine: GDF Versatile Defense, Defensive Frenzy, Defensive Growth, Fortified Growth, Reanimation,
  Reinforcement, Ambush Re-Deployment, Dash; AoF Shadowborn Boost, Wild Veil Boost, Fortified Growth; plus Hive
  Bond's (see #434). Inquisitorial Agent's "if all models" is correct and must stay. (2)
  `AllModelsRuleGateIntegrationTests.cs` is now `MostModelsRuleGateIntegrationTests.cs` (the CounterAttack_*
  case is still in it).
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
