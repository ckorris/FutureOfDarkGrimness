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

- [~] 159 — `DefinePathStage` cohesion crash: residual isolated (CLI/AI HOLD-EXACT submitting an already-broken unit's positions) and fixed 2026-07-18 via lenient movement coherency (mirrors the ConsolidateStage fix), 90/90 clean; GUI human-movement Done-gate facet explicitly deferred + awaiting GUI hand-verify. ([WorkItems/159](WorkItems/159-definepath-cohesion-crash.md))
- [ ] 209 — Weapon-choice option order is nondeterministic (ConcurrentDictionary keyed by Weapon identity): multi-weapon units swing/fire in random order, breaking #193 same-seed replay and benchmark hash reproducibility; fix candidate awaiting sign-off. ([WorkItems/209](WorkItems/209-weapon-choice-order-nondeterminism.md))
- [ ] 210 — Residual bench nondeterminism at --dop > 1 (scattered per-game flips under CPU contention; serial runs exact after #209). Needs the #198 tracer wired into bench to isolate. ([WorkItems/210](WorkItems/210-dop-concurrency-nondeterminism.md))
- [~] 214 — Reposition placements (Teleport, Fanatic, reposition-at-activation) drew no reach circle: the bound rides `MaxDistanceFromStartInches`, which nothing rendered. Per-model rings added 2026-07-23; awaiting GUI hand-verify. ([WorkItems/214](WorkItems/214-teleport-range-circle.md))
- [~] 269 — Teleport/reposition placement rejected most of its own #214 reach ring: the unit's own not-yet-moved models were scanned as obstacles, and per-click 1in cohesion in list order pinned every model after the first to a band around model 1. Own models excluded; cohesion moved to a "not worsened" Done gate (deployment keeps the incremental check). Implemented + tested; awaiting GUI hand-verify. ([WorkItems/269](WorkItems/269-reposition-placement-too-restrictive.md))
- [ ] 216 — Tactician plans rejected by the #205 friendly-stacking check silently fall back to the SOLO resolver (suspected DE/RL mirror-drift driver); charge candidate made friendly-aware, resolver-level repair + drift attribution still open. ([WorkItems/216](WorkItems/216-tactician-solo-fallback-on-stacked-plans.md))
- [~] 263 — Off-table (Ambush reserve) units were chargeable at the origin (round-1 charge on undeployed Shifters): melee family now gated at the AreUnitsInMeleeRange chokepoint + standoff filter + off-table wound diagnostic; implemented + tested, awaiting GUI hand-verify. ([WorkItems/263](WorkItems/263-off-table-units-chargeable.md))
- [~] 277 — Formation cycling in Group mode (Ctrl+Wheel: line/5x2/4-3-3..., index 0 = current shape) for deploy/teleport/movement/consolidation, layout math consolidated into engine FormationLibrary; implemented + tested, awaiting GUI hand-verify. ([WorkItems/277](WorkItems/277-formation-cycling.md))
- [~] 282 — Rotating mid-path (Wheel/R) re-oriented already-committed waypoints (single scalar offset applied to the whole path): PathTemplate now captures the offset per waypoint at placement, so rotation only shapes the next ghost; implemented + tested, awaiting GUI hand-verify. ([WorkItems/282](WorkItems/282-rotation-only-affects-ghost.md))
- [~] 283 — Consolidation group rotation was preview-only (facing offsets silently dropped at Done AND ConsolidateStage never applied entry facings): executed via a new rotate-in-place derivation on #282's per-step offsets; implemented + tested, awaiting GUI hand-verify. ([WorkItems/283](WorkItems/283-consolidation-rotation-executes.md))
- [ ] 284 — Deploy overlap (YellowDeployedOverGreen): commit-time guard SHIPPED (warn + auto-repair at all 5 mandatory-placement seams); root cause still open (occupants invisible/uncommitted at deploy time - stale-engine race suspected; watch for the WARNING log line). Was #282 pre-reconciliation-27. ([WorkItems/284](WorkItems/284-deploy-overlap-invisible-occupants.md))

## Shooting & cover

- [~] 201 — Shooting *out of* cover grants the defender cover: fixed 2026-07-21 via lobby-toggled proximity house rules (default on: 2" exit w/ both-hugging amendment + 6" shared cover), previews kept truthful; implemented + tested on `201-cover-proximity`, awaiting GUI hand-verify. ([WorkItems/201](WorkItems/201-cover-attacker-side.md))
- [~] 276 — Attack animation truthfulness: occluded/out-of-range carriers no longer roll dice (engine bug) nor draw beams; split Takedown shots fire one beam each, rotating snipers. Implemented + tested; awaiting GUI hand-verify. ([WorkItems/276](WorkItems/276-attack-animation-truthfulness.md))

## Model bases & geometry

- [~] 149 — Configurable model base size + shapes (per-unit circle/rect via the `IBaseShape` seam): core landed; remaining facets in the detail file. The deferred hard-path geometry became #150 (awaiting verification below). ([WorkItems/149](WorkItems/149-base-shapes.md))
- [~] 225 — Base shape/size audit: two importer defects, both FIXED 2026-07-19 — rectangle axis swap (OPR writes length-first) and the 28mm default on 102 vehicles/titans (now estimated from Hero + Tough, with an import warning). Corpus clean; awaiting GUI hand-verify. ([WorkItems/225](WorkItems/225-army-list-base-shape-audit.md))

## Special rules — framework

- [~] 042 — Special rules architecture (data-driven Condition x Effect over named hooks + token state). Phases 1-8 largely shipped; remaining: attack/reactivate primitives, then morale/casting invocability. ([WorkItems/042](WorkItems/042-special-rules-architecture.md) + `WorkItems/042-implementation-checklist.txt`)
- [ ] 087 — Custom special-rule authoring + standalone rules files (author new rules as data in the builder; import/export rule sets independent of armies). Builds on #059. ([WorkItems/087](WorkItems/087-custom-rule-authoring.md))
- [~] 100 — Special-rule engine primitives umbrella: Part 1 + cross-unit pre-attack targeting done; open: dormant hooks, RangeModifier/Strider, Part-2/3 primitives (deferred-debuff, dice-pool, markers). Corpus reference is off-repo (`../GDF Armies/`, do not commit). ([WorkItems/100](WorkItems/100-special-rule-primitive-gaps.md))
- [~] 267 — Unit-wide abilities were offered on `Condition.Always`, so a joined hero's Teleport teleported the whole squad; Teleport/Vanguard/Fanatic/Martial Prowess + the four supplement reposition rules now gate on `AllModelsHaveThisRule`, enforced by a new `RuleValidator.ValidateAuthoring` (kept out of the load gate so old army files still open). Implemented + tested; awaiting GUI hand-verify. ([WorkItems/267](WorkItems/267-all-models-gate-unit-wide-abilities.md))
- [ ] 104 — Single-unit `Evaluate` doesn't consume `NextTrigger` grants — correct today; build the opt-in when a rule needs it. ([WorkItems/104](WorkItems/104-single-unit-evaluate-grant-consume.md))
- [ ] 197 — Faction rule coverage pt.2: the dead names needing new engine primitives (incl. #196's F16 handoff: Sergeant per-model attribution, Armor(X) defense floor). DONE: slice 0, the ">9in" gate, P5a, P5b, reposition-at-activation, Teleport, Delayed Action, Darkborn, P15 Unpredictable, RuleFireLint consumption check, and (2026-07-22) the marker cluster - P13 token-scaled Frenzy/Growth (+41) + P14b prompted-spend Tag/Target/Spotter (+28); P12 deferred (dice invariant, owner-ruled); P10 DONE - Ravage + Crossing Attack + Storm of X (+44); P21 DONE (re-scoped like P22) - Fanatic (+19) + Re-Deployment (+27), 14 refs re-filed; P11 reflect DONE - Retaliate + Deathstrike + Self-Destruct (+27); and (2026-07-23) P6 DONE - only Casting Debuff needed a primitive (ERollKind.Cast), the other four were data on built seams (+23 with riders); Versatile Defense DONE, closing P5a at 175/175 (+21); P23 Caster Group DONE (+3), which turned every in-play rule-identity check (casting, transport, re-deployment, menu-action routing) into a capability the rule graph answers, Spell Accumulator DONE (+7) as the first slice built entirely on that seam, and Spell Conduit DONE (+9) - the cast-origin relay, no prompt (origin derived from targets, made visible in picker/target-rows/banner/breakdown) - which closes P23 at 19/19. ([WorkItems/197](WorkItems/197-faction-rule-engine-primitives.md))

All 33 GF v3.5.1 core special rules are implemented (verified 2026-06-30; see archive #029-#032/#051).
Corpus coverage is a different story: 199 of 13,870 book rule references (1.4%) do nothing today — see #197 (#196, the data-only half, closed 2026-07-22). The 2026-07-23 Misc-primitives pass shipped 10 of 13 triaged rules (243 -> 199); Instinctive, Repel Ambushers, Inquisitorial Agent, Extended Buff Range re-filed as their own slices.

## Casting

- [~] 034 — Spell content: targeting primitives Part 1 complete; remaining: conferred-rule implementations (coordinate with #100) and per-faction spell JSON (copyrighted — authored locally, never committed; partially generated). ([WorkItems/034](WorkItems/034-spell-content.md))
- [~] 234 — Cast gated on `HasAttacked` (shooting or melee closes the casting window; moving does not), per v3.5.1 Caster(X) "at any point before attacking". Implemented + tested; awaiting GUI hand-verify. ([WorkItems/234](WorkItems/234-cast-after-charge-legality.md))
- [~] 249 — Caster's "only one try per spell" now enforced via a per-activation attempted-spell set (recorded with the cost, so a failed cast burns the try); casting different spells in one activation stays legal. Implemented + tested; awaiting GUI hand-verify. ([WorkItems/249](WorkItems/249-one-try-per-spell.md))
- [~] 244 — Caster self-boost: own tokens for +1/each in a new dedicated spell picker (`ChooseSpellRequest`, one-panel GUI with useful-cap-gated boost stepper). Implemented + tested; awaiting GUI hand-verify. ([WorkItems/244](WorkItems/244-caster-self-boost.md))

## Army Forge

- [~] 218 — "Replace All" charged per model instead of flat. Convention confirmed from a real share list and fixed 2026-07-19 (Havoc list reconciles 1120 both ways; 200 priced All options were overcharging). Open: `Affects.Any` pricing unverified (1185 options ride on it) + GUI hand-verify. ([WorkItems/218](WorkItems/218-army-forge-replace-all-cost.md))
- [ ] 220 — Version control for Army Forge lists (undo/revision history); mechanism not yet decided, surface the design fork first. ([WorkItems/220](WorkItems/220-army-forge-list-version-control.md))
- [~] 236 — Freeform builder silently stripped a Forge army's embedded book/selections on save; now gated behind an explicit "Save detached" confirm. Implemented + tested; modal awaits GUI hand-verify. ([WorkItems/236](WorkItems/236-freeform-save-strips-forge-block.md))
- [~] 241 — Army Forge share-link importer: paste an army-forge.onepagerules.com share link -> preview -> .fdgarmy (engine `OprListImporter` + Forge-screen UI + `--import-army`); gates on OPR version 3.5.x. Points model corrected 2026-07-19 (per-unit `cost` is BASE, total comes from `listPoints` - imports were light); GUI modal still awaits hand-verify. ([WorkItems/241](WorkItems/241-army-forge-share-import.md))
- [ ] 242 — Import campaign/narrative list features (XP, traits, campaign mode) that #241's importer warns about and drops. ([WorkItems/242](WorkItems/242-campaign-import-features.md))
- [~] 261 — Import mispriced 39% of upgrades (flat generic cost read in preference to OPR's per-unit `costs[]`) and never matched quantity-prefixed replace targets ("2x Rapid Shard Cannon"), which also greyed the swap out as "none to replace". Both fixed, 47 books re-priced; awaits GUI hand-verify. ([WorkItems/261](WorkItems/261-opr-per-unit-costs-and-quantity-targets.md))
- [~] 259 — Special rules in the Forge are underlined and explain themselves on hover (all four rule-bearing surfaces), reading `SpecialRuleDefinition.Description` via a per-book glossary. Implemented + suite green; awaits GUI hand-verify. ([WorkItems/259](WorkItems/259-army-forge-rule-tooltips.md))

## Victory & scoring

- [~] 257 — Team-based victory scoring: teammates pool objectives, unique top team wins, banner names every winning player ("Alpha and Bravo win!"); `GameResult.WinnerPlayers` added, 1v1 results byte-identical. Implemented + tested; awaiting GUI hand-verify. ([WorkItems/257](WorkItems/257-team-victory-scoring.md))

## Transport

- [ ] 097 — Disembark/embark full movement: replace the Advance-equivalent simplifications (disembark-then-Rush/Charge from the 6" drop; real move-into-contact to embark). ([WorkItems/097](WorkItems/097-transport-full-movement.md))

## AI agent (Tactician)

Master plan: `docs/ai-agent-plan.md` (heuristics -> MCTS -> learned value net; gates, invariants, vocabulary).

- [ ] 191 — Tactician AI agent umbrella: challenge-level game-playing AI as a new profile alongside the solo-rules bot; phased A-D, benchmark-gated. ([WorkItems/191](WorkItems/191-tactician-agent.md))
- [~] 264 — Tactician unit behind large impassible terrain rushes sideways/backwards round 1: all 8 causes fixed and merged to master, 11 pins green (`TacticianWalledUnitTests`); issue 1's melee half folded in 2026-07-25. Open: 8b hysteresis (owner's call, deferred) + GUI eyeball check. ([WorkItems/264](WorkItems/264-tactician-walled-unit-lateral-retreat.md))

## Networking & infrastructure

Internet-play readiness pass (QF1-10) landed 2026-07-08 — password gate, keepalive/NoDelay, single-buffer
frames, targeted PlayerID assignment, greeting-timeout eviction, post-launch join gate, client host-loss
detection, host-IP display, DNS host entry. See `NetworkingHandoff-2026-07-08.md`. Remainders below.

- [~] 187 — Disconnect recovery: a dropped connection now ends with its own `EGameOutcome.Disconnect`, and the host auto-writes `Saves/recovery-<utc>.fdgsave` (newest 5) named on the game-over card; rejoin covered by the suite's first real-socket tests (saved-PlayerID adoption, distinct slots). Implemented + tested; awaiting the two-machine hand-verify in the detail file. ([WorkItems/187](WorkItems/187-disconnect-recovery.md))
- [ ] 188 — Multi-remote-client support: live-test 3+ players / 2+ remote clients (QF5 enabled it; roster/team/routing edge cases). ([WorkItems/188](WorkItems/188-multi-remote-client.md))
- [~] 189 — Broadcast gating (roster-only) DONE + tested; configurable listen/connect port DONE (both modals, `NetworkProtocol.DefaultPort`, browser auto-fills from listing). Engine `46f387d`; awaiting GUI hand-verify of the port fields. ([WorkItems/189](WorkItems/189-broadcast-gating-configurable-port.md))
- [~] 190 — Networked clients never received mid-game token updates: host-side `TokenChangeBroadcaster` re-Sets the owning UnitData/ModelData on any token add/count-change/removal (Option A, rides the existing update path). Implemented + tested 2026-07-26; awaiting GUI/live hand-verify. ([WorkItems/190](WorkItems/190-networked-token-sync.md))
- [~] 271 — Server browser: $0-tier master list server (Cloudflare Worker registry, TTL heartbeats) + "List publicly" host checkbox + browser-first join UI. P1-P3 + deploy + UPnP auto-forward done 2026-07-23; remaining: lobby status surface, GUI hand-verify, live 2-machine + real-router UPnP test. ([WorkItems/271](WorkItems/271-server-browser.md))
- [ ] 058 — (low) Migrate message/save serialization off Newtonsoft onto System.Text.Json; pure consolidation. ([WorkItems/058](WorkItems/058-stj-migration.md))
- [ ] 057 — (low) Make state-machine contexts store-backed/serializable so #052's `GameProgressData` mirror can be deleted; deferred for risk.
- [ ] 054 — (low) Client-initiated save: host produces the `.fdgsave` on the client's behalf. Follow-up to #052.

## Client / renderer

- [~] 162 — Tactical overlay: opportunity fields + per-model instruments (instruments call real rules, never the field texture). P0 scaffolding done; P1-P7 remain. Threat frontiers were removed by #247 (the discs still feed movement snap). Design doc: `docs/tactical-overlay-plan.md`. ([WorkItems/162](WorkItems/162-tactical-overlay.md))
- [ ] 161 — Resolver UI consistency pass: stat/highlight parity, right-click undo on deploy, shared canvas-selector base (also absorbs the `GuiModelSelectionResolver` alive-filter gap), dialog chrome. Findings + canonical click scheme in the detail file. ([WorkItems/161](WorkItems/161-resolver-consistency.md))
- [~] 056 — Presentation beat stream: architecture shipped and live on master; remaining animation polish + a hands-on pass. ([WorkItems/056](WorkItems/056-presentation-beat-stream.md))
- [~] 268 — Terrain palette split from the auto layout (which fed both, so appending would have made every generated map denser): 18 new templates, mostly small impassible objects, plus an optional `TerrainPieceEntry.Name` so a 30-row picker reads. Implemented + tested; awaiting GUI hand-verify. ([WorkItems/268](WorkItems/268-terrain-palette-expansion.md))
- [ ] 049 — Multi-pool terrain selection: lobby picker for which `TerrainLayoutFile` feeds AutoFromLayout / Alternating. Spun off #002.
- [ ] 281 — `GridPathfinder.StringPull` re-tests shortcuts with an Impassible-only clearance check, so every bend the A* made to avoid DIFFICULT ground is pulled straight back through it; `DifficultCostMultiplier` is a no-op except where impassible terrain forces the bend. Also what hides Strider. ([WorkItems/281](WorkItems/281-stringpull-erases-difficult-routing.md))
- [~] 055 — Special-rule attribution in resolvers: (a) movement overlay + (b) shooting resolver done; remaining (c): markers on units carrying relevant defensive/relational rules (e.g. a Stealth tag at the source).
- [~] 221 — Lobby color picker: 8-color RTS-style dropdown per player row, picks synced through the lobby protocol (engine `ColorIndex` + protocol v3), no stealing of defaults or picks. Awaiting GUI hand-verification (incl. a live two-machine check). ([WorkItems/221](WorkItems/221-lobby-color-picker.md))
- [ ] 222 — Tie dice rolls (`RollOffBeat`) take too long; tune beat/overlay pacing. ([WorkItems/222](WorkItems/222-dice-roll-speed.md))
- [~] 223 — Deploy/activate unit picker: hovering a valid option raises a full-spec stat tooltip (shared `UnitStatBlockRenderer`). Implemented; awaiting build + GUI hand-verification. ([WorkItems/223](WorkItems/223-deploy-option-tooltip.md))
- [ ] 224 — (exploratory, unconfirmed) Persistent "selected unit" inspector panel: stat detail, charge/shoot threat radius, hover-to-explain special rules. ([WorkItems/224](WorkItems/224-persistent-unit-inspector-panel.md))
- [ ] 226 — In-app bug reporting system; mechanism not yet decided. ([WorkItems/226](WorkItems/226-bug-reporting-system.md))
- [~] 227 — Hero indicator: white dark-outlined star on the hero model's base + hover-tooltip tag with the hero's own Q/D. Implemented + tested; awaiting GUI hand-verification. ([WorkItems/227](WorkItems/227-hero-visual-indicator.md))
- [ ] 228 — Terrain texture shifts when zooming (texture likely sampled in screen space, not table space). ([WorkItems/228](WorkItems/228-terrain-texture-zoom-shift.md))
- [ ] 229 — (exploratory) Bottom in-game menu should maybe be its own panel; confirm which strip + intent before building. ([WorkItems/229](WorkItems/229-bottom-menu-own-panel.md))
- [~] 230 — #162's ghost-anchored opportunity field (LoS + cover aware, GPU) extended to placement via a new `IGhostFieldSource` opt-in, so deploy/ambush/disembark/teleport show what the spot would reach; `V` toggles. Implemented + tested; awaiting GUI hand-verify. ([WorkItems/230](WorkItems/230-placement-weapon-range-rings.md))
- [ ] 231 — Remove the LoS blocking-line visual from shooting (confusing when a valid target stands in front of the blocker). ([WorkItems/231](WorkItems/231-remove-los-blocking-lines.md))
- [~] 247 — Range/threat overlay UI: slice 1 built 2026-07-26 — `FieldAnchorPlan` collapses the anchor decision to one contest (hover > pin/ghosts > placement, exactly one field), hover anchors on any unit, `V` is the global toggle, rebuilds signature-cached; the `GhostAnchoredField` mode and the red threat frontiers (F, Q5 option a) both retired. Legend still open; awaiting GUI hand-verify. ([WorkItems/247](WorkItems/247-range-overlay-ui.md))
- [~] 233 — Add a dice-roll beat for casting: `CastSpellStage` presents a "Roll to Cast" `DiceRolledBeat` with the shifted threshold. Implemented + tested (with #244); awaiting GUI hand-verify. ([WorkItems/233](WorkItems/233-cast-roll-beat.md))
- [~] 237 — Single-option pick shortcuts: shooting pre-selects a sole fireable target, sole-defender charge shows a one-click confirm card (keeps #202's Back), Enter = Auto-assign All. Implemented + tested; awaiting GUI hand-verify. ([WorkItems/237](WorkItems/237-single-option-pick-shortcuts.md))
- [~] 238 — Attack animation now plays WHILE the to-hit dice tumble (AttackBeat is a zero-lead-in held beat) + gunshot/melee sound per volley instead of once. Implemented + tested; awaiting GUI hand-verify. ([WorkItems/238](WorkItems/238-attack-dice-overlap-volley-sound.md))
- [~] 239 — Weapon effect sets: 13 ranged / 10 melee themed visual+sound styles baked into books/armies as explicit keys (faction defaults + global fallback), truthful hit/miss impacts + impact sounds. Implemented + tested; awaiting GUI hand-verify (checklist in detail file). ([WorkItems/239](WorkItems/239-weapon-effect-sets.md))
- [~] 245 — Dice roll panel redesign: bottom caption strip, target badge, overlap ghost-fade; v2 roll-offs join + toolbar vertical; v3 glance metadata (category stripe/word, who-vs-who context, modifier + proc chips, stretched beats — engine + app). Implemented + tested; awaiting GUI hand-verify. ([WorkItems/245](WorkItems/245-dice-caption-strip.md))
- [~] 246 — In-game escape menu (Esc): Save/Load/Options/quit-to-menu/quit; bottom-left toolbar collapsed to one Menu button, Field GPU/CPU button deleted. Implemented (S1-S3) 2026-07-18; awaiting GUI hand-verify. ([WorkItems/246](WorkItems/246-escape-menu.md))
- [~] 248 — Resolver keyboard hotkeys (fixed action letters, list numbers/arrows/Enter) + engine back-out of a pristine activation (extends #202, absorbs part of #161). Implemented + tested 2026-07-19; awaiting GUI hand-verify. ([WorkItems/248](WorkItems/248-resolver-hotkeys-and-backout.md))
- [~] 250 — Per-model visuals now follow the base shape: 4 raw-circle draws (spotlight halo, shooting target rings, ghost threat ring, cast-assist) + 4 dropped-facing call sites, plus a new `DrawOutlineRaylib`. Implemented 2026-07-19; awaiting GUI hand-verify. ([WorkItems/250](WorkItems/250-per-model-visuals-follow-base-shape.md))
- [~] 251 — Ruler overlay measured rectangular bases as circles (inscribed radius): edge reading and snapping now use the engine's facing-aware shape geometry; nose-to-nose bikes were reading ~0.98in too far. Implemented 2026-07-19; awaiting GUI hand-verify. ([WorkItems/251](WorkItems/251-ruler-shape-aware-measurement.md))
- [ ] 252 — Anchored field texture ignores the #201 cover proximity rules (tint over-paints cover; pips/aim lines already truthful): needs per-piece polar cover intervals, target-anchored mode first; approach + estimate in the detail file. ([WorkItems/252](WorkItems/252-field-cover-proximity-truthfulness.md))
- [~] 278 — 2026-07-25 playtest fixes: spillout dice batched into one row; all-saved volley morale pinned (already fixed by #254, real-path regression added); Harassing strike-back move verified rules-legal (no change); Toast banner on Shaken recovery (both paths). Implemented + tested; awaiting GUI hand-verify. ([WorkItems/278](WorkItems/278-playtest-fixes-2026-07-25.md))- [~] 266 — Console word-wrap (the `HorizontalScrollbar` flag was widening the wrap rect to the content, so `TextWrapped` never bit) + resolver/console split moved from 50% to 60% of screen height, which widened every docked resolver at once. Implemented; awaiting GUI hand-verify. ([WorkItems/266](WorkItems/266-console-wrap-and-panel-height.md))
- [ ] 253 — New movement visual: colored area showing where ending your move earns the cover bonus vs a pinned enemy (#201-aware, samples `VoidsCover`); attacker-side "shoot over this wall" sibling facet awaiting owner call. ([WorkItems/253](WorkItems/253-cover-bonus-placement-visual.md))

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

- [~] 168 — Rule-load diagnostics surfaced in the UI: aggregated "N rules ... not implemented" in the game log at launch (buffered `RuleLoadWarnings`, GUI modes) + live army-builder pane lines on a store-free `ArmyRuleAudit` parity-pinned to the launch path. Implemented + tested; awaiting GUI hand-verify. ([WorkItems/168](WorkItems/168-rule-load-diagnostics-ui.md))
- [~] 164 — `DealHits.WithRules` resolver seam so Blast(3) multiplies pre-attack/Strafing hits (Breath Attack residual). Shared `SyntheticHitResolution` fold + dispatch-time rule resolution landed 2026-07-19 (also fixed Strafing dropping the effect's AP); awaiting GUI hand-verify. ([WorkItems/164](WorkItems/164-dealhits-withrules-seam.md))
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

- [~] 243 — Objective placement mode (Auto-Placed / Player-Placed) mirroring terrain modes; Auto uses the solo-rules AI algorithm via a shared `ObjectiveAutoPlacer`, debug options sort last in Release, roll-off skipped in Auto. Verify: the four GUI checks in the detail file. ([WorkItems/243](WorkItems/243-objective-placement-mode.md))
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
- [~] 095 — Special rules re-attached on save/load resume; army-level residual (granted supplement rules + spell lists) fixed 2026-07-23. Verify: in the same #052 session, rules still fire and a Caster still has spells after resume. ([WorkItems/095](WorkItems/095-rules-not-rehydrated-on-resume.md))
- [~] 156 — Army Forge catalog builder. Verify: remaining hand-verify rounds (all core facets landed). ([WorkItems/156](WorkItems/156-army-forge-builder.md))
- [~] 106 — Army builder authoring UX. Verify: read-only stat block, per-unit Duplicate, auto-unfold of new units/weapons/spells. ([WorkItems/106](WorkItems/106-army-builder-ux.md))
- [~] 053 — Sound cues on the beat stream. Verify: hear the placeholder tone per beat; real `.wav`s drop into `FdgRaylib/Assets/Sounds/` by filename. ([WorkItems/053](WorkItems/053-sound.md))
