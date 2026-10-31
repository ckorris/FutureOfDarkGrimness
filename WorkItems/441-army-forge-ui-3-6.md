# 441 — Army Forge + glossary UI for 3.6

**Status**: todo
**Related**: #415, #424 (Assault), #430 (force org), #437/#438, #259 (rule hover), #168 (import summary), #304 (join warnings), #378 (system filter)
**Branch**: `feature/opr-3.6`. Model/effort: Opus 5.5 / medium. Read `docs/ResolverGuide.md` and `ArmyForgeScreen.cs` structure first.

## Goal

The Army Forge screen, unit cards, hover glossary and import summary present 3.6 data correctly:

1. Weapon lines: an Assault weapon shows once with its range and "Assault" underlined/hoverable; the unit's
   derived "ranged / melee" summaries count it in both.
2. Book roster: 48 + 40 books listed under their 3.6 names, Voidspace Stalkers present, no parent entries;
   the system filter still works; any hardcoded 3.5 name in `BundledBookRulebook` / display tables fixed.
3. Unique Hero: shows as a Hero (join control available) and the once-per-army warning (#430).
4. Force-org warnings (#430) wording per system.
5. Hover glossary: 3.6 descriptions (#436) for every rule; "Enhanced X" hovers explain the base rule they lift;
   rules with no implementation still show the #259 "not enforced" marker driven by `--rule-coverage` data.
6. Import summary (#168 path): after loading a 3.6 army, the "N rules not implemented" line reflects the new
   corpus; after loading a 3.5 army, the Q5 notice (#440) appears.
7. Versatile Expert / Versatile Warrior: nothing to pick in the Forge (the pick is at activation) - make sure
   no stale "Versatile Attack" pick UI remains.

"Done" = the above hand-verified by Chris with one GDF and one AoF 3.6 army, plus `FdgRaylib.Tests` cases for
the weapon-line formatter and the roster listing.

## Acceptance

Tests green; hand-verify checklist in this file ticked by Chris.

## Code anchors (survey 2026-10-29)

- `FdgRaylib/Rendering/ArmyForgeScreen.cs`, `ForgeUnitDetail.cs`, `RuleTextFlow.cs`, `Import/BundledBookRulebook.cs`,
  `CombatCalc/UnitPicker.cs`. See the appended survey rows below.

### Survey rows (code survey 2026-10-29)

- Roster is discovered at runtime (`FdgRaylib/Rendering/BookLibrary.cs:36-45`; DemoBook fallback :52) but
  matched by NAME: `Import/BundledBookRulebook.cs:36, :83-103` (Faction/Name + system; `Defines` :62-69/:109-129),
  `ArmyForgeScreen.cs:196-198` `FindLibraryIndex`, :73 comment "all 47 bundled books", :97-101 systems combo,
  :388-392/:442-445 import errors, :651-669 `AdoptLoaded`.
- Glossary/text: `Rendering/RuleGlossary.cs:10-16, :22, :51-59`; `ForgeUnitDetail.cs:89-266`; `RuleTextFlow.cs:54,
  :230`; `UnitOptionLabel.cs:37`; `ArmyBuilderScreen.cs:286, :395, :905`; `CombatCalculatorScreen.cs:114-622`;
  `CombatCalc/UnitPicker.cs:105`; `LobbyArmySource.cs:19`.
- README: `README.md:8` frames the game as GDF-only; `:128` still says AoF armies are not added (stale) - #444.

## Notes

- 2026-10-31 (from #440): (1) The Q5 legacy notice is lobby + CLI only (`FDG.ArmyBuilding.LegacyArmyNotice`, amber,
  "Army '<name>' was built for OPR v3.5.3; it plays with 3.6 rules"). The Forge reopens a 3.5 list against its OWN
  embedded 3.5 book and says nothing - decide whether it should show the same line, and whether it should offer
  "rebuild against the 3.6 book": `FdgRaylib/Cli/ArmyReforge.Reforge` (the `--reforge` dev flag) does exactly that
  per file (solver, label translation, nearest fit) and could back such a button. (2) Every `armies/` list now embeds
  the `OPR v3.6.0` book with its selections, so a Forge reopen of a catalog list is lossless (`--retrofit-editable`
  reports 40/40 already editable).
- 2026-10-31 (from #439): spell display. (1) Spells carry no prose (`SpellDefinition`), and the Forge / picker text
  comes from `SpellText.Describe`; with #439 the grants read as canonical names ("Grants Shred when shooting (next
  time)") rather than OPR's "gets Shred on its weapons when shooting" - if the glossary / spell list should show the
  printed text, store it at import (a schema decision for this item). (2) New effect kind on the spell path:
  `Effect.ConsumeToken` - `SpellText` renders "Loses 2 spell tokens - one enemy unit within 18\", in line of sight";
  check it reads well in the Forge spell list. (3) AoF Shadow Stalkers ships 5 spells: Combat Possession's 3.6.0
  source text is truncated (#439 D1) - the Forge could say so rather than silently list five.
- 2026-10-30 (from #438): (1) a 3.6 mount / crew item now carries its weapons (`ItemEntry.Weapons`, #438 D4): the
  weapons appear in the unit's weapon list AND the item stays listed by name (Jackals Charger Beast "Beast Rider" +
  Explosive Spear, Goblin "Spider Mounts" + Poison Fangs) - the roster may want to show them as one line; "Replace
  <item>" now removes the item's weapons too. (2) ListCompiler lets a counted swap yield to a later pass-through combo
  (#438 D3: 3.6 Assault Brothers' "Replace any Heavy Pistol" + "Replace one Heavy Pistol and Hand Weapon -> Heavy
  Pistol, Energy Hammer"); the compile now lands both, so check the Forge stepper offers the full count with the combo
  bought. (3) Every 3.6 per-model section is an "Any model may take ... attachment" (16 across 15 GDF books, none
  Replace) - `ArmyForgeScreenTests.VeteranBattleBrothers_AnyModelMayTakeAnAttachment_TakesThreeOfTheSameMod` pins the
  stepper. (4) High Elf Fleets' Stinger Dagger is the one melee Takedown weapon (#438 D5): the individual-model pick now
  appears in close combat for Stingers - the GUI melee target flow should be hand-verified once. (5) Voidspace
  Stalkers is a new GDF book in the Forge list.
- 2026-10-30 (from #437): share-link import works for 3.6 lists (live check `UGDhJMMH0QBP`, exit 0, playable). For
  the import summary: (1) `OprVersionMismatchException` now has two remedies by direction - an older book: "That book
  has not been updated to this OPR version yet, so lists using it cannot be imported."; a newer one: "The game's data
  needs an update ..." - both start "Army book 'X' is at OPR version A; this game supports 3.6.x." (ASCII). (2) Until
  #438 re-bakes, a 3.6 import attaches the bundled 3.5 book, so the preview lists Assault / Anti-Cover / Fracture /
  Armor(3) as "not enforced" and shows base-cost drift for most units - expected, gone after #438 (checked against a
  scratch 3.6 book: no inert rules). (3) The points reconciliation still shows #262 residuals on old lists (stale
  "Replace X" selections whose target the unit lacks; Army Forge charges them) - worth wording so users do not read
  them as a bug. (4) Parent entries ("Wormhole Daemons", "Rift Daemons") never become bundled books, so the Forge
  book list needs no filter for them. (5) The list importer no longer warns about `attacksMultiplier` (it was noise:
  Army Forge's `attacks` is already multiplied).

- 2026-10-30 (from #423): Parry needs no Forge code - a numeric core rule whose glossary / hover text is the 3.6
  sentence from the catalog (`ParryShippedDataTests` pins `RuleGlossary.Describe("Parry")` and the `Parry(3)` tooltip).
  Two display considerations for you: (1) the Combat Calculator screen gets Parry as a volley NOTE ("Parry: 4 dice
  ignored 0.67 of 4 hits before the saves.") and the save buckets already carry the post-Parry hits - a dedicated chip or
  column would read better (#442 has the same note); (2) the melee weapon menu's #336 option rules already list "Parry(1)"
  with the sentence (generic code), nothing to add.
- 2026-10-30 (from #433): the Enhanced family needs no Forge code - each name is an ordinary supplement rule whose
  description is the 3.6.0 sentence verbatim (so the glossary prints OPR's "they stops being Shaken" and the Versatile
  Warrior family's "If this model has Versatile Attack ..." until #436 normalises). One display consideration: a list
  that takes `Enhanced X` (or its Aura / Buff) on a unit without X is legal data and loads silently inert (the gate is
  `unitHasRule X`); the Forge could flag "Enhanced X without X" as a warning. Army Forge itself only offers the
  upgrades where X is present, so this only arises from hand-edited lists.
- 2026-10-30 (from #424): the Forge already prints an Assault weapon once, as its ranged line with "Assault" as a
  hoverable rule segment (`5x Heavy Pistol (12\", A1, AP(1), Assault)` - pinned in `RuleTextFlowTests` /
  `ArmyBuilderScreenTests`), and the glossary text is the 3.6 sentence once a book embeds the definition (#438). The
  in-game surfaces changed instead: the table hover, the wound dialog and the unit picker print `12\"/melee` for a weapon
  that is both (`WeaponStatFormatter.RangeLabel`), the melee weapon pick prints the melee line without the range, the
  shoot panel keeps the full line. No Forge layout change was needed; if you want the Forge card to say "also melee"
  explicitly, `WeaponRoleQueries.MeleeUseSource` gives the rule name to attribute it to.
- 2026-10-30 (from #442): the Combat Calculator (main menu) gained a whole-inch "Charged from (in), 0 = not set"
  field in its melee row (shown while charging) and its shooting checkbox now reads "Attacker advanced (untick =
  Hold)"; a Ravage / Regenerative Strength unit gets its own pre-strike "Ravage" row. Nothing in the Forge or lobby
  changed; listed here only because the calculator shares the book picker - awaiting GUI hand-verify with #442.
- 2026-10-30 (from #430): force-org and Unique Hero hand-off. (1) Where the warnings surface: the Forge's list pane
  prints every `ListValidator` issue under the list (amber for Warnings, red for Errors; `ArmyForgeScreen` ~:992) and
  the Army Builder prints `ForceOrgValidator.Validate` lines under the points box; the LOBBY shows none of the amber
  force-org lines (only the `LaunchGate` blocking problems and, on click, the Errors as a confirm), so "force-org
  warnings wording per system" in goal item 4 means those two screens. A lobby amber line would be a new UI fork.
  (2) New warning text, all ASCII: "Too many Heroes: N (max M at L pts; 1 per 500)", "Too many copies of "X": N (max M at
  L pts; 1 + 1 per 1000)", "Unit "X" is too large: N pts (max M at L pts; 35% of the army)", "Too many units: N (max M at
  L pts; 1 per 200)", "Too many models/Tough: N (max M at L pts; 1 model or Tough per 20)" (AoF: 375 / 750 / 150 / 15);
  the Unique Hero duplicate is the Error "X: Unique Hero - the army may only include one copy." on the second row. The
  lines are plain strings with no per-row index except the Error, so they sit under the list, not on a row. (3) Already
  done in the Forge: a new list starts at the book system's starter size (GDF 1000 / AoF 750), a system switch moves a
  stock limit (2000 -> 1500), and the "pts limit" box has a hover with the suggested sizes and the force-org numbers
  (`ArmyForgeScreen.PointsLimitTooltip`). (4) Unique Hero: a bearer reports `ForceOrgValidator.IsHero` true, so the join
  control (`ArmyForgeScreen` ~:1093/:1131, the calculator's `CalculatorSide.MainIsHero`) is offered with no change; its
  rule list will show "Unique Hero" (the glossary text is the 3.6 sentence, from `CoreRuleCatalog.UniqueHero`). Nothing
  in the bundled 3.5 books carries Unique Hero until #438, so the Forge-side hand-verify needs a re-baked book or a
  fixture. (5) Needs a UI pass: whether the amber lines should name the cap's number inline or in a hover, and a lobby
  line, are Chris's call.
- 2026-10-30 (from #426): glossary text. `RuleGlossary` reads definition descriptions, so Scout, Ambush, Vanguard and
  Fanatic tooltips already show the full 3.6 text (catalog). Drakesworn, Reinforcement, Spawn and Split are book-embedded:
  their tooltips keep the 3.5 text until #438 re-bakes the books from the updated supplements. The 3.6 texts are 2-4x
  longer than the old one-liners (Scout and Ambush especially) - worth a look at tooltip wrapping in the Forge and the
  in-game rule hovers.
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
