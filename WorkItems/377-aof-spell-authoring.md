# 377 — AoF spells: author all 240 army spells as data

**Status**: todo
**Related**: #378 (spells land inside the `.fdgbook` spells arrays it produces), #375/#376 (spells reference AoF rule names that must resolve). Reference doc: `/home/chris/Projects/GDF Armies/Age of Fantasy/Special Rules and Spells by Army.md` (local only, do not copy text into the repo).

## Goal

All 40 AoF books carry their 6 spells as working `SpellDefinition` data (name, threshold,
target spec, effect), verified in-engine. Done means: every spell is castable and produces
its printed effect, with the `generated-spell-armies`-style probe recipe (see
`reference_headless_testing` memory / the GDF spell verification runs) exercising each one
headless.

No new engine machinery is expected: the 2026-08-22 appraisal classified all 240 against
the spell-effect patterns the GDF books already use —

- dealHits-style damage: 120
- addRule-once buffs/debuffs ("gets X once (next time the effect would apply)"): 88
- markTarget ("friendly units get X against once"): 20
- moraleTestThen: 5
- countAsInTerrain (Difficult/Dangerous Terrain once): 5 — effect kind exists; confirm the
  spell path accepts it (GDF spells never used it; CastSpellStage pattern-matches effects,
  so this is the one seam worth probing early)
- move-modifier once (+/-X" on Advance/Rush/Charge): 2 — authorable as addRule of a
  move-modifier micro-rule (Musician precedent)

Casting values are uniformly (1)(1)(2)(2)(3)(3) across all 40 books. Several single-model
target spells carry the "resolved as if the target was a unit of [1]" clause — reuse the
GDF handling. Watch for spells granting rules that only #375/#376 define; sequence those
books after the rules land or accept the engine's skip-with-warning until then.

## Notes

- 2026-08-22: Filed. Pattern counts above are from a regex classification of the verified
  reference doc; the 7 "outlier" spells are listed in the appraisal (5x counts-as-terrain,
  2x move modifier).

## Decisions

## Outcome
