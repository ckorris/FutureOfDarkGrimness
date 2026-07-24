# Work-item number reconciliations

Log of cross-instance number collisions and how they were resolved, moved verbatim out of
`WorkItemsList.md` (2026-07-08). Read this before filing new numbers on a branch that has drifted
from origin/master. Standing precedent: numbers are never reused, and when two parallel sessions
claim the same number, the *unmerged local* item yields to the *merged* one and takes a fresh number.
A per-clone pre-push hook blocks duplicate numbers across the index and the archive.

> **2026-07-23 — reconciliation 21.** The other half of reconciliation 20's crossing. The
> table-background session filed **266 = "a game resumed through the lobby cannot be saved and loaded
> again"** (found while hand-verifying #265) against a local master at `2275ef0`, where 265 was the
> highest number in use and 266 looked free. It was: the five-issue session had already claimed
> **266 = console word-wrap + resolver panel height** and pushed it. Same precedent, other direction this
> time - the console-wrap item was merged, the resave item was not, so **resave-after-lobby-resume
> 266 -> 270** (`WorkItems/270-resave-after-lobby-resume-unloadable.md`; 267-269 were taken by the same
> session, so 270 was the lowest free number). The detail file, its title, the index line, and #265's
> forward reference to it were repointed. Nothing in the source referenced #266 - the bug is filed, not
> fixed. **Left as-is on purpose:** the commit message that filed it as #266, which predates the renumber.
> Net effect of 20 + 21: the table background keeps **265**, console wrap keeps **266**, and the two
> yielding items are **267** (all-models gate) and **270** (resave-after-resume).

