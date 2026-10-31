# 323 — Replace upgrades starved by a later section (Titan Lords double Heavy Hammer)

**Status**: awaiting verification (engine + app suites green, headless smoke exit 0)
**Related**: #156 (Army Forge builder), #241 (share-list import / Open in Forge), #261 (quantity-prefixed
replace targets — the previous "the swap silently applied nothing" bug in this same code)

## Goal
Reported 2026-08-02 by a playtester using the bundled Army Forge: on the **War Errant Mini-Titan**
(War Disciples Titan Lords), after trading the Titan Shield for a second Heavy Hammer, only ONE of the two
Heavy Hammers could be upgraded — and importing the same loadout from OPR Army Forge silently discarded the
second Heavy Hammer swap. Done looks like: both hammers swap, both are charged, and an imported list keeps
every selection Army Forge sent.

## Notes

- 2026-08-02 (later): Owner pushed back that the shape looked far more common than the first census
  suggested — correctly. A second, independent audit of all 47 books settled the numbers: **1955 Replace
  sections total; 1471 self-sufficient; 429 fed only from an EARLIER section (always worked); 54 fed only
  from a LATER section; 1 fed from both directions** (Dark Brother Bikers' "Replace Energy Sword" — the
  earlier feed made it look safe, but a player taking only the later one hit the same bug). Of the 55
  broken-or-suspect rows, 24 were FULLY starved (the swap did nothing and charged nothing) and 17 partially
  starved (the reported Titan Lords shape). Alien Hives IS affected: the Hive Lord's "Replace any Heavy
  Razor Claw" is fed by the later "Replace Shredder Cannon" option that grants `2x Heavy Razor Claws`, so
  only 2 of a possible 4 claws could be swapped. New corpus guard
  `FdgRaylib.Tests/ForgeCrossSectionReplaceShippedDataTests` walks every book and asserts each cross-fed
  Replace can spend its whole pool at the right price: **12 books fail it on the pre-fix compiler**
  (AlienHives, Battle/Blood/Dark/Knight/Wolf Brothers, OrcMarauders, all five TitanLords chapters), all 47
  pass after. A second test pins the census at 54 so a re-import that changes the corpus shape is loud.
- 2026-08-02: Fixed in `ListCompiler.CompileUnitDetailed`. Replace applications the loadout can't afford at
  their section's turn are now banked (`OwedApplications`) and settled to a fixpoint after every section has
  had its pass (`ApplyStarvedReplaces`), charging normally as they land. Forge-side, the counted-section
  stepper bound came out of the ImGui draw loop into `ArmyForgeScreen.StepperMax` so it can be tested (its
  arithmetic is unchanged — the UI was already offering the second swap; the compiler was throwing it away).
  6 new compiler tests + 1 import test (engine 2600/2600), 1 shipped-data test on the real
  `TitanLordsWarDisciples.fdgbook` (app 905/905), headless smoke exit 0.

## Decisions

- **Root cause: book section order is not a dependency order.** Compilation applies choices in the book's
  section order (#156's fix for click-order dependence), and that is right for the common case — "Replace one
  Shard Carbine" authored after the "Replace all ... with Shard Carbines" that grants it. But the Titan Lords
  entries author "Replace any Heavy Hammer" *above* the "Replace Titan Shield" whose only option buys the
  second hammer, so the dependency runs backwards: at the hammer section's turn the unit still holds one
  hammer, `Applications()` clamped 2 to 1, and the surplus vanished — no weapon, no points, no warning. The
  Forge's stepper reads availability off the FINAL compiled state, where the shield's hammer is present, so
  the UI happily offered a count the compiler would not honor. Same clamp ate the second swap on import,
  where the reconstructed `UpgradeChoice.Count` was 2 (the importer was never at fault).
- **Retry the shortfall, don't reorder the sections.** The obvious alternative — topologically sort choices
  by grants/targets — changes the application order for ~55 unit/section pairs across the shipped corpus that
  work correctly today (a "Replace all Pistols" would start eating pistols that a later sergeant swap grants
  back). Banking only what a Replace *could not* buy is strictly narrower: sections that were satisfied on the
  first pass behave exactly as before, and the retry can only ever spend targets that are genuinely sitting
  unclaimed at the end. Verified by pinning the no-change cases as tests (never-fed no-op, no-invention clamp,
  all-swap not re-applied).
- **"Replace all" stays a single-pass evaluation.** "Every match" means every match *when the section
  applies*; it must not come back and eat a target a later section grants (High Elf Retributors' sergeant
  keeps the Energy Sword bought after the all-swap). The one exception is an all-swap that found *nothing* —
  it parks and takes the whole pool if a target ever turns up, closing the same hole for that variant. No
  bundled book exercises that shape today (corpus census 2026-08-02), so it ships covered by a unit test only.
- **The fix reaches further than the reported unit.** Corpus census over all 47 bundled books: 23 Replace
  sections target a weapon absent from their unit's base loadout that only a LATER section grants — the four
  Titan Lords chapters' Errant/Pilgrim/Questor/Knight titans, plus the Battle/Blood/Dark/Knight/Wolf Brothers
  "Replace Gravity Pistol" sections, whose target arrives from a later "Replace Flamer Pistol". Those swaps
  used to apply nothing and charge nothing; they now work.

## Found along the way (NOT fixed here)

- **Dead Replace target in the shipped data.** `DwarfGuilds.fdgbook` / "Guardians" / "Replace all Pistols
  and Bashes" targets `Bashes`, which never matches the unit's `Bash` weapon: `ListCompiler.Normalize`
  strips ONE trailing "s", so "Bashes" -> "bashe" and "Bash" -> "bash". Because the section is Affects=All
  (max across targets, not min) it still fires off the Pistols half, so a player taking it keeps all 5
  Bashes **in addition to** the new gear, free. Corpus-wide this is the ONLY dead target (verified twice,
  independently). It is a name-normalisation gap, not an ordering one, so #323 does not touch it — the fix
  is a fork worth an owner decision (book data vs. `Normalize` vs. `OprBookImporter`), see the index line.

## Outcome
Pending hand-verification in the running Army Forge (see the index line for what to check).
