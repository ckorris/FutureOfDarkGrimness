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

- 2026-10-30 (from #423): Parry is a CATALOG entry (`CoreRuleCatalog.Parry`, weapon scope, `EngineArgumentCount 1`) with
  OPR's 3.6 sentence verbatim as its description - including "(defender picks which)", which the engine resolves on the
  player's behalf (worst hits first, D1). Keep the wording as OPR's; if you normalise core descriptions, this one has no
  3.5 predecessor to reconcile. Neither supplement may define a "Parry" (a copy would shadow the catalog by name and fire
  nothing) - `ParryShippedDataTests` pins that.
- 2026-10-30 (from #433): the Enhanced family is authored (112 definitions, both files). Text-parity items it leaves
  you: (1) two 3.6.0 Aura texts are OPR's own typo, kept verbatim like #432 D12 - "This model and its unit Enhanced
  Brutal." / "... Enhanced Ferocious." (no "get"); (2) "they stops being Shaken" in Enhanced Steadfast / Vale Oath
  (OPR's grammar, verbatim); (3) Enhanced Versatile Warrior / Watchborn / Vinci Tech read "If this model has Versatile
  Attack ... from Versatile Attack" (OPR names the mechanic, not the rule); all three pinned verbatim by
  `Enhanced36ShippedDataTests`, so a normalisation re-pins there. (4) The catalog still carries the 3.5 `Harassing
  Boost` and `Guerrilla Boost` definitions (`CoreRuleCatalog.cs`, the "Boost variants" block before the auras) - the
  last Boost survivors after #432 D9; `Enhanced Harassing` / `Enhanced Guerrilla` in the GDF file replace them, so
  delete them with the other dropped 3.5 definitions. (5) Comment-only: `RerollCondition.cs`, `RerollSink.cs`,
  `ITokenClearRollSink.cs`, `TokenClearRollSink.cs` and `Effect.ClearTokenOnRoll`'s doc still describe "Boost
  variants" (the min-threshold convention they document is exactly what the Enhanced 5-6 / 3+ entries rely on).
- 2026-10-30 (from #442): two user-facing log strings seen in the 3.6 smokes, neither AI-side: (1)
  `RuleOperation.ClearTokenOnRoll.Describe()` (`Rules/Definitions/RuleOperation.cs:522`) interpolates the `TokenType`
  record, so the log reads "Peasant Levy's Vale Oath clears TokenType { Id = Shaken } on a 4+." - wants the token's
  display name; (2) ChooseActionStage's no-shoot reason prints a raw float and a double space: "Shoot: Moved 11.998999
  inches, when max to move and shoot for Destroyer  is 6." (3) On the struck-back Ravage twin the operation's narration
  names the striker as the target: "Tribe Master's Ravage(6) rolled 6 dice at Tribe Master, each 6+ a direct wound." -
  the next line ("dealt 1 unsaveable wound(s) to Errant Knights") and the wounds are right. All ASCII, wording only.
- 2026-10-30 (from #435): (1) **AoF-file definitions no 3.6.0 AoF book or spell names** (not deleted - yours): Steadfast
  Buff, Piercing Assault Buff, Versatile Attack Buff, and "AP" (the #375 C9a unit-level wargear quirk, ranged-gated
  Save -1; only the bundled 3.5 Wood Elves book embeds it - a 3.6 book only carries weapon AP(X), which the importer reads
  natively). (2) **AoF-removed names (census) that still have a definition**, none named by a 3.6 GDF book either unless
  noted: Breath Attack, Casting Debuff, Courage Buff, Grounded Reinforcement Aura, Ignores Cover, Indirect when Shooting,
  Mobile Artillery, Piercing Shooter, Quick Readjustment, Re-Position Artillery, Tear, Unique, Versatile Attack (GDF file);
  Courage Aura, Crack, Piercing Assault (+ Aura), Piercing Hunter Aura, Resistance (+ Aura), Storm of Change / Lust /
  Plague / War, Strafing (catalog); Shred in Melee (catalog) and Guarded (GDF file) - GDF 3.6 books still define them. Keep Courage itself
  (Bravery Aura grants it, #434 D6). (3) **Overrides:** all six `AofBookOverrides/*.json` are deleted (every entry moot,
  #435 D11); the bundled 3.5 AoF books keep their embedded copies until #438. `WeaponEffectAssigner`'s AoF "Rift Daemons
  of X" rows stay beside the new "Daemons of X" rows for the bundled 3.5 books - drop them after the re-bake. (4) **Text
  parity:** the AoF file now carries every 3.6 AoF book text except Retreating Strike (#435 D9 - its description keeps
  #381's ruling: "or X dice in total for single-model units", "Cannot be used while Shaken"; decide whether those stay)
  and "AP". Banner / Musician carry the AoF Core Rules PDF wording ("... triggered by shooting or melee.", "... move +1"
  ...") rather than Army Forge's common-rules JSON ("shooting/melee", "moves") - #435 D2, owner review. AoF-defined names
  whose GDF-file or catalog description is still a 3.5 paraphrase (3.5 text = 3.6 text): GDF file - Armor, Bounding Aura,
  Caster Group, Casting Buff, Extended Buff Range, Furious Mark, Infiltrate, Instinctive, Melee Slayer Aura, Mischievous,
  Precision Attacks Buff, Precision Charge Aura, Precision Fighter Aura / Buff, Precision Fighting Mark, Precision Shooter
  Aura / Buff, Precision Shooting Mark, Predator Fighter, Primal, Quick Shot Mark, Ranged Slayer Aura, Regeneration Buff,
  Relentless Mark, Slayer Mark, Speed Feat Aura, Spell Accumulator, Spell Conduit, Steadfast Aura, Strider Aura, Surprise
  Attack, Swift (+ Aura), Unpredictable Fighter Mark, Versatile Defense Aura; catalog - Ambush / Evasive / Fast / Fearless
  / Fortified / Furious / Hit & Run Fighter / Hit & Run Shooter / Melee Evasion / Melee Shrouding / Ranged Shrouding /
  Rapid Advance / Rapid Charge / Rapid Rush / Regeneration / Relentless / Scout / Shielded / Stealth / Teleport /
  Unpredictable Fighter / Unpredictable Shooter Aura, Crossing Attack, Deathstrike, Delayed Action, Furious Buff, Good
  Shot, Mend, Precise, Quick, Rapid Advance / Charge / Rush, Re-Deployment, Retaliate, Unpredictable (+ Fighter /
  Shooter), plus the core rules. Unstoppable: every GDF and AoF 3.6 book prints "This weapon ignores Regeneration, and
  ignores all negative modifiers which are not from this model's special rules." (the catalog has the core PDF's). (5)
  **Teleport is a behaviour gap, not only a paraphrase:** 3.6 (and 3.5) "within 3" of its position when using
  Advance/Charge actions, or fully within 6" of its position when using Rush actions" - the catalog's Teleport is a flat
  6" placement at action choice (Ethereal's own 3.6 text is the flat 6", #435 batch 2).
- 2026-10-30 (from #434): (1) **Dead 3.5 names seen (no 3.6.0 GDF book names them):** "Unwieldy in melee" (kept for
  3.5 data; 3.6 grants "Unwieldy on its weapons in melee"), "Dangerous Terrain Debuff (Immediate)" (+ its Effect helper;
  the importer no longer renames to it), Courage Aura / Courage Buff (replaced by Bravery Aura / Buff - but KEEP the
  catalog's "Courage": Bravery Aura grants it), Grounded Reinforcement / Grounded Stealth (3.6 renamed them Grounded
  Defense / Grounded Evasion, same shapes), Predator Fighter, Piercing Assault (+ Aura) and Piercing Hunter (+ Aura) in
  the catalog (3.6 names them Piercing Charge / Piercing Marksman, authored in the GDF file), Versatile Attack (+ helpers,
  Buff), the Feat helpers only if their Feats go (Piercing / Precision Shooting and Precision Fighting stay: the 3.6
  Marks still grant them), `TokenType.RegenerativeStrengthSpent` (its stage code is deleted, #434
  D9). (2) **Paraphrased descriptions left for you** (3.6 text equals 3.5 text, shipped copy paraphrases it): GDF supplement
  Armor, Bloodborn, Bounding Aura, Caster Group, Casting Buff, Clan Warrior, Defensive Growth Aura, Entrenched Buff,
  Extended Buff Range, Furious Mark, Grounded Precision Aura, Guarded Buff, Infiltrate (+ Aura), Instinctive, Melee Slayer
  Aura, Mischievous, Precision Attacks Buff, Precision Charge / Fighter Aura, Precision Fighter Buff, Precision Fighting /
  Shooting Mark, Precision Shooter Aura / Buff, Predator Shooter (+ Aura), Primal, Protection Feat (+ Aura; see #434 D4),
  Quick Shot Mark, Ranged Slayer Aura, Rapid Advance Buff, Regeneration Buff, Relentless Mark, Scrapper, Screened Aura,
  Slayer Mark, Speed Feat Aura, Spell Accumulator, Spell Conduit, Steadfast Aura, Strider Aura, Surprise Attack, Swift (+
  Aura, Buff), Unpredictable Fighter / Shooter Mark, Versatile Defense (+ Aura; interim, #434 D3), Versatile Reach Aura;
  catalog (core and auras) Bane, Blast, Deadly, Fearless, Furious, Regeneration, Relentless, Rending, Shred, Stealth,
  Surge, Takedown, Tough, Unstoppable, Thrust, Slow, Precise, Good Shot, Quick, Rapid Advance / Charge / Rush (+ Auras),
  Teleport (+ Aura), Mend, Delayed Action, Re-Deployment, Crossing Attack, Retaliate, Deathstrike, Self-Destruct, the
  Unpredictable family and the remaining "X Aura"s ("gain" vs 3.6 "get"). #434 put the 3.6 text on every census
  "changed definition" it holds and on the catalog's non-core changed names (pinned by `GdfSweep36ShippedDataTests`).
  (3) The "Defense" rule (48 books define "This model gets +X to Defense rolls.", 0 unit refs) has no definition - the
  importer folds it into the stat; nothing to do unless #438 finds a reference.
- 2026-10-30 (from #421): `CoreRuleCatalog.Hero`'s description is now the Core Rules v3.6.0 Hero text verbatim (ASCII;
  pinned by `HeroMoraleAuraRuleIntegrationTests.HeroDescription_IsTheCore360Wording`), so the description sweep can
  skip it. No Hero definition exists in either supplement.

- 2026-10-30 (from #430): the 3.5 `Unique` definition. Still shipped and still handled: `GdfRuleSupplement.json` "Unique"
  ("Your army may only include one copy of this unit."), the `Unique` branch beside Unique Hero in
  `ListValidator.Validate` (`uniqueRule`), the lint exemptions `["Unique"]` in `RuleSupplementLintTests.cs:23` and
  `BookRuleFireLintTests.cs:27`, the `Unique` fixtures in `ListValidatorTests.cs` (UniqueBook and its three tests),
  `LaunchGateTests.ForgeArmyWithCatalogError_IsAProblem_AttributedToThePlayer`, and the `Core("Unique")` in
  `DefaultBaseEstimatorTests.Hero_GetsACircle_NotAVehicleRectangle`. Drop them together once the books are re-baked
  (#438): 3.6 books carry "Unique Hero" instead (26 books, 60 units, none with Hero alongside), which is a CATALOG rule
  now (`CoreRuleCatalog.UniqueHero`, in `All`), so there is no supplement entry to write. Parity: the catalog description
  is the book sentence verbatim ("Counts as having Hero, and this model may only be taken once per army."), identical in
  all 26 books. When `Unique` goes, delete the `uniqueRule` ternary's second arm and rename nothing else.
- 2026-10-30 (from #426): text already 3.6, skip: `CoreRuleCatalog.Scout`, `.Ambush`, `.Vanguard`, `.Fanatic`
  (pinned by `ScoutPlacementRuleIntegrationTests`, `AmbushRuleIntegrationTests`, `VanguardRuleIntegrationTests`);
  supplement `Reinforcement`, `Spawn`, `Split` (GDF) and `Drakesworn` (AoF) (pinned by
  `DeploymentDistancesShippedDataTests`); the `PendingReinforcementArrival` token description. Seen, NOT changed (yours or
  the owner's): (1) Reinforcement's 3.6 text still says "a unit where ALL models have this rule", but its shipped gate is
  #428's `mostModelsHaveThisRule` - text and gate disagree; (2) Teleport: the census lists no 3.6 change, but the book
  text is "fully within 3" of its position when using Advance/Charge actions, or fully within 6" ... when using Rush
  actions" while `CoreRuleCatalog.Teleport` is a flat 6" with a "place each model fully within 6"" description - a
  pre-existing behaviour gap as well as a text one, so it probably wants its own number; (3) Infiltrate's supplement text
  matches 3.6.
- 2026-10-30 (from #425): yours to delete - Mobile Artillery, Re-Position Artillery and Quick Readjustment (all
  dropped in 3.6; #425 left them in place). Entries: `GdfRuleSupplement.json` "Mobile Artillery", "Re-Position
  Artillery", "Quick Readjustment" (none is a catalog rule; the bundled 3.5 books that name them - 3, 18 and 4
  books - embed their own copies until #438). Tests and fixtures that go with them: `FdgRaylib.Tests/
  MobileArtilleryShippedDataTests.cs`, `FdgRaylib.Tests/QuickReadjustmentShippedDataTests.cs` (now pins the INERT
  entry), the "Quick Readjustment" allowlist line in `FdgRaylib.Tests/RuleSupplementLintTests.cs` (remove it with the
  entry, or `AllowlistNamesExistInSupplement` fails), `FutureOfDarkGrimness/Tests/QuickReadjustmentRuleIntegrationTests.cs`
  (hand-built copies - retire), `Scenarios/mobile-artillery-defensive.json` + `mobile-artillery-moved.json` +
  `Scenarios/armies/MobileArtillery.fdgarmy`. Keep the `MovedThisRound` token and its tests
  (`MovementBackOutTests` "#197 Mobile Artillery" block, `MovedTokenVisibilityTests`): 3.6 Artillery's defensive -1
  reads it now (`ArtilleryRuleIntegrationTests`) - re-word their comments rather than deleting them. Re-Position
  Artillery's mechanism is also exercised generically by `BeforeAttackActionRuleIntegrationTests` (~:159, a
  `requiredRule` selector) and `UnitWideAbilityGateTests` (~:162) - those use the shape, not the rule, and can stay.
  Text already 3.6, skip in the parity pass: `CoreRuleCatalog` Cumbersome (new), Indirect, Artillery, and the GDF
  supplement's new "Quick Throw". Re-authored but under a 3.5 NAME: the supplement's "Indirect when shooting" is now
  the 3.6 Indirect shape (Hold-only LoS + Sight-Blocker-cover bypass) because "Indirect when Shooting Aura" - still a
  3.6.0 book rule - grants it; if you delete "Indirect when shooting" (it is on your drop list), re-point the aura's
  `ruleName` at "Indirect" in the same change. "Indirect Mark" (Robot Legions, Dragon Empire) is still 3.6 and grants
  the catalog Indirect, so it is Hold-gated with no change.
- 2026-10-30 (from #429): casting text. Already 3.6, skip: `CoreRuleCatalog.Caster` (pinned by
  `CasterRuleIntegrationTests.Caster_CarriesTheCore360Wording_WithNoLineOfSightClause`) and the new supplement entry
  `Spell Drain`. Seen but NOT changed (supplement descriptions vs the 3.6.0 snapshots' `specialRules`): `Spell
  Accumulator`, `Spell Conduit` and `Casting Buff` say "unit" where 3.6 says "model"; `Fatigue Debuff` and `Mind
  Control` capitalise "Morale Test" (and "Fatigued") in 3.6; `Mind Control` 3.6 also ADDS "Units may only be moved once
  per round in this way." - a behaviour facet, so #434's, not just text; `Caster Group` is reworded in 3.6 (pick one
  model with the rule to have Caster(X); if it is killed, pick another and transfer all spell tokens; the caster loses
  unspent tokens at round end) - the engine pools tokens on the unit, so the transfer is already implicit, but the
  description is still 3.5's. `Casting Debuff` is defined by no 3.6 book (drop candidate if nothing else names it).
  `Spell Resistance` (new in 3.6, in at least six books: spell wounds ignored on 2+, others on 6+) has no definition -
  a #434/#435 sweep item.
- 2026-10-29 (from #419): `CoreRuleCatalog.Fear`'s description is the Core Rules v3.6.0 text verbatim ("When in melee
  with this model, enemies count as having taken +X wounds when checking who won."), so the parity pass can skip it.
  No supplement or `AofBookOverrides` entry defines a Fear-like rule (nothing uses `extraMeleeWoundCount`).
- 2026-10-29 (from #427): text changed to the Core Rules v3.6.0 wording, so the parity pass can skip these:
  `CoreRuleCatalog` Unstoppable (and "Unstoppable when shooting" / "Unstoppable in melee": "Ignores Regeneration, and
  ignores all negative modifiers to this weapon which are not from this model's special rules." with the combat-kind
  prefix on the variants), Aircraft, Flying, Strider, Immobile, Transport, Embark, Disembark (Embark / Disembark are
  engine-internal: "Use a move action to ..."), and the new Spell Amplifier ("Gets +1 to casting rolls."). All
  ASCII-folded; Aircraft's "adding 30" to its total move" is the 3.6 text while the forced-move code still flies a
  30-36" band (#029, untouched). NOT changed: the supplement's "Unstoppable Mark" description ("...gets Unstoppable.",
  still true), the AoF-only wording of the same core rules (identical in the AoF PDF), and the Army Forge common-rules
  JSON's Transport entry, which still says "getting within 2" to enter" (3.5) against the core PDF's "getting in
  contact to enter" (427 D7) - do not copy that line when reconciling.
- 2026-10-29 (from #432): yours to delete once nothing old needs them: the catalog's "Darkborn (Offensive)" / "Darkborn
  (Defensive)" (3.6 has one bare "Darkborn", defined in the GDF supplement; the importer no longer rewrites to them),
  and - with the bundled books re-baked - the dropped 3.5 rules still on `attackedFromOverInches` (Mobile Artillery,
  Versatile Attack + helpers; the other users, Versatile Defense's helpers and Primeborn / Guardian / Ossified / Warden,
  are #434/#435 re-authors). Keep the `attackedFromOverInches` KIND and
  `IHasAttackOriginDistance`: old army files embed them. Text parity: Changebound / Machine-Fog / Screened / Empyrean
  Spirit carry OPR's verbatim "-1 to hit rolls ." (stray space; #432 D12) - normalise or keep, owner's call. The family
  #432 re-authored already carries 3.6 text verbatim, so the parity pass can skip it.
- 2026-10-29 (from #431): no 3.6 book names "Ignores Cover", "Ignores Cover when Shooting", "Ignores Cover Aura",
  "Ignores Cover when Shooting Aura", "Ignores Regeneration" or "Ignores Regeneration in Melee" - their 3.6 renames
  (Anti-Cover + variants, Anti-Regen) are in the GDF supplement, so the six old definitions are yours to delete.
  Strafing (catalog) is now duplicated by the supplement's "Crossing Strike" (same ability, same text); deleting
  Strafing also means updating `StrafingStage`'s "Choosing a Strafing Target" display name and its "carries a
  Strafing weapon" warning, the `StrafingRules`/`StrafingStage` doc comments, and `StrafingShippedDataTests` /
  `StrafingRuleIntegrationTests` (re-point them at Crossing Strike or retire them; `CrossingStrikeRuleIntegrationTests`
  already covers the renamed rule).
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
