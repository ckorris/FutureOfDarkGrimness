# 379 — AoF weapon-type animations and sounds

**Status**: todo
**Related**: #239 (weapon effect-set system, `WeaponEffectCatalog`), #053/#294 (sound cue pipeline), #378 (assigns the keys in the AoF book bundles).

## Goal

Age of Fantasy attacks read as fantasy, not as re-skinned gunfire: bows, crossbows, slings,
thrown weapons, war machines, breath attacks, and magic bolts each get an effect-set key
with a draw style and sound cues, and fantasy melee (swords, axes, great weapons, claws,
crushing maws, spectral touch) gets palettes/accents that fit. Done means: every AoF
`.fdgbook` ships per-book defaults plus per-weapon keys where the default is wrong, no AoF
weapon falls back to `ballistic-slug` tracers, and the styles are hand-verified in a real
GUI battle (user-checked — no desktop automation).

Building on #239's seams (all front-end; the engine transports keys as opaque strings):

- `WeaponEffectCatalog` (`FdgRaylib/Rendering/Presentation/WeaponEffectCatalog.cs`): add
  keys in the existing 18-key style (e.g. arrow-loose, crossbow-bolt, sling-stone,
  thrown-spear, ballista-bolt, breath-flame, arcane-bolt variants; melee: great-weapon
  smash, spectral touch, beast maw). Existing `RangedForm`s (Lobbed, Cone, Bolt, Tracer)
  cover much of it; the one likely new form is a proper arced **arrow** (thin, fast,
  parabolic — Lobbed is a shell with a big burst). Reuse Slash/Smash/Thrust/Rake +
  accents for melee.
- Sound: cue names ride the styles; `.wav`s drop into `FdgRaylib/Assets/Sounds/` by
  filename (see that folder's README). Decide placeholder-tone vs sourcing real samples
  per new cue; keep files short/quiet per the #294 conventions.
- Key assignment happens in #378's import (per-book `defaultRangedEffectSet` /
  `defaultMeleeEffectSet` + per-weapon overrides), same as the GDF books.

## Design forks to surface before building

- How many distinct keys are worth it (per-weapon-family vs per-book flavor) — propose a
  key list with example weapons before authoring styles.
- Whether spell-cast visuals (SpellOverlay) also get AoF flavor here or stay out of scope.

## Notes

- 2026-08-23: **#378 minted the keys and shipped the assignments** (owner-approved fork: assign now,
  implement visuals here). New ranged keys: arrow-loose, crossbow-bolt, sling-stone, thrown-spear,
  ballista-bolt, breath-flame, arcane-bolt; new melee keys: great-weapon-smash, spectral-touch,
  beast-maw (plus reuse of the existing melee/bio/mortar/ballistic sets). Per-faction defaults + the
  AoF keyword tables live in `WeaponEffectAssigner` (AofFactionDefaultsTable / AofRangedKeywords /
  AofMeleeKeywords - system-keyed, since four Disciples faction names collide with GDF). Until this
  item lands, the minted keys draw as the front-end global defaults (ballistic tracer / standard
  blade) by design - this item's scope is now purely `WeaponEffectCatalog` styles + sound cues for
  the 10 new keys, plus hand-verify and any per-weapon keyword refinements.

- 2026-08-22: Filed alongside #375-#378.

## Decisions

## Outcome
