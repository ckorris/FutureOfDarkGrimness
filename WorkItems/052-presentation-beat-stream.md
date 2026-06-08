# 052 — Presentation beat stream (engine-owned pacing for lifelike play)

**Status**: in-progress
**Related**: engine submodule branch `052-presentation-beat-stream`

## Goal
The game currently teleports models, vanishes the dead instantly, and surfaces "what happened" only as flat text logs. Make play feel lifelike: moving models glide, getting shot plays projectile → save/hurt → death beats with real pacing, dice tumble, and stage/decision changes flash. "Done" for the *architecture* slice: the engine owns a **presentation-beat stream** — an ordered sequence of semantic, paced beats emitted inline from stages via `await context.Present(beat)` — and the Raylib app consumes it as animations while headless degrades it to text. Any future app on the same submodule would reuse the beat stream and its pacing one-to-one; only the rendering differs.

## Core architecture (decided 2026-06-07/08)

**Ownership principle (user's):** anything a brand-new app on this submodule would re-derive identically belongs in the engine; only the literal pixels belong in the app.

**Engine owns:**
- The **beat stream**: ordered, semantic beats (`UnitMoved`, `ProjectileFired`, `SaveMade`, `WoundDealt`, `ModelDied`, `DiceRolled`, `StageEntered`, …), each carrying its render payload **and a nominal duration**.
- **Pacing**, via `await context.Present(beat)` — emits the beat to all consumers, then awaits its nominal duration on a wall clock. **Not** an ack handshake with the renderer; the engine never waits on the consumer, it sleeps a duration *it* owns.
- Replication of the stream host→clients (it's canonical engine output).

**App owns:**
- The retained **visual model** / per-model actor state machines (idle/flinch-saved/flinch-hurt/dying/dead), tweening, dice meshes, banner layout — the stuff a 3D client would reimplement differently.
- A **delta-time tween/timeline** system (none exists today; `GetFrameTime()` is unused).
- **Cosmetic sub-timing within the engine's duration envelope** — engine says "ranged volley, these outcomes, ~1500ms"; app distributes tracers/sparks/death inside that 1500ms however it likes.

## Decisions
- **Free-running, not blocking.** The sim is paced on a wall clock it owns, never gated on the renderer. This is what avoids the "awful pause" *and* the Civ-style catch-up artifact: a real-time producer leaves no backlog to replay in slow motion. (Rejected: engine dumps beats into a queue that the app drains at nominal speed — that *is* the Civ model.)
- **Inline emission, not a decorator/observer.** Under the ownership principle an outside observer can't own pacing and can't see mid-resolution state (only aggregate histograms / per-model wounds), so emission must be inline in stages — shaped like the existing `context.Log()` calls. The decorator idea dissolves under the principle. Cost: combat stage files gain `Present()` calls (additive, narration-like).
- **Engine emits *semantic* beats with *total* duration; app sub-divides cosmetic timing.** Keeps the engine from having to model individual tracers while still owning the pacing envelope.
- **Multiplayer = synchronized / host-authoritative.** Falls out for free: the beat stream is canonical engine output, so it replicates; the host paces by sleeping, clients receive in real time → same sequence, same tempo, same real dice values everywhere. Likely **subsumes the existing `AddTempVisualMessage` channel** — decide whether to formalize or retire it.
- **Headless degrade = duration scale 0×.** Keep *state computation* instant always; only *emission* sleeps, and that sleep is injectable so tests/automation stay fast and rules logic stays deterministic (no real-time dependency in the rules).
- **Logs and beats are siblings, not parent/child.** Existing `Log()` calls keep firing unchanged. A beat *may optionally* carry a text projection for CLI/unified-feed convenience, but logs are not forced through beats nor vice-versa. (User correction to an earlier "log is just a projection of the stream" idea.)
- **Dice beat carries histogram + side count + threshold + roller mode — not "dice".** `IDiceResults` is a per-face *float* histogram. `RealisticDiceRoller` → integer face counts → render N tumbling dice on those faces, highlight ≥ threshold. `ProbabilisticDiceRoller` → `rollCount/sideCount` per face → no discrete dice exist; render a different vocabulary (probability bar / fractional / expected-value). Mode rides on the beat (don't infer from integrality — probabilistic counts can be whole by coincidence).
- **Probabilistic mode is fractional end-to-end.** Hits, saves, *wounds*, and *deaths* are fractional (a model dies when fractional accumulated wounds cross its total). `WoundDealt`/`ModelDied` beats, health/damage representation, and the app's whole vocabulary must tolerate fractions in this mode — not just the dice widget. App keeps two presentation vocabularies keyed off the mode flag.

## Relevant existing-code facts (from Apr/Jun 2026 survey)
- Rendering is pure immediate-mode off *final* state (`RaylibRenderer.DrawModels` reads `model.Position` live each frame); dead models are `GetIsAlive()`-skipped and the engine never removes them → app already has full control of death visuals.
- `OnPositionChanged` / `OnWoundsDealt` are `DataValueChangedHandler<T>` → carry **(old, new)**; the deltas to animate exist at the instant of change but nothing captures them.
- Combat resolves as one synchronous aggregate burst; results are histograms, no per-shot target identity. **`AssignWoundsResults.PendingWounds` is per-model**, so which model took/lost how many is recoverable; per-shot→per-model mapping for saves must be synthesized app-side.
- Only blocking engine→app backchannel today is the resolver request/reply (a TCS the engine awaits). Logs + data-sync + an existing `TempVisual` channel are fire-and-forget.

## Testing stance
Solid unit tests required throughout (user ask). Key targets:
- `Present()` pacing: with duration scale 0× emission is instant and ordering preserved; with non-zero scale total elapsed ≈ sum of nominal durations (use an injectable clock, not real `Task.Delay`, so tests are deterministic and fast).
- Beat *ordering & payload* per stage: a scripted shoot/melee resolution emits the expected beat sequence with correct per-model wound/kill data and dice histograms.
- Dice beat fidelity in both roller modes (integer multiset vs balanced fractional); threshold/success counts correct.
- Replication: beats emitted host-side arrive client-side in order with identical payloads (incl. real dice values).
- Headless: beat stream present but instant; existing logs still fire unchanged.

## Notes
- 2026-06-08: **Contract types landed (engine).** New `FDG.Presentation` namespace: `IPresentationClock` (Scale + `Wait`), `PresentationBeat` (abstract: `NominalDuration` + optional `Text`), `IPresenter` (`Present`), `IPresentationSink` (`OnBeat`), `LocalPresenter` (emit-to-sink-then-await-clock), and `RealtimePresentationClock` / `InstantPresentationClock`. Test doubles in `Tests/Doubles/PresentationDoubles.cs` (`FakePresentationClock` records waits, `RecordingPresentationSink`, `TestBeat`). `Tests/PresentationContractTests.cs` — 9 tests green (emit-then-pace, ordering, null-sink still paces, instant/zero/negative-scale edges, text opt-in). Networked presenter + per-player fan-out deferred to the context-wiring step.
- 2026-06-08: **`ModelID` landed (engine).** `readonly record struct ModelID(Guid ID)` mirroring `UnitID`; `IModel.ID` + `ModelData.ID` (fresh GUID in both runtime ctors; `ModelID? id = null` tail param on the `[JsonConstructor]` so it survives JSON/network round-trips). `Tests/ModelIDTests.cs` mirrors `UnitIDTests` (assigned/unique/explicit-id/round-trip) — 4 green. Full suite 285→ green, full solution builds.
- 2026-06-08: Branch `052-presentation-beat-stream` created on both repos off master. Architecture above agreed in design discussion. Next: draft the `Present()` / beat contract as an interface sketch (engine submodule) before any rendering work — it's the seam everything hangs off. No implementation yet.
</content>
</invoke>
