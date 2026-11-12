# 379 — AoF weapon-type animations and sounds

**Status**: implemented 2026-08-28 - awaiting GUI hand-verify (checklist in the wrap note)
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

- 2026-08-28 (wrap): **Both slices shipped.** Engine `a64ef2f`: Sets.ToxicRend + Sets.BombingRun;
  melee form upgrades applied AFTER the keyword tables so payload rows keep priority (crude-melee +
  size word + blunt noun -> great-weapon-smash, BOTH systems per owner; toxic-melee + claw noun ->
  toxic-rend, both systems); range-0 Strafing -> bombing-run by RULE (name keywords can't unite the
  21 AoF + 12 GDF bombing weapons, and payload words would misread); AoF keyword gaps closed
  (stare/gaze/shriek/screech -> arcane-bolt, blowpipe/dart -> bio-organic, hooves -> titan-impact,
  bash -> crude-melee). App `4a6a46f` (submodule bump): new `RangedForm.Arrow` (arced fletched
  shaft, rotates with flight path, `ArcScale` on the style record - longbow 1.0 / javelin 0.75 /
  ballista 0.45 / crossbow 0.35, told apart by width+palette too), sling-stone on Lobbed,
  breath-flame on Cone (deeper red than flame-jet), arcane-bolt on Bolt (azure/violet); melee
  great-weapon-smash (Smash steel), spectral-touch (Slash + Smoke + afterimage, pale teal),
  beast-maw (Rake + Teeth, ivory), toxic-rend (Rake + Ooze), bombing-run (Smash, fire palette -
  the overhead drop + ground ring reads as the bombs landing); accents now render on Rake (was
  Slash-only, no existing set affected). 24 synthesized ToneSynth cues (fire/impact/swing/connect
  x 12 sets), README updated. Tests: engine assigner cases (upgrades, payload priority, aliased
  Strafing, ranged-Strafing negative), app catalog pins (all 35 keys resolve, Arrow arc ordering,
  form/accent design pins), cue distinctness count raised. Engine 3070 + app 1549 green, headless
  smoke exit 0. NOTE: armies SAVED before this change keep their baked keys (cosmetic only;
  re-import/recompile or a null-key retrofit picks up the new routing). Deliberately unchanged:
  AoF "cannon" family stays ballistic-slug (no Indirect in corpus - direct-fire tracer defensible).
  **GUI hand-verify checklist**: (1) Wood Elves / High Elves bows fly as arced fletched arrows, Dark
  Elves crossbows flatter+heavier, javelins/ballistae heavier still; (2) Giant Tribes rocks lob as
  grey stones; Volcanic Dwarves lava roars as a deep red cone; a caster faction's Magic Bolt is an
  azure orb, NOT pink GDF psychic fire; (3) a Great Weapon / Giant Hammer smashes overhead (and a
  GDF Heavy Hammer now does too); Ghostly Undead attacks are pale trailing slashes; fangs/maws rake
  with teeth; Toxin Claws rake green with ooze; (4) a flyer's bombing run whistles then booms with
  an orange smash (both systems); (5) each new voice is audible and distinct in a real battle.

- 2026-08-28: **Corpus audit** (subagent sweep of all 40 bundled books): 839 unique weapon names,
  3,124 refs (ranged 847 / melee 2,277). Verdict: all 10 minted keys are earned - no cuts, no new
  key strictly required; the rest of the corpus reuses existing GDF sets (ballistic-slug,
  spear-pierce, claw-rend, crude-melee, daemon-arcane-melee, toxic-melee, energy-blade,
  blade-standard, titan-impact). Effective counts (keyword-matched + book-default fallback):
  arrow-loose 182, sling-stone 114, crossbow-bolt 100, arcane-bolt 91, thrown-spear 78,
  breath-flame 26, ballista-bolt 18; great-weapon-smash 171, beast-maw 102, spectral-touch 13
  (thin, single-faction, kept for qualitative fit - cheap palette reuse). Confirmed zero per-weapon
  effectSet stamps in the books; the 40 book-level defaults match AofFactionDefaultsTable exactly.
  **Assigner gaps to fold into this item (+ rebake)**: (1) 21 refs of range-0 `Strafing` bombing-run
  attacks across 9 factions (Bombing Run, Drop Bombs, Ember Storm...) render as the faction's melee
  default -> needs a rule-based override (Strafing -> mortar-artillery), not a name keyword;
  (2) Hooves/Heavy Hooves, 41 refs / 13 factions, fall to sword-slash defaults -> titan-impact or
  beast-maw; (3) gaze/psychic attacks (Death Stare, Mind-Piercer Screech...) fall to crossbow/
  thrown-spear defaults -> arcane-bolt keywords (stare/gaze/shriek/screech); (4) Blowpipe (4 refs,
  Bane) -> bio-organic; (5) Bash (15 refs, 5 factions) -> crude-melee keyword. Also noted: the
  `breath` keyword has zero corpus hits (population is all flame/lava). **Forks surfaced to owner**:
  (a) two-handed blunt weapons (hammers/mauls/flails, inside crude-melee's 358 effective refs)
  render Slash not Smash; (b) Toxin Claws family (22 refs, 7 factions) keyword-steals toxic-melee's
  Slash form and loses the claw Rake motion; (c) spell-cast visual scope; (d) placeholder vs real
  sound samples. Awaiting sign-off before authoring styles.

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

- 2026-08-28 (owner sign-off on the audit forks):
  - **Two-handed blunt routing: yes** - route heavy blunt names (hammer/maul/flail family) to
    great-weapon-smash's Smash form in the AoF keyword tables; owner added that some GDF hammers
    would benefit too, so give the obvious GDF two-hander names the same routing once the style
    exists (GDF's crude-melee stays Slash for one-handed clubs/axes).
  - **Toxic claws: mint an 11th key** (`toxic-rend`: Rake motion + toxic ooze palette) so the
    Toxin Claws family keeps claw motion AND poison accent.
  - **Spell-cast visuals: out of scope** for #379; file separately if wanted.
  - **Sounds: synthesize** - existing cues were model-generated; do the same for the new keys
    (drop-in `.wav` by filename, replaceable later).

## Outcome
