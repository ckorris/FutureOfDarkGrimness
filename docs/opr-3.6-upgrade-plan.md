# OPR v3.6.0 upgrade plan (Grimdark Future + Age of Fantasy)

**Status:** planning complete 2026-10-29; implementation not started. Umbrella work item: **#415**.
**Branch:** `feature/opr-3.6` in BOTH repos (superproject `fdg-raylib` and the engine submodule
`FutureOfDarkGrimness`). Nothing from this upgrade lands on `master` until the whole plan is green (#444).
**Owner:** Chris. **Sources of truth:** section 3. **Open questions:** section 6 (defaults are assumed until
Chris answers; every item that depends on an answer says so).

OPR published v3.6.0 on 2026-10-29 ("v3.6 is Now Available!"). Army Forge already serves 3.6.0 for every
official book, so this plan sources armies from the Army Forge API, not from PDFs; only the two core-rules
PDFs and the two Beginner's Guides were downloaded (rule text). We drop 3.5 support entirely, except that
already-saved `.fdgarmy` files (which embed their rule definitions) must still load.

---

## 1. Ground rules for every agent working on this upgrade

These apply on top of `CLAUDE.md`, which still governs (no AI attribution in commits, verify before commit,
submodule-first cadence, ASCII-only game text, one vertical slice at a time, never silently cut scope).

1. **Branch.** Work on `feature/opr-3.6` in both repos. `git -C FutureOfDarkGrimness branch --show-current`
   and `git branch --show-current` must both print `feature/opr-3.6` before you commit. Never commit to
   `master`, never merge to `master`, never push `master`. Only #444 merges the feature branch, after #445.
2. **Commit cadence.** Engine change -> commit in the submodule on its `feature/opr-3.6` -> bump the
   submodule pointer together with the app-side change in a superproject commit on `feature/opr-3.6`.
   Commit subjects start with the work-item number (`423: Parry(X) ...`). Push with
   `git push origin feature/opr-3.6` in each repo (set upstream on first push). Do not force-push.
3. **Picking up master.** Fixes keep landing on `master` meanwhile. Only the #415 owner merges `master`
   into `feature/opr-3.6` (both repos, submodule first), and does so after each phase. Do not rebase.
4. **Numbers.** Items #415-#447 are reserved in `WorkItemsList.md` (section "OPR 3.6 upgrade") and the
   reservation is recorded in `WorkItems/Reconciliations.md`. A new sub-item needs a fresh number:
   `git fetch origin` first and take max(origin/master index + archive, this plan) + 1, per
   `WorkItems/README.md`.
5. **Rule text.** The 3.6 rules you implement come from, in priority order: (a) `GF - Core Rules v3.6.0.txt`
   / `AoF - Core Rules v3.6.0.txt` in the local-only folder `~/Projects/GDF Armies/Rules 3.6.0/`;
   (b) the Army Forge common-rules JSON in the same folder; (c) the 3.6.0 book snapshots' `specialRules`
   definitions (`~/Projects/GDF Armies/opr-json-snapshots-3.6.0/` and
   `~/Projects/GDF Armies/Age of Fantasy/opr-json-snapshots-3.6.0/`); (d) the OPR patch notes summarised in
   section 2. The 3.5.1 PDF, the OPR wiki, memory and old code comments are NOT sources. Never copy the
   PDFs, their text extracts, or the census into the repo (copyrighted; the folder's own `CLAUDE.md`
   forbids it). Quoting a rule's text in a work-item file or a code comment, as past items do, is fine.
6. **Scope policy.** 3.6 replaces 3.5. Delete or rewrite 3.5 behaviour; do not add version switches,
   `IsV36` flags, or dual code paths. The single exception: old `.fdgarmy` files with embedded rule
   definitions keep loading (#440). `.fdgsave` files from 3.5 are not supported.
7. **Verification per item** (in addition to CLAUDE.md's engine suite + build + headless smoke):
   `FdgRaylib --rule-coverage FdgRaylib/Assets/Books` must not regress (Dead count only goes down), and
   any rule you (re)implement gets an integration test mirroring the nearest `*RuleIntegrationTests`.
   Items that touch a GUI resolver end in "awaiting GUI hand-verify" for Chris, like every other item.
8. **Ledger discipline.** Each item's detail file (`WorkItems/NNN-*.md`) is the running record: dated notes
   newest-first, decisions, deferred facets named explicitly. Update the status column in section 5 of
   this plan when an item changes state. Design forks (sections marked "fork") need Chris's sign-off
   before building; present options + a recommendation in the detail file and stop there.
9. **Model / effort.** Section 5 recommends a model and effort per item. They are recommendations for
   whoever launches the session (Chris), not something an agent switches itself.

---

## 2. What changed in 3.6 (facts, with where they came from)

### 2.1 Core rules (two-page Core Rules v3.6.0; PN = also in the OPR patch notes)

| # | Area | 3.5.1 | 3.6.0 | Item |
|---|------|-------|-------|------|
| C1 | Melee strike range | models within 2" strike | "Models within 3" of enemies must strike"; charged defenders move "up to 3"" into contact; after melee the survivor may move up to 3", chargers move back 1" (PN) | #417 |
| C2 | Fatigue | hit on unmodified 6 after attacking in melee | same, plus "those hits don't count as rolls of 6 for special rules" (PN) | #418 |
| C3 | Melee result | compare wounds each unit caused | "compare the number of wounds each unit took during this activation, and the unit with the highest total loses"; Fear(X): "enemies count as having taken +X wounds" (PN) | #419 |
| C4 | Dangerous terrain | one die per model | "roll as many dice as the number of wounds it would take to fully destroy them, 1 = one wound" (PN) | #420 |
| C5 | Hero | hero may take morale tests on behalf of its unit | "The Hero's unit, and units within 6" of friendly Heroes with Tough(9) or higher, may use the Hero's Quality for Morale Tests as long as the Hero isn't Shaken" (PN) | #421 |
| C6 | Impact(X) | rolled when attacking after charging | "When charging, before either unit attacks, roll X dice, unless this unit is Fatigued" (PN) | #422 |
| C7 | Counter | strikes first when charged AND -1 Impact die per Counter model | "May strike first with this weapon when charged, unless this unit is Fatigued"; the Impact facet became **Brace(X)**: "Enemies charging this unit get -X total Impact rolls, unless this unit is Fatigued" (PN) | #422 |
| C8 | Parry(X) | (did not exist) | "When this unit takes hits in melee, roll X dice per model with this rule in it, up to as many dice as hits taken. For each 6+ one hit is ignored (defender picks which)". AoF Hand Weapons carry it (PN) | #423 |
| C9 | Assault | (did not exist) | "Counts as both a ranged weapon and a melee weapon." 2740 GDF + 763 AoF weapon references (PN) | #424 |
| C10 | Cumbersome | (did not exist) | "May only use Hold or Advance actions, but can't shoot when moving." Artillery weapons carry it (PN) | #425 |
| C11 | Indirect | -1 to hit after moving; ignores LoS and cover from sight obstructions | "When using Hold actions and shooting, may target enemies that are not in line of sight as if in line of sight, and ignores Cover from Sight Blockers" (PN) | #425 |
| C12 | Artillery | unit-wide "all models" | "If most models in this unit have this rule, as long as they haven't moved during this round, enemy units shooting at them from over 12" away get -1 to hit"; +1 to hit at Hold over 12" unchanged | #425 |
| C13 | Scout | within 12" of own zone | "anywhere fully within 9" of their deployment zone, over 9" away from non-Scout enemy units" (PN) | #426 |
| C14 | Vanguard / Fanatic (book rules) | place within 9" of position | adds "over 9" away from non-Vanguard enemy units" (PN) | #426 |
| C15 | Unstoppable | ignores all negative modifiers | "ignores all negative modifiers to this weapon which are not from this model's special rules" (PN) | #427 |
| C16 | Aircraft | -12" range to targeters | "-12" range and -1 to hit rolls"; "can't be moved in contact with or charged" (PN) | #427 |
| C17 | Flying | ignores terrain effects whilst moving | "ignores the effects of Difficult and Dangerous terrain" (PN) | #427 |
| C18 | Transport | enter within 2" | "getting in contact to enter, and staying fully within 6" when exiting" | #427 |
| C19 | Spell Amplifier | (did not exist) | "Gets +1 to casting rolls." (Caster Staff upgrades carry it) (PN) | #427, #429 |
| C20 | "all models" gates | Fearless, Regeneration, Stealth and ~40 faction rules gate on ALL models | all read "where most models have this rule" (PN: "Rules that apply to the whole unit now require most models to have them") | #428 |
| C21 | Caster | "Roll one die, on 4+ resolve the effect on a target in line of sight"; boosters within 18" in LoS | "Pick a target and roll one die, and on 4+ resolve the spell's effect"; LoS moved into each spell's text; boosters "within 18" of the Caster" (no LoS clause); "Once per activation, before attacking" (PN) | #429 |
| C22 | Force org | 1 Hero/500pts, 1+X copies per 1000pts, 35% cap | adds "max. 1 unit per 200pts, and max. 1 model or Tough per 20pts" (PN: 100 models at full match); AoF: 1 Hero/375pts, copies per 750pts, suggested 750/1500pts | #430 |
| C23 | Shred, Surge | book rules | core rules (already in the engine catalog) | #436 |
| C24 | Takedown | | reworded for clarity, same function | #436 |
| C25 | AoF Sergeant / Banner / Musician | Sergeant: hero-style morale; Banner +1 morale | Sergeant "gets AP(+1) when attacking"; Banner "+1 to Morale Test rolls triggered by shooting/melee"; Musician "+1" when using move actions" (PN) | #435 |

Core special rules per the Army Forge common-rules API (39, identical for GDF and AoF): AP, Aircraft,
Ambush, Artillery, **Assault**, Bane, Banner, Blast, **Brace**, Caster, Counter, Deadly, Fast, Fear,
Fearless, Flying, Furious, Hero, Immobile, Impact, Indirect, Limited, Musician, **Parry**, Regeneration,
Relentless, Reliable, Rending, Scout, Shred, Slow, **Spell Amplifier**, Stealth, Strider, Surge, Takedown,
Thrust, Tough, Transport. The core PDF additionally lists **Cumbersome** and Unstoppable; the AoF PDF also
lists Sergeant. Both lists are in `~/Projects/GDF Armies/Rules 3.6.0/`.

### 2.2 Faction (book-defined) special rules

Generated by `scripts/census-opr-books.py` from the 3.5.x vs 3.6.0 snapshots (full report:
`~/Projects/GDF Armies/Rules 3.6.0/census-3.5.x-vs-3.6.0-books.md`):

| | GDF (48 books) | AoF (40 books) |
|---|---|---|
| rule names newly referenced | 93 | 82 |
| rule names no longer referenced | 69 | 67 |
| definitions whose text changed | 144 | 149 |
| references with no definition after a scratch import against today's supplements | 4774 across 122 names | 2038 across 111 names |

The churn falls into a few families (this is what makes the faction work tractable):

- **F1 Weapon rules, new.** Anti-Cover ("This weapon ignores Cover", 45 GDF books), Anti-Regen ("ignores
  Regeneration"), Smash (re-roll 6s to block vs targets over half the weapon's range away), Pierce
  (ignores positive Defense modifiers; AP+1 over half range), Fracture (AP+2 on 6s), Fragment (vs Def 5+/6+
  targets re-roll 5-6 saves), Puncture (vs Def 2+/3+: AP+2, ignores Regen), Obliterate (vs Tough(3)+:
  re-roll 5-6 saves, ignores Regen), Decimate (vs targets above half strength: re-roll 6 saves),
  Destructive (now: vs targets above half strength AP+1), Demolish (ignores Cover; AP+1 vs most models
  within 1" of terrain), Purge (AP+2 vs Shaken/Fatigued), Reap (re-roll 5-6 saves vs Shaken/Fatigued),
  Scourge (each 1 to block = 2 extra wounds for melee result / morale trigger only), Unwieldy, Imprecise
  (-1 to hit), Crossing Strike (attack a unit moved through, as shooting), Cumbersome. -> #431
- **F2 Range-conditional army-wide rules.** The 3.5 "Boost" mechanic is gone. Offensive rules now work
  "when this model shoots/charges enemies over 9"/6" away" (Devout, Ferocious, Brutal, Havocbound,
  Infected, Violent, Warbound, Piercing Warrior, Versatile Warrior...), defensive ones "when shot/charged
  from over 9"/6" away" (Changebound, Machine-Fog, Guarded, Sturdy, Screened, Knightborn, Warding,
  Angelic Blessing...), morale ones likewise (Hive Bond, Hold the Line, Valiant), and range debuffs
  (Darkborn, Shadowborn, Shrouded, Wild Veil: -4"/-2" to a min. of 6"). Needs one new condition family:
  "attacked from over X inches" measured per attack (shooting range) or per charge (charge distance). -> #432
- **F3 "Enhanced X".** ~60 new names of the form `Enhanced <Rule>` (+ Aura / Buff variants): "If this model
  has Lustbound, it moves +2"/+4" from Lustbound (instead of only +1"/+2")", "If most models have
  Changebound, enemies attacking them always get -1 to hit (instead of only from over 9"/6")", 5-6 instead
  of 6, AP(-2) instead of AP(-1), 2D3 instead of D3, 3+ instead of 4+. They replace the old `X Boost`
  family one-for-one. Design fork (how to express "upgrade the base rule's parameter"). -> #433
- **F4 Everything else.** Phrasing "gets X on its weapons when shooting/in melee/when attacking" (grants
  are now weapon-scoped), "markers" -> "tokens", Bounding/Dash/Nimble "move" instead of "place", Highborn
  (+4"/+2" range), Lustbound/Scurry/Agile (+1"/+2" move), Entrenched (12"/-1), Guardian (unconditional
  AP-1), Disintegrate (vs Tough(3)+), Hazardous (X wounds for Blast/Deadly), Regenerative Strength (4+),
  Mind Control (once per round), Precision/Piercing Tag and Target (new marker semantics), Spawn/Split
  (no objectives the round placed), Dodge (not when Fatigued; replaces Melee Evasion on several units),
  Bravery Aura/Buff (+1 morale; replaces Courage Aura/Buff), Unique Hero (counts as Hero; once per army;
  replaces Unique), Spell Resistance (+Aura), Spell Drain, Quick Throw, Thrust/Counter/Brace "in Melee"
  grants, Versatile Expert (unconditional pick), Piercing/Precision Charge/Marksman/Fighter, Extra Reach,
  Grounded Defense/Evasion, Predator (+Aura), Infiltrating Precision Strike. -> #434 (GDF), #435 (AoF)
- **F5 Dropped.** Counter-Attack (+Aura), Courage Aura/Buff, Crack, Tear, Versatile Attack (+Buff),
  Resistance (+Aura), Strafing, Breath Attack, Mobile Artillery, Re-Position Artillery, Storm of X,
  Unique, every `X Boost` / `X Boost Aura` / `X Boost Buff`, Ignores Cover / Ignores Regeneration
  variants (now Anti-Cover / Anti-Regen), Piercing Assault (+Aura/Buff), Point-Blank Piercing/Surge,
  Quick Readjustment, Grounded Stealth/Reinforcement, Casting Debuff, Brutal Fighter, Predator Fighter,
  Melee Evasion (GDF; AoF keeps it), Shred in Melee, Indirect when Shooting, Courageous, Reach Hunt,
  Reinforced, Precision Hunter. -> #436

### 2.3 Books, units, weapons

- Roster: GDF 47 -> 48 real books (+ **Voidspace Stalkers**, new; "Wormhole Daemons of X" renamed
  "Daemons of X"; "Saurian Starhost" -> "Saurian Starhosts"). AoF 40 -> 40 ("Rift Daemons of X" ->
  "Daemons of X"). Each system also gained a 0-unit parent entry ("Wormhole Daemons", "Rift Daemons") that
  the importer must skip. Patch-note books "Gangs of New Eden" and "Guilds of the Nexus" are not in the
  official index and are out of scope.
- Units: every book changed; the Prime Brothers family and Havoc/Disciples families were restructured
  (9 units gone / 6 new per Prime book; Disciples units renamed "<Flavour> Havoc <Unit>"), Infected
  Colonies and Rebel Guerrillas were rewritten (15-18 new unit names each).
- Weapons: "Many weapons were renamed" (PN); 9-33 new base weapon names per book. Vehicles lose Impact
  for an **Overrun** melee weapon with Thrust; pintle weapons gain Assault; Combat Shields give Armor.
  Weapon effect-set keyword tables (#239/#379) will need the new names.
- Points changed everywhere; the bundled books are re-imported, not patched.

### 2.4 Spells

Every book's six spells changed (threshold and/or text). Patterns: LoS now written into the spell
("within 18" in line of sight"), levels reordered (specialised spells lower, generic higher), level-3
spells +6" range, grants phrased "gets Shred on its weapons when shooting", new rule names inside spells
(Smash, Fracture, Enhanced X), the Dark/Dark Prime Brothers lost "Blessed Ammo" for "Blessed Shroud",
Ratmen renamed two. The spell parser's output today contains names like "Shred on its weapons when
shooting" as unresolved rules (see the coverage report), so the parser needs the new grammar. -> #439

### 2.5 Terrain (Beginner's Guide v3.6.0 p.11-12)

New types **Dense** (LoS into/out of but not through), **Unstable** (counts as Dangerous if any model
moved over 6" into/through it), **Steep** (Difficult going up, open going down); **Elevated renamed
Climbable** (up to 3" tall may be climbed as part of a move; over 3" is Impassable; 3" coherency at mixed
elevations; terrain up to 1" tall ignored for movement). -> #443 (open question Q3)

### 2.6 Out of scope

Advanced rules (Assault action removal, 2D6 casting, command points, side missions), skirmish
"Triplification", Quest/Star Quest, campaign/Horde/Solo expansions, translated PDFs.

---

## 3. Reference material and tooling

Local only (never commit): `~/Projects/GDF Armies/Rules 3.6.0/` (README inside lists every file),
`~/Projects/GDF Armies/opr-json-snapshots-3.6.0/` (49 GDF books + `_index.json`),
`~/Projects/GDF Armies/Age of Fantasy/opr-json-snapshots-3.6.0/` (41 AoF books + `_index.json`). The 3.5.x
snapshots next to them stay as the baseline for the census.

Army Forge API (verified 2026-10-29): book index `GET /api/army-books?filters=official&gameSystemSlug=
{grimdark-future|age-of-fantasy}`; book `GET /api/army-books/{uid}?gameSystem={2|4}`; core rules
`GET /api/rules/common/{2|4}`. Base URL `https://army-forge.onepagerules.com`.

Repo tooling: `scripts/census-opr-books.py` (this plan's section 2.2 numbers); `FdgRaylib --import-opr`,
`--apply-rules`, `--validate-rules`, `--rule-coverage`, `--import-spells`, `--import-section-shapes`,
`--import-book`, `--retrofit-effects`, `--book-to-army` (see `FdgRaylib/Program.cs`); `scripts/
bake-aof-books.sh` (to be generalised by #438). The scratch 3.6 import used for the coverage numbers is
reproducible: import every snapshot with `--import-opr <book.json> <out.fdgbook> <supplements...>` into a
folder outside the repo and run `--rule-coverage` on it.

---

## 4. Phases and order

```
Phase 1  Intake (done): #416
Phase 2  Engine core rules: #417 #418 #419 #420 #421 #422 #423 #424 #425 #426 #427 #428 #429 #430 #446 #447
Phase 3  Faction rules: #431 #432 #433 (fork) #434 #435 #436
Phase 4  Data pipeline: #437 -> #438 -> #439 -> #440
Phase 5  App: #441 #442 #443 (fork)
Phase 6  Release: #444 -> #445
```

Dependencies that matter: #424 (Assault) before #437/#438 (the importer and bake must know the weapon
class); #428 (most-models condition) before #432-#435; #422 (Brace) before #435 (Brace in Melee); #429
before #439; #431-#436 before #438 (books bake against the finished supplements); everything before
#444. Items inside a phase are independent and can run in parallel sessions as long as each stays on
its own files and commits small.

Each engine item is a full vertical slice: engine change + CLI resolver + GUI resolver/overlay + AI
resolver legality + integration test + `docs/ResolverGuide.md` / `docs/EngineNotes.md` line if a number
there changes. Do not leave the GUI half for "later".

---

## 5. Work items

Model: F = Fable 5.1 (hard design / pervasive change), O = Opus 5.5 (engine rule work), S = Sonnet 5.5
(mechanical, data, docs). Effort as the Claude Code `/effort` level.

| # | Title | Phase | Depends on | Model | Effort | Status |
|---|-------|-------|------------|-------|--------|--------|
| 415 | OPR 3.6 upgrade umbrella: plan, branch policy, master merges, release | all | - | F | medium | in-progress |
| 416 | 3.6 reference snapshot + census/coverage tooling | 1 | - | S | low | done 2026-10-29 |
| 417 | Melee ranges: 3" strike, 3" pile-in/consolidate, vertical convention | 2 | - | O | medium | done 2026-10-29 (awaiting GUI hand-verify; owner: D5-D7) |
| 418 | Fatigue: fatigued 6s do not trigger "unmodified 6" rules | 2 | - | O | high | done 2026-10-29 (awaiting GUI hand-verify) |
| 419 | Melee result = wounds taken this activation; Fear(X) rewording | 2 | - | O | medium | done 2026-10-29 (owner review: D1, D3) |
| 420 | Dangerous terrain dice = wounds to destroy the unit | 2 | - | S | medium | done 2026-10-29 (D5 resolved by #446) |
| 421 | Hero morale: Tough(9+) 6" Quality aura; not-Shaken gate | 2 | #428 | O | high | done 2026-10-30 (owner review: D3, D4) |
| 422 | Impact before attacks + Fatigue gate; Counter split; new Brace(X) | 2 | - | O | high | done 2026-10-29 (awaiting GUI hand-verify; owner: D1, D4; D8 resolved by #447) |
| 423 | Parry(X): new melee hit-ignore step with defender pick | 2 | - | O | high | done 2026-10-30 (ParryHitsStage at the new Melee_OnHitsTaken hook, catalog Parry, worst-group-first auto-resolve, B1 pool per attack, group-naming Defense dice beat + "(after Parry)" save label, CombatMath / Calculator mirror; all-90 scratch import Dead 0; awaiting GUI hand-verify; owner review: D2, D3) |
| 424 | Assault weapons count as ranged and melee | 2 | - | O | xhigh | done 2026-10-30 (capability-operation seam `WeaponRoleQueries` + B1 melee effect key; all-90 scratch import Assault 0 dead; awaiting GUI hand-verify after #438; owner review: D2, D3) |
| 425 | Cumbersome; Artillery and Indirect 3.6; Quick Throw | 2 | - | O | medium | done 2026-10-30 (owner review: D1, D3, D4, D5) |
| 426 | Deployment distances: Scout, Vanguard, Fanatic, Reinforcement, Ambush objectives | 2 | - | O | medium | done 2026-10-30 (owner review: D1, D2, D3, D4, D5, D6) |
| 427 | Core small batch: Unstoppable, Aircraft, Flying, Transport contact, Spell Amplifier, Immobile | 2 | - | S | medium | done 2026-10-29 (owner review: D1, D2, D3, D5, D6) |
| 428 | "Most models" gating replaces "all models" everywhere | 2 | - | O | medium | done 2026-10-29 (owner review: D2, D3) |
| 429 | Casting 3.6: LoS per spell, +1 casting modifiers, once per activation, Spell Drain | 2 | #427 | O | high | done 2026-10-30 (owner review: D2, D6) |
| 430 | Force org and points validation 3.6 (GDF + AoF levels, Unique Hero once per army) | 2 | - | S | medium | done 2026-10-30 (owner review: D1, D2, D3, D4, D6) |
| 431 | New weapon-rule family (Anti-Cover ... Crossing Strike) | 3 | #418 | O | high | done 2026-10-29 (owner review: D2, D3, D5, D8) |
| 432 | Range-conditional army-wide rules (9"/6" family) | 3 | #428 | O | high | done 2026-10-29 (owner review: D1, D2, D4, D5) |
| 433 | "Enhanced X" family | 3 | #432 | O | xhigh | done 2026-10-30 (Option B everywhere: 112 definitions as deltas on untouched bases, two engine primitives; all-90 scratch import Enhanced 0 dead; owner review: D4, D5) |
| 434 | GDF supplement sweep: remaining reworded + new rules | 3 | #431 #432 #433 | O | high | done 2026-10-30 (Enhanced family deferred to #433; owner review: D2, D3, D4, D7, D12, D13) |
| 435 | AoF supplement sweep + book overrides + AoF-specific core (Sergeant/Banner/Musician/Brace in Melee) | 3 | #422 #431 #432 #433 | O | high | done 2026-10-30 (Enhanced family deferred to #433; overrides all moot, deleted; owner review: D2, D3, D7) |
| 436 | Remove dropped 3.5 definitions; catalog/supplement text parity with 3.6 wording | 3 | #434 #435 | S | medium | todo |
| 437 | Importer 3.6: shape, version stamp, renamed/parent books, share-link gate, Assault weapons | 4 | #424 | O | medium | todo |
| 438 | Re-bake all 88 books; unified bake script; post-bake passes; effect-set keywords | 4 | #436 #437 | S | medium | todo |
| 439 | Spells 3.6: parser grammar, re-import, per-book parity verification | 4 | #429 #438 | O | high | todo |
| 440 | Regenerate derived data (Random Army catalog, scenarios, fixtures, built-in army); legacy .fdgarmy load check | 4 | #438 | S | medium | todo |
| 441 | Army Forge + glossary UI for 3.6 (Assault display, roster, Unique Hero, import summary) | 5 | #437 #438 | O | medium | todo |
| 442 | Tactician AI + Combat Calculator parity with 3.6 | 5 | phase 2 | O | high | done 2026-10-30 (Assault / Parry / Enhanced halves deferred to #424 / #423 / #433; bench 85.9% vs 85.6%; awaiting GUI hand-verify; owner review: D7, D9) |
| 443 | Terrain 3.6: Dense, Unstable, Steep, Climbable (design fork; Q3) | 5 | - | O | high | deferred past the 3.6 release (Q3 answered 2026-10-29) |
| 444 | Version, docs, release: RULES_VERSION, app version, README, CLAUDE.md, merge to master, tag | 6 | all | S | medium | todo |
| 445 | Final verification: coverage Dead 0, smokes per book, hand-verify checklist | 6 | #444 | O | medium | todo |
| 446 | Units that activate in Dangerous terrain take the test (pre-existing gap found by #420) | 2 | #420 | O | medium | done 2026-10-30 (owner review: D1, D2, D3, D4) |
| 447 | Impact(X) rolled per model, not once per unit (pre-existing bug found by #422) | 2 | #422 | O | medium | done 2026-10-30 (owner review: D1, D3) |

Each number has a detail file `WorkItems/NNN-*.md` with the goal, the 3.6 text, the code anchors
(Appendix A), the acceptance test, and the branch rules restated.

---

## 6. Open questions for Chris

**Answered 2026-10-29: Chris accepted every default below (Q1-Q10).** Pushes done (master reservation,
both feature branches). The agents' per-item decisions (the D entries in #428, #420, #418, #417, #422, #427, #431, #432, #419, #429, #425, #426, #430, #421, #446, #447, #434, #435, #442, #424, #433, #423) are a separate set
and remain open for his review; each item file lists its own.

- **Q1 Number reservation on master.** Items #415-#445 are filed on the feature branch. Other sessions file
  numbers from `origin/master`, so until the reservation is visible there every new master item will
  collide with these. Default: a one-commit docs-only reservation on `master` (index lines + reconciliation
  entry), pushed once you OK it. If you prefer not to touch master, say so and we live with renumbering.
- **Q2 Pushing the feature branches** to `origin` in both repos (so remote sessions can continue). Default: yes.
- **Q3 Terrain 3.6 (#443).** In this release or deferred? It changes terrain layout files, the renderer and
  movement/LoS. Default: deferred to after the 3.6 release; only the Elevated -> Climbable rename and the new
  type names are reserved in the enum. Nothing in #443 is built before you answer.
- **Q4 Vertical melee range.** 3.6 gives one 3" number. The engine's #022 cylinder is 2" horizontal /
  4" vertical (the vertical figure was our convention, not OPR's). Default: horizontal 3", vertical stays 4".
- **Q5 Legacy army files.** 3.5 `.fdgarmy` files load unchanged (embedded definitions). Default: the lobby
  shows a one-line amber notice when a list stamped `OPR v3.5.x` is fielded; it does not block.
- **Q6 "Enhanced X" mechanism (#433) - SIGNED OFF 2026-10-30.** Option B everywhere: additive deltas and keep-best
  thresholds (the Reliable `QualityFloor` ordering precedent: better value first, modifiers after), the complementary
  range arm for the "always" families so base rules are never edited and base + Enhanced cannot stack. Details in
  `WorkItems/433-enhanced-rule-family.md` "Signed-off design".
- **Q7 App version.** `Directory.Build.props` is 0.3.0. Default: 0.4.0 for the 3.6 release (pre-1.0 minor
  bump for a breaking ruleset change), `RULES_VERSION=OPR_3_6_0`, tag `v0.4.0`, title "... v0.4.0 (OPR 3.6.0)".
- **Q8 Bundled roster.** Default: bundle Voidspace Stalkers; skip the two 0-unit parent entries; rename the
  Daemons books to OPR's new names (file names follow); regenerate the Random Army catalog accordingly.
- **Q10 Dangerous terrain "or that activate in it" (found by #420).** A unit that Holds or shoots while standing
  in Dangerous terrain without moving takes no test today; only moves across it (and the debuff) roll. The
  clause is in the 3.6 text (and was in 3.5), so it is a pre-existing gap, not part of C4. Default: file it as
  its own item above #445 once Q1 is settled (so the number is reserved on master too), implement after phase 2.
- **Q9 Parallel sessions.** How many sessions do you want running at once on phase 2/3? The plan assumes up
  to three in parallel (one per independent item), each on its own files.

- **Q11 Parry (#423) design fork - SIGNED OFF 2026-10-30.** Chris chose A1: always auto-resolve, removing the
  worst hits first (hits in one swing differ only by save threshold, so the pick has one right answer), with NO new
  request or UI; a pick dialog for mixed groups was considered and withdrawn because its only purpose was visibility,
  and that is carried by a Parry dice beat that names the parried groups ("ignored 2 Rending hits, 1 base hit") plus a
  "(after Parry)" label on the save beat. B1 (one dice budget per enemy melee attack). Impact hits parryable. Details in
  `WorkItems/423-parry.md` "Signed-off design".
- **Q12 Assault (#424) design fork - SIGNED OFF 2026-10-30.** Chris chose A1 with the name marker replaced by a
  capability operation (`EnableMeleeUse` / `EnableRangedUse` answered by a `WeaponRoleQueries`, roles are grants only,
  resolved once at weapon build and cached) plus B1 (two presentation effect keys derived from the army defaults at
  load, no per-weapon authoring). Details in `WorkItems/424-assault-weapons.md` "Signed-off design". Unblocks #437/#438.

---

## 7. Exit criteria (the #445 checklist)

1. Engine suite green; full `dotnet build` clean; headless smoke exits 0 with the default army and with
   `--army` for at least one GDF and one AoF 3.6 army compiled via `--book-to-army`.
2. `FdgRaylib --rule-coverage FdgRaylib/Assets/Books` reports **Dead: 0**, or every remaining name is listed
   in #436's deferred ledger with a reason.
3. Every bundled book stamps `OPR v3.6.0`; 48 GDF + 40 AoF books; Random Army catalog regenerated.
4. The share-link importer accepts a 3.6 Army Forge list and refuses a 3.5 one with a clear message.
5. All items #417-#442 closed (GUI hand-verify by Chris done or explicitly waived), #443 answered.
6. `RULES_VERSION=OPR_3_6_0` in `scripts/build-dist.sh` and `.github/workflows/release.yml`; README,
   CLAUDE.md examples, `docs/EngineNotes.md`, `docs/ResolverGuide.md` carry 3.6 numbers.
7. Feature branch merged to `master` in the submodule first, then the superproject; `v0.4.0` tagged.

Post-release follow-up (recorded here so it is not lost; NOT a gate for the 3.6 release):

- **Re-train the Strategist leaf on 3.6.** `BuiltInAssets/StrategistLeafV1.json` was trained on 3.5 armies and its
  inputs drifted at #417 (3" melee reach). On small 3.6 scratch armies it scores 69.4% vs the Tactician (#442 D11, N =
  160) against 78.3% on the 3.5 2k panel (#191); the two runs are not comparable (stronger Tactician after #442,
  different pool and points, small N). Step 1, after #438: repeat the #191 head-to-head at 2,000 points on the re-baked
  shipped books with the #442 D11 recipe. Step 2, if the 3.5 figure is not recovered: re-train on 3.6 self-play under
  #191's promotion rule (beats the incumbent on the benchmark, G4). Chris, 2026-10-30: "Retraining should definitely be
  on the to-do list, but it seems like now is too early."

---

## Appendix A - code anchors for the core changes

(Filled from the 2026-10-29 code survey; each work item repeats its own rows.)

Repo-relative; `E/` = `FutureOfDarkGrimness/`, `CRC` = `E/Rules/Dispatch/CoreRuleCatalog.cs`,
`MUA/` = `E/StateMachine/MainPhaseRoundStage/SingleRoundStage/SingleTurnStage/MainUnitActionStage/`.
Full per-item anchor lists (with tests) are in each `WorkItems/NNN-*.md` under "Survey rows".

| Change | Where it lives today | Today's behaviour | Item |
|---|---|---|---|
| C1 melee 3" | `E/Utilities/GameWideConstants.cs:16` (2"), `:22` (4" vertical); `MUA/MeleeStage/MeleeRangeUtilities.cs:19-31`; `PileInUtilities.cs:21` (3"); `ConsolidateStage.cs:11-12` (3"/1") | strike 2"; pile-in and consolidation already 3"/1" | #417 |
| C2 Fatigue 6s | token `E/Rules/Foundation/TokenType.cs:117`; threshold `MUA/.../DetermineHitRollStage.cs:143-147`; raw rolls into `RollToHitStage.cs:84` -> `Condition.UnmodifiedRollEquals` (`Condition.cs:240-244`) | hits only on 6, but 6-triggers (Surge/Furious/Rending/Relentless) still fire | #418 |
| C3 melee result | `MUA/MeleeStage/DetermineMeleeWinnerStage.cs:30-61`; snapshots `CombatActionContext.cs:399, :510`; Fear `CRC:646-656` | already "wounds taken since the melee snapshot"; pre-melee activation wounds excluded | #419 |
| C4 dangerous dice | `MUA/MovementStage/MovementExecutor.cs:65-154`; spillout `E/Rules/Dispatch/TransportUtilities.cs:269-290` | one die per testing model; each 1 wounds that model | #420 |
| C5 hero morale | `E/Rules/Dispatch/HeroStatRules.cs:27-39`; `HeroJoinResolver.cs:26` (Tough cap 6) | hero's Quality while alive; no Shaken gate; no 6" aura | #421 |
| C6 Impact | `CRC:557-567`; `MUA/MeleeStage/ResolveImpactHitsStage.cs`; order `MeleeStage.cs:81-133` | already before strikes; no Fatigue gate | #422 |
| C7 Counter / Brace | `CRC:599-616` (both facets), Counter-Attack `:624-635`; `Effect.cs:979-993`; AoF "Counter in Melee" `AofRuleSupplement.json:2332` | Counter = strike first + impact reduction; Brace absent | #422 |
| C8 Parry | absent; hit groups `MUA/PerHitApSplitter.cs:47-51`; sinks `E/Rules/Dispatch/WoundIgnoreSink.cs` | - | #423 |
| C9 Assault | `E/GameObjects/Core/IWeapon.cs:196-204` (`IsRanged = RangeInches > 0`); ~40 direct range tests in engine/AI/app (list in #424) | melee iff range 0; Assault absent | #424 |
| C10 Cumbersome | absent; Immobile `CRC:2280`; action gating `MUA/ChooseActionStage.cs:656/:708/:825/:849` | - | #425 |
| C11 Indirect | `CRC:140-159`; `SightRuleQueries.cs:35/:45`; `Condition.AfterMoving` `Condition.cs:405-408` | -1 after moving; ignores LoS and ALL cover unconditionally | #425 |
| C12 Artillery | `CRC:~115-125` (all-models gate :122) | unit gate on all models | #425/#428 |
| C13 Scout | `CRC:1798-1808` (12"); `E/StateMachine/DeploymentStage/.../PlaceDeferredUnitsStage.cs:33-88` | zone + 12", list order, no enemy distance, no alternation | #426 |
| Ambush | `CRC:1817-1827`; `StartOfRoundExtraActionStage.cs:100-144`; lockout `ReconcileObjectivesStage.cs:146` | arrival 9" from enemies; objective lockout exists; no alternation | #426 |
| C14 Vanguard/Fanatic | `CRC:1527`, `:1549` | place within 9"; no enemy clause | #426 |
| C15 Unstoppable | `CRC:735-746` | ignores Regeneration only | #427 |
| C16 Aircraft | `CRC:2018-2048`; `AircraftRules.cs:16-19`; `MovementUtilities.cs:283` | -12" range, -1 to hit, uncontactable: all present | #427 |
| C18 Transport | `TransportUtilities.cs:195` (6"), `:208` (1" contact), `:215-216` (centre-to-centre) | contact band already; "fully within 6"" is centre-based | #427 |
| C19 Spell Amplifier | absent; cast roll `MUA/CastSpellStage/CastSpellStage.cs:56, :207, :221` | - | #427/#429 |
| C20 most models | `E/Rules/Definitions/Condition.cs:91-140` `AllModelsHaveThisRule`; validator `RuleValidator.cs:26-40, :177-181`; 19 catalog entries, ~85 supplement uses, 228 book uses | all living models | #428 |
| C21 Caster LoS | `E/Rules/Foundation/TargetSelector.cs:59-61`; `MUA/CastSpellStage/SpellTargeting.cs:108-133`; importer `OprBookImporter.cs:638, :648...` | per-spell flag already; set by spell SHAPE at import | #429/#439 |
| C22 force org | `E/SaveLoad/ForceOrgValidator.cs:15-62`; `ListValidator.cs:16`; `GameSystems.cs:8-49` | 500/hero, flat 3 copies; no 35% / 200pts / 20pts; no AoF levels | #430 |
| version gate | `E/ArmyBuilding/OprListImporter.cs:40` (`"3.5"`), :77-87, :229-239, :552-565; stamp `OprBookImporter.cs:108` | share imports refused once books report 3.6 | #437 |
| book names | `E/ArmyBuilding/WeaponEffectAssigner.cs:192-296`; `OprBookImporter.cs:355-361`; `AofBookOverrides/*.json`; `BundledBookRulebook.cs:83-103`; `ArmyForgeShareService.cs:150-167` | matched as plain strings in ~10 places | #437/#438 |
| spells | `OprBookImporter.cs:125-140, :516-800`; phrase rules `CRC:918-1079` | unparsed spell dropped with a warning; LoS by shape; grant phrase = rule name | #439 |
| armies/ | 33 hand-built files; precedence `E/SaveLoad/ArmyListRuleResolution.cs:79-110` | embedded 3.5 book + definitions win over bundled books | #440 |
| terrain | `E/TableState/Zones/Terrain/ETerrainType.cs:5-14` (Elevated unused); saves store flag names | - | #443 |

## Appendix C - app-side risks the survey flagged

- Every Army Forge share-link import is refused the moment OPR's books report 3.6.x (they do, since
  2026-10-29) until `OprListImporter.SupportedVersionPrefix` is bumped (#437). Until then players can only use
  bundled books and saved files.
- Saved armies carry their own copy of the book, rule definitions and spells, and that copy wins over the
  bundled book, so re-baking books changes nothing for existing `.fdgarmy` files (#440).
- Book names are matched as plain strings in about ten places; a renamed book silently loses its effect
  sets, override file, rule-name fixes, rulebook backfill and Forge matching (#437/#438).
- A spell whose 3.6 wording the parser does not recognise is dropped at import with only a console warning;
  no test pins the spell count per book (#439).
- About 45 `*ShippedDataTests.cs` pin 3.5 rule counts, unit names and verbatim text against the live bundled
  books; they all re-pin after the bake (#438).

## Appendix B - unresolved rule names after a 3.6 scratch import (worklist)

Counts are references (weapon profiles x units x upgrade options), from `--rule-coverage` over the scratch
import against today's supplements. Family letters refer to section 2.2.

GDF top of list: Assault 2740 (C9), Anti-Cover 762 (F1), Smash 114 (F1), Spell Amplifier 114 (C19),
Pierce 100 (F1), Bravery Aura 70 (F4), Scourge 70 (F1), Fracture 62 (F1), Versatile Expert 50 (F4),
Anti-Regen 49 (F1), Demolish 43, Purge 40, Unique Hero 30, Decimate 25, Spell Resistance (+Aura) 43,
Puncture 23, Shadowborn 22, Reap 20, Fragment 19, Cumbersome 18, Crossing Strike 17, Obliterate 16,
Dodge 15, Unwieldy 13, Parry 12, then the Enhanced family (F3) and spell-grant phrasings (#439).

AoF top of list: Assault 763, Parry 252 (C8), Brace 169 (C7), Anti-Cover 88, Spell Amplifier 75,
Anti-Regen 45, Purge 38, Scourge 37, Fragment 35, Spell Resistance 35, Reap 32, Unique Hero 30,
Obliterate 27, Fracture 23, Dodge 21, Cumbersome 19, Decimate 19, Puncture 18, Smash 18, Demolish 17,
Pierce 16, Crossing Strike 15, then Counter in Melee Aura, Piercing Charge (+Aura/Buff), Piercing
Marksman Aura, Versatile Expert/Warrior, the Enhanced family and spell-grant phrasings.

Full lists: `~/Projects/GDF Armies/Rules 3.6.0/rule-coverage-{gdf,aof}-3.6.0-scratch-import.txt`.
