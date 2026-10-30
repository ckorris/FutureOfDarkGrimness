# 454 — Engine: playback (ReplayReader, ReplayTimeline, ReplayPlayer, StoreDeltaApplier, steps, describer)

**Status**: in progress (2026-10-30)
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
- (still to record: the seek timing)
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
(open)
