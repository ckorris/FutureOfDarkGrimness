# 441 — Army Forge + glossary UI for 3.6

**Status**: done pending hand-verify (built 2026-10-31; forks F1-F9 all ruled, built or declined; GUI hand-verify pending)
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

- 2026-10-31 (last) **F6 ruled C and built** (Chris: "I agree, F6 should be C."). No new plumbing: the compiled unit's
  weapon list already holds the item's weapons (the importer routes them there, #438 D4) and `CompileUnitDetailed`
  hands the card the final items with their `ItemEntry.Weapons`, so the attribution is computed at draw time by a
  pure row model, `ForgeUnitDetail.GearRows` (D18), splitting rows on the compiler's own merge key, which the engine
  now exposes as `ListCompiler.SameProfile` (D19, the only engine change). Every fork is now ruled and built or
  declined. Gates: engine 4485 / 1 skipped, app 4062 (+7), build 0 errors / 6 warnings (pre-existing), headless smoke
  exit 0 `Game result:`, ASCII grep clean.
- 2026-10-31 (later) **F3 and F7 ruled** (Chris: "For F3, I think A is okay." / "For F7, let's do B."). F3 = A, nothing
  to build. F7 = B built: engine `1a2badd4` (`EnhancedRuleGate`, `IRuleResolver.Definitions`, the four request-text
  sites); the app change, the submodule bump and this note ride one superproject commit. One composition point for the "Base rule: X." line (D17); the base is read off the
  gate with the activation helpers folded to the rule whose pick grants them (D16) - which also fixed an F5 false
  advisory the shipped Watch Brothers / Duchies of Vinci lists would have hit in the Army Builder. F6 stays open (Chris
  leaning C, not ruled). Gates: engine 4485 / 1 skipped (+6), app 4055 (+2), build 0 errors / 6 warnings (all
  pre-existing, none in touched files), headless smoke exit 0 `Game result:`, ASCII grep clean.
- 2026-10-31 **forks ruled and built** (Chris: "I agree with you on: F1, F2, F4, F5, F8, F9. Haven't decided on the rest,
  not enough context or time."). Built the recommended option of each: F1 B (lobby force-org line; engine `e610806e`, app
  `1e60fd2`), F2 B (Forge legacy notice; app `6b670f2`), F4 B (Parry chip; engine `f88e2e05`, app `251f518`), F5 B (Army
  Builder "Enhanced X does nothing without X"; engine + app, hashes in the Outcome). F2 C (the "Rebuild against the 3.6
  book" button) waits on #440 D1-D4; F5 C (the launch-time #168 summary) not built. F8, F9 = A, nothing to build. F3, F6,
  F7 stay open. Decisions D10-D15 below; hand-verify lines added here and to #445 section 9. One app-suite run hung at
  100% CPU for 9+ minutes during F4's verification (the same suite passes in ~37 s before and after, 4051 green on the
  rerun); not reproduced, not chased - noted for #445 pass 2.
