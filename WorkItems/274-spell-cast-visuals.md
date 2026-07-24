# 274 — Spell cast animations + sounds

**Status**: in-progress
**Related**: #033 (Caster), #103 (cast assist), #244 (self-boost), #233 (cast-roll dice beat), #056 (beat stream), #053/#239 (sound cues)

## Goal
Casting has no visual language of its own — the whole action reads as banners and a die. Give it one:
a cast-success and a cast-failure effect on the caster, a per-target landing effect with a beneficial
and a harmful variant, and an assist effect with a boost and a hinder variant for the token spends that
move the roll. Each variant gets its own sound. Done when a cast in the GUI reads as a sequence without
looking at the log, the beats round-trip to networked clients, and headless/CLI play is unaffected.

## Notes

- 2026-07-24: Built as one new beat type rather than six. `SpellEffectBeat(ESpellVisual, Positions,
  SpellName, Sources, Magnitude)` in `Presentation/Beats/`; the six variants are the same shape (an
  effect at a set of model positions, optionally fed by a set of source positions), so the front-end
  picks both the visual and the sound off `Visual`. Durations in `PresentationDurations`
  (SpellCast 700ms / SpellTarget 650ms / SpellAssist 600ms).
- 2026-07-24: Emission order in `CastSpellStage.Enter`: assist boost -> assist hinder (batched, right
  before the roll) -> the existing cast-roll `DiceRolledBeat` -> the existing result banner ->
  CastSuccess/CastFailure -> per-target Boon/Bane. `CollectCastAssist` now returns a `CastAssistResult`
  (net modifier + who spent on each side + how much) instead of a bare int; the roll still reads only
  `Net`, so no rules behaviour changed.
- 2026-07-24: Front end — `SpellOverlay` (world-space, drawn in the canvas pass beside `SaveOverlay`),
  a `_activeSpell` slot on `PresentationPlayer` (non-held, so the sequence stays strictly ordered), and
  six synthesized cues (`spell-cast`, `spell-fail`, `spell-boon`, `spell-bane`, `spell-boost`,
  `spell-hinder`) that drop-in replace from `Assets/Sounds/{key}.wav` like every other cue.
- 2026-07-24: Tests — 3 beat round-trip tests, 6 `CastSpellStage` emission/ordering tests + the
  disposition table (engine), 8 player/cue/stagger tests (app). Engine 2099 green, app 574 green,
  headless smoke exits 0.

## Decisions

- **One beat type, not six.** The variants differ only in look and sound, and a single type keeps the
  serializer, the player track and the overlay dispatch to one place each. `Magnitude` rides along so
  the front-end can scale an assist with the tokens spent without the engine deciding how.
- **Affinity decides beneficial vs harmful, effect only breaks the `Any` tie** (`SpellDisposition`).
  Effect-kind alone gets the corpus's most common hostile shape wrong — "enemy unit gets a bad rule"
  is an `AddRule`, which looks like a buff. Affinity is already authored on every spell. This is
  presentation-only: a wrong answer costs a mismatched colour, never legality.
- **Assists batched into at most two beats immediately before the roll** (one per direction), rather
  than one beat per assister at spend time. Chosen with the user 2026-07-24: tighter pacing, and a
  three-caster scrum resolves in two beats instead of three-plus.
- **The #244 self-boost joins the boost beat with no `Sources`.** It is a spend on the same side of the
  same roll, so it should not be silently invisible; with nothing to stream from, the overlay draws the
  caster pulse alone. Chosen with the user 2026-07-24 (their prompt said "another caster"; including
  self-boost was the recommended widening).
- **Cast outcome fires AFTER the result banner, not before it.** The stated constraint was that the
  target landing follows the caster's success immediately; putting the burst before the banner would
  wedge a 1300ms banner between the two halves of one sequence.
- **A placeless beat is dropped, not emitted.** A unit whose models are all dead or still in reserve
  yields no positions; emitting anyway would pace real engine time for nothing on screen.

## Outcome
