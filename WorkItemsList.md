# Work Items

Numbered, persistent backlog of engineering tasks. Each item is roughly "one Jira ticket" sized — some are umbrellas that will fragment when picked up.

See `WorkItems/README.md` for the per-item file template. Per-item working notes live in `WorkItems/NNN-slug.md`, created when work starts on that item.

Numbers are permanent and never reused. If an item is split, its line stays and points at the new numbers.

> **2026-06-13 — reconciliation 3.** The 2026-06-10 audit follow-ups (`Audit-6-10-2026.md`) were authored on a local branch that numbered its four HIGH-priority stage-machine/networking items **055–058**; by the time they were folded into this index, origin/master had already assigned 055–058 to other work (rule attribution, presentation beat stream, contexts refactor, STJ migration). Per the never-reuse rule the audit squatters yield: **055→083, 056→084, 057→085, 058→086** (internal cross-references updated). The remaining audit items (**059–070, 073–082**) kept their numbers, which were free on master. Audit item **060**'s dead-field cleanup landed the same day (commit `b0aebc9`); the rest of #060 remains open. (The same local branch had also renumbered the presentation beat stream / sound to 071/072 — those are *not* carried here, since master already settled them as #056/#053 in reconciliation 2.)
>
> **2026-06-11 — reconciliation 2.** The never-reuse rule was violated again on master: **052** meant both *save/load* and the *presentation beat stream*, and **053** meant both the *contexts-into-store refactor* and *sound cues* (a new pre-push hook now blocks duplicates). Resolved by the same detail-file/cross-reference precedent as #055's renumber: **save/load keeps 052** (the #039/#054/#057 "follow-up to #052" references all mean it; merge commit `b7acb76` names it) and **sound keeps 053** (owns `053-sound.md`). The presentation beat stream is now **056** (`WorkItems/056-presentation-beat-stream.md`, renamed) and the contexts refactor is now **057**. Branch names / old commit messages containing `052-presentation-beat-stream` and `#053` predate the renumber.
>
> **2026-06-03 — reconciliation.** This index had drifted out of sync with the `WorkItems/NNN-*.md` detail files and git history. Numbers **044/045/046** had each been reused across two parallel efforts (a terrain/deployment effort and a line-of-sight effort), violating the never-reuse rule. Resolved by treating the on-disk detail files + merged commits as authoritative: **044/045/046 now mean the line-of-sight cluster** (matching `WorkItems/044-046-*.md`). The two terrain tasks that had been squatting on 044 and 046 were reassigned fresh permanent numbers **049** and **050**. Work item **012** (merged: engine `a967fa1`, GUI `3a6f189`) and **044** (LoS ally-exclusion, merged `8701abf`) were complete but never checked off — fixed. Terrain rotation, formerly listed as its own #045, is folded into the #002 entry where it actually shipped. Items **041 / 045 / 046** are implemented and on master but parked in *Awaiting verification* until manually eyeballed in the running app.

---

## Setup & map

- [ ] 003 — Force organization validation (optional rule: hero/unit/copy/cost caps)

## Deployment

- [ ] 006 — Hero joins unit + takes morale on behalf of unit
- [ ] 007 — Resolve `DeployAllUnitsStage.Enter` `NotImplementedException` and "actually move the models" TODO

## Activation flow

