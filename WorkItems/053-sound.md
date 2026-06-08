# 053 — Sound cues on the presentation beat stream

**Status**: done (pipeline + placeholder; real assets pending)
**Related**: builds on #052 (presentation beat stream)

## Goal
Make combat audible: play audio cues for presentation beats (gunshot, clang, death thud, dice
clatter, banner sting, …) riding the **existing** beat stream from #052. App-side, like the visual
overlays — no engine change expected (the engine already emits the semantic beats; the app maps
beat → sound, exactly as a 3D client would with its own sounds).

## Plan (design, agreed before starting)
- **Where it lives:** app (`FdgRaylib`). Sound *cues* are rendering, not domain — the engine owns the
  beats and their pacing (`NominalDuration`); the app decides what each sounds like. So no engine
  change; this parallels `DiceOverlay`/`AttackOverlay`/`SaveOverlay`.
- **Mechanism:** Raylib audio. `Raylib.InitAudioDevice()` in `RaylibRenderer.Run()` startup,
  `CloseAudioDevice()` on shutdown. `LoadSound` each cue once into a map. **Play on the main thread**
  when a beat becomes *active* (i.e. from `PresentationPlayer.Update` when a beat is dequeued, or a
  "beat started" hook the renderer consumes) — NOT in `OnBeat` (engine thread). Playing on beat-start
  keeps audio in sync with the visual that starts the same moment.
- **Beat → cue mapping (initial):** `AttackBeat` ranged → gunshot, melee → clang/whoosh;
  `DiceRolledBeat` → dice clatter; `SaveBeat` → deflection ping; `ModelWoundedBeat` → grunt/thud;
  `ModelDiedBeat` → death thud/boom; `BannerBeat` → sting; `UnitMovedBeat` → (optional) footsteps.
- **Multiplicity:** a volley `AttackBeat` could play one cue or N; start with one cue per beat.
- **Headless:** no audio — `CliApp` never inits the audio device and its sink is null; guard so audio
  is GUI-only.
- **Testing:** playback is GUI-only (untestable here, like the overlays). Keep the **beat → cue-key
  mapping a pure function** and unit-test that; the actual `PlaySound` is not tested.

## OPEN QUESTION — settle first
Sound **assets**. We have none yet. Options:
- (a) user supplies `.wav`/`.ogg` files (e.g. under `FdgRaylib/Assets/Sounds/`),
- (b) wire the whole pipeline against **one placeholder sound** for all cues to prove it end-to-end,
  with a clean mapping table so real assets drop in later,
- (c) generate simple synth tones.
Recommended: (b) — build the pipeline + mapping now with a placeholder, swap real assets in after.
Ask the user which before loading any files.

## Outcome (2026-06-08)
Pipeline built end-to-end, app-side, no engine change. All GUI-only; headless never inits audio.

Asset decision: **placeholder-first (b)** — a built-in in-memory tone covers every cue until real
`.wav` files land in `FdgRaylib/Assets/Sounds/` (drop-in by filename, no code change).

Shipped:
- `FdgRaylib/Audio/AudioManager.cs` — **general-purpose** (per user request: reusable for UI clicks,
  menu stings, etc., not just beats). Owns the Raylib audio device, caches sounds by string key,
  `Play(key)`, `Dispose()` (unloads all + closes device). No-ops entirely if no audio device
  (`Enabled` false). Missing files fall back to a generated placeholder (sine + exp decay, built as
  an in-memory WAV via `LoadWaveFromMemory`; needed `<AllowUnsafeBlocks>` for the pointer overload).
- `FdgRaylib/Rendering/Presentation/PresentationSoundCues.cs` — the only beat-aware sound code. Pure
  `CueFor(beat)` mapping (Attack→gunshot/melee, Dice, Save, Wound, Death, Banner, Move) + `LoadInto`
  that registers each cue from `Assets/Sounds/{key}.wav`.
- `PresentationPlayer.BeatStarted` — audio-agnostic `Action<PresentationBeat>` raised on the render
  thread (outside the lock) the frame a beat becomes active. Renderer wires it to `AudioManager.Play`
  so the cue fires in lockstep with the visual.
- `RaylibRenderer`: builds `AudioManager` + `LoadInto` at `Run()` startup; disposes before
  `CloseWindow()`; hooks `BeatStarted` in `TransitionToGame`.
- `FdgRaylib/Assets/Sounds/README.md` — cue→filename table for whoever supplies real assets.

Tests: none added — playback is GUI-only and the user opted out of app-level tests; the only pure bit
(`CueFor`) lives in the app. Engine suite still 314 green (no engine touch). Build clean.

Caveat: not yet heard on a machine with audio (user away from their box). One cue per beat (volleys
play a single shot sound for now). Real assets + per-volley layering are the obvious follow-ups.

## Notes
- 2026-06-08: Created as the next slice after #052's visuals. Resolved the asset question
  (placeholder-first) and built the full pipeline same day — see Outcome.