- 2026-10-31 **built** (engine `494cd8d0`; app `c0eadb9`, `2b0b49d`, `bf59959`, `e969e93`, `e5098e4`). Every goal and
  hand-off checked; the UI choices are written up as forks F1-F9 below, NOT built. Per goal:
  1. Assault: the Forge card prints the shipped 3.6 Assault Brothers' Heavy Pistol once (`5x Heavy Pistol (9", A1,
     AP(1), Assault)` - 3.6 made it 9"), Assault a solid-underlined hover with the 3.6 sentence. The Forge has no
     derived ranged/melee summary of its own (D6); the in-game ones are #424's `RangeLabel`, already pinned.
  2. Roster: 48 GDF + 40 AoF under their 3.6 names, Voidspace Stalkers in, no Wormhole/Rift/Starhost name, no parent
     entry, all stamped `OPR v3.6.0`, the system filter picks the right "Daemons of War" (both systems have one).
     No hardcoded 3.5 book name in `FdgRaylib/` code (`BundledBookRulebook` already goes through `OprBookNames`);
     fixed the stale comments ("all 47 bundled books", "~9 MB" / "~7 MB" -> ~19 MB, the share service's "bundled 3.5
     Wormhole Daemons of War ... until #438").
  3. Unique Hero: HDF Colonel Rixxon / AoF Gamharl get the join control (host list offered), the hover is the 3.6
     catalog sentence, the second copy is the row Error, the advisory duplicate line is dropped. No code change.
  4. Force-org wording: per system in the Forge list pane and the Army Builder (both print `ForceOrgValidator` /
     `ListValidator` strings verbatim); pinned for GDF 500/1000/200/20 and AoF 375/750/150/15. No code change.
  5. Glossary: FOUND a bug - text-valued rules (`SpecialRuleEntry_Text`: Spawn(...) / Split(...), 16 shipped
     references in 13 books) hovered faint with "not enforced" though they work; fixed (D2). Every rule a shipped
     book can print now has a description (test over units, weapons, items, item weapons and every option). Long 3.6
     texts: the Forge and in-game rule hovers wrap at 28em via ImGui (no clipping anywhere; Transport, ~580
     characters, is the longest); two surfaces did not wrap well - the action menu's companion-button hover was
     unwrapped and the table unit hover wrapped at a flat 300px (D3). "Enhanced X" hovers: 2 shipped names
     (Watchborn, Vinci Tech) say "Versatile Attack" instead of their base rule - OPR verbatim (#436 D3), fork F7.
     The #259 marker is description-driven, which equals resolvability (D8).
  6. Import summary: a clean 3.6 list reports no rule as not enforced (every roster unit of every book, imported the
     share-link way, plus the #168 audit line); #437's two remedies reach the popup verbatim (seam + test); the #262
     note sits under the reconciliation when a unit prices below Army Forge (D4); the lobby Q5 notice test exists
     (`Legacy35ArmyTests.TheLobbyShowsOneAmberLinePerLegacyArmy_AndNoneForA36One`); Shadow Stalkers' spell list says
     "Combat Possession: text missing in OPR's 3.6.0 data, so it is not listed or castable." (D5); the
     `ConsumeToken` spell line (Aether-Cloud Rune) reads "Loses 2 spell tokens - ...", every shipped spell line ASCII.
  7. Versatile: no "Versatile Attack" pick UI anywhere in `FdgRaylib/` (only a factual comment in
     `DisplayRuleSet.cs`); no book option offers it. Nothing to remove.
  8. Stepper (#438 D3): with the hammer combo bought the "Replace any Heavy Pistol" stepper offers 5 and both land
     (new test on the SHIPPED book - the existing ones were the compiler's miniature and a census line). FOUND the
     reverse order broken: with 5 Plasma Pistols taken first the combo's radios were grayed out ("none to replace")
     though the compiler honours it; fixed via a public engine predicate (D1).
  9. Hand-verify checklist: below.
  Hand-offs: #440 (1) -> F2, (2) nothing to do. #439 (1) -> F3, (2) checked, (3) built (D5). #438 (1) -> F6, (2) built
  + fixed (D1), (3) already pinned, (4) checklist, (5) roster test. #437 (1) built (seam), (2) gone after #438 (test:
  no inert rule), (3) built (D4), (4) roster test, (5) nothing. #423 (1) -> F4, (2) nothing. #433 -> F5 (and F7).
  #424 checked, "also melee" on the card -> F9. #442 nothing in the Forge (calculator hand-verify stays with #442).
  #430 (1)/(5) -> F1 + F8, (2) pinned, (3) already built, (4) pinned. #426 wrapping -> D3.

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

- **D1 - The Forge offers a pass-through combo the compiler would honour, via a public engine predicate.**
  `ListCompiler.CountedSwapYieldsTo(roster, swapId, claimant, option)` (engine, tested) is exactly the condition
  `ReservedForLaterSwaps` yields on (#438 D3); `ArmyForgeScreen.PassThroughPool` measures an option's pool with those
  swaps set aside, and `ForgeUnitDetail` uses it for the radio/checkbox enable, the stepper bound and the "(none to
  replace)" header, only when the ordinary pool is empty. Per OPTION, because the compiler only yields to an option
  that hands the target back: the Energy Hammer combo is live, the Great Chain-Sword stays grayed (the compiler would
  drop it unpaid). Alternatives: leave it (buy the combo first - a trap the compiler no longer has); an app-side copy
  of `HandsBack` (two copies drift; #324 set the shared-predicate precedent with `CompetesForTarget`); a section-wide
  enable (would offer the chain-sword).
- **D2 - `RuleGlossary.Describe` handles `SpecialRuleEntry_Text`.** It fell through to the printable name
  ("Spawn(Spores [5])"), which is no glossary key, so 16 shipped Spawn / Split references hovered as "not enforced".
  Now looked up by the rule's name like the numeric and core kinds. No alternative worth listing.
- **D3 - One em wrap for every rule hover; the two that did not wrap now do. OWNER REVIEW (layout).** The Forge
  (`RuleTextFlow`) and in-game (`RuleHoverText`) hovers already wrapped at 28em; the constant is now one
  (`RuleHoverText.TooltipWrapEm`, also used by the wound-chip hover). The action menu's companion-button hover
  (`GuiStringSelectionResolver`, a rule's own description) used an unwrapped `SetTooltip` and now goes through
  `RuleHoverText.ShowTooltip`; the table unit hover's rule/token descriptions wrapped at a flat 300px (a sliver at 2x
  UI scale) and now wrap at 17em (`TableTooltipOverlay.DescriptionWrapEm`, the same ~300px at the default 18px font).
  Alternatives: leave the pixel width (fine at 1x only); widen the table hover to 28em (a much bigger box over the
  table).
- **D4 - The #262 note: grey, under the reconciliation's per-unit deltas, only when a unit prices BELOW Army Forge.
  OWNER REVIEW (wording).** "A unit below Army Forge's price usually carries a stale "Replace X" pick for gear it no
  longer has (common on older lists). Army Forge still charges it; it changes nothing on the table, so our Forge
  charges nothing. Re-pick that unit's upgrades on Army Forge to clear it." (`ArmyForgeScreen.StaleReplaceNote`).
  Alternatives: name each stale selection (needs a per-choice applied/unapplied report from `ListCompiler` - not
  built, recorded as post-release in the plan's section 7); recolour the red "Points check" line amber (would also
  hide a real #218-style pricing bug).
  2026-11-01: the per-choice report IS now built (#438 D13-D15, owner request on D3): the reconciliation lists each
  unapplied Replace by name in the same grey, and this generic note yields to them - it stays only for an under-priced
  unit no specific line explains (`StaleReplaceNote`, `TheStaleReplaceNote_YieldsToASpecificUnappliedLine_ForThatUnit`).
- **D5 - Shadow Stalkers' missing spell is a one-line amber note under the spell list, from an app-side table.
  OWNER REVIEW (wording).** "Combat Possession: text missing in OPR's 3.6.0 data, so it is not listed or castable."
  under the roster pane's SPELLS and every caster's spell list (`ArmyForgeScreen.SpellDataNotes`, keyed by game system
  + book name). The book cannot record a skipped spell without a schema change and a re-bake (both out of scope; F3
  is the related schema question), so the table is pinned to the data: `ArmyForge36Tests` fails if any shipped book
  is short of six spells without a note, or carries a note with six. Alternative: no note (silently five - the
  reading the brief ruled out).
- **D6 - Goal 1's "derived ranged/melee summaries": none exist in the Forge; nothing changed.** The card's weapon
  table puts the Assault pistol in one row (RNG 9", SPE Assault) and the list pane prints one line; the in-game
  hover / wound dialog / unit picker summaries are #424's `WeaponStatFormatter.RangeLabel` ("9\"/melee"), pinned by
  `WeaponStatFormatterTests` / `UnitOptionLabelTests`. Whether the card should say "also melee" is F9.
- **D7 - Goal 3/4 needed tests, not code.** #430 had already routed Unique Hero through `ForceOrgValidator.IsHero`
  (Forge join control, calculator picker, default bases) and made every force-org string per system; this item pins
  both on shipped 3.6 books in both screens' code paths.
- **D8 - The #259 marker stays description-driven.** It already equals resolvability (same case-insensitive lookup
  as the resolver - `RuleGlossaryTests.Describe_IsSilentExactlyWhenTheResolverCannotResolve`), and the new corpus
  test shows no shipped rule without a description, matching `--rule-coverage` Dead 0. Reading the coverage report at
  runtime would add a second source for the same fact. Alternative: drive it from `--rule-coverage` output (a build
  artefact the app does not ship).
- **D9 - "Clean 3.6 list reports 0 not-enforced rules" is verified offline.** Tests cannot fetch a share link, so the
  test imports the share-link way by hand (every roster unit of every shipped book, units + the bundled book's
  definitions attached, `OprListImporter.UnresolvedRuleNames`) and also runs the #168 audit line. The live link
  (`UGDhJMMH0QBP`, #437) is in the hand-verify checklist.
- **D10 - F1's cap report rides the roster, measured at the lobby's points limit (2026-10-31).** The host computes
  `ForceOrgValidator.CompositionWarnings(army, ArmyPoints)` into a new optional `ArmyListSummary.ForceOrgWarnings`
  (the `RulesVersion` precedent: positional record field, Newtonsoft round-trip pinned) and re-runs the roster sync in
  `SetArmyPoints`, so a client sees the host's line and the line follows the limit. The points-limit cap is left out of
  that report - the Pts cell and the launch gate already say it, and it would double-count. Alternatives: host-side
  only (clients would miss it; the precedent was cheap to follow); re-sending the whole army file (heavy, and the
  summary exists for exactly this).
- **D11 - F1 shows one line per distinct army, hover verbatim.** Two slots fielding the same list collapse to one
  line, as the #440 lines do (`Distinct` on line + hover); the hover is the cap strings unchanged, so the lobby, the
  Forge and the Army Builder read the same words. Line and hover are an ImGui-free `LobbyScreen.LobbyNotice` record,
  `RosterNotices` is the drawing loop's single source (legacy lines first, then force-org).
- **D12 - F2 is a new amber status kind, on both open paths, not on import.** `EForgeStatusKind.Notice` paints the
  lobby's `LegacyNoticeColor`; `Adopt` (direct open and the #356 drift-confirmed reopen both end there) sets "Loaded
  <file> - <LegacyArmyNotice.Line>". Not shown for an import (the share-link gate admits 3.6 only) and not a modal (the
  list opened fine). Alternative: the grey Info kind (would hide it among routine status text).
- **D13 - F4's chip sits first on the Save line, labelled "Parry", numbers in the hover. OWNER REVIEW (placement).**
  Parry acts between the hits landing and the saves, and what it changes is how many hits the save buckets carry, so
  the Save line is where its chip explains a number the player sees change. The engine now reports it structured
  (`VolleyReport.Parry`, a `ParrySummary` whose `Describe()` is the old sentence minus the "Parry: " prefix) and emits
  no note; chips gained a hover (`ChipView(Label, Hover)`, `UiChrome.DrawChip(text, hover)` - the `Dummy` item is the
  hover target). Alternatives: the To-hit line (Parry does not change the hit roll either, and the Hits column is
  pre-Parry by #423's design); a third chip line for one rule (a new line type).
- **D14 - F5 reads the base rule off the definition's gate, never the name.** `EnhancedRuleAudit.BaseRuleNames` walks
  the Enhanced rule's passive conditions and activated availability for `UnitHasRule` / `WeaponHasRule` (through
  And/Or, not under Not - a negative gate is not a dependency). So "Enhanced Lustbound Aura" (no gate of its own; it
  grants) says nothing, an unresolvable "Enhanced X" says nothing (that is `ArmyRuleAudit`'s drop), and F7's oddly
  worded hovers do not matter. "Carries X" is what `UnitHasRule.Holds` sees at game time: unit rules, weapon rules,
  #407 model grants, and the rules of a joined Hero (or of the unit a Hero joins) - both ways. Aura-GRANTED bases are
  invisible from a list, so a unit whose X arrives by aura may get a false advisory; accepted for an advisory pane and
  pinned in the class summary. Alternative: parse "Enhanced " + name (the `ArmyForge36Tests.BaseOfEnhanced` test
  convention) - rejected, it would flag the Aura variants and anything not named after its base.
- **D15 - F5 lives in the engine beside `ArmyRuleAudit` and prints through the pane's `Warn`.** `SaveLoad/EnhancedRuleAudit`
  (same resolver recipe: core catalog + the list's embedded definitions, validation failure tolerated), recomputed per
  frame like the #168 audit (dictionary lookups). The line is "<unit>: Enhanced X does nothing without X" in the pane's
  amber "!" style - advisory, not an Error, not launch-blocking. The headless/launch-time summary (fork option C) is
  not built.
- **D16 - An Enhanced rule's base is its gate with activation helpers folded to the rule whose pick grants them (2026-10-31).**
  The shipped Enhanced Watchborn / Enhanced Vinci Tech (and the supplement's Enhanced Versatile Warrior) gate on
  `UnitHasRule("Watchborn (Piercing)")` / `("Watchborn (Precision)")` - the one-activation helpers Watchborn's
  activation pick `AddRule`s onto its own unit - because those are what the Enhanced rule lifts; no list carries a
  helper, the unit carries Watchborn. `EnhancedRuleGate.GrantingRule` folds a gate name to the rule whose
  Self-targeted activated ability grants it (`ETargetAffinity.Self` + `Effect.AddRule`); a grant aimed at ANOTHER unit
  ("Furious Buff" / "Guarded Buff" hand Furious / Guarded to a friend) is a separate rule and does not fold, so
  "Enhanced Guarded" still depends on Guarded. Without a candidate set (no resolver in reach) the gate name stands.
  `EnhancedRuleAudit.BaseRuleNames` now goes through this, which fixes a latent F5 false advisory: a Forge-built Watch
  Brothers unit (Watchborn + Enhanced Watchborn) read as "does nothing without Watchborn (Piercing) and Watchborn
  (Precision)" - pinned on the shipped book now. Needed `IRuleResolver.Definitions` (enumeration; the only
  implementation is `RuleResolver`, aliases repeat their definition). Alternatives: print the raw helper names ("Base
  rules: Watchborn (Piercing) and Watchborn (Precision).") - true to the gate, false to what the unit carries, and
  leaves F5 wrong; strip the parenthetical from the name - name parsing, the D14 rejection; a hand table of
  helper -> base - a second source for a fact the definitions already state.
- **D17 - One composition point for the hover text: `EnhancedRuleGate.HoverDescription`, in the engine.** Every
  surface that explains a rule reads `Description` through it (the text as authored, plus the line when there is one),
  so the Forge, the calculator, the in-game hovers, the engine's request texts and the CLI say the same words. The
  resolver each surface passes: the glossary builds one from core + the book's definitions (the same core-then-embedded
  set it describes); the overlays pass their `DisplayRuleSet` resolver; the engine stages pass
  `GameContext.RuleEvaluator.RuleResolver`; `UnitStatBlockRenderer` (no resolver in reach) folds against the unit's own
  rules, which is where the base rides; `RuleHoverText.RuleSegments` takes an optional resolver (the army list overlay
  passes its own; the shoot panel and the calculator's weapon rows pass none - a weapon-scoped gate names the weapon's
  own rule, so the line reads right without one; today no weapon-scoped Enhanced rule needs a line). Not changed: the
  token chip's "Gains X - <description> (lifetime)." sentence (`TokenDisplay.RuleDescription`), which splices the text
  mid-sentence; a "Base rule" line would not fit, and an Enhanced rule arrives by token only via an Aura. The CLI
  indents a continuation line under its rule (`StringSelectionResolver`). `SpecialRuleDefinition.Description` itself
  is untouched - OPR verbatim (#436 D3) and serialized. Alternatives: a computed `DisplayDescription` property on the
  record (System.Text.Json would write it into books and saves); per-surface composition (nine copies of one line).
- **D18 - F6 is a draw-time row model over the compiled unit and its items; nothing new is compiled or stored.**
  `ForgeUnitDetail.GearRows(unit, items)` returns the card's `WeaponRow`s (`Weapon`, `Quantity`, `SourceItem`; `Name`
  = "Explosive Spear (Beast Rider)", `Cell` = the counted name) and the item lines to print. For each compiled weapon
  entry it peels off, per item with `Weapons`, up to `bundled.Quantity x item.Quantity` copies whose profile matches,
  tags them with the item NAME (so two copies of one item tag ONE row, and the tag is part of the row key: an
  item's Explosive Spear and the unit's own identical one never share a row), and leaves the remainder untagged -
  untagged first, then the items' rows in item order. An item's line is dropped only when it has no rules AND at
  least one of its weapons was found; an item whose weapons are all gone (a Replace that took the weapon by name
  and left the mount) keeps its bare line so nothing the unit carries disappears. Alternatives: an optional
  "source item" on `WeaponFileEntry` set by the compiler (a schema field that would ride into every saved army and
  into play, for a display tag); tagging in the importer's weapon NAME (would change books and the merge key, and
  "(Beast Rider)" would then show in game, the shoot panel and the CLI, where the Sergeant rename already shows
  how a name-tag spreads).
- **D19 - The split uses the compiler's merge key, exposed as `ListCompiler.SameProfile` (engine, visibility only).**
  The compiler merges weapons on name + range + attacks + AP + rule list; the card must split on exactly that key,
  or it would split a row the compiler merged or merge two it kept apart. #441 D1 / #324 set the precedent of
  sharing the predicate over an app-side copy. The engine change is one word and a comment; no behaviour moves.

## Forks (F1-F9 all signed off 2026-10-31)

- **F1 - A force-org line in the lobby (#430 hand-off).** Today the lobby shows only `LaunchGate` blockers and, on
  launch, the Errors as a confirm; the amber caps live in the Forge and the Army Builder. (A) Nothing (status quo).
  (B) One amber line per army over its caps, beside the Q5 legacy line (`LobbyScreen.LegacyArmyNotices` shows where):
  "Army 'X' is over 2 force-org caps", the lines themselves in its hover; advisory, never blocking. (C) Every warning
  line inline under the player table (tall with four armies). **Recommend B** - visible where the game is agreed,
  compact, and the same shape players already know from the Q5 notice.
  **Signed off B, built 2026-10-31:** "Army 'X' is over 2 force-org caps" (singular "1 force-org cap") in the lobby's
  amber, under the roster beside the #440 / #444 lines, the cap lines (the Forge's / Army Builder's strings) in its
  hover. The host measures the caps against the LOBBY's points limit (`ForceOrgValidator.CompositionWarnings`) and the
  report rides the roster summary (`ArmyListSummary.ForceOrgWarnings`), so a client sees the same line; re-synced when
  the host changes the limit. Never blocks. D10, D11. Tests: engine `LobbyForceOrgNoticeTests`, `ForceOrgValidatorTests`;
  app `LobbyForceOrgNoticeTests`.
- **F2 - The Forge and 3.5 lists (#440 hand-off).** The Forge reopens a 3.5 list against its own embedded 3.5 book and
  says nothing. (A) Nothing. (B) Show `LegacyArmyNotice.Line` (the lobby/CLI amber line) in the Forge status line when
  the opened book is stamped 3.5. (C) B plus a "Rebuild against the 3.6 book" button backed by `ArmyReforge.Reforge`
  (solver, label translation, nearest fit), landing as an unsaved list with its substitution report. **Recommend B
  now and C after Chris rules on #440 D1-D4** (Reforge's substitutions are themselves owner-review decisions).
  **Signed off B, built 2026-10-31** (C deferred until Chris rules on #440 D1-D4): opening a list whose embedded book
  is stamped for an older ruleset sets the Forge status line to "Loaded <file> - Army 'X' was built for OPR v3.5.3; it
  plays with 3.6 rules" in a new amber `EForgeStatusKind.Notice`; a 3.6 list says "Loaded <file>" in green as before.
  D12. Tests: `ForgeLegacyNoticeTests` (the `1k - Ogres` 3.5 fixture vs a Demo-book 3.6 list).
- **F3 - OPR's printed spell text (#439 hand-off; schema).** (A) Keep `SpellText.Describe` everywhere (one renderer,
  always what the engine does). (B) Store the printed text at import (an optional `SpellDefinition.Description`, a
  re-bake) and show it in the Forge, with the canonical line beneath it as "plays as". (C) Store it and show only the
  printed text. **Recommend B** - players compare against the Army Forge list they built, and the canonical line keeps
  the mechanics honest; it is a schema field plus a re-bake, so its own item after the release.
  **Signed off A (keep the engine's canonical spell line everywhere) 2026-10-31. Chris: 'For F3, I think A is okay.'**
  Nothing to build; the plan's section 7 carries no F3 bullet to mark.
- **F4 - Parry in the Combat Calculator (#423 / #442 hand-off).** (A) Keep the volley note ("Parry: 4 dice ignored
  0.67 of 4 hits before the saves."). (B) A "Parry" chip on the volley row like the other rule chips, the numbers in
  its hover. (C) A "parried" column in the save buckets. **Recommend B** - consistent with how the calculator already
  shows rules, no new column.
  **Signed off B, built 2026-10-31:** a "Parry" chip, first on the volley's Save chip line, hover "4 dice ignored 0.67
  of 4 hits before the saves."; the "Parry: ..." note is gone. Chips gained an optional hover (`ChipView`,
  `UiChrome.DrawChip(text, hover)`); the engine's `VolleyReport.Parry` (`ParrySummary`) replaces the note string. D13.
  Tests: engine `CombatCalculatorTests` (Parry cases), app `CombatReportViewTests`.
- **F5 - "Enhanced X without X" (#433 hand-off).** Army Forge only offers Enhanced X where X is present, so this only
  arises in hand-edited lists - which are edited in the Army Builder, not the Forge. (A) Nothing (loads silently
  inert). (B) An advisory line in the Army Builder's validation pane ("Unit: Enhanced X does nothing without X") from
  a small check beside `ArmyRuleAudit`. (C) B plus the launch-time #168 summary. **Recommend B.**
  **Signed off B, built 2026-10-31** (C, the launch-time summary, not built): "Lust Daemons: Enhanced Lustbound does
  nothing without Lustbound" as an amber "!" line in the Army Builder's validation pane, from the engine's
  `EnhancedRuleAudit` beside `ArmyRuleAudit`. The base rule is read off the Enhanced definition's own
  `unitHasRule` / `weaponHasRule` gate - the evaluator's gate - never parsed from the name. D14, D15. Tests: engine
  `EnhancedRuleAuditTests`; app `EnhancedRuleAuditShippedDataTests` (Daemons of Lust's shipped definitions).
- **F6 - How a mount/crew item and its weapons show (#438 D4 hand-off).** Today the item is listed by name ("1x Beast
  Rider") and its weapons are ordinary rows in the weapon table. (A) As now. (B) One line: the item with its weapons
  nested, the weapons left out of the table. (C) Keep the weapons in the table but tag them with the item ("Explosive
  Spear (Beast Rider)") and drop the bare item line when the item has no rules of its own. **Recommend C** - the table
  stays complete for comparing guns and the origin stays visible.
  **Signed off C, built 2026-10-31** (Chris: "I agree, F6 should be C."): on the Forge card (the config pane; the
  Combat Calculator's unit block draws through the same `ForgeUnitDetail.DrawGear`) a weapon row that comes from a
  mount/crew item reads "Explosive Spear (Beast Rider)" in the Weapon column, its stat cells unchanged; the bare
  "1x Beast Rider" line is gone because the item has no rules of its own. An item with rules keeps its line
  ("1x Light Chariot (Armor(4), Fast, Impact(4), Tough(6))") and its Tusks row is tagged "(Light Chariot)" all the
  same. A unit's own weapon never gets a tag. Display only: the compile, the books, the saved army and play are
  untouched; the roster PREVIEW (a book unit, not a built one) and the list pane's one-line weapon summaries are as
  before. D18, D19. Tests: app `ForgeItemWeaponRowsTests` (Jackals Charger / Scavenger Beast, AoF Beastmen's Light
  Chariot, every shipped item with weapons, the synthetic merge/split and vanished-weapon cases).
- **F7 - (new) The two "Enhanced" hovers that do not name their base rule.** Enhanced Watchborn (Watch Brothers, Watch
  Prime Brothers) and Enhanced Vinci Tech (AoF Duchies of Vinci) read "If this model has Versatile Attack ... from
  Versatile Attack" (OPR 3.6.0 verbatim; #436 D3 keeps grammar verbatim); the unit carries Watchborn / Vinci Tech, not
  "Versatile Attack". (A) Leave it. (B) Append "Base rule: Watchborn." to the hover of an Enhanced rule whose text
  never names its base (display-only, every rule-hover surface, OPR text untouched). (C) Normalise the texts in the
  supplements and re-bake (reverses #436 D3 for these). **Recommend B.** Pinned by
  `ArmyForge36Tests.EnhancedHovers_NameTheBaseRuleTheyLift_ExceptTheVersatileFamilyTexts`.
  **Signed off B, built 2026-10-31** (Chris: "For F7, let's do B."): the hover of an Enhanced rule whose OPR text never
  names its base (case-insensitive) ends with one display-only line, "Base rule: Watchborn." / "Base rule: Vinci
  Tech." ("Base rules: X and Y." for several), the OPR text above it untouched. Composed ONCE in the engine,
  `EnhancedRuleGate.HoverDescription(definition, resolver)`, and read from there by every surface that explains a
  rule: the Forge card / upgrade hovers and the Combat Calculator panes (`RuleGlossary.Build`), the in-game unit
  hovers (`TableTooltipOverlay`), the army list overlay (unit and weapon rules), the deployment / unit-picker stat
  block (`UnitStatBlockRenderer`), the shoot panel's weapon rules (`RuleHoverText.RuleSegments`), and the engine's
  own request texts - the action menu's companion hover and its description-rule underlines (`ChooseActionStage`),
  the melee weapon pick's rule rows (`ChooseMeleeWeaponStage`) and the effect-pick dialog (`AbilityEffectChoice`) -
  which the CLI prints too (a second line indented under the rule). Not on: the token chip's "Gains X - ..." sentence
  (`TokenDisplay`, a grant sentence, not a rule hover). FOUND on the way: the shipped gate is on the ACTIVATION
  HELPERS ("Watchborn (Piercing)" / "(Precision)"), not on Watchborn, so F5 would have flagged every Forge-built
  Watch Brothers unit; fixed in the same lookup (D16). The Army Builder has no rule hovers (nothing to add). D16, D17.
  Tests: engine `EnhancedRuleGateTests`, `EnhancedRuleAuditTests` (Watchborn shape); app
  `ArmyForge36Tests.EnhancedHovers_NameTheBaseRuleTheyLift_TheVersatileFamilyTextsThroughTheBaseRuleLine` (the
  "except" turned into the line: exactly those two get it, every other shipped Enhanced hover is the bare text),
  `RuleGlossaryTests`, `EnhancedRuleAuditShippedDataTests` (Watch Brothers).
- **F8 - (new, #430 hand-off item 5) Cap numbers inline or in a hover.** (A) Inline as now: "Too many Heroes: 3 (max
  2 at 1000 pts; 1 per 500)." (B) A short line ("Too many Heroes: 3 of 2") with the basis and the rule in a hover.
  **Recommend A** - plain strings already read well, and a hover would add a hit target per line in two screens.
  **Signed off A (status quo) 2026-10-31.**
- **F9 - (new, #424 hand-off) "Also melee" on the Forge card.** (A) As now: one row, RNG 9", Assault in SPE, the
  glossary explains the melee use - the printed Army Forge list's spelling. (B) RNG cell "9\"/melee", matching the
  in-game hover's `RangeLabel`. (C) An "also melee" tag after the weapon name, attributed via
  `WeaponRoleQueries.MeleeUseSource`. **Recommend A** - match the printed list; the in-game surfaces already say it.
  **Signed off A (status quo) 2026-10-31.**

## Hand-verify checklist (Chris)

Build `feature/opr-3.6`, run `./FdgRaylib/bin/Debug/net8.0/FdgRaylib`. Tick each.

GDF (Battle Brothers, Human Defense Force, Alien Hives, High Elf Fleets):
- [ ] Army Forge: the system combo shows Grimdark Future; the book dropdown has 48 books, including Voidspace
      Stalkers, Daemons of Change/Lust/Plague/War and Saurian Starhosts, and no "Wormhole Daemons" name. Switch to Age
      of Fantasy: 40 books, its own Daemons of War; switching back shows the GDF list again.
- [ ] Battle Brothers -> add Assault Brothers. Config pane weapon table: one "Heavy Pistol" row (RNG 9", ATK A1, AP 1,
      SPE "Assault"), one "Hand Weapon" row; hovering "Assault" shows "Counts as both a ranged weapon and a melee
      weapon." with a solid underline. The list pane prints "5x Heavy Pistol (9", A1, AP(1), Assault)" once.
- [ ] Same unit, upgrades: set "Replace any Heavy Pistol" (Plasma Pistol) to 5 FIRST. In "Replace one Heavy Pistol
      and Hand Weapon" the Energy Hammer radio is clickable, the Great Chain-Sword radio is gray, the header does not
      say "(none to replace)". Pick the hammer: 5 Plasma Pistols + Energy Hammer, 185 pts. Then the other order (new
      unit, hammer first): the Plasma Pistol stepper goes to 5.
- [ ] Human Defense Force: add a squad, Colonel Rixxon twice and a Company Leader. Rixxon's config pane has the join
      dropdown offering the squad; its rule list shows "Unique Hero" (hover: "Counts as having Hero, and this model
      may only be taken once per army."). The second Rixxon row has the red "!", the list pane shows the red "Colonel
      Rixxon: Unique Hero - the army may only include one copy." and amber "Too many Heroes: 3 (max 2 at 1000 pts; 1
      per 500)."
- [ ] Alien Hives: a unit with a Spawn option (e.g. the "Swarm Carrier" upgrade) - "Spawn(Hive Swarms [3])" hovers with the Spawn
      description and a solid underline (it used to be faint with "not enforced").
- [ ] Long hovers in the Forge: Scout, Ambush, Transport wrap into a 28em-wide block,
      nothing cut off; repeat at a larger UI scale (Options).
- [ ] Import the share link `UGDhJMMH0QBP` (High Elf Fleets, 3.6): no "RULES NOT ENFORCED" section; if a unit shows
      "our Forge N pts, Army Forge M pts" with N < M, the grey stale-"Replace X" note sits under it.
- [ ] In game (#424): Assault Brothers shoot with the Heavy Pistols, then charge - the melee weapon pick offers the
      pistol's melee line; the table hover shows `9"/melee` for it.
