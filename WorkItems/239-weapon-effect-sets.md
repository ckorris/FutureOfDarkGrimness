# 239 — Weapon effect sets (per-weapon combat visuals + sounds)

**Status**: todo (plan signed off 2026-07-16; implementation not started)
**Related**: #238 (attack/dice overlap + volley sounds), #056 (beat stream), #053 (sound cues), #233 (cast roll beat), #218 (ListCompiler cost bug — same forge seam)

## Goal

Replace the one-size-fits-all combat presentation (every shot a yellow tracer + "gunshot", every
melee swing a gray blade + "clang") with data-driven **weapon effect sets**: ~13 ranged and ~10
melee themed visual/audio styles. Each ranged set = projectile graphic + firing sound + impact
sound; each melee set = swing graphic + swing sound + impact sound. Assignment is per weapon in the
data files, falling back to per-army defaults, falling back to a global default. Additionally the
attack animation becomes **truthful**: only shots that actually hit show an impact (misses visibly
overshoot), and impacts get their own sound. Done = all bundled books/armies carry effect keys, all
sets render + sound distinct in GUI play, misses/hits read correctly, suite green, headless smoke
unaffected.

## Decisions

- **Effect keys are baked into the data files, not resolved by runtime keyword matching.** Owner
  call, 2026-07-16: weapon-name keywords ("plasma", "fusion") only work in English; explicit
  language-neutral keys riding the data survive localization and hand-authored content. The keyword
  matcher still exists, but only as an **authoring/migration tool** (book patch, forge bake,
  one-time army retrofit) — never consulted at render time.
- **Truthful + concurrent hit reveal.** Engine rolls to-hit just before emitting the AttackBeat
  (same RNG call order) and the beat carries hit/attack counts. Misses overshoot, hits impact,
  while the to-hit dice tumble alongside per #238. The minor "spoiler" (impacts telegraph the dice
  result a moment early) is accepted.
- **No new UI.** Defaults are army-template-level data; the Forge does not expose a picker, and the
  freeform Army Builder is slated for deprecation. Overrides are hand-edited JSON.
- **The engine treats effect keys as opaque strings.** No enum, no vocabulary engine-side; the
  FdgRaylib catalog defines what keys exist and maps unknown/null keys to the global default. Any
  alternate front-end gets the same keys off the wire and styles them however it likes.
- Hit counts shown are the **natural** to-hit successes (before Furious/Blast synthetic
  injections) — the right ratio for "which projectiles connected"; synthetic hits are not shots.
- `WeaponComparer` deliberately ignores the new key (same-name weapons share keys in practice;
  batch grouping must not split on presentation data).

## Plan

Data flow end to end:

```
.fdgbook (faction defaults + rare per-weapon keys)
  -> Army Forge bake (explicit key = book key ?? keyword match; army defaults copied from book)
    -> .fdgarmy (WeaponFileEntry.EffectSet + ArmyListFile default fields)
      -> engine load (Weapon.EffectKey = entry ?? army default for its ranged/melee kind)
        -> RollToHitStage emits AttackBeat{WeaponEffect, HitCount, AttackCount}
          -> FdgRaylib WeaponEffectCatalog -> AttackOverlay style + fire/impact sound cues
```

### Slice 1 — engine (submodule; edits authorized for this item, owner 2026-07-16)

1. `WeaponFileEntry.EffectSet` (string?, null = fall back) and
   `ArmyListFile.DefaultRangedEffectSet` / `DefaultMeleeEffectSet` (string?). Additive, nullable —
   old files load unchanged.
2. `IWeapon.EffectKey` / `Weapon.EffectKey` (string?). Resolved once at `UnitData` construction
   (the WeaponFileEntry -> Weapon loop, UnitData.cs ~130-150): entry value, else the army default
   matching the weapon's ranged/melee kind. Verify how the army-level defaults reach the UnitData
   ctor (army load call chain via FDGServer) and thread them through.
3. `AttackBeat` gains `WeaponEffect` (string?), `HitCount` (float), `AttackCount` (float) — floats
   because Realistic-randomness dice are fractional. Wire round-trip coverage in
   PresentationBeatSerializationTests.
