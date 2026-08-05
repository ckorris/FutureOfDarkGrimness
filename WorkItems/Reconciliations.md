# Work-item number reconciliations

Log of cross-instance number collisions and how they were resolved, moved verbatim out of
`WorkItemsList.md` (2026-07-08). Read this before filing new numbers on a branch that has drifted
from origin/master. Standing precedent: numbers are never reused, and when two parallel sessions
claim the same number, the *unmerged local* item yields to the *merged* one and takes a fresh number.
A per-clone pre-push hook blocks duplicate numbers across the index and the archive.

> **2026-08-04 - reconciliation 51 (RESOLVED).** Third collision of the day, and the same trap as 49 and
> 50: this session filed **337 = Takedown/Sniper aims one rifle at a time** against an `origin/master`
> whose index topped out at 336 (fetched immediately before filing, per the rule), built and closed it in
> both repos, then waited three minutes at the owner's request before pushing - and the pre-push fetch
> found origin six commits further on, owning **337 = Shaken badge in the activation picker**,
> **338 = Notice banners linger 2400ms** and **339 = strike-back survivor consolidates** (339 itself the
> product of reconciliation 50, an hour earlier). Merged wins per standing precedent: **Takedown per-rifle
> targets 337 -> 340** (`WorkItems/340-takedown-per-rifle-targets.md`). The renumber landed before
> publication - detail file + filename, the archive entry (the item was already closed and archived), the
> engine source comments in five files, both engine test files, both app resolvers, and the hand-verify
> fixtures (`Scenarios/340-sniper-split-targets.json` / `.fdgsave`, `armies/340-Snipers.fdgarmy`,
> `armies/340-Targets.fdgarmy`). **Left as-is on purpose:** the four pre-renumber commit messages saying
> "#337", per every prior reconciliation. Both merges of origin/master were clean: the incoming work
> (Shaken labels, strike-back consolidation, Notice durations) touches no file this item changed, and the
> only conflict was the superproject's submodule pointer, resolved by taking this clone's engine branch,
> which had already merged origin's.
>
> **Lesson, three times over in one day:** filing a number from a fresh fetch is not enough when the work
> then takes an hour. The number is only safe at PUSH time - fetch again before publishing, and expect to
> renumber.