- [ ] In game (#438 D5): High Elf Fleets Stingers in melee - the Stinger Dagger's Takedown individual-model pick
      appears; pick a model and the wounds go to it.
- [ ] In game: hover a unit with long rules on the table - the descriptions wrap in a column, also at 2x UI scale; an
      action-menu row with a second (companion) button wraps its hover.

AoF (Dwarves, Shadow Stalkers, Sky-City Dwarves):
- [ ] Army Forge, Age of Fantasy: a new list starts at 750 pts. Dwarves: a squad, Gamharl twice, a Dwarf Champion ->
      red "Gamharl: Unique Hero - the army may only include one copy." and amber "Too many Heroes: 3 (max 2 at 750
      pts; 1 per 375)."
- [ ] Shadow Stalkers: the roster pane's SPELLS lists 5 spells and then the amber "Combat Possession: text missing in
      OPR's 3.6.0 data, so it is not listed or castable."; the same under a caster's spell list in the config pane.
- [ ] Sky-City Dwarves: "Aether-Cloud Rune" reads "Loses 2 spell tokens - ..." legibly in the spell list.
- [ ] Army Builder (freeform): load the Dwarves list saved above - the warnings under the points box use AoF's numbers
      (1 per 375) and say "Unique Hero "Gamharl" taken 2 times (once per army)."
