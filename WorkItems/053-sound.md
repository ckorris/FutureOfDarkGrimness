# 053 — Sound cues on the presentation beat stream

**Status**: todo
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

## Notes
- 2026-06-08: Created as the next slice after #052's visuals. Not started — resume by settling the
  asset question above, then wiring the audio device + a `SoundCues`/`SoundPlayer` (mapping +
  playback) driven off `PresentationPlayer`.
