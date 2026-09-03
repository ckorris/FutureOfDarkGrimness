# 375 — AoF rules pt.1: data authoring (renames + composable residue)

**Status**: done (closed 2026-08-22 — census floor reached: every dead ref is a #376 name)
**Related**: #376 (primitives half), #377 (spells), #378 (books), mirrors the #196/#197 split that closed GDF coverage. Reference doc: `/home/chris/Projects/GDF Armies/Age of Fantasy/Special Rules and Spells by Army.md` (local only, copyrighted extract — never copy its text into the repo; see the CLAUDE.md in that folder).

## Goal

Every AoF-book rule name that can be expressed with the existing effect/condition vocabulary
resolves to a definition that actually fires, authored as data. Done means: the AoF corpus
census (to be built like #196's) reports zero dead refs attributable to authorable rules —
whatever genuinely needs a new primitive is explicitly handed to #376, nothing dropped
silently.

Appraisal baseline (2026-08-22, against the ~347-name GDF vocabulary): 40 books, 306
distinct rule names, 852 instances.

- **181 names** already match by name (shared families: Fortified, Resistance, Steadfast,
  Caster Group, the aura library, the Disciples "-bound" families, ...). Expect mostly
  zero work; spot-check text drift.
- **67 names** are exact-text renames of existing GDF rules (Bestial=Scrapper,
  Shadowborn=Darkborn, Empyrean Spirit=Screened, Destroyer=Warbound, Unmovable=Honor Code,
  Lucky=Ferocious, Cursed Undead=Self-Repair, ...) plus **13** Boost/Aura derivatives that
  resolve after substituting the renamed base. Authoring is aliasing/cloning existing defs
  under the AoF names.
- **~41 residue names** (of 45; the other 4 go to #376) are composable from existing
  effects: Boost upgrades (unconditional AP-reduction, always-on -1 to hit, threshold or
  dice tweaks like 2D3 placement), Buff/Mark/Aura wrappers, terrain-proximity rules
  (`mostModelsWithinInchesOfTerrain` exists), spell-only wound ignores (`isSpell` exists),
  scoped variants (Takedown when Shooting), simple stat rules (Good Fighter).
- **7 same-name rules** have AoF text that diverges from the GDF definition (mechanically
  meaningful: Fortified Growth marker timing; LoS clauses on Difficult Terrain Debuff and
  Quick Shot Mark) — author as per-book `ruleDefinitions` overrides, not global edits.

## Design forks to surface before building

- Where AoF defs live: a separate `AofRuleSupplement.json` vs extending
  `GdfRuleSupplement.json` vs book-embedded only. Interacts with name collisions between
  systems and with #378's book bundling.
- Whether renames are aliases (one def, many names) or cloned defs; the rule-name hover
  glossary (#259) and rule tracing should show the AoF-facing name either way.

## Authoring work list (slice B output, 2026-08-22)

Machine-derived by `../GDF Armies/Age of Fantasy/appraisal-2026-08-22/classify-rules.py`
(normalized-text comparison of the two reference docs, self-name masked, rename-substitution to
fixpoint; full texts in `authoring-ledger.md` there - LOCAL ONLY, never commit text). Parse
reproduces the appraisal exactly: 306 distinct names, 852 instances. Work list = 62 census-dead
names + 15 grant-closure names = **77 to author**, plus the 7 same-name divergent redefinitions
(invisible to the census - their names resolve, their AoF text differs).

Batches (one commit each, #196 verification loop + AoF rebake before every commit; base before
wrapper within a batch):

- [x] C1 ignore-wound family DONE: Cursed Undead (+Boost, +Boost Buff -> Self-Repair/Plaguebound
  chain), Angelic Blessing (+Boost, +Boost Buff -> Knightborn). Census dead 495 -> 434 (-61, exact).
  Ethereal turned out to be a reposition rule, not a wound ignore (the plan's guess was wrong) -
  moved to C4, nothing dropped.
- [x] C2 defensive-distance family DONE: Empyrean Spirit trio (-> Changebound chain), Ossified trio
  + Warden trio (-> Guardian chain), 9 defs. Census dead 434 -> 373 (-61, exact).
- [x] C3 on-6 offense family DONE: Bestial trio (-> Mischievous chain), Lucky trio (-> Devout
  chain), Royal Warrior trio (-> Clan Warrior chain; the Boost's near-miss was substitution
  ambiguity, GDF's Clan Warrior Boost is equally ungated - exact clone), Great Sergeant
  (Sergeant + a second 5s hook entry, Weapon scope), 10 defs. Census dead 373 -> 301 (-72, exact).
- [x] C4 movement/reposition DONE (9 defs): Wave-Step trio (-> Rapid Blink chain incl. the
  d3-increment Boost), Royal Legion trio (matches the Titan-Lords Lustbound VARIANT, not base
  Lustbound: rangeModifier +4 shooting + movementBonus +2 Charge, hand-authored on the Versatile
  Reach (Range) shape), Drakesworn (Vanguard's shape as JSON: post-deploy triggeredMove 9),
  Traversal (ignoreEnemyMovementBlock, consumed at MovementRuleQueries; the "friendly" clause is
  redundant - friendlies never block movement here), Ethereal (activated Effect.Teleport +
  Slow-style negative movementBonus entries; fire-lint allowlisted - ChooseActionStage routes on
  the effect TYPE to TeleportStage, name is only the menu label, 6" matches the stage constant).
  Census dead 301 -> 219 (-82, exact). **Grounded Speed -> #376**: mostModelsWithinInchesOfTerrain
  requires IHasTerrain and MoveActionDeclaredContext does not provide it - the #196 "context
  capability" class, deferred not forced (4 refs stay dead until then).
- [x] C5 morale/steadfast + champion wargear DONE (7 defs): Unmovable + Vale Oath (-> Steadfast
  clone), Steadfast Buff / Great Banner / Great Musician / Hold the Line Boost Buff / Defense Buff
  (all F13 Buff wrappers; grants target the GDF bases Steadfast, Courage, Musician, Hold the Line
  Boost, Entrenched - no new bases needed). Census dead 219 -> 185 (-34, exact). **Vale Oath Boost
  (+ its Aura) -> #376**: Shaken recovery rides clearTokenOnRoll, an imperative executable that
  rolls per firing entry - base 4+ plus a boosted 3+ would roll TWICE (P 0.833 vs the intended
  0.667); a threshold shift is not composable as data (2 refs stay dead).
- [x] C6 conditional modifiers/misc offense DONE (16 defs): Buccaneer trio (-> Targeting Visor
  chain), Vinci Tech (-> Versatile Attack choice machinery, helpers cloned as Vinci Tech
  (Piercing)/(Precision)) + Boost (both effects, >9" gate KEPT - the AoF text's "instead of"
  clause removes the pick, not the distance gate; interpretive call, flag if play disagrees) +
  Aura, Shadowborn + Wild Veil families (Darkborn (Defensive) shape as JSON; the min-clamp Boosts
  compose fine - RangeRuleQueries sums deltas and takes the max floor, so the #376 borderline
  worry dissolves), Good Fighter (-> Precision Fighter), Takedown when Shooting (core Takedown's
  targetIndividualModel entry; ordering + per-copy aiming are effect/query-driven, not
  name-driven). Census dead 185 -> 79 (-106, exact).
- [x] C7b Destroyer trio (-> Infected chain, 3 defs) - the F5 extra-wound-on-1 family fell through
  the C1-C7 batch plan entirely; the census caught it. Dead 53 -> 36 (-17, exact). Remaining dead
  = the 10 known names: 8 deferred to #376 (34 refs) + the AP / Counter in Melee quirks (C9).
- [x] C7 wrappers DONE (11 defs): Buff wrappers (Melee Evasion / Piercing Assault / Rapid Rush /
  Versatile Attack - all grant existing core/supplement bases), Mark wrappers (Piercing Fighting
  -> grants Piercing Fighter, Rapid Charge, Surge), Feats (Precision + Piercing on the Speed Feat
  once-per-game self-grant shape, each with its own Boost helper). Census dead 79 -> 53 (-26,
  exact). **Grounded Protection (+ Aura) -> #376**: same class as Grounded Speed -
  SaveRollCompleteContext lacks IHasTerrain, and re-homing the entry to the hit-roll hook risks
  the emit-but-never-consumed trap (8 refs stay dead).
- [x] C8 divergent-7 DONE: only TWO needed redefinitions - Difficult Terrain Debuff (AoF adds a
  line-of-sight requirement to the pick) and Fortified Growth (start-of-round accrual gated on
  not-Shaken, markers never lost, AP per TWO markers with the 2+ marker gate stated in the
  condition so the effect can never no-op). Quick Shot Mark's shipped GDF def already requires
  LoS (matching the AoF text), and Hold the Line / Mobile Artillery / Piercing Spotter /
  Precision Shooting Mark diverge only in grammar/typo - no redefinition, recorded here. AoF-baked
  books verified to embed the AoF versions (later-wins).
- [x] C9 loose ends DONE. (a) The two wargear quirks authored as data: `Counter in Melee` = core
  Counter clone (Counter is already melee-only, the qualifier is redundant); `AP` (Wood Elves
  Barbed Arrows, coreNumeric AP(1) as an item rule) = Unit-scoped ranged-gated Save -1 with
  engineArgumentCount 1 - the arg is UNREAD (data effects cannot read args), exact for the only
  carrier; the description states "(1)" so any future AP(2) reference would be visibly wrong, and
  this ledger is the record. (b) Within-AoF variant review: 8 of 9 names mechanically meaningful
  (Piercing Assault Buff's missing "once" rider is an OPR text omission - cosmetic, standard
  NextTrigger def kept). Owner chose **per-book override files**: `Assets/Books/AofBookOverrides/
  <Book>.json`, baked as that book's LAST supplement (zero new code - slice A's multi-supplement
  flags). Nine files authored: Lust Disciples (melee-wide Melee Slayer + banded regen-ignoring
  Shatter + ungated Versatile Attack helpers), Change/Plague Disciples (ungated VA helpers), War
  Disciples (ungated VA helpers + ungated Warbound Boost), Giant Tribes Lust Disciples + Rift
  Daemons of Lust (range-variant Lustbound pair; RD-Lust also no-LoS Mind Control), Vampiric
  Undead (no-LoS Mind Control), Chivalrous Kingdoms + Dark Elves (unclamped Melee Shrouding -
  overrides CORE by replace-by-name, bake warns as designed). AoF-global Melee Slayer authored
  charge-gated (the 16-book majority + RAW). Lint fixture extended to walk override files.
  Per-book divergence verified in the baked books (Beastmen isCharging vs Lust Disciples isMelee;
  RD-Lust rangeModifier vs HW-Lust movement). **#378 pickup**: its bundling must pass
  `AofBookOverrides/<Book>.json` as the last --apply-rules argument when present. The GDF-side
  Melee Slayer over-grant is filed as **#380** (owner: separate item).
- **-> #376 hand-off (10 names, confirmed non-composable):** Bloodthirsty Fighter,
  Retreating Strike, Reckless Piercing, Reckless Piercing Aura, Ravage Aura (argumented grant),
  Grounded Speed (movement-declare context lacks IHasTerrain; C4), Vale Oath Boost + Vale Oath
  Boost Aura (clearTokenOnRoll threshold not composable - double roll; C5), Grounded Protection
  + Grounded Protection Aura (save-roll context lacks IHasTerrain; C7).

Doc-only names needing NOTHING (defined on rules pages, never referenced by AoF unit/upgrade
data, not granted by any work-list rule): Break, Butcher, Slam, Slayer, Slash, +1 to Defense,
Storm of Change/Lust/Plague/War, Unwieldy and the like - deliberately skipped; the census +
validate-rules closure will catch any of them that ever becomes reachable.

## Notes

- 2026-08-22 (session 2, slice A): **Multi-supplement infra shipped** (`b90bebb`, app-side only):
  empty `AofRuleSupplement.json` beside the GDF one; `--import-opr`/`--apply-rules`/
  `--validate-rules` accept 1+ supplement files merged later-wins by name (new app-side
  `RuleSupplementSet.LoadMerged`); `BundledBookRulebook.Defines()` reads both files; lint fixture
  walks both files (concat, not merge, so an AoF redefinition never shadows the GDF entry out of
  the lint) + 3 merge-semantics tests. Verified: app suite 1185 green, GDF-alone validation
  unchanged (251 defs), GDF book re-baked with GDF+AoF byte-identical, headless smoke exit 0,
  engine untouched.
- 2026-08-22 (session 2, slice 0): **AoF corpus acquired + baseline census.** All 40 official AoF
  book JSONs fetched (Army Forge API, gameSystem=4, slug `age-of-fantasy`, index verified 40/40
  vs the local PDFs) into `../GDF Armies/Age of Fantasy/opr-json-snapshots/`; imported clean via
  `--import-opr` (no game-system gate in `OprBookImporter`) into local uncommitted
  `../GDF Armies/Age of Fantasy/fdgbooks-baseline/` (raw) and `fdgbooks-gdfbaked/` (GDF-supplement
  baked). `--rule-coverage` needed zero changes. **Baseline: 7,815 references; raw dead 2,411
  (197 names); after GDF bake dead 495 (64 names); 0 scope-mismatch either way.** The 64-name
  dead list undercounts the authoring surface: the census does not walk granted names (e.g.
  `Bestial Boost` hides behind `Bestial Boost Aura`), so the appraisal's ~128-name list stays the
  work list; census + validate-rules granted-name closure is the done gate. Census outputs saved
  beside the appraisal artifacts. Oddities to chase: `AP` and `Counter in Melee` each 1 dead ref
  (data quirk?). Naming: index "Chivalrous Kingdoms" = PDF "Chivalrous Knights"; AoF's four
  "Change/Lust/Plague/War Disciples" book names COLLIDE with GDF's own Disciples books - matters
  for #378 bundling (BundledBookRulebook faction matching is name-based) - recorded there when it
  lands.
- 2026-08-22 (session 2): Work started on branch `375-aof-rule-data-authoring`. The filing
  session's appraisal artifacts (reference doc split into 8 batches, the 57-name residue
  list incl. #376's four primitives, a 23-rule within-AoF text-variant checklist, and the
  346-name engine vocabulary snapshot) were recovered from its ephemeral scratchpad and
  preserved at `../GDF Armies/Age of Fantasy/appraisal-2026-08-22/` (local only - contains
  copyrighted text). The full 67-name rename mapping was NOT persisted; it is
  machine-re-derivable by normalized-text comparison of the AoF reference doc against the
  GDF one + the implemented-vocabulary list, and rebuilding it is part of the census work.
  Note the memory/index examples cover only 7 of the 67. The "7 same-name divergent rules"
  list is also unpersisted beyond three examples (Fortified Growth marker timing, LoS
  clauses on Difficult Terrain Debuff and Quick Shot Mark) - re-derive alongside.
- 2026-08-22: Filed. Appraisal numbers above come from a machine-verified comparison of the
  reference doc against CoreRuleCatalog + GdfRuleSupplement + book defs; ~94% of the 852
  instances resolve via existing behavior modulo renames.

## Decisions

- 2026-08-22 (owner sign-off on the filed forks):
  - **Separate `AofRuleSupplement.json`**, beside the GDF one. The supplement CLI flags
    (`--apply-rules`, `--import-opr`, `--validate-rules`) learn to take multiple supplement
    files, merged later-wins by name, so AoF books bake against GDF+AoF. The 7 GDF-divergent
    same-name rules become plain AoF-supplement entries (they win only in AoF bakes);
    per-book `ruleDefinitions` overrides are reserved for names that diverge WITHIN AoF, if
    the census finds any. `BundledBookRulebook.Defines()` learns the second filename. All
    app-side; engine untouched (mirrors #196's guardrail - anything needing engine -> #376).
  - **Census corpus = local AoF book imports.** Fetch AoF Army Forge JSON snapshots into
    `../GDF Armies/Age of Fantasy/` (local only, like GDF's `opr-json-snapshots/`), import
    to `.fdgbook`s in a local uncommitted dir, point the existing `--rule-coverage` at it
    (the flag already takes any directory). True attachment-scope census mirroring #196;
    #378 keeps product integration (slug parameterization, bundling, picker UX).
  - **Renames are cloned defs, not aliases** - re-affirms #196's "data, not aliases"
    (alias shares the definition instance; `ignoreRule` compares identity; clones carry
    AoF-worded descriptions).

## Outcome

**Closed 2026-08-22, same session it started.** Every AoF-book rule name expressible in the
existing vocabulary now resolves to a definition that fires, authored as data on branch
`375-aof-rule-data-authoring` (14 commits, `b90bebb`..`7fea4ff`, engine submodule untouched
throughout - 2969 engine tests green at every step).

- **Census arc: 7,815 references; dead 2,411 raw -> 495 after the GDF bake -> 34**, where 34 =
  exactly the 10 #376 names' references minus the two grant-only ones (8 referenced names), 0
  scope-mismatch throughout. Zero dead refs attributable to authorable rules - the item's goal.
- **77 work-list names + 2 divergent redefinitions + 2 wargear quirks authored**: 75 defs in the
  new `AofRuleSupplement.json` + 9 per-book override files (`AofBookOverrides/<Book>.json`, baked
  as that book's last supplement). 10 names handed to #376 with mechanism-level reasons, each
  recorded in both ledgers the day it was found.
- **Infra that outlives the item**: multi-file supplement merge (later-wins) in the three CLI
  flags; `RuleSupplementSet`; `Defines()` over both files; lint fixture walking both supplements
  + all override files (1280 app tests green); the AoF census corpus (40 local book imports off
  Army Forge snapshots, local-only) and `classify-rules.py` (machine classification of the two
  reference docs - reproduced the filing appraisal exactly: 306 names / 852 instances / 7
  divergent).
- **Verified end-to-end**: full loop (validate, fire-lint, engine suite, 40-book rebake,
  GDF-book byte-identity, headless smoke) before every commit; final smokes played complete
  headless games with compiled Lust Disciples and Ghostly Undead armies - zero rule drops,
  Ethereal's teleport live in the log.
- **Side-finds**: #380 filed (GDF Melee Slayer isMelee over-grant); the AoF/GDF Disciples
  book-name collision recorded for #378; #378 must pass a book's `AofBookOverrides/` file as the
  last `--apply-rules` argument, and the census regression pin lands with #378's bundled books.