> **2026-08-04 - reconciliation 50 (RESOLVED).** Same day, same shape as 49, one number along. This
> session filed **337 = strike-back survivor consolidates** against an `origin/master` whose index topped
> out at 336 (fetched immediately before filing, per the rule), implemented and committed it in both
> repos, then waited three minutes at the owner's request before pushing - and the pre-push fetch found
> origin four commits further on, owning **337 = Shaken badge in the activation picker** and
> **338 = Notice banners linger 2400ms**. Merged wins per standing precedent: **strike-back survivor
> consolidation 337 -> 339** (`WorkItems/339-strike-back-survivor-consolidates.md`). The renumber landed
> before publication - detail file + filename, the index line, and the engine source comment + both test
> files. **Left as-is on purpose:** the two pre-renumber commit messages saying "#337", per every prior
> reconciliation. Both merges of origin/master were clean: the incoming engine work (`UnitStatusLabel`,
> the Shaken label tests) touches no file this item changed, and the only conflict was the superproject's
> submodule pointer, resolved by taking this clone's engine branch, which had already merged origin's.
> **Worth noting for the next session:** the fetch-before-filing rule is now failing to prevent
> collisions two sessions running, because the gap that matters is between filing and *pushing*, not
> between fetching and filing. A number is only really reserved once it is on origin.
>
> **2026-08-04 - reconciliation 49 (RESOLVED).** Third session in a row to collide on **333**, and the
> second to lose it to `origin/master`'s *confirm unmoved models on Done*. This session filed
> **333 = melee weapon rules read the way shooting's do** against an `origin/master` whose index + archive
> topped out at 332, implemented and committed it across three commits (engine + app), then found on the
> pre-push fetch that origin had moved 11 commits: 333 was merged, and reconciliation 48 had already spent
> **335** renumbering ITS 333 away. Merged wins per standing precedent, and the local item takes the first
> free number above everything on origin: **melee weapon rules 333 -> 336**
> (`WorkItems/336-melee-rules-match-shooting.md`). The renumber landed everywhere before publication - the
> detail file, the index line, the engine + app source comments and tests, and the showcase scenario
> (`Scenarios/336-weapon-rules-showcase.json`, renamed with it). **Left as-is on purpose:** the three
> pre-renumber commit messages saying "#333", matching every prior reconciliation's precedent that commit
> messages are not rewritten. The engine and app merges of origin/master were clean - the 11 incoming
> commits (#334 forced-charge band, #335 deploy-normally) touched no file this item changed, and the only
> conflict was the submodule pointer, resolved by taking this clone's engine branch, which had already
> merged origin's.
>
> **Worth noting for the next session:** 333 has now been contested three times because three clones each
> filed from a stale index. The rule in CLAUDE.md is `git fetch origin` *immediately* before filing, and
> even that is not enough on a long session - this session's fetch was correct when it filed and stale by
> the time it pushed. Re-checking the number at push time, not just at filing time, is the actual guard.

> **2026-08-04 - reconciliation 48 (RESOLVED).** The SAME item colliding a second time, hours after
> reconciliation 47 moved it. This clone renumbered deploy-normally 331 -> **333** against an `origin/master`
> whose highest number was 332, then spent the session on #334 (forced-charge band) without pushing; by the
> pre-push fetch, origin had landed **333 = confirm unmoved models on Done**, merged. Merged wins again:
> **deploy-normally 333 -> 335** (`WorkItems/335-deploy-normally-button.md`). #334 did NOT collide and keeps
> its number, so 335 is the first free number above it. Markers moved before publication in both repos,
> the same list as 47 plus `docs/ResolverGuide.md`; upstream's own `#333` markers (`MovementStage`,
> `MovementUtilities`, `MovementBackOutTests`, `ModelRoster`, `ModelRosterTests`, and the CLI/GUI Done
> confirmation) were left untouched. Both repos' commit messages saying "#331"/"#333" stay as-is per
> precedent. **The lesson is now unambiguous across 46, 47 and 48: a pre-filing fetch cannot close the gap
> between filing and pushing, and this clone has NO pre-push hook installed** - three collisions on one item
> is what that costs. Install it (snippet in `WorkItems/README.md`) before filing anything else here.
>
> **2026-08-04 - reconciliation 47 (RESOLVED).** The mirror image of 46, from the other side. This clone
> filed **331 = "Deploy Normally" is its own button** against an `origin/master` whose highest number was
> 330; meanwhile the *other* session filed **331 = victory fireworks**, pushed it, closed it and archived
> it. Merged wins, so the unmerged local item yields: **deploy-normally 331 -> 333**
> (`WorkItems/333-deploy-normally-button.md`), 333 being the first free number above origin's archived 332.
> Markers moved before publication in both repos: the engine's `SelectionRequest`, `AiSelectionResolver`,
> `AiStringSelectionResolver`, `ChooseDeployActionStage`, `ChooseUnitToDeployStage`,
> `TransportDeploymentChoiceTests`, `AiSelectionResolverTests`, `AiStringSelectionResolverTests`; the app's
> `GuiSelectionResolver`, CLI `SelectionResolver`, `SelectionResolverTests`, `docs/ResolverGuide.md`; plus
> the detail file + filename and the index line. Upstream's own `#331` markers (`TeamScoreTally`,
> `VictoryCalculationStage`, `VictoryFireworks`, `ViewSettings`, `RaylibRenderer`) were left untouched -
> they are the winning item. The two commit messages saying "#331" stay as-is per precedent. Note for the
> next session: 46 and 47 are the same collision seen from each clone, and neither pre-filing fetch was
> stale at the time it ran. The pre-push hook is the only thing that closes this gap; install it.
>
> **2026-08-04 - reconciliation 46 (RESOLVED).** This session filed **330 = early match decision** (end a
> match once no remaining play can change the result) from an `origin/master` index whose highest number
> was 329, and **331 = victory fireworks** immediately after. Between those two filings and the push,
> origin/master landed **330 = pile-in contact maximization**, already merged AND archived. Per merged-wins
> precedent the unmerged local item yields: **early match decision 330 -> 332**
> (`WorkItems/332-early-match-decision.md`). It took 332 rather than 331 because 331 was already spoken for
> by this session's own fireworks item, which did not collide and keeps its number. The renumber landed
> everywhere before publication - detail file + filename, index line, the engine source comments in
> `MatchDecision.cs` / `ReconcileObjectivesStage.cs`, `MatchDecisionTests.cs`, the repro scenario
> (`Scenarios/332-match-already-decided.json`, renamed, seed included) and #331's cross-references - so no
> reference predates it except the two commit messages saying "#330", **left as-is on purpose** per every
> prior reconciliation's precedent. Worth noting for the next session: the fetch that produced 330 was
> correct when it ran; the gap that bit was the hours between filing and pushing, which no pre-filing fetch
> can close. The pre-push hook is what would have caught it, and did not need to.
>
> **2026-08-02 - reconciliation 45 (RESOLVED).** The dice-stack session collided TWICE, which is worth
> recording as one story. It filed **322** from an index fetched minutes earlier; by the time it went to
> merge, origin/master had taken **322 = "Waiting on" line in the status HUD** (reconciliation 41's
> renumber). 323/324 were gone too (reconciliation 42, visible only in the engine repo at that point), so
> it took **325** - and then, before it could push, reconciliations 43 and 44 landed on origin and
> assigned **325 = pre-roll shooting forecast** and **326 = single-model move roster**, colliding with
> BOTH of this session's numbers. Per merged-wins precedent the unmerged local items yield again:
> **dice stack 322 -> 325 -> 327** (`WorkItems/327-dice-stack-non-blocking.md`) and
> **token-container render-thread race 326 -> 328** (`WorkItems/328-token-container-render-thread-race.md`).
> This entry was itself filed as "reconciliation 43" before origin's 43 and 44 existed, and is renumbered
> **45** here for the same reason.
>
> All markers moved before publication: both detail files + filenames, both index lines, and every
> `#325`/`#326` marker from this session in the engine (`DiceRolledBeat`, `RollToSaveStage`,
> `DiceBeatHoldTests`, `TokenContainer`, `ITokenContainer`, `TokenContainerConcurrencyTests`) and the app
> (`PresentationPlayer`, `DiceOverlay`, `BannerOverlay`, `RaylibRenderer`, `TableTooltipOverlay`,
> `DiceStackTests`, `BannerTierPlayerTests`, `BannerBandLayoutTests`, `UnitOverlayOcclusionTests`).
> Deliberately NOT touched: the merged sessions' own `#325`/`#326` markers in the shooting-forecast and
> model-roster files and `docs/ResolverGuide.md`, and `RaylibRenderer`'s `#322` status-HUD line, which
> belongs to the waiting-HUD item. Commit messages saying `#322`/`#325`/`#326` for this work predate the
> renumbers, as usual.
>
> **Reconciliation 44's observation, confirmed from the other side.** This session fetched at filing time
> AND re-fetched before pushing, and still collided twice - because the second collision was created by
> work that landed *while this session was mid-flight*. Re-checking before the first commit that bakes
> the number in (44's suggested guard) would not have helped either: the collision arrived after that
> point. On the evidence of 39/40/42/43/44/45, reading origin cannot prevent this at any cadence; only
> reserving the number on origin at filing time can.

> **2026-08-02 - reconciliation 44 (RESOLVED).** The model-roster session fetched at filing time and took
> **325** from origin/master at `0f21304`, where it was free across index + archive + `WorkItems/` - but
> reconciliation 43 (below) landed on origin *while the work was in progress* and renumbered the shooting
> forecast INTO **325**, merged and pushed first. Per merged-wins precedent the unpushed local item
> yields: **model roster 325 -> 326** (`WorkItems/326-single-model-move-roster.md`). References updated
> before any push - detail file + filename, index line, `docs/ResolverGuide.md` section, and the app-side
> comments in `ModelRoster`, `GuiDefineMovementResolver`, `ResolverHotkeys`, `EscapeMenuOverlay` and
> `ModelRosterTests`. The local commit predating the renumber was rebased and amended rather than left
> saying "#325", since it had never been pushed and origin's real #325 is the shooting forecast; nothing
> shared ever carried the old number.
>
> **This is the third instance of one race** (39/40, then 43, now 44), and it is worth naming precisely:
> fetching at filing time bounds only what is *already* taken, never what another in-flight session
> claims and pushes first. The pre-push hook catches it, but only after the number has been copied
> everywhere. Nothing here fixes that - a genuinely collision-free scheme would have to reserve the
> number on origin at filing time (an empty commit touching the index, pushed immediately) rather than
> merely reading it. Filed as an observation, not a change.
>
> **2026-08-02 - reconciliation 43 (RESOLVED).** The shooting-forecast session fetched at filing time
> and took **323** from a then-synced origin/master - but reconciliation 42 (the Army Forge session,
> below) landed on origin *afterwards* and renumbered its items INTO **323/324**, merged and pushed
> before this session's first push. Same race as 39/40, from the other side: a fetch at filing only
> protects against numbers already taken, not numbers claimed later by a session that pushes first.
> Per merged-wins precedent the unpushed local item yields: **shooting forecast 323 -> 325**
> (`WorkItems/325-shooting-forecast-ui.md`). References updated before any push - detail file, index,
> engine source comments (ShootingForecast, ChooseRangedAttackRequest, ChooseRangedAttackStage,
> GrantedRollModifiers, CombatActionContext, IWeapon, both test files), app-side resolver comments and
> `docs/ResolverGuide.md` - so only the pre-renumber commit messages say "#323" for it, per precedent.
> Engine merge was clean (Forge vs shoot-stage, no shared files); 2643/2643 green post-merge.
>
> **2026-08-02 - reconciliation 42 (RESOLVED).** The Army Forge upgrade session (playtester bug: only one
> of a Titan's two Heavy Hammers could be swapped) fetched at session start, saw master in sync, and filed
> **318** and **319** from that view. The Limited-weapon and waiting-HUD work landed on origin/master
> *during* the session - 13 superproject / 11 engine commits, including reconciliations 40 and 41 - so by
> the time the work was ready to push, **318 = melee hold-back** and **319 = Limited hold fire** were both
> merged and closed. Per the standing precedent the unmerged local items yield:
> **starved-Replace retry 318 -> 323** (`WorkItems/323-starved-replace-upgrade.md`) and
> **all-swap yields + "-es" plurals 319 -> 324** (`WorkItems/324-all-swap-yields-and-plurals.md`), both
> renamed. All 21 markers were renumbered before publication, confined to the Army Forge files
> (`ListCompiler`, `ArmyForgeScreen`, `ArmyForgeCompilerTests`, `OprListSelectionsTests`,
> `ArmyForgeScreenTests`, `ForgeCrossSectionReplaceShippedDataTests`) plus the two detail files and the two
> index lines; the `#318`/`#319` markers in the shooting/melee files and `docs/ResolverGuide.md` are the
> merged items' own and were left alone. Commit messages from before the renumber predate it, as usual.
>
> **Note on the rule that was supposed to prevent this.** `CLAUDE.md` gained "`git fetch origin` BEFORE
> filing a number and take it from `origin/master`'s index + archive" in `c25861f`, citing reconciliations
> 39 and 40. This session *did* fetch first - the collision came from drift that landed mid-session, which
> that wording doesn't cover. The cheap guard for a long session is to re-fetch and re-check the number
> before the first commit that bakes it into filenames and source comments, not only before filing it.

> **2026-08-02 - reconciliation 41 (RESOLVED).** The waiting-HUD session filed **318** ("Waiting on"
> line in the status HUD) against a local master that predated reconciliation 40, whose session had
> meanwhile taken **318 = melee hold-back is Limited-only** (merged + closed, engine `dcf6e04`).
> Per the standing precedent the unmerged local item yields: **waiting-HUD line 318 -> 322**
> (`WorkItems/322-waiting-on-hud-line.md`, renamed). The renumber landed everywhere before
> publication: the detail file, the index line, and every `#318` marker from that session in the
> engine (`IStageTaskRequest`, `IFDGGame`, the six request classes, `RequestSystemTests`) and the app
> (`StatusHudOverlay`, `RaylibRenderer`, `GuiOutstandingTaskDisplay`) - the surviving `#318` markers
> in `ChooseMeleeWeaponStage` / `MeleeLimitedTests` are the melee item's own. Commit messages
> containing `#318` from before the renumber predate it, as usual.

> **2026-08-02 - reconciliation 40 (RESOLVED).** The Limited-weapon session filed **315** (shooting hold
> fire), **316** (melee Limited enforcement), **317** (companion actions on a menu row) and **318** (melee
> hold-back narrowed to Limited only) against a local master that was 12 commits stale. The pre-push fetch
> found origin/master had meanwhile taken all three of the first numbers: **315 = embarked-unit activation
> disambiguation** (merged, `aa11c54`), **316 = round opens on the wrong player** (merged + closed,
> `d9f887c`) and **317 = difficult-terrain shortfall preview** (merged + closed, itself reconciliation 39's
> 315 -> 317 renumber). Per the standing precedent the unmerged local items yield: **shooting hold fire
> 315 -> 319**, **melee Limited 316 -> 320**, **companion actions 317 -> 321**
> (`WorkItems/319-limited-hold-fire.md`, `320-melee-limited-not-enforced.md`,
> `321-menu-companion-actions.md`). **318 was free on origin/master and is KEPT**, per reconciliation 7's
> precedent that a non-colliding local item does not move — so this group is deliberately non-contiguous
> (318 is a correction of what is now 320). The renumber landed everywhere before publication: the three
> detail files (renamed + retitled + cross-references), #318's cross-references, the archive entries, and
> every `#315`/`#316`/`#317` marker in the engine (`LimitedRules`, `ChooseRangedAttackRequest`,
> `StringSelectionRequest`, `ChooseRangedAttackStage`, `ChooseMeleeWeaponStage`, `CombatActionContext`,
> `MeleeStage`, `StrikeBackStage`, `AiStringSelectionResolver`, 4 test files), the app
> (`GuiChooseRangedAttackResolver`, `GuiStringSelectionResolver`, `ResolverHotkeys`, both CLI resolvers,
> 2 test files), `docs/ResolverGuide.md` and both scenario JSONs. The replacement was scoped to the files
> this session's own commits touched, since master's merged work carries its own `#315`/`#316`/`#317`
> markers that must not be rewritten. **Left as-is on purpose:** the nine pre-renumber commit messages,
> matching every prior reconciliation's precedent that commit messages are not rewritten.

> **2026-08-02 - reconciliation 39 (RESOLVED).** The difficult-terrain movement-preview session filed its item
> as **315** against a local master that was 4 commits stale, not having seen that origin/master had already
> taken **315 = embarked-unit activation disambiguation** (merged, `aa11c54`, `WorkItems/315-embarked-activation-disambiguation.md`)
> and **316 = round opens on the wrong player** (merged + closed, `d9f887c`). Per the standing precedent the
> unmerged local item yields: **difficult-terrain shortfall preview 315 -> 317**
> (`WorkItems/317-difficult-terrain-shortfall-preview.md`; the index line, the detail file, and the `#315`
> markers in `DifficultShortfallPlan`, `ImpassibleBlockLabel`, `GuiDefineMovementResolver` and both test
> fixtures were updated). Master's #315 keeps the number, and the `#315` references in `GuiSelectionResolver` /
> `GuiUnitSelectionResolver` / `TransportOptionLookup` mean THAT item and were deliberately left alone.
> **Left as-is on purpose:** the two commit messages saying "#315" for the movement work (`d8c39cc`, `1bdf123`)
> predate the renumber, like every prior reconciliation. Caught before pushing, by inspecting git state rather
> than by the pre-push hook.
>
> **2026-08-02 - reconciliation 38 (RESOLVED).** The Takedown rule-facet session yielded TWICE in one
> push, to two different sessions racing the same range. It filed its item as **311** against a local
> master 6 commits stale, not having seen that origin/master had closed **311 = accidental passes in the
> Choose Action menu** (reconciliation 35) and taken **312 = charge reach / swallowed clicks**
> (reconciliation 36); it renumbered to **313**, and while its merge was being verified origin/master
> landed **313 = shot-eligibility preview parity** (reconciliation 37, above). Per merged-wins precedent
> the unmerged local item yields both times, landing on **Takedown facet correction 311 -> 313 -> 314**
> (`WorkItems/314-takedown-facet-correction.md`). Renumbered in the index line, the detail file, the
> three annotations in `042-implementation-checklist.txt`, and 16 code/test comments in the engine
> (submodule `a7b537b` then `3d525de`). This entry is **38**, not 37: reconciliation 37 collided too -
> the shot-eligibility session took that ordinal while this one was mid-merge.
> **Left as-is on purpose:** the commit messages saying "#311" for the Takedown work (`24cafde` engine,
> `5a18182` superproject) and "#313" for the first renumber (`a7b537b`), which predate the later
> renumbers, exactly as every prior reconciliation has handled them. The same session's other item,
> **#175** (Fear/Fearless hero gating), did not collide - it edited an existing entry and kept its
> number. The merge also corrected one stale comment in the incoming `ShotEligibility.cs`: its
> `ignoresLineOfSight` parameter doc still named Takedown, which this item's whole point is that it no
> longer qualifies (the logic was already right - callers derive the flag from `SightRuleQueries`).

> **2026-08-02 - reconciliation 37 (RESOLVED).** Immediately after reconciliation 36 landed, the
> shot-eligibility session hit the number it had just created: this session filed its preview-parity item
> as **312**, and the push-time fetch found origin/master had meanwhile landed **312 = charge-won't-allow
> + swallowed clicks** (`fec819a` / engine `03bf1a4`, itself a #310 yield via reconciliation 36). Per
> merged-wins precedent the local item yields: **shot-eligibility preview parity 312 -> 313**
> (`WorkItems/313-shot-eligibility-preview-parity.md`). Renumbered in the detail file + title, the index
> line, and the six comments carrying it (engine `ShotEligibilityTests`; app
> `GuiChooseRangedAttackResolver` x2, `GuiDefineMovementResolver`, `TacticalOverlayController`,
> `GuiChooseRangedAttackResolverTests`, `TeamAwarenessTests`) — the many #312 comments the merged charge
> item legitimately owns in those same files were left untouched, so the renumber was done per line, not
> by search-and-replace. Commit messages naming "#312" (engine `c2ca754`, superproject `1a20b4d`) predate
> the renumber, as usual. The merge also took origin/master's DELETION of
> `GuiDefineMovementResolver.HandleEnemyPinClick` (the charge item removed the pin gesture) over this
> branch's edit to that same method, which had only rerouted it through `TeamAwareness`.
> **2026-08-02 - reconciliation 36 (RESOLVED).** The charge/swallowed-clicks session filed its item as
> **310**; the push-time fetch found origin/master had meanwhile landed BOTH **310 = per-user config
> file** (`d545861`, itself a #309 yield via reconciliation 34) and **311 = pass confirmation**
> (closed, via reconciliation 35). Per merged-wins precedent the local item yields past both:
> **charge-won't-allow + swallowed-clicks 310 -> 312**
> (`WorkItems/312-charge-wont-allow-and-swallowed-clicks.md`). Renumbered in the detail file + title,
> the index line, and the seven sources carrying the comment (engine `MovementUtilities`,
> `ChargeReachValidationTests`; app `GuiDefineMovementResolver`, `GuiConsolidationMoveResolver`,
> `ModelPicker`, `TacticalOverlayController`, `ModelPickerTests`) — the app files legitimately
> referencing the config item's #310 were left untouched. The commit messages naming "#310" (engine
> `03bf1a4`, superproject `fec819a`) predate the renumber, as usual.
>
> **2026-08-02 - reconciliation 35 (RESOLVED).** A double collision, and the second half of it is the
> rare one. This session had filed the Choose Action Pass-confirmation work as **309**; the pre-push
> fetch found origin/master had landed **309 = networked client's invisible late-deployed models**
> (merged and archived, `91451c2` / engine `3c2ac8d`), so per merged-wins precedent the local item
> yielded to **310** - and while THAT renumber was being verified, origin/master landed again with
> `d545861` / `f9cb236`, in which a parallel session had independently yielded its own #309 to **310**
> and logged it as **reconciliation 34**. So the local item yields a second time: **pass confirmation
> 309 -> 310 -> 311** (`WorkItems/311-pass-confirmation.md`), and this entry - written first, pushed
> second - takes **35**. As in reconciliation 15's identical clash over an entry number, this log is
> authoritative: the merged entry keeps 34. Renumbered in the detail file + its title, the archive
> entry, and all three app sources carrying the comment (`ActionMenuLayout`,
> `GuiStringSelectionResolver`, `ActionMenuLayoutTests`). **Left as-is on purpose:** the three
> pre-renumber COMMIT MESSAGES, which still say `#309` / `#310` and name reconciliation 34 - commit
> messages are not rewritten (precedent 2/3, and reconciliation 33's separate renumber commit `8e81c8e`
> is the pattern this follows). The item was hand-verified and closed in the same pass, so it goes
> straight to the archive and never appears in the index under 310 or 311. No engine change on this
> side; the only submodule movement was checking out master's new pin (`3c2ac8d`).

> **2026-08-02 — reconciliation 34 (RESOLVED).** Same collision class as 29/31/32/33, caught by the
> pre-push fetch. This session had filed the per-user config file (remembered player name + host
> settings) as **309**; while the work was in progress origin/master landed **309 = networked client's
> invisible late-deployed models** — merged *and* already archived (`91451c2`, engine `3c2ac8d`). Per
> merged-wins precedent the unmerged local item yields: **per-user config 309 -> 310**
> (`WorkItems/310-user-config-file.md`). Nothing had been pushed or even committed at that point, so
> like reconciliations 12/13 the renumber landed everywhere *before* publication — detail file + title,
> the index line, all nine app sources and tests carrying `#309` comments (`UserConfig`, `Program`,
> `HostModal`, `ClientModal`, `LobbyScreen`, `IAppScreen`, `NatPortMapper` and both test files), and
> the commit message. No reference of any kind predates the renumber. The rebase onto master's three
> incoming commits was clean (master's #309 touched `RaylibRenderer.DrawModels`, this item touched
> `NavigateTo`).

> **2026-08-01 — reconciliation 33 (RESOLVED).** Same collision class as 29/31/32, caught by the
> pre-push fetch. This session had filed the 2026-07-31 playthrough findings (Blast per-hit cap,
> "Moved" token visibility, shooting/deployment Back, sticky shoot target) as **305**; while the work
> was in progress origin/master landed **305 = CLI army-prompt EOF loop**, **306 = weapon-chooser
> name keying** and **307 = Forge failed-load saves default** (all merged, `4f8d6af` / `dde2955`).
> Per merged-wins precedent the unmerged local item yields: **playthrough findings 305 -> 308**
> (`WorkItems/308-playthrough-findings-2026-07-31.md`). Renumbered in the detail file + title, the
> index line, and all 19 engine/app sources and tests carrying `#305` comments (Blast in
> `RollToHitStage`/`CombatMath`, the token-visibility trio, `ChooseRangedAttackRequest`/Stage and both
> ranged resolvers, `PlaceObjectsRequest`/`DeployUnitStage`/`DeployAllUnitsStage`/
> `ChooseUnitToDeployStage`/`PlacementCommitGuard` and the placement resolver). **Left as-is:** the
> pre-renumber COMMIT MESSAGES on both sides of the submodule boundary, which still say `#305` - the
> superproject commits pin exact submodule hashes, so rewriting engine messages would orphan the
> pointers (reconciliation 32's reasoning, and the messages-predate-the-renumber precedent of 2/3).
> Upstream's #305/#306/#307 keep their numbers and their `WorkItems/197-*.md` references untouched.

> **2026-07-28 — reconciliation 32 (RESOLVED).** Same collision class as 29/31, caught by the
> pre-push fetch. This session had filed the "Alternating: Points" terrain placement mode as **299**;
> origin/master had meanwhile landed **299 = casualty beats for batched wounds** (merged, with `#299`
> comments in `DangerousTerrainWoundTests.cs` / `TransportSpilloutTests.cs`) and **300 = dice-panel
> category colors** (already archived). Per merged-wins precedent the local item yields:
> **alternating-points terrain 299 -> 301** (`WorkItems/301-alternating-points-terrain.md`). Renumbered
> in the detail file + title, the index line, all engine sources/tests of the mode (ledger, budget,
> stage, pool, request, AI resolver, lobby VM docs) and the app sources (CLI + GUI terrain resolvers,
> `LobbyScreen`, CLI resolver tests). Unlike 31, the pre-renumber COMMIT MESSAGES keep `#299`: the
> submodule history already contained a merge and the superproject's earlier commits pin those exact
> submodule hashes, so a message rewrite would have orphaned the pointers - the older
> messages-predate-the-renumber precedent (reconciliations 2/3) applies.

> **2026-07-28 — reconciliation 31 (RESOLVED).** A pre-push fetch caught the same class of collision as
> 29, one number along. This session had filed the resolver option-button-height work as **296**; the
> engine's origin/master had meanwhile landed **296 = Tactician crowded-game fix set** (merged - itself
> renumbered 294 -> 296 by the engine-side reconciliation 30, `5d4cae1`, and carrying `#296` comments
> across ~10 `Ai/` files), plus **297 = objectives held per side** (`0910e05`). Neither number was in the
> superproject index yet: that clone had pushed its engine commits ahead of its index, so only a
> `git fetch` in the submodule revealed them. Per merged-wins precedent the local item yields:
> **resolver button height 296 -> 298** (`WorkItems/298-resolver-option-button-height.md`). The renumber
> landed everywhere before publication - detail file + its title, the index line, the engine sources
> (`ChooseMeleeWeaponStage.cs`, `MeleeWeaponRuleDescriptionTests.cs`) and the app sources (the resolver
> panels, `ResolverPanelLayout.cs`, `PlacementPanelLayout.cs`, `ResolverText.cs`, `StringSelectionResolver.cs`
> and both layout test files) - and, unlike prior reconciliations, **the commit messages too**: all three
> commits were still unpushed, so they were rewritten to say #298 rather than leaving a stale number in
> shared history. The engine commit was rebased onto origin/master before the push (the superproject had
> been pinning a submodule commit that predated #296's fix set and #297).

> **2026-07-27 — reconciliation 30 (RESOLVED).** Merging origin/master into this session's local
> master surfaced the same-day sibling of reconciliation 29: this session had filed the Tactician
> crowded-game drift investigation as **294** (and committed its whole fix set under that number)
> while origin/master had meanwhile landed **294 = movement footstep cue** (merged, `5ef6803`) and
> **295 = click-to-select** (reconciliation 29's own renumber). Per merged-wins precedent the local
> item yields: **crowded-game drift 294 -> 296** (`WorkItems/296-tactician-crowded-game-drift.md`;
> index line, detail-file self-references, and the engine's Ai/ comment references updated - engine
> `5d4cae1`). Commit messages saying "#294" for it (`e26c98d`, `f9f5105`, engine `a25e6a6`,
> `0a4549d`) predate the renumber, per precedent. The objectives-team UI item filed the same day
> takes **297**.
>
> **2026-07-27 — reconciliation 29 (RESOLVED).** A pre-push fetch caught a single collision: this
> session had filed the click-to-select / Space-confirms work as **294** while origin/master had
> meanwhile landed **294 = movement footstep cue** (merged, `5ef6803`). Per merged-wins precedent the
> local item yields: **click-to-select + Space-confirm 294 -> 295**
> (`WorkItems/295-click-to-select-model-space-confirms.md`). The renumber landed everywhere before
> publication - detail file + its title, the index line, `docs/ResolverGuide.md`, and the app sources
> (`ResolverKeybinds.cs`, `ModelPicker.cs`, `ResolverButtons.cs`, `ResolverHotkeys.cs`,
> `GuiDefineMovementResolver.cs`, `GuiConsolidationMoveResolver.cs`, `ModelPickerTests.cs`,
> `ResolverKeybindsTests.cs`). **Departure from the usual "commit messages are left as-is":** the one
> local commit was unpushed and was being rebased onto master anyway, and leaving it titled "#294"
> would have put two different #294 subjects in the SAME branch history (master's footstep commit is
> its immediate parent) - so the message was amended to #295 as part of the rebase. No engine change
> was involved, so the submodule pointer is untouched.
>
> **2026-07-26 — reconciliation 28 (RESOLVED).** A pre-push fetch caught a double collision. This
> session filed eight items from a play-session bug report (**284-291**) while origin/master had
> meanwhile landed **284 = deploy overlap** (reconciliation 27's own renumber, merged) and
> **285 = self-contained file dialogs** (merged). Per merged-wins precedent both local items yield:
> **weapon-select rule hovers 284 -> 292** (`WorkItems/292-weapon-select-rule-hovers.md`) and
> **spell effect banner 285 -> 293** (`WorkItems/293-spell-effect-banner.md`). **286-291 were free on
> origin and keep their numbers.** Nothing had been pushed, so the renumber landed everywhere before
> publication - both detail files + their titles, the index lines, `docs/ResolverGuide.md`, the app
> sources (`RuleHoverText.cs`, `GuiChooseRangedAttackResolver.cs`, `RuleHoverTextTests.cs`) and the
> engine sources (`SpellText.cs`, `CastSpellStage.cs`, `CasterRuleIntegrationTests.cs`). **Left as-is
> on purpose:** the commit messages saying "#284"/"#285" for these two (engine `fc2a1d9`, `1138461`,
> `c0d0e9e`/`ae55836`, superproject `f907b3d`, `84e8e7c`, `b8a976f`), which predate the renumber, per
> every prior reconciliation's precedent. Care was taken NOT to touch origin's own #284 comments in
> `PlacementCommitGuard.cs` / `SpilloutExecutor.cs` / `PlacementCommitGuardTests.cs` - they mean the
> deploy-overlap item and are correct as they stand.

> **2026-07-26 — reconciliation 27 (RESOLVED).** **282** was claimed twice: origin/master's
> **282 = "rotation only affects ghost"** (merged, `WorkItems/282-rotation-only-affects-ghost.md`,
> plus its #283 follow-up) vs this clone's locally-filed **282 = "deploy overlap / invisible
> occupants"** (PlacementCommitGuard; its ENGINE commit `1e17708` "PlacementCommitGuard (#282)"
> was already pushed, but the detail file and index line were still uncommitted here). Per
> merged-wins precedent the local item yields: **deploy-overlap 282 -> 284** (284 free on index +
> archive after a same-day near-miss: this session briefly filed 284 for the DOP-nondeterminism
> finding, then withdrew it as a duplicate of #210 before pushing). Renumbered: detail file +
> title (`WorkItems/284-deploy-overlap-invisible-occupants.md`), index line, and the three engine
> source comments (PlacementCommitGuard.cs, SpilloutExecutor.cs, PlacementCommitGuardTests.cs;
> engine commit `8b4e23a`). Commit messages saying "#282" for it (engine `1e17708`, superproject
> `2f2d78d`) predate the renumber and keep their text, per reconciliation-26 precedent.
>
> **2026-07-25 — reconciliation 26.** The preview session filed **277 = networked decision
> previews** against a local master where 276 was the highest number in use; before its push,
> origin/master landed reconciliation 25's **277 = formation cycling** plus **278 = playtest
> fixes** — and, while the first renumber (to 279) was still local, **279 = lobby network
> teardown** landed and closed as well, colliding a second time. Per merged-wins precedent the
> unpushed item yields both times: **networked previews 277 -> 280**
> (`WorkItems/280-networked-decision-previews.md`; 280 free on index + archive). Renumbered in the
> detail file + title, archive line, and every engine + app source comment - but unlike
> reconciliations 23-25 the five pre-merge commit messages (engine `3546835`, superproject
> `5ba0b6b`/`e29e519`/`1981268`/`085bcbc`) keep their `#277`: the engine commit is baked into
> three superproject submodule-pointer trees, so a message rewrite would orphan the SHA the
> published history references. Collateral fix while renumbering: reconciliation 25's sed had also
> rewritten the banner-tier comment at `RaylibRenderer.cs` line 495 (a `#275` tag, introduced by
> superproject `1538d52`) to `#277`; restored to `#275`.

> **2026-07-25 — reconciliation 25.** The formation-cycling session filed **275 = formation cycling
> in Group mode** against a local master where 274 was the highest number in use (the same starting
> point as reconciliations 23 and 24 — a three-way claim on the number). Pre-push re-verification
> found origin/master had meanwhile landed AND closed **275 = banner tiers** (reconciliation 23's
> renumbered item) plus **276 = attack-animation truthfulness** (reconciliation 24's). Per merged-wins
> precedent the unpushed item yields, skipping the also-taken 276: **formation cycling 275 -> 277**
> (`WorkItems/277-formation-cycling.md`; 277 free on index + archive). Nothing had been pushed, so the
> renumber landed everywhere before publication: detail file + title, index line, all engine + app
> source comments, `docs/ResolverGuide.md`, and the commit messages (engine commit amended during its
> rebase onto the merged master; the three superproject messages rewritten with
> `filter-branch --msg-filter` after theirs, per the reconciliation 23/24 free-while-local precedent).
> The banner-tier work's own `#275` source tags were left strictly alone. The superproject rebase's
> only conflict was the submodule pointer (both sides had bumped it); the reconciliation-15
> `#NNN:`-subject hazard was pre-empted with `core.commentChar`.
>
> **2026-07-24 — reconciliation 24.** The attack-animation session filed **275 = attack animation
> truthfulness** (occluded-shooter dice trim + truthful beat endpoints) against a local master where
> 274 was the highest number in use. Pre-push re-verification found the submodule's origin/master had
> meanwhile gained `449310f` "**#275**: banner tiers (Headline / Notice / Toast)" — reconciliation
> 23's renumbered item, from the parallel session. Per merged-wins precedent the unpushed item
> yields: **attack-animation truthfulness 275 -> 276** (`WorkItems/276-*`), renumbered in the detail
> file, the index line, and the engine source-comment tags. The two unpushed commits (one submodule,
> one superproject) were amended to say #276 — both were still local-only, so the rewrite was free
> and keeps `#275` unambiguous in history (this entry was itself renumbered 23 -> 24 when the merge
> surfaced reconciliation 23 below, written the same day by the other half of the collision).
>
> **2026-07-24 — reconciliation 23.** The banner-tier work was filed as **274** on a local master that
> had gone 3 behind origin/master mid-session (it was level when the session started, so the number was
> free when it was taken). By merge time origin/master had landed AND archived its own **274 = spell
> cast visuals** (engine `befce91`, superproject `dbf51ba`, closed by `8e52d80`). Per merged-wins
> precedent the local item yields: **banner tiers 274 -> 275** (`WorkItems/275-banner-tiers.md`; 275 was
> free on both index and archive). Nothing had been pushed, so — as in reconciliations 12 and 13 — the
> renumber landed everywhere *before* publication: detail file + its title, index line, all 26 engine and
> app source comments, and the commit messages (engine commit amended; the four superproject messages
> rewritten with `filter-branch --msg-filter`). **No reference anywhere still calls this work #274**,
> which mattered more than usual here because the other #274 lives in the same repo's history.
> The two efforts overlapped in code as well as in numbering — both touched `PresentationDurations`,
> `CastSpellStage`, and `PresentationBeatSerializationTests`. Those auto-merged; the one real conflict
> was `PresentationSoundCues.BaseCues`, where master's array still listed the single `Banner` cue that
> #275 had split into three tiers. Resolved by keeping both sets (3 banner tiers + 6 spell voices) and
> dropping the dead `Banner` key. Engine 2104/2104, app 582/582, headless smoke exit 0 after the merge.
>
> **2026-07-23 — reconciliation 22.** Bringing origin/master into the long-lived `264-server-browser`
> branch (14 ahead / 32 behind) surfaced a *triple* collision: master had independently used **264**
> (Tactician walled-unit lateral retreat, `264-walled-unit-pins`), **265** (lobby Battlefield dropdown),
> and **266** (console word-wrap + panel height) - all merged - while this branch had used the same three
> for the server-browser epic (264) and its two security prerequisites (265 = file-load `$type` allowlist,
> 266 = FDGHost pre-auth connection limits). Master also self-reconciled its own 265->267 and 266->270 in
> reconciliations 20/21. Per merged-wins precedent this branch's three unpushed items yield to the next
> free numbers (master's highest is 270): **server browser 264 -> 271**, **file-load allowlist 265 -> 272**,
> **FDGHost pre-auth limits 266 -> 273** (`WorkItems/271-*`, `272-*`, `273-*`). The renumber landed
> everywhere before the merge - the three detail files (renamed + titled), the index line, the two Archive
> entries, cross-references in #186/#189, and all `#264/#265/#266` source-comment tags across `FdgRaylib/`,
> `tools/list-server/`, and the tests. **Left as-is on purpose:** the branch name `264-server-browser`, the
> pre-renumber commit messages (superproject and the 5 engine security commits), and the engine-side source
> comments in the read-only submodule - all predating the renumber, per every prior reconciliation. The
> merge and submodule resolution follow in the same session; nothing was pushed.
>
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