- [ ] 008 — Shaken unit activation behavior (idle, can't seize/contest, clears at end of activation)
- [ ] 009 — General end-of-activation morale test (half-size trigger outside melee)
- [ ] 010 — Custom actions branch in `ChooseActionStage` (currently hardcoded `false`)

## Movement

- [ ] 011 — `MovementUtilities.ValidateMovingThroughEnemyUnits` (currently empty)
- [ ] 050 — Movement validation ignores model base radius for terrain footprints. `MovementUtilities.ValidateMovingThroughImpassibleTerrain` (and the difficult/dangerous variants) test a zero-width center-to-center line against terrain footprints, so a model can park with its center just outside an impassable shape while its base overlaps it. Fix: inflate the terrain footprint by the model's `BaseRadiusInches` (Minkowski expansion) or use swept-disc distance, in `MovementUtilities`. Resolver layer needs no changes. Pre-existing — surfaced more by #002's richer terrain. (Reassigned from 046, whose number was reused for the line-of-sight cluster.) ([WorkItems/050](WorkItems/050-movement-base-radius.md))

## Shooting

- [ ] 015 — Attack-count modifiers in shooting flow (`RollToHitStage` TODO)
- [ ] 016 — Hit→wound effect propagation (`DetermineSaveRollsNeededStage` TODO)

## Melee

- [ ] 017 — In-range attacker/defender determination (2" horizontal, 4" vertical) — replace current "everyone fights" behavior
- [ ] 020 — Fatigue: per-unit/per-round flag — hit on unmodified 6s after first melee attack this round. May not need to be a stage; current `ApplyFatigueStage` may be deletable.
- [ ] 021 — Morale roll modifiers + Fear/Fearless effects in `DetermineMeleeWinnerStage` and `RollForMoraleStage`
- [ ] 022 — Vertical melee range handling (`ChooseMeleeDefenderStage` TODO)

## Wound assignment

- [ ] 023 — Tough wound-priority (continue wounding same Tough model until killed; heroes last)
- [ ] 024 — Validate wound splits in `AssignWoundsResults` (currently allows illegal splits)

## Special rules — framework

- [~] 042 — Special rules architecture (Plan B: data-driven `Condition × Effect` records over a named hook surface, with a unit/model token system as the state primitive). Prerequisite for 026–034. **Phase 7 complete (suite 241/0)**: polymorphic `RuleEvaluator` + `RuleValidator`, all passive rules + activated abilities (7c) + cross-unit token cleanup (7g); suppression first-pass (`IgnoreRule`/`SuppressRule`) done. **Phase 8 integration underway (suite 281/0): 15 rules live across 8 sinks via the `SinkOperation<TSink>` pattern** — Stealth/Artillery/Indirect/Reliable (hit), Fast/Very Fast/Slow (movement), Surge/Relentless/Furious (extra-hit), Deadly (wound-mult), Regeneration (wound-ignore), Tough (max-wounds at army-load), Rending (save-mod, hit→save carry), Bane (save-reroll), + Unstoppable (suppressor). **Engine-primitive refactor (Phase 7h) begun — two subsystems now invocable: MOVEMENT (Vanguard, via `MovementExecutor` + the `ExecutableOperation`/`OperationExecutor` imperative-op seam, the mirror of `SinkOperation`) and DEPLOY (Scout + Ambush, via the defer/reserve primitive).** Remaining: attack/reactivate primitives (Reactivate/Strafing/Impact), then morale/casting; JSON loader (**no longer gated — #027 landed 2026-06-11: rules attach at unit or weapon scope, schema is weapon-attachment-capable**). See `WorkItems/042-special-rules-architecture.md` and `042-implementation-checklist.txt` (INTEGRATION PROGRESS cont. 1–30).
- [~] 026 — Unit special rules framework wiring (`UnitData.SpecialRules`, `GetRealSpecialRulesFromArmyList`, `GetMobility`) — depends on 042. **Army-list → #042 rule-registry resolution done** (2026-06-07): `Rules/Dispatch/CoreRuleCatalog.cs` (19 rule defs incl. Vanguard/Scout/Ambush + numeric `Deadly(X)`/`Tough(X)` loaded with args via `DescribeRuleEntry`) + `RuleResolver.TryResolve`; `FDGServer` resolves each `UnitFileEntry.SpecialRules` name and `AttachRuleDefinition`s it (skip+log for unimplemented), replacing the old hardcoded-Stealth `//TEST` hack — declared rules now fire in live headless games. Remaining: the legacy `GetRealSpecialRulesFromArmyList`/`GetMobility` paths (separate `SpecialRule` type).
- [ ] 028 — Deadly weapon priority (resolve first, wounds don't carry across models)

## Special rules — implementations

These are umbrellas; will fragment per-rule when picked up.

- [ ] 029 — Movement-modifier rules: Fast, Slow, VeryFast, Immobile, Strider, Aircraft, Flying
- [ ] 030 — Combat-modifier rules: Furious, Impact, Counter, Thrust, Surge, Relentless
- [ ] 051 — Furious charge gate: extra-hits-on-6 should fire only when the bearer is in melee **AND charging**. The combat-kind (melee) gate shipped with the #042 extra-hit slice; the "charging" condition was deferred — it needs charge/action state threaded into the hit-roll-complete context (same shape as the `AttackerMoved` threading). Until then Furious over-applies to *any* melee attack, not just charges. Hold until the melee subsystem (#017/#020) is fleshed out — charge-precision on a stubby melee engine is premature. (Spun off from the #042 extra-hit slice, 2026-06-07.)
- [ ] 031 — Defense/unit rules: Tough, Regeneration, Stealth, Fear, Fearless, Hero, Scout, Ambush
- [ ] 032 — Weapon rules: AP, Rending, Blast, Reliable, Indirect, Takedown, Limited, Unstoppable, Bane

## Casting

- [ ] 033 — Caster(X) subsystem: spell tokens per round, casting attempts (4+), friendly Caster ±1 assist within 18"
- [ ] 034 — Spell content (initial set per faction)

## Transport

- [ ] 035 — Transport(X) system: embark/disembark via move actions, deploy with units inside, dangerous terrain test on destruction

## Networking & infrastructure

- [ ] 036 — Server readiness handshake (`FDGServer.cs:148` TODO — wait for all clients ready)
- [ ] 037 — Replace non-concurrent collections in `FDGHost` (`FDGHost.cs:75, :130` TODOs)
- [ ] 038 — Resolve `LobbyViewModel_Host` `NotImplementedException` paths (`:288, :400`)
- [ ] 039 — Resolve `GameDataStore.CreateFromTypeMap` `NotImplementedException` / introduce builder — folded into #052 (durable save format needs it)
- [ ] 058 — (low priority, no functional gain) Migrate the message/save serializer off Newtonsoft `TypeNameHandling.Auto` to System.Text.Json, so the whole codebase is on one JSON library (the new rule-definition loader is STJ). Not gating anything; pure consolidation. ([WorkItems/058](WorkItems/058-stj-migration.md))
- [ ] 052 — Save / Load a game in progress: snapshot the `GameDataStore` to a `.fdgsave` file + a new `GameProgressData` component (round/turn/activation state promoted into the store); load drops into a host lobby where saved slots are re-crewed (PlayerID remap), then `FDGServer` resumes mid-round via a new resume path. Save "any time" = rolling snapshot at each activation boundary; restore re-plays the current activation. Mostly submodule work (branch + bump). ([WorkItems/052](WorkItems/052-save-load.md))
- [ ] 057 — (low priority) Refactor state-machine contexts into the directly-serializable source of truth: make `MainPhaseContext` / `SingleRoundContext` (and `TeamPlayerAlternationCursor`) store-backed and JSON-serializable in place — teams keyed by `TeamNumber`, the `IGameContext` service refs `[JsonIgnore]`'d and re-injected on load — so the store *is* the save and the separate `GameProgressData` mirror added in #052 can be deleted. Same end result as the mirror but more invasive to the live engine (changes the context types every stage already uses), so deferred for risk. Follow-up to #052.
- [ ] 054 — (low priority) Client-initiated save: let a non-host player trigger a save. The host owns the authoritative `GameDataStore`, so the client would request it and the host produces the `.fdgsave` and sends it back over the network (or the host saves on the client's behalf). Today only the host can save (`CanSaveGame` is false on `LobbyViewModel_Client`). Follow-up to #052.

## Client / renderer

- [ ] 040 — Post-game navigation back to main menu in GUI mode (currently window just stays open)
- [ ] 053 — Sound cues on the presentation beat stream: BUILT (app-side, no engine change) — reusable `AudioManager` (device + cache + headless no-op, repurposable for UI sounds), `PresentationSoundCues.CueFor` beat→cue mapping, `PresentationPlayer.BeatStarted` hook fires cues in lockstep with visuals. Placeholder tone covers every cue until real `.wav` files land in `FdgRaylib/Assets/Sounds/` (drop-in by filename). Held open only until heard by hand. See `WorkItems/053-sound.md`.
- [ ] 056 — Presentation beat stream: engine-owned, paced, semantic event stream (`context.Present(beat)`) so play feels lifelike — gliding movement, projectile→save/hurt→death beats, tumbling dice, stage-change flashes. Free-running (engine self-paces on a wall clock it owns; no renderer ack), inline emission from stages, host-authoritative & replicated, headless degrades to instant + text. App owns the visual model/tweens; engine owns the beats and pacing. Spans the engine submodule + client. See `WorkItems/056-presentation-beat-stream.md`. (Renumbered from a reused #052 on 2026-06-11 — see the reconciliation note above; the `052-presentation-beat-stream` branch name and older commit messages predate the renumber.)
- [ ] 049 — Multi-pool terrain selection: lobby picker for which `TerrainLayoutFile` feeds `AutoFromLayout` / `Alternating`. Spun off from #002 — that ships with one hardcoded built-in pool. (Reassigned from 044, whose number was reused for the line-of-sight cluster.)
- [~] 055 — Special-rule attribution in the resolvers (originally tracked as **#052** in the 2026-06-11 commits + code comments, before the merge revealed origin/master had already reused #052 for save/load and the presentation beat stream; renumbered to 055 per the never-reuse rule — the `#052` strings in `SightRuleLabel`/resolver comments and commit messages `b76ec49`/`5768bab` predate the renumber). Make it visible *why* a shot/move behaves differently. **(a) + (b) DONE 2026-06-11.** (a) Movement targeting overlay — names the rule causing each cover/LoS-ignore inline on the per-weapon fire-line labels (e.g. `Huge Gun (Indirect ignores line of sight)`), with overflow handling (anchor-flip + clamp to screen on both axes). (b) Shooting resolver (CLI + GUI) — surfaces the same per-weapon rule info in the weapon list/details panels. Engine addition shipped: `RuleEvaluator.EvaluateAllNamed` (non-logging, pairs each op with its alias-aware origin name) + `SightRuleQueries.CoverIgnoreSource`/`LineOfSightIgnoreSource`; the names ride `WeaponOption` + `WeaponSightProfile`; shared `FdgRaylib.SightRuleLabel` composes the wording (one rule that ignores both → named once). Side-effect: the per-build sight-query log spam is gone (queries are now non-logging). **(c) LATER** — markers on the *units* that carry a relevant rule (e.g. a "Stealth" tag over enemy units with Stealth) so defensive/relational rules are visible at the source. Per-weapon accuracy landed with #027 (2026-06-11): `SightRuleQueries` evaluates the queried weapon's own rules, so the labels are weapon-accurate. Builds on #041/#045.

---

## 2026-06-10 audit follow-ups

From `Audit-6-10-2026.md` (section references therein). High-priority items are marked; they involve real or potential bugs.

### Stage machine & stage resolution

- [ ] 083 — **(HIGH PRIORITY)** Fix the async-void transition chain in the stage machine (audit §4). `ParentStage.Transition` is a `void` delegate fed `async` lambdas, so the await chain breaks at every transition: stage exceptions after the first transition are unobservable (process-crash or silent, never a faulted Task), `StateMachine.Enter`'s Task completes long before the game ends, and `SignalEvent` discards `ExecuteTransition`'s Task. Fix: make `Transition` return `Task`, await through `ExecuteTransition`/`SignalEvent`/`StageBinding.Activate`, give `FDGServer` one end-to-end game Task with a top-level fault handler, and add a `Task.Yield` at a round boundary to stop synchronous-continuation stack growth. Mechanical but wide (~70 stages); compiler finds every site. (Renumbered from a local 055 that collided with master — see 2026-06-13 reconciliation note.)
- [ ] 084 — **(HIGH PRIORITY)** Stage-resolution thread safety (audit §5): `RequestMessageSender._pendingTaskAndResolvers` is mutated from engine + bus threads with no lock (→ `ConcurrentDictionary<TaskID,…>`), and its `TaskCompletionSource`s lack `RunContinuationsAsynchronously`, so replies resume engine stage code synchronously on the network read loop. Note: adding `RunContinuationsAsynchronously` removes the accidental serialization that currently masks network write races — do #086's write lock in the same change or immediately after. (Renumbered from a local 056.)
- [ ] 085 — **(HIGH PRIORITY)** Tolerate unknown/duplicate `TaskID` replies (audit §5): `OnReceivedReplyMessage`/`OnReceivedErrorMessage` throw inside bus dispatch for unknown TaskIDs; with no dispatch try/catch a stray or duplicate reply can kill a connection. Log-and-ignore (idempotent), throw only in DEBUG. (Renumbered from a local 057.)
- [ ] 086 — **(HIGH PRIORITY)** Per-connection network write lock (audit §6): `CommandProtocol.WriteCommandAsync` makes three separate stream writes and `FDGHost`/`FDGClient` have no per-connection send lock, so concurrent sends (data sync + beats + requests + log relay) can interleave bytes inside a frame and corrupt the stream. Add a per-connection `SemaphoreSlim(1,1)` or an outbound queue with a single writer task. Companion to #084. (Renumbered from a local 058.)
- [ ] 059 — Route decision requests only to the target player's connection instead of broadcasting to all clients and filtering receiver-side (audit §5): saves bandwidth, stops baking in an open-information assumption, and stops shipping every player's decision payloads to everyone. The per-player plumbing already exists (`NetworkedPresentationSink` targets single connections). Also simplify `StageTaskNotifyAwaitingMessage` to carry `PlayerID` + display name instead of a `DataBinding<PlayerSlotInfo>` (removes a deserialization-order coupling).
- [ ] 060 — Stage-resolution cleanup (audit §5): delete dead `StageResolverRegistry._gameDataStore` field, fix "Remove client" → "Remote client" exception text, delete the commented-out PlayerID filters in `RequestMessageSender`. **Dead-field part (`_gameDataStore`) landed 2026-06-13 (commit `b0aebc9`); the exception-text fix and commented-filter deletion remain.**

### Data layer

- [ ] 061 — Defuse the store-capacity time bomb (audit §2): `ComponentStore<T>` never grows and the default map registers `ModelData` at 64 / `Position` at 128 — two ~40-model armies crash at army creation. Either make stores growable (`DataReference`s stay valid; save format already records capacities) or raise defaults (512/1024) and add a startup assert with a clear error. Small change either way.
- [ ] 062 — Shed the ECS costume on `GameDataStore` while keeping its real role (audit §2). The store earns its keep as the single replication/persistence surface (JSON change events feed network sync, save, and `TableState` observability) — do NOT remove it. Recommended simplifications: (a) key the type map by a stable string name instead of registration position, killing the "append only at the end" save/wire fragility (save files already store `FullName`s); (b) replace the per-type reflection-built `DataBindingJsonConverter<T>` instances with one non-generic converter that handles any `DataBinding<>` against the store; (c) if #061 chooses growable stores, swap the linear `Create` scan for a free-list; (d) delete dead weight: `_setValueCache`, the unused private parameterless ctor, `GameDataStoreBuilder.RegisterType`'s dead capacity normalization and unused `typeID` local. Until (a) lands, a unit test pinning the default type-map order (see #063) guards the positional contract.

### Test coverage (one item per audit section that notably lacks it)

- [ ] 063 — Data-store unit tests (audit §2/§12): capacity exhaustion, generation reuse after `Destroy`, `IsValid` reason codes (one was wrong and fixed in the audit), `CreateFromReference` rejection paths, and a test pinning the default type-map registration order so a reorder fails CI instead of corrupting saves.
- [ ] 064 — Stage-machine tests (audit §4/§12): `VictoryCalculationStage` (winner/tie tally is implemented but fully untested), `StartOfRoundExtraActionStage` reserve arrival, `DetermineMeleeWinnerStage`, and direct tests pinning `ParentStage` enter/exit/reconcile ordering before #083 refactors it.
- [ ] 065 — Networking tests (audit §6/§12): a loopback `FDGHost`+`FDGClient` fixture on an ephemeral port (connect → frame echo → disconnect → truncated-frame), one concurrent-send interleaving test (catches #086 deterministically), and lobby view-model protocol tests over a mock bus (join/chat/settings/launch — 600+ lines currently untested).
- [ ] 066 — AI resolver tests (audit §10/§12): 11 of 12 AI resolvers have zero coverage; the AI's contract is "always produce a legal answer". One happy-path test per resolver asserting the answer passes the corresponding validator (deployment in zone, move within cap, wound assignment complete), plus pinning `AiUnitClassifier` scoring.
- [ ] 067 — Save/load content-parser tests (audit §9/§12): `ArmyListParser`, `TerrainLayoutLoader`, `SpecialRuleRegistry` error paths (malformed JSON, unknown rule alias, bad argument counts, negative quantities) — currently raw exceptions mid-lobby; convert to displayable errors and test them.

### Structure & migrations

- [ ] 068 — Split the engine test suite into its own project (audit §1): `Tests/` (9.5k lines) plus NUnit/Moq/Test SDK currently compile into and ship with the game library (a stray `using NUnit.Framework.Constraints;` even sat in `LobbyViewModel_Host`, removed 2026-06-10). Mechanical: new `FutureOfDarkGrimness.Tests` csproj referencing the engine; move `Tests/`; drop test packages from the engine csproj; update the parent build/test docs.
- [ ] 069 — Clean removal of the legacy `Special Rules/` system (audit §8). Nine-tenths corpse but still load-bearing in signatures: `IUnit.SpecialRules`/`IModel.SpecialRules`/`Weapon`'s rule set are typed against old interfaces and never populated (`GetRealSpecialRulesFromArmyList` returns empty with a TODO); `CombatStage` still gathers always-empty `ICombatEffect`s; old `Regeneration.cs` contains misleading real-looking logic against retired types; `RuleHookBus` is an acknowledged vestige. Work: delete `Special Rules/`, remove the dead properties + empty shims + `RuleHookBus`, fix compile fallout in engine and parent. **Coordinate with the active special-rules development branch — do this on/after that branch merges, not in parallel.**
- [ ] 070 — Save/load rename fragility (audit §9): `GameSaveSerializer.ResolveType` matches `Type.FullName`, so renaming/moving any registered type (or any `$type`-embedded payload type) silently breaks every existing save. Proposed solution: introduce stable string IDs for registered types (`"model"`, `"unit"`, …) via a name→type registry used by both the save fingerprint and (with #062a) the wire type map, keeping a `FullName`-based fallback path for loading older saves; bump `CurrentVersion` and decide the migration story (`IGameSaveMigrator` hook or a documented "saves don't survive versions" stance) before v2 exists.

### Networking robustness (second batch, added 2026-06-10)

- [ ] 073 — **(HIGH PRIORITY)** Bus dispatch hardening (audit §6): message-bus dispatch has no exception isolation — one throwing handler propagates into `FDGHost.HandleClientAsync`'s catch-all and disconnects that client. Wrap each handler invocation in try/catch with logging. Also split the silent-discard behavior in `MessageSerializer.DeserializeMessage`: keep tolerating *deliberately ignored* types, but log unknown types visibly (once per type, via `ITextOutput`, not `Debug.WriteLine`) so version skew stops vanishing without a trace.
- [ ] 074 — **(HIGH PRIORITY)** Fix the `_lastMessageConnectionID` race in `MessageBusHost_Networked` (audit §6): each connected client has its own read loop, so with 2+ remote clients concurrent dispatches overwrite the ambient connection id and `GetCurrentMessageConnectionID` (used to answer `RequestAllDataMessage` and the lobby greeting) can return the *other* client's connection — misrouted full-state sync. Pass `ConnectionID` through the dispatch/registrar callback signature instead of ambient state.
- [ ] 075 — Version / type-map handshake on client join + enums-as-strings on the wire (audit §6): both ends currently assume identical builds (same store type map, message registry, enum ordinals). Host includes `(version, type-map hash)` in the greeting reply; client refuses on mismatch with a readable error. Serialize enums as strings (`StringEnumConverter`) so member reordering stops being a silent wire-format change.
- [ ] 076 — Disconnect lifecycle (audit §6/§10): a dropped client is currently just removed — its pending decision requests hang forever (no timeout on `RequestDecision`) and the game stalls. Fail a departed player's pending requests, surface "player disconnected" to the stage layer, and optionally offer AI takeover of the slot (the resolver architecture makes this nearly free).
- [ ] 077 — In-game chat: finish or remove (audit §13.20): clients send `NetworkPlayerSubmitChatMessage` post-launch but the host's handler registration is commented out (`LobbyViewModel_Host`), so in-game chat from network players vanishes (lobby chat works). Either re-register and relay through `LogAndChatMessageRelayer`, or delete the send path until the feature lands.
- [ ] 078 — `CommandProtocol` read hardening (audit §13.25): no upper bound on the payload-length prefix, so one corrupt frame can rent a near-`int.MaxValue` buffer. Clamp to a sane max (e.g. 16 MB) and throw `IOException`; add the missing `ConfigureAwait(false)` on the payload read.

### Cleanup & object model (second batch, added 2026-06-10)

- [ ] 080 — GameModel cleanup (audit §13.22–23): `FDGGame_AsLocal` creates an `OutstandingTaskLister`, discards it, then creates another (its own TODO admits it; `FDGGame_AsClient` has the same shape) — create once and pass through. Remove `FDGServer.TEST_SINGLE_TURN` (compile-time debug flag routing to a private test harness) or promote it to a `GameSettings` debug option.
- [ ] 081 — `ModelData` visuals & `UnitData.Models` allocation (audit §3): `ModelData.MeshProvider`/`MaterialProvider` construct new provider objects on every property access — move visual lookup behind an app-side resolver keyed by `ModelID` (engine shouldn't own mesh selection), or at minimum cache the providers. `UnitData.Models` materializes a new `List<IModel>` per access (`RemainingWounds` makes wound math O(models²) with allocations) — expose the bindings or cache with invalidation.
- [ ] 082 — AI & player-controller lifecycle nits (audit §10): `AiYesNoResolver` answers `true` to every yes/no in the game, current and future — route yes/no requests through an intent tag so AI defaults are explicit per question. `LocalPlayerController`'s two-phase UI subscription has a race window (null-check then deferred event subscription) — make subscription idempotent and re-run on assignment. `NetworkPlayerController.IsReady` is set from bus dispatch without idempotency — duplicate ready messages double-fire `OnReadyStateChanged`.

---

## Awaiting verification

Implemented and merged to master; engine test suite green. Held open only until the behavior is confirmed by hand in the running app — tick and move to `## Done` once verified.

- [ ] 048 — Block deployment of models into impassible terrain: shared `PlacementUtilities.OverlapsImpassibleTerrain` (base-radius disc-vs-zone, built on #050's swept-disc overload) wired into the AI, CLI, and GUI place resolvers. Engine + AI + CLI unit-tested (`AiPlaceObjectsResolverTests.DoesNotPlaceModelsOnImpassibleTerrain`); GUI block (red ghost + click toast) needs a hand-eyeball. ([WorkItems/048](WorkItems/048-deployment-into-impassible.md))
- [ ] 041 — Factor line of sight into movement resolver's ranged-targeting overlay: both the per-enemy-unit weapon list and the per-model fire lines now require LoS (terrain + model-base blockers), with a red block-stub when no model in the unit is visible. ([WorkItems/041](WorkItems/041-movement-resolver-ranged-los.md)) — commit `ec2f552`
- [ ] 045 — Cover indication in targeting overlay and shot UI: fire lines through cover render dashed yellow; shot picker spells out "Cover (+1 Def)". Presentation-only, no engine change. ([WorkItems/045](WorkItems/045-cover-indication.md)) — commit `cc341b0`
- [ ] 046 — `GetFirstBlockingHit` engine API: returns the closest `Blocking` terrain entry point along an (attacker, target) segment so overlays can draw a stub + marker; `IZone.GetFirstSegmentEntry` on circle/rect. 6 new `LineOfSightTests` cases, suite 135/135. ([WorkItems/046](WorkItems/046-los-first-blocking-hit.md)) — commit `d9e60fb`
- [ ] 027 — Weapon-scoped special rules: engine-complete 2026-06-11 (branch `027-weapon-special-rules`, both repos; suite 396/0, headless-verified). Weapons carry #042 `ResolvedRule`s resolved from `WeaponFileEntry.SpecialRules` at army load with `SpecialRuleDefinition.Scope` enforcement (misattached rules warn + skip); dispatch is per-weapon through the fire pipeline + defender melee weapons (Counter) + `SightRuleQueries`; legacy `ISpecialRule_Weapon` deleted. **JSON loader / army creator no longer gated.** Verify in GUI: per-weapon rule labels in the shot picker / movement targeting overlay (the test army's Heavy Rifle carries Surge + Blast(3), Fists carry Counter, Infiltrators' Rifle carries Takedown — labels should show on those weapons only), and a melee charge into Heavy Gunners should show Counter striking first. ([WorkItems/027](WorkItems/027-weapon-special-rules.md))
- [ ] 005 — Scout deployment: set aside during normal deployment, then placed after all others within 12" of the zone (forward deploy). Built on the #042 deploy defer/reserve primitive (`PlaceDeferredUnitsStage` + forward-expanded zone). Headless-verified, unit-tested; try in GUI via a `.fdgarmy` with the Scout rule. (#042 INTEGRATION PROGRESS cont. 13.)
- [ ] 004 — Ambush deployment: kept in reserve, brought on at the owner's choice from round 2+ and placed anywhere >9" from enemies. Built on the same primitive (`StartOfRoundExtraActionStage` arrival + `PlaceObjectsRequest.MinDistanceFromEnemiesInches` honored by CLI/GUI/AI place resolvers; reserves excluded from activation/targeting via `IUnit.GetIsOnBattlefield`). Headless-verified, unit-tested. DEFERRED: "can't seize the round it arrives" objective nuance. (#042 INTEGRATION PROGRESS cont. 14.)

---

## Done

- [x] 079 — csproj dependency cleanup: dropped `System.Drawing.Common` (Windows-only GDI+; only `System.Drawing.Color` was used and it ships in the base-framework `System.Drawing.Primitives`) and deleted the dead duplicate ImageSharp comment. Engine suite 424/424, build clean, headless smoke exits 0. Submodule `2b14fd9`. ([WorkItems/079](WorkItems/079-csproj-dependency-cleanup.md))
- [x] 025 — `AssignWoundsResults.AutoFill` bug — **stale, closed 2026-06-13**: verified the described `modelWoundsRemaining always 0` bug no longer exists. `AutoFill()` was rewritten to fill via `TryAddWounds` and throws if it cannot place every wound (`AssignWoundsResults.cs:96`); the AI wound resolver relies on it and the suite is green (416/0). Remaining wound-assignment gaps stay tracked as #023/#024.
- [x] 001 — D3+2 objective placement: interactive alternating-team placement w/ validator + AI strategy + debug auto-place toggle ([WorkItems/001](WorkItems/001-objective-placement.md))
- [x] 012 — Decouple Advance / Rush / Charge distances: engine splits `MaxRushDistance` from `MaxChargeDistance`, `PathTemplate` carries all three explicitly (no more hardcoded half), `ValidatePaths` gains a "beyond Rush ⇒ a model must end in melee" check + Pass-gating; GUI movement resolver gains a three-band (Advance/Rush/Charge) ring preview + Done gating. Tests added (`ChargeReachValidationTests`, `ChooseActionPassDisableTests`). Engine `a967fa1`, GUI `3a6f189`.
- [x] 013 — Weapon-group target selection (up to 2 targets per shoot action): already implemented via `GameWideConstants.MAX_TARGETED_UNITS_PER_SHOOT_ACTION` + `attackedDefenderRefs` tracking in `ChooseRangedAttackStage`; item was stale
- [x] 014 — `RangedContext` NIE paths: file was dead code (entire body in a `/* ... */` block); actual ranged flow uses `CombatActionContext`. File deleted.
- [x] 018 — Pile In move: defender models not already in BTB step up to 3" toward nearest charging model, with impassible-terrain and strict coherency fallbacks ([WorkItems/018](WorkItems/018-pile-in.md))
- [x] 043 — Filter dead models out of `IUnit.AllWeapons` so dead models no longer contribute weapons to attack/strike-back/shoot lists or the tooltip readout
- [x] 019 — Consolidation moves after melee resolution: 3" Wipeout / 1" Disengage with per-model GUI path-builder, AI resolver, table-bounds clamp, and validation against terrain + cohesion + cap ([WorkItems/019](WorkItems/019-consolidation-moves.md))
- [x] 044 — Allied/same-team models don't block line of sight: `LineOfSightUtilities.BuildModelBlockers` now excludes every model on the attacker's team (via `tableState.Teams`) plus the defender unit's models, falling back to attacker-player-only when no team is registered. New `ModelBlockerTests` cases (ally exclusion, third-party enemy still blocks). ([WorkItems/044](WorkItems/044-los-ally-exclusion.md)) — commit `8701abf`
- [x] 002 — Terrain placement workflow: three-mode lobby setting (AutoFromLayout / Alternating / LoadFromFile), AI + human + CLI resolvers, `CompositeZone` for L-shapes, `RotatedZoneWrapper` + SAT for 45° rotation, GUI thumbnails + R-key rotate. (Includes terrain rotation, formerly tracked separately as #045.) ([WorkItems/002](WorkItems/002-terrain-placement.md))
- [x] 047 — Deployment zone selection: draw labelled zones on the canvas, allow clicking zones directly, synchronise hover between dialog and table, and renumber in reading order ([WorkItems/047](WorkItems/047-deployment-zone-labels.md))