> **2026-07-23 — reconciliation 20.** A five-issue session (console wrap, resolver panel height, reposition
> placement, terrain palette, all-models gate) filed **265-269** against a local master whose tip was
> `d517b59`, where 264 was the highest number in use. While it was in progress origin/master landed
> **265 = lobby table background / Battlefield dropdown** (merged, and already moved to the archive). Per
> merged-wins precedent the unmerged local item yields: **all-models gate on unit-wide abilities 265 -> 267**
> (`WorkItems/267-all-models-gate-unit-wide-abilities.md`; 266/268/269 were free on both sides and kept their
> numbers, and 267 was the lowest free number). The detail file, its title, the index line, and the **code
> comments** in `CoreRuleCatalog`, `RuleValidator`, `BookRuleSupplement`, `UnitWideAbilityGateTests` and
> `TeleportRuleIntegrationTests` were all repointed to #267 — the table-background side's own `#265`
> comments (renderer, lobby, `GameSettings`, `ScenarioFile`, and their tests) were left alone. **Left as-is
> on purpose:** the commit messages on both sides, which say "#265" for two different things and predate the
> renumber, as in every prior reconciliation.
>
> **Note for the hook:** this collision would NOT have been caught by the pre-push hook, which only greps
> `WorkItemsList.md` for duplicates *within that file*. Master's 265 had already been moved to
> `WorkItems/Archive.md`, so the merged index contained exactly one 265 line and the hook passed; the clash
> only shows up when the index and the archive are checked *together* — which the hook's own header claims
> it does. Worth widening the hook to `cat WorkItemsList.md WorkItems/Archive.md` (the check that actually
> found this one), since an archived-vs-open collision is now a demonstrated failure mode.
>
> **2026-07-22 — reconciliation 19.** The push of #255's follow-on (team-based victory scoring,
> locally filed as **256**) found origin/master had meanwhile landed reconciliation 18's
> **256 = AI repack clamp immobilizes big/clustered units** (merged, S1 shipped). Per merged-wins
> precedent the local item yields: **team victory scoring 256 -> 257** (`WorkItems/257-team-victory-scoring.md`;
> 257 free on index + archive). Nothing had been pushed, so the renumber landed everywhere before
> publication - detail file, index line, engine source comments (`GameResult.cs`,
> `VictoryCalculationStage.cs`, its tests), and the engine commit message was amended to #257 on
> rebase. The two sessions' engine changes (Ai/Tactician vs GameModel/StateMachine) did not overlap;
> combined suite 1809/0 before push.
>
> **2026-07-22 — reconciliation 18.** Pre-push fetch (before a machine switch) found origin/master
> had meanwhile landed reconciliation 17's **254 = wound-driven morale** and **255 = lobby team
> selection** (both merged and archived), colliding with this session's locally-filed **254 = AI
> repack clamp immobilizes big/clustered units** (unpushed, one engine commit + two superproject
> commits). Per merged-wins precedent the local item yields, skipping the also-taken 255:
> **repack clamp 254 -> 256** (`WorkItems/256-ai-repack-clamp-immobilizes-big-units.md`; 256 free
> on index + archive). Nothing had been pushed, so per reconciliation 13/16 precedent the renumber
> landed everywhere before publication - detail file, index, engine source comments, and the
> (amended/recreated) commit messages on both repos; the engine commit rebased cleanly onto the
> morale + lobby-team work (1804 tests green). Reconciliation 16's `#`-subject rebase hazard was
> again dodged with `-c core.commentChar=';'`.
>
> **2026-07-21 — reconciliation 17.** The push of the wound-driven-morale fix found origin/master had
> meanwhile landed the #201 cover-proximity work and filed **252 = field-texture cover proximity** and
> **253 = cover-bonus placement visual** (both merged), colliding with this session's locally-filed
> **252 = wound-driven morale at half or less** (unpushed, one engine commit + one superproject
> commit). Per merged-wins precedent the local item yields: **wound-driven morale 252 → 254**
> (`WorkItems/254-wound-morale-every-activation.md`; 254 free on index + archive). Nothing had been
> pushed, so the renumber landed everywhere before publication — detail file, archive entry, engine
> source comments, and the amended engine commit message (rebased onto the #201 merge with
> `-c core.commentChar=';'` per reconciliation 16's hazard note; no file overlap, 1793 tests green).
>
> **2026-07-18 — reconciliation 16.** The push of the dice-caption-strip work found origin/master had
> meanwhile landed reconciliation 15's **244 = caster self-boost** (merged), colliding with this
> session's locally-filed **244 = dice caption strip** (unpushed, three superproject commits + one
> engine commit). Per merged-wins precedent the local item yields: **dice caption strip 244 → 245**
> (`WorkItems/245-dice-caption-strip.md`; 245 free on index + archive). Nothing had been pushed, so
> per reconciliation 13's precedent the renumber landed everywhere before publication — detail file,
> index, engine + app source comments, the amended engine commit message, and the three superproject
> commit messages (rewritten via `filter-branch --msg-filter`); no published references predate the
> renumber. The engine commit rebased cleanly onto the caster/clamp work (no file overlap, 1708 tests
> green). Reconciliation 15's `#`-subject rebase hazard was dodged with `-c core.commentChar=';'` —
> worth repeating for any rebase carrying `#NNN:`-style subjects.
>
> **2026-07-18 — reconciliation 15.** The push of the caster self-boost work found origin/master had
> meanwhile landed **243 = objective placement mode** (engine `714cb54`/`d6e79bd`, superproject
> `041b04d`, reconciliation 14 below), colliding with this session's locally-filed **243 = caster
> self-boost** (unpushed). Per merged-wins precedent the local item yields: **caster self-boost
> 243 → 244** (`WorkItems/244-caster-self-boost.md`; 244 free on index + archive). Renumber landed in
> the detail file, index, both repos' source comments, and `docs/ResolverGuide.md` before push.
> **Left as-is on purpose:** the four pre-renumber commit messages say "#243" for this work (per the
> standing commit-messages-are-not-rewritten precedent; two rebased superproject messages were
> recreated with "#244" because a conflicted `rebase --continue` had stripped their `#`-leading
> subject lines as comments — a hazard worth remembering with `#NNN:`-style subjects). The engine
> renumber commit `db076fe` was pushed calling this "reconciliation 14" before the parallel session's
> entry below surfaced in the rebase and claimed the number; this log is authoritative: it's 15.
>
> **2026-07-18 — reconciliation 14.** First push of the objective-placement-mode work tripped the fast-forward reject: origin/master had meanwhile landed reconciliation 13's **241 = Army Forge share-link importer** and **242 = campaign import features** (both merged). This session had filed the objective-placement-mode item locally as **241**. Per merged-wins precedent the local item yields, skipping the also-taken 242: **objective placement mode 241 -> 243** (`WorkItems/243-objective-placement-mode.md`). The renumber landed everywhere before publication - detail file, index, the engine test comment (a one-line follow-up submodule commit `d6e79bd` since the first engine commit had already been pushed as #241), the app-side source comment, and the (amended) engine commit message - so no references predate the renumber except the already-pushed engine commit `714cb54`'s message, which is left as-is per precedent. The engine commit rebased cleanly onto reconciliation 13's Army Forge + casualty-cascade work (1691 tests green).
>
> **2026-07-16 — reconciliation 13.** Pre-push fetch caught a double collision: origin/master had meanwhile landed **239 = weapon effect sets** and **240 = stuck-key hardening** (already archived), while this session had locally filed **239 = Army Forge share-link importer** and **240 = campaign-feature import**. Per merged-wins precedent both local items yield: **share-link importer 239 → 241** (`WorkItems/241-army-forge-share-import.md`) and **campaign import 240 → 242** (`WorkItems/242-campaign-import-features.md`). Nothing had been pushed, so like reconciliation 12 the renumber landed everywhere before publication — detail files, index, engine + app source comments, and the (amended) commit messages; no commit messages predate the renumber. The engine commit also gained the #239 weapon-effect integration on rebase (`WeaponEffectAssigner.ApplyToArmy` stamps imported armies).
>
> **2026-07-09 — reconciliation 12.** The first push of the #194 FdgLab work tripped on a fast-forward reject: origin/master had meanwhile landed the faction-rule coverage items as **196/197** (that session's own 191→196 / 192→197 renumber, per its commit `4103181` "yield to master's Tactician AI umbrella" — it consumed two of the numbers reconciliation 11 had reserved "for the other instance", and added no log entry here). This session had just filed **196 = engine run-to-run nondeterminism** locally (unpushed). Per merged-wins precedent the local item yields: **nondeterminism 196 → 198** (`WorkItems/198-engine-run-to-run-nondeterminism.md`; references updated in the index, #159, #191, #194, and the FdgLab source comments before any push, so for once no commit messages predate the renumber). 199-200 remain free.
>
> **2026-07-09 — reconciliation 11 (pre-emptive, no collision occurred).** This session filed the playtest work as **191** (shooting out of cover) and **192** (the 2026-07-09 playtest fix batch) while a parallel instance was about to claim 191/192 for unrelated work. Rather than let the pre-push hook catch it, this session's two items yielded *before* either side pushed: **cover 191 → 201** (`WorkItems/201-cover-attacker-side.md`) and **playtest fixes 192 → 202** (`WorkItems/202-playtest-fixes-2026-07-09.md`). 193-200 are deliberately left free for the other instance. Nothing else referenced the old numbers; the index, both detail files, and their cross-references were updated in the same commit. **Left as-is on purpose:** the pre-renumber commit message `File #191: shooting out of cover grants the defender cover`, matching every prior reconciliation's precedent that commit messages are not rewritten.
>
> **2026-07-03 — reconciliation 10.** A parallel session shipped **#70** (save/load stable type IDs; engine rebased onto master as `9b78d5b`, carrying two pre-existing local engine commits — Rending per-hit AP + Counter banner). Scoping #70 also closed **#039** (`CreateFromTypeMap` was already implemented, folded into #052) and **#154** (destroyed-transport morale — closed won't-do: transport destruction already auto-Shakes occupants per the rule text, so a morale test would *replace* that, not add to it), and opened a follow-up to audit the STJ rule-attachment blob (`RuleAttachmentPersistence`) for the same rename fragility. That follow-up was locally filed as **156**, colliding with master's Army Forge builder (#156, reconciliation 8); per the never-reuse rule it yields **STJ-rule-blob-audit 156 → 160** (157/158/159 taken). #039/#070/#154 moved to Done.
>
> **2026-07-03 — reconciliation 9 (RESOLVED).** First push of the `153-army-forge-builder` branch tripped the pre-push duplicate check: **154** was claimed twice — origin/master's **154 = "Destroyed-transport morale test"** (merged, via the #093 branch's reconciliation 7, where it was noted "free on master and kept") vs this branch's locally-filed **154 = "Intermittent `DefinePathStage` cohesion crash"** (2026-07-02, hero-join smoke-flake investigation; never pushed). Reconciliation 8's own parenthetical already treated 154 as destroyed-transport, so the cohesion-crash line surviving the merge unrenumbered was an oversight the hook caught. Per merged-wins precedent, **cohesion crash renumbered 154 → 159** (157/158 were taken by the 2026-07-03 hand-verify items); references updated in #017's Done line and the #156 ledger. Commit messages saying "#154" for it predate the renumber.
>
> **2026-07-02 — reconciliation 8 (RESOLVED).** Merging origin/master into the `153-army-forge-builder` branch surfaced a second **153** collision, orthogonal to reconciliation 7. This branch had used **153 = "Army Forge catalog builder"** for its entire life, developed in parallel and never seeing that origin/master's #150 pass had assigned **153 = "Shape-owned pairwise geometry"** (merged, Done, `WorkItems/153-shape-pairwise-geometry.md`). Per the never-reuse rule + reconciliation 5–7 precedent (the *unmerged local* item yields to the *merged* one), **Army Forge renumbered 153 → 156** (`WorkItems/156-army-forge-builder.md`; 154 = destroyed-transport, 155 = terrain-indication). The detail file, its title, the index line, and the forward-pointing ledger references in #107 and #154 were updated; **left as-is on purpose:** commit messages ("#153 …", pre-renumber, like all prior reconciliations) and the git branch name `153-army-forge-builder`. Master's #153 (shape geometry) keeps the number. The engine/app merge itself is complete and green (submodule `fc71ebb`, superproject `1c360e5`); this was a pure bookkeeping follow-up.
>
> **2026-07-02 — reconciliation 7.** Merging origin/master into the `093-per-model-special-rules` branch surfaced one number collision and (silently) some mis-merged movement code. (1) **153** collided: origin/master's #150 base-shape pass had already assigned **153 = "Shape-owned pairwise geometry"** (merged, Done), while this branch had locally filed **153 = "movement GUI terrain indication"**. Per the never-reuse rule the local item yields: **terrain-indication 153→155** (`WorkItems/155-movement-terrain-indication.md`; commit messages saying "#153" for it predate the renumber). The **154** destroyed-transport item was free on master and kept. (2) The auto-merge of the overlapping movement files (both #150 facing/footprints and #093 per-model budgets touched them) silently reverted a few of #093's per-model `ValidatePaths` call sites back to the unit-scalar overload in `AiDefineMovementResolver` and the CLI `DefineMovementPathResolver` — restored by hand post-merge (engine `1eaeaae`), caught by grep-audit since they compiled + passed tests.
>
> **2026-06-22 — reconciliation 6.** Merging origin/master into the `033-caster` branch surfaced two more parallel-instance collisions. (1) **094** was claimed a third way: this branch had filed *friendly-Caster ±1 cast assist* as 094, but master's 094 is *group-move coherency repair* (kept; reconciliation 5 had already moved the rules-rehydration item to 095). The caster-assist item yields: **caster-assist 094→103** (`WorkItems/103-caster-assist.md`). (2) Master's **#100 special-rule-primitives** independently shipped the granted-rule read-back + FirstTrigger consume this branch had built as **#101 keyword-buff bridge**; the branch adopts master's #100 bridge (this branch's redundant bridge commits dropped) and keeps **101** for the keyword-buff item (free on master) with three fixes folded onto #100's bridge (robust consume-at-combat-hooks, occurrence-based consume, payload-precise token clear). Branch/commit messages saying "#094"/"#095" for these predate the renumbers.
>
> **2026-06-21 — reconciliation 5.** Number **094** collided: origin/master assigned it to *group-move coherency repair* (merged), while a parallel session had filed it as *special rules not re-attached on save/load resume*. Per the never-reuse rule the unmerged item yields: **rules-rehydration 094→095** (`WorkItems/095-rules-not-rehydrated-on-resume.md`; index + the #035 cross-references updated; its commit messages predate the renumber). Master's #094 (group-move cohesion) keeps its number. The Transport follow-ups opened the same day take **096/097/098**.
>
> **2026-06-14 — reconciliation 4.** The morale epic (Shaken/Rout, fatigue, Fear/Fearless, decisive rolls) was built on a local master that had fallen behind origin/master, which had meanwhile assigned **089** and **090** to other already-merged work (089 = AI charge-to-contact; 090 = enemy-check consolidation/executor). Per the never-reuse rule the morale items yield: **morale-core 089→091** and **decisive-rolls 090→092** (`WorkItems/091-morale-core.md` / `WorkItems/092-decisive-rolls.md`; index + cross-references updated). The branch name `089-morale-core` and the slice commit messages predate the renumber and keep #089/#090. The shared-number items #008/#009/#020/#021 kept their numbers (same meaning on both sides).
>
> **2026-06-13 — reconciliation 3.** The 2026-06-10 audit follow-ups (`Audit-6-10-2026.md`) were authored on a local branch that numbered its four HIGH-priority stage-machine/networking items **055–058**; by the time they were folded into this index, origin/master had already assigned 055–058 to other work (rule attribution, presentation beat stream, contexts refactor, STJ migration). Per the never-reuse rule the audit squatters yield: **055→083, 056→084, 057→085, 058→086** (internal cross-references updated). The remaining audit items (**059–070, 073–082**) kept their numbers, which were free on master. Audit item **060**'s dead-field cleanup landed the same day (commit `b0aebc9`); the rest of #060 remains open. (The same local branch had also renumbered the presentation beat stream / sound to 071/072 — those are *not* carried here, since master already settled them as #056/#053 in reconciliation 2.)
>
> **2026-06-11 — reconciliation 2.** The never-reuse rule was violated again on master: **052** meant both *save/load* and the *presentation beat stream*, and **053** meant both the *contexts-into-store refactor* and *sound cues* (a new pre-push hook now blocks duplicates). Resolved by the same detail-file/cross-reference precedent as #055's renumber: **save/load keeps 052** (the #039/#054/#057 "follow-up to #052" references all mean it; merge commit `b7acb76` names it) and **sound keeps 053** (owns `053-sound.md`). The presentation beat stream is now **056** (`WorkItems/056-presentation-beat-stream.md`, renamed) and the contexts refactor is now **057**. Branch names / old commit messages containing `052-presentation-beat-stream` and `#053` predate the renumber.
>
> **2026-06-03 — reconciliation.** This index had drifted out of sync with the `WorkItems/NNN-*.md` detail files and git history. Numbers **044/045/046** had each been reused across two parallel efforts (a terrain/deployment effort and a line-of-sight effort), violating the never-reuse rule. Resolved by treating the on-disk detail files + merged commits as authoritative: **044/045/046 now mean the line-of-sight cluster** (matching `WorkItems/044-046-*.md`). The two terrain tasks that had been squatting on 044 and 046 were reassigned fresh permanent numbers **049** and **050**. Work item **012** (merged: engine `a967fa1`, GUI `3a6f189`) and **044** (LoS ally-exclusion, merged `8701abf`) were complete but never checked off — fixed. Terrain rotation, formerly listed as its own #045, is folded into the #002 entry where it actually shipped. Items **041 / 045 / 046** are implemented and on master but parked in *Awaiting verification* until manually eyeballed in the running app.

> **2026-06-21 — morale epic reconciled.** Items 008/009/020/021/091/092 were marked `[~]` "on branch / pending merge / unmerged" in their topical sections (Activation flow / Melee / Networking), but all nine underlying engine commits have since merged to submodule master (via the `089-morale-core` → `021-morale-rules` line, tip `2c7d342`, now in master's history) and are ancestors of the superproject-pinned commit `f467933`. Moved here; the only remaining step is GUI hand-verification.
