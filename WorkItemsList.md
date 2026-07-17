# Work Items

Numbered, persistent backlog of engineering tasks, roughly Jira-ticket sized. Numbers are permanent
and **never reused**, even across deletions; a split item's line stays and points at the new numbers.

- Per-item working memory: `WorkItems/NNN-slug.md`, created when work starts. Template + conventions: `WorkItems/README.md`.
- Completed / closed items: `WorkItems/Archive.md` (entries moved there verbatim).
- Number-collision log: `WorkItems/Reconciliations.md` — read before filing new numbers on a branch that has drifted from origin/master. A per-clone pre-push hook blocks duplicate numbers across this file and the archive.
- Cross-session priority handoff: `WorkItems/FableWindowPlan.md` — what shipped during the 2026-07 Fable window and the agreed order for what's next (top item: #169 design fork, sign-off before building).

**Keep this index lean — every work session reads it whole.** An entry is at most ~3 lines: number,
title, one-sentence scope/status, link. Running notes, commit hashes, root-cause narratives, and test
tallies belong in the item's detail file, never here — move overflow there the same day it appears.
When closing an item: write the Outcome in its detail file, tick the line, and move it to the archive.

---

## Movement

- [~] 159 — `DefinePathStage` cohesion crash: four root causes fixed, but a residual still repros ~1 run in 10 on the default army (2/24 on origin/master, 2026-07-09) — *not* fixed; not yet isolated to the CLI auto-advance vs the AI resolver. ([WorkItems/159](WorkItems/159-definepath-cohesion-crash.md))
- [ ] 209 — Weapon-choice option order is nondeterministic (ConcurrentDictionary keyed by Weapon identity): multi-weapon units swing/fire in random order, breaking #193 same-seed replay and benchmark hash reproducibility; fix candidate awaiting sign-off. ([WorkItems/209](WorkItems/209-weapon-choice-order-nondeterminism.md))
- [ ] 210 — Residual bench nondeterminism at --dop > 1 (scattered per-game flips under CPU contention; serial runs exact after #209). Needs the #198 tracer wired into bench to isolate. ([WorkItems/210](WorkItems/210-dop-concurrency-nondeterminism.md))
- [ ] 211 — Solo AI mover submits a path through impassible terrain (~1/1800; #159's family, impassible flavor). Fix with one validate-or-decline ladder on the solo mover alongside #159. ([WorkItems/211](WorkItems/211-solo-mover-impassible-terrain.md))
- [ ] 214 — Teleport (#197) doesn't draw a range-of-motion circle like movement does; placement is bounded correctly, just add the reach-circle visual. ([WorkItems/214](WorkItems/214-teleport-range-circle.md))
- [ ] 216 — Tactician plans rejected by the #205 friendly-stacking check silently fall back to the SOLO resolver (suspected DE/RL mirror-drift driver); charge candidate made friendly-aware, resolver-level repair + drift attribution still open. ([WorkItems/216](WorkItems/216-tactician-solo-fallback-on-stacked-plans.md))
- [ ] 217 — Tactician Bot lobby name is enumerated by total player count, not by how many bots of that profile already exist ("Tactician Bot 3" as the first Tactician added). ([WorkItems/217](WorkItems/217-tactician-bot-naming.md))

## Shooting & cover

- [ ] 201 — Shooting *out of* cover grants the defender cover: `EvaluateSightLine` folds every terrain piece on the segment into one worst-effect with no notion of *where* it sits, so the attacker's own wall counts. Blocked on a rules ruling (proximity to defender, shoot-through depth). ([WorkItems/201](WorkItems/201-cover-attacker-side.md))

## Model bases & geometry

- [~] 149 — Configurable model base size + shapes (per-unit circle/rect via the `IBaseShape` seam): core landed; remaining facets in the detail file. The deferred hard-path geometry became #150 (awaiting verification below). ([WorkItems/149](WorkItems/149-base-shapes.md))
- [ ] 225 — Audit bundled army/book data for default or wrong-aspect (wider-than-long) base shapes/sizes against real OPR bases. ([WorkItems/225](WorkItems/225-army-list-base-shape-audit.md))

## Special rules — framework

- [~] 042 — Special rules architecture (data-driven Condition x Effect over named hooks + token state). Phases 1-8 largely shipped; remaining: attack/reactivate primitives, then morale/casting invocability. ([WorkItems/042](WorkItems/042-special-rules-architecture.md) + `WorkItems/042-implementation-checklist.txt`)
- [ ] 087 — Custom special-rule authoring + standalone rules files (author new rules as data in the builder; import/export rule sets independent of armies). Builds on #059. ([WorkItems/087](WorkItems/087-custom-rule-authoring.md))
- [~] 100 — Special-rule engine primitives umbrella: Part 1 + cross-unit pre-attack targeting done; open: dormant hooks, RangeModifier/Strider, Part-2/3 primitives (deferred-debuff, dice-pool, markers). Corpus reference is off-repo (`../GDF Armies/`, do not commit). ([WorkItems/100](WorkItems/100-special-rule-primitive-gaps.md))
- [ ] 104 — Single-unit `Evaluate` doesn't consume `NextTrigger` grants — correct today; build the opt-in when a rule needs it. ([WorkItems/104](WorkItems/104-single-unit-evaluate-grant-consume.md))
- [ ] 196 — Faction rule coverage pt.1: author the 107 dead rule names (1,243 refs) that are clones of live primitives, as data in `GdfRuleSupplement.json`. App-side only, no engine changes. **Done except F16** (48 refs blocked on owner input re: wargear mechanics); 1,169/1,243 resolved, 26 moved to #197. ([WorkItems/196](WorkItems/196-faction-rule-data-authoring.md))
- [ ] 197 — Faction rule coverage pt.2: the 97 dead names (942 refs) needing new engine primitives. DONE: slice 0 (145 refs), the ">9in shot or charged" gate (+10, also fixed 3 defect classes in #196's data), P5a's activation-choice hook (+154), P5b's round-start recovery (+66), reposition-at-activation (+96), Teleport (+19, via #206's proximity Pass gate), Delayed Action (+47), Darkborn (+59, naming-only - mechanics already built), P15 Unpredictable (+48, decisive per-action branch die; 2 Mark variants deferred), and RuleFireLint's operation-consumption check. ([WorkItems/197](WorkItems/197-faction-rule-engine-primitives.md))

All 33 GF v3.5.1 core special rules are implemented (verified 2026-06-30; see archive #029-#032/#051).
Corpus coverage is a different story: 528 of 13,870 book rule references (3.8%) do nothing today — see #196/#197.

## Casting

- [~] 034 — Spell content: targeting primitives Part 1 complete; remaining: conferred-rule implementations (coordinate with #100) and per-faction spell JSON (copyrighted — authored locally, never committed; partially generated). ([WorkItems/034](WorkItems/034-spell-content.md))
- [ ] 234 — Casting is possible after Charge + melee in one activation; check the rulebook, then gate the Cast option if illegal. ([WorkItems/234](WorkItems/234-cast-after-charge-legality.md))

## Army Forge

- [ ] 218 — "Replace All" upgrade sections charge `option.Cost` once per model instead of a flat section cost; needs an OPR-pricing-convention check before fixing `ListCompiler`'s multiplication. ([WorkItems/218](WorkItems/218-army-forge-replace-all-cost.md))
- [ ] 219 — Audit bundled `.fdgbook` upgrades for options that should cost points but show/charge 0 (importer or data gap, not yet isolated). ([WorkItems/219](WorkItems/219-army-forge-zero-cost-upgrades.md))
- [ ] 220 — Version control for Army Forge lists (undo/revision history); mechanism not yet decided, surface the design fork first. ([WorkItems/220](WorkItems/220-army-forge-list-version-control.md))
- [~] 236 — Freeform builder silently stripped a Forge army's embedded book/selections on save; now gated behind an explicit "Save detached" confirm. Implemented + tested; modal awaits GUI hand-verify. ([WorkItems/236](WorkItems/236-freeform-save-strips-forge-block.md))
- [~] 241 — Army Forge share-link importer: paste an army-forge.onepagerules.com share link -> preview -> .fdgarmy (engine `OprListImporter` + Forge-screen UI + `--import-army`); gates on OPR version 3.5.x. ([WorkItems/241](WorkItems/241-army-forge-share-import.md))
- [ ] 242 — Import campaign/narrative list features (XP, traits, campaign mode) that #241's importer warns about and drops. ([WorkItems/242](WorkItems/242-campaign-import-features.md))

## Transport

- [ ] 097 — Disembark/embark full movement: replace the Advance-equivalent simplifications (disembark-then-Rush/Charge from the 6" drop; real move-into-contact to embark). ([WorkItems/097](WorkItems/097-transport-full-movement.md))

## AI agent (Tactician)

Master plan: `docs/ai-agent-plan.md` (heuristics -> MCTS -> learned value net; gates, invariants, vocabulary).

- [ ] 191 — Tactician AI agent umbrella: challenge-level game-playing AI as a new profile alongside the solo-rules bot; phased A-D, benchmark-gated. ([WorkItems/191](WorkItems/191-tactician-agent.md))

## Networking & infrastructure

Internet-play readiness pass (QF1-10) landed 2026-07-08 — password gate, keepalive/NoDelay, single-buffer
frames, targeted PlayerID assignment, greeting-timeout eviction, post-launch join gate, client host-loss
detection, host-IP display, DNS host entry. See `NetworkingHandoff-2026-07-08.md`. Remainders below.

- [ ] 186 — Harden network deserialization: allowlist binder for the wire path (no `DefaultSerializationBinder` fallback from untrusted `$type`); saves keep the permissive fallback. ([WorkItems/186](WorkItems/186-network-deserialization-hardening.md))
- [ ] 187 — Disconnect recovery: auto-save on `PlayerDisconnectedException` game-end + live-test #052's networked resume-rejoin. ([WorkItems/187](WorkItems/187-disconnect-recovery.md))
- [ ] 188 — Multi-remote-client support: live-test 3+ players / 2+ remote clients (QF5 enabled it; roster/team/routing edge cases). ([WorkItems/188](WorkItems/188-multi-remote-client.md))
- [ ] 189 — Broadcast gating (roster-only, not every connection) + configurable listen/connect port. ([WorkItems/189](WorkItems/189-broadcast-gating-configurable-port.md))
- [ ] 190 — Networked clients never receive mid-game token updates (in-place `TokenContainer` mutations bypass the data-sync path; join snapshot only). ([WorkItems/190](WorkItems/190-networked-token-sync.md))
- [ ] 058 — (low) Migrate message/save serialization off Newtonsoft onto System.Text.Json; pure consolidation. ([WorkItems/058](WorkItems/058-stj-migration.md))
- [ ] 057 — (low) Make state-machine contexts store-backed/serializable so #052's `GameProgressData` mirror can be deleted; deferred for risk.
- [ ] 054 — (low) Client-initiated save: host produces the `.fdgsave` on the client's behalf. Follow-up to #052.

## Client / renderer

- [~] 162 — Tactical overlay: opportunity fields, threat frontiers, per-model instruments (instruments call real rules, never the field texture). P0 scaffolding done; P1-P7 remain. Design doc: `docs/tactical-overlay-plan.md`. ([WorkItems/162](WorkItems/162-tactical-overlay.md))
- [ ] 161 — Resolver UI consistency pass: stat/highlight parity, right-click undo on deploy, shared canvas-selector base (also absorbs the `GuiModelSelectionResolver` alive-filter gap), dialog chrome. Findings + canonical click scheme in the detail file. ([WorkItems/161](WorkItems/161-resolver-consistency.md))
- [~] 056 — Presentation beat stream: architecture shipped and live on master; remaining animation polish + a hands-on pass. ([WorkItems/056](WorkItems/056-presentation-beat-stream.md))
- [ ] 049 — Multi-pool terrain selection: lobby picker for which `TerrainLayoutFile` feeds AutoFromLayout / Alternating. Spun off #002.
- [~] 055 — Special-rule attribution in resolvers: (a) movement overlay + (b) shooting resolver done; remaining (c): markers on units carrying relevant defensive/relational rules (e.g. a Stealth tag at the source).
- [~] 221 — Lobby color picker: 8-color RTS-style dropdown per player row, picks synced through the lobby protocol (engine `ColorIndex` + protocol v3), no stealing of defaults or picks. Awaiting GUI hand-verification (incl. a live two-machine check). ([WorkItems/221](WorkItems/221-lobby-color-picker.md))
- [ ] 222 — Tie dice rolls (`RollOffBeat`) take too long; tune beat/overlay pacing. ([WorkItems/222](WorkItems/222-dice-roll-speed.md))
- [~] 223 — Deploy/activate unit picker: hovering a valid option raises a full-spec stat tooltip (shared `UnitStatBlockRenderer`). Implemented; awaiting build + GUI hand-verification. ([WorkItems/223](WorkItems/223-deploy-option-tooltip.md))
- [ ] 224 — (exploratory, unconfirmed) Persistent "selected unit" inspector panel: stat detail, charge/shoot threat radius, hover-to-explain special rules. ([WorkItems/224](WorkItems/224-persistent-unit-inspector-panel.md))
- [ ] 226 — In-app bug reporting system; mechanism not yet decided. ([WorkItems/226](WorkItems/226-bug-reporting-system.md))
- [~] 227 — Hero indicator: white dark-outlined star on the hero model's base + hover-tooltip tag with the hero's own Q/D. Implemented + tested; awaiting GUI hand-verification. ([WorkItems/227](WorkItems/227-hero-visual-indicator.md))
- [ ] 228 — Terrain texture shifts when zooming (texture likely sampled in screen space, not table space). ([WorkItems/228](WorkItems/228-terrain-texture-zoom-shift.md))
- [ ] 229 — (exploratory) Bottom in-game menu should maybe be its own panel; confirm which strip + intent before building. ([WorkItems/229](WorkItems/229-bottom-menu-own-panel.md))
- [ ] 230 — Show weapon range rings during placement (deploy / embark / ambush arrival), anchored to the candidate position. ([WorkItems/230](WorkItems/230-placement-weapon-range-rings.md))
- [ ] 231 — Remove the LoS blocking-line visual from shooting (confusing when a valid target stands in front of the blocker). ([WorkItems/231](WorkItems/231-remove-los-blocking-lines.md))
- [ ] 232 — Remove the saved-hits beat, especially its sound (keep failed-save presentation); goes beyond #204's pacing fix. ([WorkItems/232](WorkItems/232-remove-save-hit-beat.md))
- [ ] 233 — Add a dice-roll beat for casting (engine beat emission in CastSpellStage). ([WorkItems/233](WorkItems/233-cast-roll-beat.md))
- [ ] 235 — Game Over card: center the result text + make the window draggable so the final board is inspectable. ([WorkItems/235](WorkItems/235-game-over-box-polish.md))
- [~] 237 — Single-option pick shortcuts: shooting pre-selects a sole fireable target, sole-defender charge shows a one-click confirm card (keeps #202's Back), Enter = Auto-assign All. Implemented + tested; awaiting GUI hand-verify. ([WorkItems/237](WorkItems/237-single-option-pick-shortcuts.md))
- [~] 238 — Attack animation now plays WHILE the to-hit dice tumble (AttackBeat is a zero-lead-in held beat) + gunshot/melee sound per volley instead of once. Implemented + tested; awaiting GUI hand-verify. ([WorkItems/238](WorkItems/238-attack-dice-overlap-volley-sound.md))

## 2026-06-10 audit follow-ups

From `Audit-6-10-2026.md`; `Audit-6-10-2026-Followup-2026-07-06.md` is the status diff.

- [ ] 062 — Store hygiene: name-keyed type map, single non-generic `DataBinding<>` converter, free-list `Create`. (The rename-fragility *risk* is already closed by #070; this is internal cleanup.)
- [ ] 063 — Data-store unit tests: capacity exhaustion, generation reuse after `Destroy`, `IsValid` reason codes, `CreateFromReference` rejection paths.
- [ ] 065 — Networking tests: loopback `FDGHost`+`FDGClient` fixture, concurrent-send interleaving, lobby view-model protocol tests. Zero TCP transport tests exist — the audit's highest-risk untested code.
- [ ] 066 — AI resolver legality tests: ~9 of 14 resolvers still uncovered; one legality test per resolver + `AiUnitClassifier` scoring pin.
- [ ] 067 — Content-parser tests + displayable errors: `ArmyListParser` splits, `TerrainLayoutLoader`, `SpecialRuleRegistry` error paths.
- [ ] 068 — Split engine tests into their own project (NUnit/Moq/Test SDK currently compile into the shipping engine assembly).
- [ ] 160 — Audit the STJ rule-attachment blob for type-rename fragility (sibling of #070; spot-checked rename-safe via `kind` tags, needs an exhaustive pass).
- [ ] 080 — GameModel cleanup: duplicate `OutstandingTaskLister` construction; remove or promote `FDGServer.TEST_SINGLE_TURN`.
- [ ] 081 — Per-access allocations: `ModelData.MeshProvider`/`MaterialProvider` construct providers per get; `UnitData.Models` materializes a new list per access.

## 2026-07-06 special-rules audit follow-ups

From `SpecialRulesAudit.md` (15 fixes already landed; plan detail, file paths, and approach live in its section 5).

- [ ] 168 — Surface rule-load diagnostics in the UI: subscribe the engine `RuleDiagnostics` channel app-side; warn once per army load in the game log + an army-builder pane ("N rules on this list are not implemented: ...").
- [ ] 164 — `DealHits.WithRules` resolver seam so Blast(3) multiplies pre-attack/Strafing hits (Breath Attack residual).
- [ ] 165 — Dangerous-terrain deaths don't reach `UnitDestructionNotifier`: widen `ApplyDangerousTerrainEffects` to carry the moving unit; also decide/record rout kill-attribution.
- [ ] 166 — Test-suite upgrades umbrella: fire-lint DONE 2026-07-08 (`RuleFireLint` over catalog + supplement); remaining: `RuleInteractionTests`, `SaveLoadRoundTrip` helper, probabilistic-dice variants, wire-crossing request, real Tough ordering test. [Notes](WorkItems/166-test-suite-upgrades.md)
- [~] 167 — Human-testing workflow tools umbrella: scenario compiler (`--make-scenario`), lobby-skip `--scenario` launch, and seeded dice DONE 2026-07-08; remaining: `--gen-ledger` manual-test ledger, OPR import reconciliation report. ([WorkItems/167](WorkItems/167-human-testing-tools.md))

## 2026-07-07 audit follow-ups

From `Audit-2026-07-06-New-Subsystems.md` (13 smaller findings were fixed in that pass; its §8 is the full bug log).

- [ ] 184 — Counter strike sequencing: engine's whole-unit role swap vs RAW per-weapon interleave (counter weapons first, charger, then the rest); exact for homogeneous units, over-grants for mixed/hero-joined ones. Deferred by design from #183. [Notes](WorkItems/184-counter-strike-sequencing.md)
- [ ] 170 — Port `CohesiveFormation.PackGrid` per-row sizing into `AiPlaceObjectsResolver`: mixed-base units deploy with small models stranded out of cohesion (the #159 bug's unfixed deploy sibling); add the missing mixed-base deploy test.
- [ ] 171 — Army Forge: enforce `UpgradeSection.MinPicks` (importer sets it for OPR "exactly" selects; validator errors on under-pick). Dormant today. Relates #156.
- [ ] 172 — Army Forge: combined-unit asymmetric rule values (`Tough(3)` + `Tough(6)`) both survive the merge and only the first is read — needs a ruling (max / warn / forbid). Relates #156/#107.
- [ ] 173 — Caster: port the `RequiredToken`/`RequiredRule` checks from `PreAttackTargeting` into `SpellTargeting`. Dormant today. Relates #033/#034.
- [ ] 174 — Caster: `SingleModel` + `MaxCount > 1` misattributes every target's wounds to one model — validate at army load or re-pick per target. Relates #034.
- [ ] 175 — Fear vs Fearless joined-hero gating asymmetry (Fear gates on `Always`, Fearless on `AllModelsHaveThisRule`) — **needs a rulebook check**, not an engineering call. Relates #021/#091.
- [ ] 176 — `UnitCreationRules.cs` missing `OperationExecutor.Execute`: needs async conversion of `FDGServer.BuildContextAndLaunch`'s chain. Harmless until a creation-time rule uses imperative ops.
- [ ] 177 — `AssignWoundsResults` residual cleanup: float `==` in `IsFinishedAssigning`, misused `ArgumentOutOfRangeException` ctor, documented wound-split exploit window. Relates #023/#024.
- [ ] 178 — (low) Save version migration hook (`IGameSaveMigrator`): deliberate deferral until a version bump is real; filed so it isn't forgotten. Relates #070/#160.
- [ ] 179 — (low) `PresentationRelayer` reaches into `PlayerSlotManager._playerSlots` instead of the public property.
- [ ] 180 — (low) Table-driven test: every concrete `PresentationBeat` has `NominalDuration >= 0` and non-throwing `Text`.
- [ ] 181 — `RuleValidator` rejects definitions referencing `Condition`/`Effect` members that don't override their throwing base — defensive (the stub list is currently empty); complements #166's lint.

---

## Awaiting verification

Implemented, merged, suite green — held open only until confirmed by hand in the running app.
Tick and move to the archive once verified. The detail files carry the full ledgers.

- [~] 202 — 2026-07-09 playtest fixes (Esc no longer quits; Fast/Very Fast reach Rush+Charge; ranged morale once per defender after shooting; Move/Charge/Disembark back-out; Ambush reserve is unit state, not an origin position). Verify: the six checks in the detail file. ([WorkItems/202](WorkItems/202-playtest-fixes-2026-07-09.md))
- [~] 003 — Force-org validation warnings. Verify: build an over-points / 3+ hero / 4+ same-unit army — amber warnings appear; save + launch never blocked. ([WorkItems/003](WorkItems/003-force-org-validation.md))
- [~] 108 — AI deploy coherent block packing. Verify: AI deploys tight square-ish grids, no stranded or scattered models. ([WorkItems/108](WorkItems/108-ai-deploy-cohesion.md))
- [~] 150 — Base-shape geometry everywhere (unified collision, swept paths, LoS, pile-in, previews). Verify: play a rect-base/aircraft army — placement, movement clamps, LoS, pile-in respect the true oriented shape. ([WorkItems/150](WorkItems/150-base-shape-bounding-radius-remnants.md))
- [~] 157 — Takedown per-shot target picks. Verify: fire HEF Snipers (3+ Sniper Rifles) — one canvas pick per shot, spreadable across models. ([WorkItems/157](WorkItems/157-takedown-per-shot-picks.md))
- [~] 158 — Dead models in shooting chooser + stale rings. Verify: shoot a unit that has taken casualties — no rings/aim lines on corpses, living-only counts. ([WorkItems/158](WorkItems/158-shooting-target-dead-models.md))
- [~] 033 — Caster(X) subsystem. Verify: Cast action end-to-end in GUI (spell menu with token counts, target pick, roll, effect). ([WorkItems/033](WorkItems/033-caster.md))
- [~] 035 — Transport(X) core. Verify: deploy-time embark, mid-game embark/disembark, destruction spillout. ([WorkItems/035](WorkItems/035-transport.md))
- [~] 096 — Transport visuals. Verify: cyan `Carrying X/Y` badge + hover cargo breakdown; spillout beats (wreck banner, Shaken banners, dangerous-terrain d6). ([WorkItems/096](WorkItems/096-transport-visuals.md))
- [~] 052 — Save/load a game in progress. Verify (with #095): save mid-game -> load -> lobby re-crew -> resume; state intact. ([WorkItems/052](WorkItems/052-save-load.md))
- [~] 095 — Special rules re-attached on save/load resume. Verify: in the same #052 session, rules still fire after resume. ([WorkItems/095](WorkItems/095-rules-not-rehydrated-on-resume.md))
- [~] 156 — Army Forge catalog builder. Verify: remaining hand-verify rounds (all core facets landed). ([WorkItems/156](WorkItems/156-army-forge-builder.md))
- [~] 106 — Army builder authoring UX. Verify: read-only stat block, per-unit Duplicate, auto-unfold of new units/weapons/spells. ([WorkItems/106](WorkItems/106-army-builder-ux.md))
- [~] 053 — Sound cues on the beat stream. Verify: hear the placeholder tone per beat; real `.wav`s drop into `FdgRaylib/Assets/Sounds/` by filename. ([WorkItems/053](WorkItems/053-sound.md))