4. `RollToHitStage`: hoist the `DetermineHitRollResults` query + `DiceRoller.Roll` above the
   AttackBeat emission (one reorder; the single Roll call keeps its position in the RNG sequence),
   emit the beat with `WeaponType.EffectKey` + natural-success/attack counts. DiceRolledBeat and
   everything after unchanged.
5. Tests: beat round-trip; emission order/payload pin (mirror AttackBeatOverlapTests style);
   army-load fallback chain (explicit key kept, null falls to army default, both-null stays null).

### Slice 2 — app data & authoring (FdgRaylib)

6. `.fdgbook` schema: optional top-level `defaultRangedEffectSet`/`defaultMeleeEffectSet`; optional
   `effectSet` on any weapon (units[].weapons[] and sections[].options[].weaponsGained[]).
7. `WeaponEffectAssigner` (new): the priority-ordered keyword table + per-faction defaults table
   (roster below). Consumers:
   - one-time book patch (script or dev flag) writing the two default fields into all 47 books;
   - the Forge compile path (ListCompiler/ArmyForgeScreen — same seam as #218): per-weapon
     EffectSet = book explicit ?? keyword match (null when neither — army default covers it);
     army defaults copied from the book;
   - one-time `.fdgarmy` retrofit over checked-in armies (FdgLab x8, root test armies,
     Scenarios/armies, GoodGuys): set army defaults (faction table; hand-map the freeform test
     armies) + per-weapon keys via keywords. Leave embedded Forge blocks untouched — they refresh
     from the patched book on the next Forge re-save. Engine submodule `armies/` examples: skip
     (read-only convention) unless trivially safe during slice 1.
   - `OprBookImporter` emits the fields on any future re-import (same tables).
8. CLI EOF fallback army (ArmyLoader): give its rifles/heavy rifles sensible keys.

### Slice 3 — app visuals

9. `WeaponEffectCatalog` (new): key -> RangedEffectStyle { form (tracer / bolt-orb / beam /
   lobbed-arc / cone / glob), palette, trail, speed profile, impact visual, fire cue, impact cue }
   and key -> MeleeEffectStyle { arc form, palette, spark, swing cue, impact cue }. Null/unknown
   key -> global defaults (`ballistic-slug` / `blade-standard`, which keep today's look).
10. `AttackOverlay` becomes style-driven: generalize DrawTracer/DrawMeleeBlade into per-form draw
    paths, keep AP scaling. Truthful hits: visual shots = From.Count x VolleyCount; visualHits =
    round(HitCount / max(AttackCount,1) x shots), clamped; a pure deterministic distribution helper
    (shared with sound timing, unit-tested) spreads hits across shots/volleys. Misses overshoot
    ~20% past the target and fade with no impact flash. Melee: whiff swings show no clash spark;
    the #238 hit-stop fires only when at least one swing hits.

### Slice 4 — app audio

11. `PresentationPlayer`: new `AttackVolleyImpact` hook (analog of `AttackVolleyStarted`) fired
    when a volley's projectiles land, only if that volley contains >= 1 visual hit (same shared
    distribution helper).
12. `PresentationSoundCues`: per-set cue keys `fire-<slug>` / `impact-<slug>` / `melee-<slug>` /
    `meleehit-<slug>`; wav override by filename under Assets/Sounds as today, per-set ToneSynth
    placeholder recipes (sound sketches in the roster). VolleyCue + the new impact cue resolve the
    slug from beat.WeaponEffect via the catalog. Existing gunshot/melee cues become the global
    default set's voices. RaylibRenderer wires the new hook (~lines 184-192).

### Slice 5 — verify & close

13. Full suite + build + headless smoke; GUI hand-verify pass across several factions (orks =
    crude slugs, High Elves = shard crystal, Robot Legions = gauss beams, plasma/fusion consistent
    everywhere); tick ledger items; archive.

## Set roster (survey, 2026-07-16)

Source: Sonnet sweep over all 47 `.fdgbook` files (1621 distinct weapon names: 1097 ranged / 524
melee, occurrence-weighted) + the 8 FdgLab armies. Coverage with these tables: 52.5% of ranged
occurrences match a tech keyword (rest are catalog-generic names like Heavy Machinegun that
correctly take the faction default); melee 77.2% (rest is almost all generic `CCW`, likewise
default territory). Effective coverage with defaults: ~100%.

### Ranged sets (13)

| Slug | Visual | Fire sound | Impact sound |
|---|---|---|---|
| plasma-bolt | glowing blue-white orb, brief charge pulse, short bright trail | rising electric whine into heavy "thoomp" | white flash + electric sizzle |
| fusion-melta | short thick orange-red beam, heat shimmer | blowtorch ignition roar | molten splash + sizzling hiss |
| flame-jet | orange/yellow particle cone + smoke wisps, lingers | sustained gas-ignition whoosh | crackling burn |
| gravity-pulse | pulsing violet distortion orb, ripple ring | deep bass charge "whump" | implosive crack, inward crumple |
| gauss-particle | thin green particle beam, disintegration shimmer | rising electric buzz | green spark crackle + fizzle |
| laser-beam | thin instant red/cyan beam, afterimage flare | sharp zap "pew" | small spark burst |
| missile-rocket | smoke-trailed projectile, small tail flame | launch whoosh + roar | boom + flash + shock ring |
| mortar-artillery | high lobbed arc, big shell | heavy thump + falling whistle | large boom + rumble + smoke |
| bio-organic | pulsating green/purple glob, dripping trail | wet splat/gurgle | acid sizzle + wet splatter |
| storm-tracer | fast hot-white tracer bursts, punchy muzzle flash | mechanical "chunk-chunk" burst | sharp metallic ping |
| ballistic-slug | short dull-yellow tracer, plain flash (global default; ~today's look) | sharp crack / rattle | dull thud + small spark |
| arcane-psychic | swirling magenta/gold sigil bolt, warp trail | eerie chant-like rising tone | reality-tear burst |
| shard-crystal | translucent cyan faceted bolt (High Elf Fleets bespoke) | crystal chime | glass-shatter tinkle |

### Melee sets (10)

| Slug | Visual | Sound (swing / impact) |
|---|---|---|
| energy-blade | glowing cyan slash arc, afterimage trail | electric hum / crackling zzt-chak |
| titan-impact | ground-impact flash, radial dust ring + crack lines | deep bass thud / metal crunch |
| shock-melee | crackling blue-white arcs, contact flash | buzz-charge / snap-zap |
| chain-blade | jagged rotating teeth along arc, spark spray | revving snarl / grinding tear |
| toxic-melee | dripping green/purple ooze trail, sickly glow | wet squelch / corrosive hiss |
| daemon-arcane-melee | dark purple/black smoke trail, warp shimmer | warped whoosh / reality-tear |
| spear-pierce | thrust lunge, bright tip glint | whoosh-thrust / puncture crunch |
| claw-rend | three-streak red slash rake | wet slash / flesh-tear snarl |
| crude-melee | heavy blunt swing, dust puff + crack lines | low whoosh / heavy thud |
| blade-standard | plain steel slash arc, metallic glint (global default; ~today's look) | metallic whoosh / clang |

### Keyword tables (authoring only — priority order, case-insensitive substring, first match wins)

Ranged: plasma-bolt <- plasma; fusion-melta <- fusion, melta, fuser; flame-jet <- flame;
gravity-pulse <- gravity; missile-rocket <- missile, rocket, rpg; mortar-artillery <- mortar,
artillery, grenade, bomb, siege, demolition, frag; bio-organic <- bio, spit, spore, acid, venom,
toxin, toxic, vomit, miasma; gauss-particle <- gauss, flux, atom, shock, reaper; laser-beam <-
laser, beam, photon, pulse, monolith; arcane-psychic <- magic, psychic, hex, curse, ritual,
chakram, fireball; storm-tracer <- storm, bolt; ballistic-slug <- bullet, slug, buckshot, revolver.

Melee: energy-blade <- energy, plasma, relic, hyper; titan-impact <- titan, walker, stomp, hull,
crushing; shock-melee <- shock, electro, taser, stun; chain-blade <- chain, saw, buzz; toxic-melee
<- venom, toxin, toxic, plague, acid, infected, poison, putrid, fungal, miasma;
daemon-arcane-melee <- cursed, hexed, power, ritual, perfect, exalted, daemon; spear-pierce <-
spear, lance, pike, halberd, glaive, scythe; claw-rend <- claw, fang, bite, jaw, talon, razor,
whip, slash, serrated, rend, swarm; crude-melee <- fist, club, mace, flail, hammer, bash, axe,
knuckle, gauntlet, crew, pick, maul; blade-standard <- sword, blade, dagger, knife.

Known misfire: melee "power" routes to daemon-arcane-melee (correct for ~90% — the Wormhole
Daemons Power Staff/Spear/etc family) but tags Saurian Starhost's one-off Power Claw daemonic; fix
with an explicit `effectSet` on that book entry during the patch.

### Faction defaults (ranged / melee)

Alien Hives bio-organic/claw-rend; Battle Brothers storm-tracer/energy-blade; Blessed Sisters
storm-tracer/energy-blade; Blood Brothers storm-tracer/energy-blade; Blood Prime Brothers
storm-tracer/energy-blade; Change Disciples ballistic-slug/energy-blade; Custodian Brothers
storm-tracer/spear-pierce; DAO Union laser-beam/energy-blade; Dark Brothers
storm-tracer/energy-blade; Dark Elf Raiders bio-organic/claw-rend; Dark Prime Brothers
storm-tracer/energy-blade; Dwarf Guilds ballistic-slug/shock-melee; Elven Jesters
mortar-artillery/blade-standard; Eternal Dynasty laser-beam/titan-impact; Goblin Reclaimers
ballistic-slug/crude-melee; Havoc Brothers ballistic-slug/energy-blade; High Elf Fleets
shard-crystal/energy-blade; Human Defense Force ballistic-slug/crude-melee; Human Inquisition
ballistic-slug/energy-blade; Infected Colonies bio-organic/toxic-melee; Jackals
ballistic-slug/spear-pierce; Knight Brothers storm-tracer/energy-blade; Knight Prime Brothers
storm-tracer/energy-blade; Lust Disciples ballistic-slug/energy-blade; Machine Cults
ballistic-slug/shock-melee; Orc Marauders ballistic-slug/crude-melee; Plague Disciples
bio-organic/toxic-melee; Prime Brothers storm-tracer/energy-blade; Ratmen Clans
ballistic-slug/shock-melee; Rebel Guerrillas ballistic-slug/energy-blade; Robot Legions
gauss-particle/titan-impact; Saurian Starhost gauss-particle/claw-rend; Soul-Snatcher Cults
ballistic-slug/crude-melee; Titan Lords (all 5 variants) fusion-melta/titan-impact; War Disciples
ballistic-slug/energy-blade; Watch Brothers storm-tracer/energy-blade; Watch Prime Brothers
storm-tracer/energy-blade; Wolf Brothers storm-tracer/energy-blade; Wolf Prime Brothers
storm-tracer/energy-blade; Wormhole Daemons of Change arcane-psychic/daemon-arcane-melee; Wormhole
Daemons of Lust ballistic-slug/daemon-arcane-melee; Wormhole Daemons of Plague
bio-organic/toxic-melee; Wormhole Daemons of War bio-organic/blade-standard.

## Deferred / out of scope (explicit, per never-silently-cut-scope)

- **Spells**: CastSpellStage emits no AttackBeat; spell visuals unchanged (arcane-psychic covers
  Wormhole Daemons *weapons* only). Relates #233/#034 — a future SpellBeat could reuse the catalog.
- **Per-faction tinting of shared sets** (e.g. Plague's Miasma Mortar with a green haze over the
  base mortar visual) — flagged by the survey as the V2 refinement instead of more discrete sets.
- **Whip sub-archetype** (Agony/Electro/Toxin Whips across 4+ factions) folded into claw-rend.
- **Impact hits / strafing** (ResolveImpactHitsStage, StrafingStage): no AttackBeat today; unchanged.
- **Any picker UI** (Forge or Army Builder — Builder deprecated).
- **Localized weapon display names**: keys are language-neutral; display translation is a future
  display-layer concern this design already survives.

## Notes

- 2026-07-16: Filed with signed-off plan. Survey (Sonnet subagent) over 47 books + 8 FdgLab armies
  produced the roster above; recommended 13 ranged / 10 melee vs the requested 7-10 (gravity-pulse
  earned a slot on 170 occurrences; arcane-psychic is the only sane fit for Wormhole Daemons of
  Change; shard-crystal is the owner's own High Elf example). If trimming to a hard 10, fold
  ballistic-slug into storm-tracer (loses the mundane-vs-sci-fi kinetic split). Repo has 47 books,
  not 49 (GdfRuleSupplement.json is not a book).

## Outcome

(when closed)