- [ ] Lobby: field a 3.5 file (e.g. `FdgRaylib.Tests/Fixtures/legacy-3.5/`) - the amber Q5 line appears (#440).

Forks built 2026-10-31 (F1, F2, F4, F5, F6, F7):
- [ ] F1, lobby: Host a GDF lobby at 1000 pts and assign an army with three Heroes (e.g. the HDF list from the Rixxon
      step above, saved; or any Forge list with 3 Heroes) -> under the roster an amber "Army '<name>' is over 1
      force-org cap"; hovering it shows "Too many Heroes: 3 (max 2 at 1000 pts; 1 per 500)."; set Army Points to 2000
      -> the line goes; LAUNCH is never greyed by it. With a client joined, the client sees the same line.
- [ ] F2, Forge: Load `FdgRaylib.Tests/Fixtures/legacy-3.5/1k - Ogres.fdgarmy` -> the status line reads, in amber,
      "Loaded 1k - Ogres.fdgarmy - Army 'Ogres' was built for OPR v3.5.3; it plays with 3.6 rules"; load a list saved
      from this build -> green "Loaded <file>" as before.
- [ ] F4, Combat Calculator: melee, defender an AoF Hand Weapon unit (Parry(1): Dwarves / High Elf warriors), any
      attacker -> under the volley row the Save chip line starts with a "Parry" chip; hovering it reads "N dice
      ignored M of K hits before the saves."; no "Parry: ..." note under the row; the defender without Parry shows no
      chip.
- [ ] F5, Army Builder: open a Daemons of Lust list, add the rule "Enhanced Lustbound" to a unit that has no Lustbound
      -> amber "! <unit>: Enhanced Lustbound does nothing without Lustbound" under the points box; add "Lustbound" to
      the unit (or to a Hero joined to it) -> the line goes; Save and the lobby launch are not blocked either way.
- [ ] F6, Forge: Grimdark Future -> Jackals -> add Charger Beast; in the config pane the weapon table has one row,
      "Explosive Spear (Beast Rider)" (RNG -, ATK A4, AP -, SPE Shred - the stat cells as before), and NO bare "1x Beast
      Rider" line under the table; add Scavenger Beast -> "Stomp" untagged and "Fumigator (Beast Gunner)". Age of Fantasy ->
      Beastmen -> Waheni Hunt Master, take "Light Chariot" -> "Tusks (Light Chariot)" in the table AND the line "1x
      Light Chariot (Armor(4), Fast, Impact(4), Tough(6))" stays under it. Combat Calculator with the Charger Beast ->
      the same row text. The list pane's one-line summary and the roster preview are unchanged.
- [ ] F7, Forge: Grimdark Future -> Watch Brothers -> add Watch Battle Brothers, take the "Enhanced Watchborn" upgrade
      (or Watch Prime Brothers); in the config pane hover "Enhanced Watchborn" -> the hover is OPR's "If this model
      has Versatile Attack ..." text and ENDS with the line "Base rule: Watchborn."; hover "Watchborn" on the same unit
      -> no such line; hover any other Enhanced rule (e.g. Daemons of Lust "Enhanced Lustbound") -> its text names
      Lustbound and has no "Base rule" line. Age of Fantasy -> Duchies of Vinci -> "Enhanced Vinci Tech" -> ends "Base
      rule: Vinci Tech.". In game with that list: the table unit hover and the army list overlay show the same line
      under Enhanced Watchborn. Army Builder: a Watch Brothers list with Watchborn + Enhanced Watchborn shows NO
      "does nothing without" line; remove Watchborn -> "...: Enhanced Watchborn does nothing without Watchborn".

## Outcome

Built 2026-10-31, **done pending hand-verify and forks**. Goals 1-8 checked against the shipped 3.6 books in
`FdgRaylib.Tests/ArmyForge36Tests.cs` (19 test cases) plus `RuleGlossaryTests` and an engine `ArmyForgeCompilerTests`
case. Two defects found and fixed: Spawn/Split hovered as "not enforced" (D2) and the Forge grayed a pass-through
combo after a counted swap (D1). Three wording/layout choices made with one obvious reading, for owner review: D3,
D4, D5. Nine UI forks (F1-F9) written up; 2026-10-31 Chris ruled six (F1, F2, F4, F5 = B, built: engine `e610806e`, `f88e2e05`,
`54341597`; app `1e60fd2`, `6b670f2`, `251f518`, `568dce7`; F8, F9 = A), and later the same day F3 = A (nothing to build) and
F7 = B (built: engine `1a2badd4`; app with the submodule bump, one commit), and last F6 = C (built: engine `a858941e`
(visibility only); app with the submodule bump, one commit). Every fork is ruled and built or declined. Not built, by design: naming each stale #262 selection
(plan section 7 post-release). Gates: engine 4485 passed / 1 skipped, app 4062, build 0 errors,
`--validate-rules` OK 401, shipped Dead 0, headless smoke exit 0.

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
