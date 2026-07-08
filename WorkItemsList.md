# Work Items

Numbered, persistent backlog of engineering tasks, roughly Jira-ticket sized. Numbers are permanent
and **never reused**, even across deletions; a split item's line stays and points at the new numbers.

- Per-item working memory: `WorkItems/NNN-slug.md`, created when work starts. Template + conventions: `WorkItems/README.md`.
- Completed / closed items: `WorkItems/Archive.md` (entries moved there verbatim).
- Number-collision log: `WorkItems/Reconciliations.md` — read before filing new numbers on a branch that has drifted from origin/master. A per-clone pre-push hook blocks duplicate numbers across this file and the archive.

**Keep this index lean — every work session reads it whole.** An entry is at most ~3 lines: number,
title, one-sentence scope/status, link. Running notes, commit hashes, root-cause narratives, and test
tallies belong in the item's detail file, never here — move overflow there the same day it appears.
When closing an item: write the Outcome in its detail file, tick the line, and move it to the archive.

---

## Movement

- [ ] 182 — Move through friendly units, but not stop on them: movement validation today only checks enemy footprints; add the "can't end overlapping a different friendly unit" guard (mirror #011's ending-stacked check, minus pass-through block and standoff), threaded through the GUI/CLI/AI resolvers + preview. ([WorkItems/182](WorkItems/182-move-through-friendly-units.md))

## Model bases & geometry

- [~] 149 — Configurable model base size + shapes (per-unit circle/rect via the `IBaseShape` seam): core landed; remaining facets in the detail file. The deferred hard-path geometry became #150 (awaiting verification below). ([WorkItems/149](WorkItems/149-base-shapes.md))

## Special rules — framework

- [~] 042 — Special rules architecture (data-driven Condition x Effect over named hooks + token state). Phases 1-8 largely shipped; remaining: attack/reactivate primitives, then morale/casting invocability. ([WorkItems/042](WorkItems/042-special-rules-architecture.md) + `WorkItems/042-implementation-checklist.txt`)
- [ ] 087 — Custom special-rule authoring + standalone rules files (author new rules as data in the builder; import/export rule sets independent of armies). Builds on #059. ([WorkItems/087](WorkItems/087-custom-rule-authoring.md))
- [~] 100 — Special-rule engine primitives umbrella: Part 1 + cross-unit pre-attack targeting done; open: dormant hooks, RangeModifier/Strider, Part-2/3 primitives (deferred-debuff, dice-pool, markers). Corpus reference is off-repo (`../GDF Armies/`, do not commit). ([WorkItems/100](WorkItems/100-special-rule-primitive-gaps.md))
- [ ] 104 — Single-unit `Evaluate` doesn't consume `NextTrigger` grants — correct today; build the opt-in when a rule needs it. ([WorkItems/104](WorkItems/104-single-unit-evaluate-grant-consume.md))

All 33 GF v3.5.1 core special rules are implemented (verified 2026-06-30; see archive #029-#032/#051).

## Casting

- [~] 034 — Spell content: targeting primitives Part 1 complete; remaining: conferred-rule implementations (coordinate with #100) and per-faction spell JSON (copyrighted — authored locally, never committed; partially generated). ([WorkItems/034](WorkItems/034-spell-content.md))

## Transport

- [ ] 097 — Disembark/embark full movement: replace the Advance-equivalent simplifications (disembark-then-Rush/Charge from the 6" drop; real move-into-contact to embark). ([WorkItems/097](WorkItems/097-transport-full-movement.md))

## Networking & infrastructure

- [ ] 058 — (low) Migrate message/save serialization off Newtonsoft onto System.Text.Json; pure consolidation. ([WorkItems/058](WorkItems/058-stj-migration.md))
- [ ] 057 — (low) Make state-machine contexts store-backed/serializable so #052's `GameProgressData` mirror can be deleted; deferred for risk.
- [ ] 054 — (low) Client-initiated save: host produces the `.fdgsave` on the client's behalf. Follow-up to #052.

## Client / renderer

- [~] 162 — Tactical overlay: opportunity fields, threat frontiers, per-model instruments (instruments call real rules, never the field texture). P0 scaffolding done; P1-P7 remain. Design doc: `docs/tactical-overlay-plan.md`. ([WorkItems/162](WorkItems/162-tactical-overlay.md))
- [ ] 161 — Resolver UI consistency pass: stat/highlight parity, right-click undo on deploy, shared canvas-selector base (also absorbs the `GuiModelSelectionResolver` alive-filter gap), dialog chrome. Findings + canonical click scheme in the detail file. ([WorkItems/161](WorkItems/161-resolver-consistency.md))
- [~] 056 — Presentation beat stream: architecture shipped and live on master; remaining animation polish + a hands-on pass. ([WorkItems/056](WorkItems/056-presentation-beat-stream.md))
- [ ] 049 — Multi-pool terrain selection: lobby picker for which `TerrainLayoutFile` feeds AutoFromLayout / Alternating. Spun off #002.
- [~] 055 — Special-rule attribution in resolvers: (a) movement overlay + (b) shooting resolver done; remaining (c): markers on units carrying relevant defensive/relational rules (e.g. a Stealth tag at the source).

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
- [ ] 167 — Human-testing workflow tools umbrella: seeded dice, `--make-scenario` save compiler, `--gen-ledger` manual-test ledger, OPR import reconciliation report.

## 2026-07-07 audit follow-ups

From `Audit-2026-07-06-New-Subsystems.md` (13 smaller findings were fixed in that pass; its §8 is the full bug log).

- [ ] 184 — Counter strike sequencing: engine's whole-unit role swap vs RAW per-weapon interleave (counter weapons first, charger, then the rest); exact for homogeneous units, over-grants for mixed/hero-joined ones. Deferred by design from #183. [Notes](WorkItems/184-counter-strike-sequencing.md)
- [ ] 185 — Replace the RuleEvaluator participant tuples with a `readonly struct RuleParticipant` (collapses 6 overloads + 2 shims into 2 methods; zero runtime cost). Mechanical refactor, full spec in [Notes](WorkItems/185-ruleparticipant-struct.md). Surfaced during #183.
- [ ] 169 — **(HIGH PRIORITY — design fork, needs sign-off)** Transport Rout never spills occupants — they stay permanently embarked/off-table; spillout only fires from the shooting/melee-swing pipelines. Decide: extract the placement flow for a second stage vs a destruction choke point; preserve the deliberate no-auto-sweep. Audit §8.18; relates #035/#096/#165.
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

- [~] 003 — Force-org validation warnings. Verify: build an over-points / 3+ hero / 4+ same-unit army — amber warnings appear; save + launch never blocked. ([WorkItems/003](WorkItems/003-force-org-validation.md))
- [~] 108 — AI deploy coherent block packing. Verify: AI deploys tight square-ish grids, no stranded or scattered models. ([WorkItems/108](WorkItems/108-ai-deploy-cohesion.md))
- [~] 159 — `DefinePathStage` cohesion crash (four root causes fixed; 0/24 repro, was 5/20). Verify: melee-heavy games (HEF/AH armies) — no `ConsolidateStage` crash, no stacked models. ([WorkItems/159](WorkItems/159-definepath-cohesion-crash.md))
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
