# 238 — Attack animation overlaps the to-hit dice + per-volley shot sound

**Status**: implemented (awaiting GUI hand-verification)
**Related**: #056 (beat stream — this ships its deferred "simultaneous beats", scoped to attacks), #053 (sound), #232/#222 (same pacing-feedback family)

## Goal

Playtest feedback (2026-07-16): combat wastes time playing the shot/swing animation and THEN the
to-hit dice. Play them simultaneously. Also: the gunshot sounds once per shoot, even when several
volleys visibly fire — sound each volley.

## Notes

- 2026-07-16: Both landed. The #056-deferred "simultaneous beats" refactor turned out to need no
  new machinery: the held-beat seam (built for lingering save dice) already lets a beat pace only
  its lead-in while the front-end keeps displaying it.
  - **Engine**: `AttackBeat` overrides `Held => true`, `HoldLeadIn => TimeSpan.Zero` — presenters
    emit it with zero pacing and immediately proceed to the `DiceRolledBeat` that always follows
    (RollToHitStage's order, both ranged and melee). The attack's duration (`ForVolleys`, capped
    1600ms) always fits inside the dice envelope (1800ms), so total combat pacing shrinks by the
    attack's former duration. New `AttackBeatOverlapTests` (2) pin the declaration + presenter
    pacing.
  - **App** (`PresentationPlayer`): AttackBeats never occupy the active slot — the dequeue loop
    transfers them to a dedicated concurrent track (`_activeAttack` + own elapsed clock) and keeps
    dequeuing, so the dice start the same frame. The track advances every frame, feeds
    `TryGetActiveAttack` unchanged, counts into `IsAnimating`, keeps parked dice alive, and owns
    the melee hit-stop now. `VolleysStarted(t, volleyCount)` is the timing seam.
  - **Sound**: new `PresentationPlayer.AttackVolleyStarted` event fires once per volley slice
    (matching `AttackOverlay`'s per-volley animation windows); renderer plays
    `PresentationSoundCues.VolleyCue` (gunshot/melee) on it. `CueFor(AttackBeat)` is now null so
    the old start-of-beat cue doesn't double the first volley. New `AttackVolleySoundTests` (3).
- Verified: engine 1649 green, app 356 green, headless smoke exit 0.

## Decisions

- **Engine change authorized by user (2026-07-16)** over an app-only fallback (impossible: the
  host paces between beats, so the dice beat doesn't even exist app-side until the attack's wait
  ends).
- **Per-volley sound replaces the single start-of-beat sound** (not in addition) — one cue per
  visible burst, ranged and melee alike.
- **Melee hit-stop stays once per attack beat** (at the first clash), not per volley — unchanged
  behavior, just relocated to the concurrent track.

**Verify by hand:**
- Shoot: tracers fly WHILE the to-hit dice tumble; one gunshot crack per volley (an A3 weapon =
  three cracks); save flow unchanged after.
- Melee: swings overlap the dice the same way; one clang per swing volley; hit-stop still lands.
- Prompts stay gated until both dice and tracers finish (no early resolver pop-in).
