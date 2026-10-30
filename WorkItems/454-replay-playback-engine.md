# 454 — Engine: playback (ReplayReader, ReplayTimeline, ReplayPlayer, StoreDeltaApplier, steps, describer)

**Status**: done (2026-10-30)
**Related**: plan `docs/replay-plan.md` sections 4.3, 4.4; depends on #450; consumed by #455 (CLI) and #456 (GUI)

## Goal
Given a replay container, the engine can list its decision steps with human-readable summaries and
materialise the game state at any step, step forward in place, step back by re-seeking, and hand the
caller the beats and log lines that belong to each step - with no renderer, no resolvers and no dice
involved. Done when the tests below pass, including "play back every step of a real recorded game and
the final store equals the live final store", and `ReplayPlayer` is usable from a plain console program.

## Scope (`FutureOfDarkGrimness/Replay/`)
- `ReplayReader` (from #450) grows: streaming read of the gzip JSONL into `ReplayDocument { Header,
  IReadOnlyList<ReplayEvent> Events }`; `BuildVersion` mismatch -> `Warnings` list on the document, not
  an exception.
- `StoreDeltaApplier` (engine, `Data/` or `Replay/`): `Apply(IReadWriteableGameDataStore, StoreAddEvent |
  StoreUpdateEvent | StoreRemoveEvent)`. Extract the three apply bodies from `GameDataUpdateReceiver`
  (`CreateFromReferenceAndJson`, the update path, the remove path) so client sync and playback share
  one implementation; `GameDataUpdateReceiver` becomes a thin adapter. Existing
  `NetworkedFullStateSyncTests` must stay green unchanged.
- `ReplayTimeline(ReplayDocument)`: indexes `Snapshots` (by `Seq`), `Markers`, `Requests` joined to
  `Replies`/`RequestErrors` by `TaskId`, and `Steps` via `IStepFilter` (`DefaultStepFilter`: a step is a
  `Request` with a `Reply`; folded when the request's option list has <= 1 entries - detect by
  deserialising the request JSON for the known option-bearing types (`SelectionRequest<T>`,
  `StringSelectionRequest`, `ChooseUnitToActivateRequest`, `ChooseActionRequest`, ...) or, cheaper and
  version-proof, by a regex on the recorded JSON for `"Options":[` and counting top-level elements -
  pick one and record why; `RequestError`s fold). Each `ReplayStep` carries `Index, RequestSeq,
  ReplySeq, Round, Activation, PlayerId, Controller, RequestType, DisplayName, Summary, Folded[]` (the
  folded requests inside its span). `Round`/`Activation` come from the latest preceding markers.
- `ReplayPlayer(ReplayTimeline)`: `Store`, `TableState`, `CurrentStep`, `OnStoreReplaced` event,
  `SeekToStep(int) -> PlaybackSpan`, `StepForward() -> PlaybackSpan?`, `StepBack() -> PlaybackSpan?`,
  `SeekToEnd()`. Seek rule (plan 4.3): the last `Snapshot` with `Seq <= step.RequestSeq` not separated
  from the step by a `Resumed` marker; `GameSaveSerializer.Load` into a fresh store (replacing
  `Store`/`TableState`, firing `OnStoreReplaced`), then apply `Store*` events in `(snapshot.Seq,
  step.RequestSeq]`. `StepForward` applies `(cursor, next.RequestSeq]` in place. "Span" = the events
  applied, exposing `Beats` (deserialized `PresentationBeat`s), `LogLines`, `Markers`. The state shown
  AT a step is the state the decision was asked in (before its effects); the effects play in the next
  step's span. Say this in the class doc; #456's UI relies on it.
- `ReplayDecisionDescriber.Describe(ReplayStep, GameDataStore storeAtStep) -> string` (ASCII only): per
  type - `ChooseUnitToActivateRequest` "P1 activates Warriors (5)", `ChooseActionRequest` "Warriors:
  Shoot", `ChooseRangedAttackRequest` "Warriors shoot Heavy Gunners (3 weapons)", `DefineMovementPathRequest`
  "Warriors move 5.2in", `ChooseMeleeDefenderRequest` "Warriors charge Grunts", `AssignWoundsRequest`
  "3 wounds -> Grunts: 2 models", `YesNoRequest` "<DisplayName>: Yes", `StringSelectionRequest`
  "<DisplayName>: <chosen>", fallback `"<DisplayName>"`. Deserialize replies with the wire settings
  against `storeAtStep` (references resolve because the playback store has the same references). Never
  throw: a describer failure yields the fallback.
- Performance target: `SeekToStep` anywhere in a 2k game < 250 ms on the dev box (one `Load` of a
  400 KB save + <= ~15 activations of deltas). Measure once and note it.

## Tests (`Tests/ReplayPlaybackTests.cs`, `Tests/ReplayTimelineTests.cs`, `Tests/StoreDeltaApplierTests.cs`)
- `StoreDeltaApplier`: add/update/remove against a fresh store equals the source store's JSON for that
  reference; `GameDataUpdateReceiver` still passes its existing tests.
- Timeline: hand-built documents - a request without a reply is not a step; a one-option
  `StringSelectionRequest` folds into the previous step; `Round`/`Activation` attribution across
  markers; a `Resumed` marker splits snapshot eligibility.
- Player, scripted: record (via #450's recorder on a test store) snapshot -> deltas -> request/reply ->
  deltas -> snapshot -> deltas; assert state at each step via `GameSaveSerializer.Save` equality with
  independently built expected stores; `StepBack` re-seeks and fires `OnStoreReplaced`; `StepForward`
  does not fire it.
- Player, real: record an in-process game (`DeterminismTests` assembly + recorder), then `SeekToStep(0)`
  and `StepForward` to the end; final `Save` == live final `Save`. Then `SeekToStep(k)` for every k from
  a fresh player and compare to the forward-walk state at k (seek == walk).
- Describer: one assertion per handled request type on recorded JSON fixtures, plus the fallback on a
  garbage reply.

## Verification
Engine suite green; `dotnet build`. No app change in this ticket.

## Notes
- 2026-10-30 - Slice 4 (describer) and close-out: `Replay/ReplayDecisionDescriber.cs` -
  `Describe(ReplayStep | ReplayRequest, IReadableGameDataStore storeAtStep, ReplayHeader?)`, never throws.
  Lines: activate "P1 activates Warriors (5)" (living models), action "Warriors: Shoot", ranged "Warriors
  shoot Grunts with Rifle" / "hold fire with" / ": Back", movement "Warriors move 6.0in" (longest model path
  from where it stood) / "stay put" / ": Back", melee target "Warriors charge Grunts", wounds "3 wounds ->
  Grunts: 2 models", yes/no, string selection, ability effect, any named `SelectionRequest` /
  `CancellableSelectionRequest` pick, objective placement "... at (68, 25.5)"; anything else = DisplayName.
  `ReplayPlayer`'s constructor describes every step in one forward walk (beats not read; skipped when the
  timeline is already described). The step filter now also counts `Options` (`ChooseAbilityEffectRequest`)
  and `AvailableZones` (`ChooseDeploymentZoneRequest`). New internal `ReplayText.Ascii` (importer fold, then
  '?') shared by the describer, the reader's warning and the recorder's failure line.
  `Tests/ReplayDecisionDescriberTests.cs` (9); the real-game playback test now also checks every summary is
  ASCII and most are not the fallback. Engine 3447 passed / 1 skipped; build clean.
  On the 2k game every one of the 312 summaries is ASCII and reads right (sample: "P1 activates Battle Tank
  (1)", "Battle Tank move 8.0in", "Battle Tank shoot Orc Warriors with Rapid Autocannon", "2 wounds -> Orc
  Warriors: 2 models", "Orc Warriors charge Destroyers"); the fallback is left for deployment placement
  ("Deploying Orc Warriors"), consolidation and the zone pick, whose display names already say it. Player
  construction 770 ms cold / 295 ms warm (Debug). Verified from a plain console program (throwaway project,
  `~/Projects/fdg-lab-scratch/replay-454/console-probe/`, engine reference only): read, describe, seek to
  step 150, seek to end == live final save.
- 2026-10-30 - Slice 3 (`ReplayPlayer`): `Replay/ReplayPlayer.cs` - `Store`, `TableState`, `CurrentStep`,
  `IsAtEnd`, `OnStoreReplaced`, `SeekToStep`, `StepForward`, `StepBack`, `SeekToEnd`; `PlaybackSpan`
  (FromSeq exclusive, ToSeq inclusive, Events, Beats, LogLines, Markers, StoreReplaced, Problems);
  `ReplayPlaybackException`. `GameSaveSerializer.Load(json, stampLegacyReserves: false)` (internal) is the
  playback load. Tests `Tests/ReplayPlaybackTests.cs` (4): scripted recording through the real recorder
  (state at every step == a save of the live store taken when that decision was asked; forward never
  replaces the store, back always does; an add into a slot recycled before the round's snapshot);
  no-snapshot refusal and no-step start; a real in-process game - final played-back state == live final
  state, seek == walk at every step; every snapshot of a real game re-saves to itself under the playback
  load, and NOT under the save path's load (the legacy stamp - see Decisions). `ReplayRecorderGameTests`'
  harness is now internal and captures the live final save at completion. Engine 3438 passed / 1 skipped;
  build clean.
  Measured on the plan's 2k game (BB vs Orks, seed 42; FdgLab exported it through a temporary env-gated
  patch, archived at `~/Projects/fdg-lab-scratch/replay-454/`, reverted): 4278 events, 349 requests -> 312
  steps + 37 folded, 5 snapshots; played back from snapshot 0 to the end it EQUALS the live final save.
  Debug build: read 371 ms, index 110 ms, player construction 363 ms (all first-call, JIT included), walk
  all 312 steps forward 375 ms; `SeekToStep` median 29 ms, p95 39 ms, max 49 ms; one 394 KB snapshot
  load 19 ms.
- 2026-10-30 - Slice 2 (reader warnings, timeline, step filter): `ReplayDocument.Warnings` (a `BuildVersion`
  mismatch is a warning naming both builds, never an error; dev builds all stamp `-dev`). The reader was
  already a line-at-a-time read off the gzip stream, so "streaming" needed no change. `Replay/ReplayTimeline.cs`:
  `ReplayTimeline` (snapshots, markers, requests joined to replies/errors by TaskId, steps, `IndexOfSeq`,
  `SnapshotFor(seq)` = the plan 4.3 seek rule), `ReplayRequest` (with the Round / Activation / activating
  unit open when it was asked), `ReplayStep` (Index, Decision, Folded, Controller, Summary; RequestSeq,
  ReplySeq, Round, Activation, PlayerId, RequestType, DisplayName derived), `IStepFilter` /
  `DefaultStepFilter`. Refuses out-of-order Seq and a task requested twice; ignores a reply to a task it
  never saw requested. `Tests/ReplayTimelineTests.cs` (7). Engine 3434 passed / 1 skipped; build clean.
  On a 2-unit test game, every "Choosing a Unit to Activate" folds (one unit left to pick) and every
  "Choosing an Action" is a step (Move/Pass at least).
- 2026-10-30 - Slice 1 (`StoreDeltaApplier`): `Data/StoreDeltaApplier.cs` - `ApplyAdd` / `ApplyUpdate` /
  `ApplyRemove` plus `Apply(store, ReplayEvent)` (true for the three Store* kinds, false otherwise), with an
  `EDeltaGenerationPolicy` (`Sequential` for the live client stream, `Adopt` for playback - see Decisions).
  `GameDataUpdateReceiver` is now a thin adapter over it; `NetworkedFullStateSyncTests` unchanged and green.
  Found on the way and fixed: a mirrored `UnitData` / `ModelData` update arrives as a new deserialized
  instance with only the rule blob, so on a networked client a unit's rules vanished (stat block,
  tooltips) after its first token change - and would have in playback. Both classes now rehydrate in
  `[OnDeserialized]`, as `Weapon` already does (#325). Verified the new test fails without the hooks.
  `Tests/StoreDeltaApplierTests.cs` (4). Engine 3427 passed / 1 skipped; build clean; headless smoke exit 0.

## Decisions
- **The describer reads JSON tokens, not wire-deserialized objects** (the ticket suggested deserializing
  replies with the wire settings). Same reasons as the step filter: no `$type` from an untrusted file is
  resolved, a renamed field costs a fallback line instead of an exception, and no generic CLR reply types
  need constructing. The store at the step still supplies names (units by reference or `UnitID`, model
  positions for path length), which is why the player, not the timeline, fills `Summary`.
- **Wording choices.** Players are "P<slot+1>" (short, and slot names can be long); a ranged pick names its
  ONE weapon (the request is per weapon - the ticket's "(3 weapons)" does not fit the reply); a null ranged
  target is #319's hold fire; the melee attacker comes from the activation marker's unit (the request has
  no attacker field); wounds are the packets' wounds after ignores, and models are those a commit LANDED
  on; whole numbers print without a decimal (probabilistic dice give fractions).
- **Seek timing: met with room to spare, no cache.** Worst `SeekToStep` on a 2k game is 49 ms in a Debug
  build against the 250 ms target (one snapshot load ~19 ms plus at most a round of deltas), so the player
  keeps no per-step cache or extra snapshots; a step back is simply a re-seek.
- **Playback loads snapshots exactly** (`stampLegacyReserves: false`). The save path's load re-derives
  InReserve for "every model at the origin", which is every unit in snapshot 0 (taken before deployment):
  each would show as in reserve until its next recorded update. Pinned: under the normal load snapshot 0
  does NOT re-save to itself; under the playback load every snapshot does.
- **Beats are read at their own place in the stream**, against the store as it stood then (a beat's
  bindings must resolve, and a later remove in the same span would break them). One that cannot be read is
  a `PlaybackSpan.Problems` line, not an exception; a delta that cannot be applied IS a
  `ReplayPlaybackException` (the state would be wrong), and the next move rebuilds from a snapshot.
- **Forward from the last step goes to the end of the recording** (`IsAtEnd`, `CurrentStep` null), so the
  last decision's effects and the game's end can be watched; `StepForward` at the end returns null.
- **Option count: parse the request JSON as a plain token tree** (`JObject`), not a regex and not a bind to
  the CLR type. A regex for `"Options":[` would have missed every real list: the wire writes interface-typed
  lists as `{"$type":...,"$values":[...]}` wrappers, and the field is `ValidOptions`. Binding to the type
  would mean resolving `$type` from an untrusted file and needs a store for the DataBinding converters
  (the timeline has none). The rule: a request with a top-level `ValidOptions` list offers its entries plus
  one for Back - when it says `AllowCancel` or its reply type is a `CancellableResult` (the
  `CancellableSelectionRequest` family always offers Back); it folds at <= 1. No `ValidOptions` (yes/no,
  movement, wounds, placement) = always a step. Back counts because choosing NOT to back out of a lone
  charge target is still a decision the player made.
- **Folded requests belong to the step before them** (`ReplayStep.Folded` = the requests after its decision,
  up to the next step): they are that decision's consequences, and they play in the span that arrives at
  the next step. Folded requests before the first step are `Timeline.FoldedBeforeFirstStep`.
- **Summary lives on the step but is filled by the player.** The timeline is store-free (plan 4.3) and the
  describer needs the store at the step for names, so `ReplayStep.Summary` reads as the DisplayName until a
  `ReplayPlayer` describes every step in its constructor (one forward walk).
- **Generation policy for adds.** A snapshot (any whole-store save) carries no generation for an EMPTY
  slot, so after loading one, an add that refills a slot the game recycled BEFORE the snapshot carries
  generation g+1 against a rebuilt slot at 0 - the live stream's "exactly one ahead" check throws
  FutureGeneration (pinned by a test). Playback therefore applies adds with `Adopt` (the #270 snapshot
  rule: adopt the recorded generation; index, type and slot-free checks still apply); the client keeps
  `Sequential`, which is a real missed-message guard there. Not fixed here: a client that joins via a
  mid-game catch-up has the same latent exposure - it is a client-sync question, noted for #453 (resume
  with clients) rather than changed under it.
- **Rules restored at deserialization** (`UnitData` / `ModelData` `[OnDeserialized]`), not by a sweep in the
  applier: it is the #325 pattern, it fixes the client too, and every other deserialization path gets it
  for free (`StoreReplay`'s sweep becomes a no-op). Wound subscriptions are NOT rewired on a mirrored
  update (unchanged client behaviour); playback never raises wound events, so it does not need them.

## Outcome
Done 2026-10-30 on `feature/replay`. The engine plays a recorded game back from its container with no
renderer, resolvers or dice: `ReplayReader` (build-stamp warnings), `ReplayTimeline` (steps per D4, folded
one-option picks, round/activation attribution, the snapshot seek rule), `ReplayPlayer` (seek / step forward
in place / step back by re-seek / seek to end, spans with beats, log lines and markers), `StoreDeltaApplier`
(now also the networked client's delta path) and `ReplayDecisionDescriber`. Proven on real games: played
back from snapshot 0, the state equals the live final save (in-process test game, and the plan's 2k BB vs
Orks game); seeking to any step equals walking to it; every snapshot reloads exactly. Worst seek on a 2k
game 49 ms (Debug) against the 250 ms target. Engine tests: 4 + 7 + 4 + 9 new (3447 total).

Found and fixed on the way: a mirrored `UnitData` / `ModelData` update lost the entity's rules (a
networked client's unit showed no rules after its first token change) - now rehydrated on deserialize;
and the save path's legacy reserve stamp would have shown every pre-deployment unit as in reserve in
playback.

Not done here, by design: the CLI (#455), the GUI viewer (#456), client recordings (#451), `Resumed`
markers and save embedding (#453) - the player already treats a `Resumed` marker as a hard boundary, but no
recording contains one until #453. Noted for #453: a client that joins through a mid-game catch-up shares
the empty-slot generation exposure the `Adopt` policy solves for playback.
