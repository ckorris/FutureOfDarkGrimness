# 294 — Movement footsteps replace the single move beep

**Status**: in-progress (implemented + tested; awaiting GUI hand-verify)
**Related**: #053 (sound cues on the beat stream), #238/#239 (per-volley / per-impact attack cues — the
mechanism this reuses), #275 (banner tiers — the precedent for retiring a cue key outright)

## Goal
Every unit movement fired exactly one `move` blip at beat start. Replace it with a softer, repeating
footfall that plays *while* the models glide, denser for larger units (sub-linearly — a horde must not
mean ten times the beeps) and lower in tone for tougher models. The whole thing has to stay subtle:
the game should not get noisier than it already is.

Done looks like: a move beat produces a run of quiet footsteps across its glide, a Tough(12) monolith
audibly treads lower and slower than a 5-model infantry squad, and nothing else in the mix changed.

## Notes

- 2026-07-27: **Implemented.** Engine `UnitMovedBeat.Toughness` + app-side footfall stream, tests, docs.
  - Engine suite 2228/2228 green; app suite 649/649 green; headless smoke plays a full 4-round game to
    exit 0 (`Game result: outcome=Tie winner=none rounds=4`).
  - Owner signed off on both design forks up front (see Decisions): one `step` cue with per-play pitch
    (over three baked tier cues), and toughness carried on the beat (over an app-side table lookup).
  - **Awaiting GUI hand-verify** — everything below is audible-only and no test can hear it:
    1. Move a 5-model infantry squad a full Advance: a run of ~3 soft footfalls across the glide, not
       one blip. They should sit *under* the action, not announce it.
    2. Move a lone model the shortest possible distance: exactly one step (parity with the old blip).
    3. Move a Tough(12)+ vehicle/monolith: noticeably lower and slower than the infantry squad.
    4. Move a 10+ model horde: busier than the squad, but nowhere near ten times as many beeps.
    5. Listen for the left/right alternation — the patter should read as walking, not as a metronome.
    6. Sanity: no clipping when a move overlaps a held banner/toast or a casualty cascade.

## Decisions

- **One `step` cue, pitched per play — not three baked tier cues.** (Owner sign-off, 2026-07-27.)
  Toughness maps *continuously* to pitch instead of landing in three hard buckets, and one real
  `step.wav` dropped into `Assets/Sounds/` still tiers itself with no code change. Cost: a new
  `AudioManager.Play(key, pitch, volume)` overload (Raylib `SetSoundPitch`/`SetSoundVolume`).
  Pitch in Raylib is a playback-rate multiplier, so a lower pitch also plays *longer* — which is the
  heavier sound we wanted anyway, so the side effect is load-bearing, not tolerated.
- **Toughness rides the beat (`UnitMovedBeat.Toughness`), not an app-side table lookup.**
  (Owner sign-off, 2026-07-27.) The beat stream is the designed presentation channel: networked
  clients pitch footfalls identically for free, `PresentationSoundCues` stays a pure mapping, and
  `PresentationPlayer` gains no `ITableState` dependency. Read off the *moving models* in
  `ExecuteMoveStage` rather than off the unit, so a joined Tough hero sets the tone only while it is
  one of the models actually moving. Floored at 1 in the constructor — the pitch curve divides by
  `1 + k(tough-1)`, so a 0 or negative Tough would mistune it.
- **Cadence is sub-linear in the model count and spread by beat *progress*, not wall-clock.**
  `StepsStarted` mirrors `VolleysStarted`: the cadence decides how many steps the whole glide gets,
  then those are spread evenly across it, so the patter stays locked to the models crossing the table
  (and rides the hit-stop time scaling) instead of drifting against the animation.
  2.4 steps/s solo, +1.15/s per doubling of the unit, capped at 6/s: 20x the models is ~2.5x the
  steps. `MaxStepsPerMove = 9` is a backstop, not a knob — at the fastest cadence the longest move
  (`MoveMax`, 1500ms) works out to exactly 9.
- **The step recipe is deliberately smaller than the blip it replaced.** The retired `move` cue ran
  100ms at amp 0.20 and rose 300 -> 360Hz. A cue that fires up to nine times per move has to sit
  under the action, so `step` is ~63ms at amp 0.13 and *falls* (240 -> 190Hz) — a falling contour
  reads as weight settling, a rising one reads as a notification. A regression test pins it quieter
  and shorter than the old blip so a future tweak can't quietly turn the patter into an alarm.
- **Clamp the pitch floor LAST.** First cut applied the off-foot (odd-step) multiplier *after*
  clamping to `StepMinPitch`, which pushed an already-bottomed-out Tough(12)+ tread to 0.517 — below
  the floor that exists to stop a titan sounding like a dying engine. Caught by
  `StepVoice_StaysSubtle_AndAlwaysNamesTheOneStepCue`. The consequence of the fix is that the very
  heaviest models walk both feet at the floor (no alternation); that's the right trade — the
  alternation is a garnish, the floor is load-bearing.
- **`CueFor(UnitMovedBeat)` now returns null**, exactly like `AttackBeat` since #238: a start-of-beat
  cue would stack the old single blip on top of the first footfall. The `Move`/`"move"` cue key is
  gone rather than deprecated, following the #275 precedent when the single `banner` cue became three
  tiers.

## Deferred (explicitly, not silently)

- **Per-model footfalls.** All models in a unit share one step stream; the count only sets the
  cadence. Staggering a step per model would be more literal but is exactly the "tons of beeps"
  outcome the owner asked to avoid.
- **Terrain-flavoured footfalls** (mud/metal/rubble by the ground being crossed). The beat carries no
  terrain context and adding one is a bigger slice than this.
- **Charge/Rush vs Advance flavour.** `UnitMovedBeat` doesn't carry the action type today (the
  duration is derived from distance); `PresentationDurations` already flags per-action pacing as a
  future extension, and footfall cadence should follow it there rather than lead it.
- **Stale `banner.wav` row in `Assets/Sounds/README.md`** — left as found; it predates #275's split
  into three tiers and is not this item's to fix.

## Outcome
_Pending GUI hand-verify._
