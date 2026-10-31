# Future of Dark Grimness v0.4.0 (OPR 3.6.0) - release notes DRAFT

DRAFT written 2026-10-31 by #444 from the upgrade items' hand-offs, for Chris and the orchestrator to edit before the
release. Not published anywhere yet. Plain ASCII; GitHub release notes have no heading anchors, so this text avoids
in-page links. The archives are `FdgRaylib-<rid>-v0.4.0-OPR_3_6_0.{zip,tar.gz}`; builds stay unsigned.

## Rules version

This release moves the whole game from the OnePageRules **v3.5** rules to **v3.6.0** (published 2026-10-29), for both
Grimdark Future and Age of Fantasy. 3.6 replaces 3.5: there is no 3.5 mode.

## Army books

- The Army Forge screen ships all **48 Grimdark Future and 40 Age of Fantasy** official books, re-imported from Army
  Forge's 3.6.0 data (every unit, weapon, upgrade and price; every book now says "OPR v3.6.0").
- New: **Voidspace Stalkers** (Grimdark Future).
- Renamed: Wormhole Daemons of Change / Lust / Plague / War -> **Daemons of Change / Lust / Plague / War** (Grimdark
  Future); Rift Daemons of X -> **Daemons of X** (Age of Fantasy); Saurian Starhost -> **Saurian Starhosts**.
- Every book's spells were re-imported for 3.6 (527 of 528; see Known gaps).
- The bundled Random Army lists were rebuilt on the 3.6 books. 3.6 removed or reshaped some units, so a few lists
  had units swapped or cut to stay inside their 1k / 2k / 3k / 4k bracket.

## What 3.6 changed for players

- **Melee:** models within **3"** (was 2") strike. The side that **took** the most wounds during the activation loses
  the melee (was: caused the fewest); Fear(X) now counts as X extra wounds taken by the enemy.
- **Impact, Counter, Brace:** Impact is rolled **per model, before either unit attacks**, and not at all by a Fatigued
  charger. Counter now only strikes first; its "enemy gets fewer Impact dice" half is the new core rule **Brace(X)**.
- **Fatigue:** a Fatigued unit's 6s to hit in melee no longer trigger "on a 6" special rules.
- **New core rules:** **Parry(X)** (Age of Fantasy hand weapons; 6+ ignores a melee hit, the worst hits go first),
  **Assault** (one weapon counts as both ranged and melee), **Cumbersome** (Hold or Advance only, no shooting after
  moving; artillery carries it), **Spell Amplifier** (+1 to cast).
- **Indirect** ignores line of sight and Sight-Blocker cover only when holding, and no longer has -1 to hit after
  moving. **Artillery's** defensive -1 needs most models unmoved this round.
- **"Most models":** unit-wide rules (Fearless, Regeneration, Stealth, Artillery and ~85 faction rules) apply when most
  models in the unit have them, instead of all.
- **Heroes:** a Hero's unit, and units within 6" of a friendly Tough(9)+ Hero, may use that Hero's Quality for morale
  tests while the Hero isn't Shaken.
- **Deployment:** Scout deploys fully within 9" of its zone and over 9" from non-Scout enemies; Vanguard and Fanatic
  stay over 9" from non-Vanguard enemies; Reinforcement arrives fully within 9" of a table edge, over 9" from enemies.
- **Terrain:** Dangerous terrain rolls as many dice as the wounds it would take to destroy the unit, and a unit that
  activates in Dangerous terrain also tests. Flying ignores Difficult and Dangerous terrain.
- **Transports** are entered by moving into contact. **Aircraft** give -1 to hit as well as -12" range.
  **Unstoppable** ignores only negative modifiers that are not its own model's.
- **Casting:** each spell says whether it needs line of sight; boosting casters need only be within 18"; one casting
  window per activation, before attacking. Spell Drain is in.
- **Army building:** max. 1 unit per 200pts and max. 1 model or Tough per 20pts (warnings, as before); Age of
  Fantasy uses its own levels (1 Hero per 375pts, copies per 750pts). **Unique Hero** may be taken once per army.
- **Faction rules:** every reworded and new 3.6 faction rule is in, including the new weapon rules (Anti-Cover,
  Smash, Pierce, Fracture, Scourge, Crossing Strike and the rest), the "over 9" / 6" away" family, and the 112
  "Enhanced X" rules. Very Fast is gone (3.6 dropped it).

## Older armies, links and saves

- **3.5 army files keep working.** A `.fdgarmy` built for 3.5 loads and plays, with 3.6 rules. The lobby (and the
  headless CLI) shows an amber notice: "Army '...' was built for OPR v3.5.x; it plays with 3.6 rules". It does not
  block the launch.
- **Army Forge share links** import as 3.6 lists (Army Forge now serves every list with its 3.6 books). The import
  summary explains a unit priced below Army Forge's figure (a stale "Replace X" pick from an old list), and says why
  Age of Fantasy Shadow Stalkers has five spells.
- **Saved games from v0.3.x and earlier load and play with 3.6 rules**, keeping the 3.5 armies they were saved with.
  The resume lobby (and the headless `--scenario` console) shows an amber notice: "This save predates the OPR 3.6
  upgrade; it plays with 3.6 rules". It does not block the resume. Saves written by 0.4.0 carry their rules version,
  so a save from another ruleset is named the same way ("This save was written for OPR v3.x.y; ..."). A fresh game is
  still the cleaner option.

## Interface

- The Army Forge lists the books under their 3.6 names.
- The Forge no longer greys out a combined upgrade (e.g. Assault Brothers' "Heavy Pistol, Energy Hammer") after every
  pistol was swapped.
- Rule hovers wrap the long 3.6 rule texts everywhere; Spawn and Split show their description instead of "not
  enforced".
- New force-org warnings in the Forge for the 3.6 caps.

## AI

- The Tactician reads the 3.6 rules: 3" melee reach, Assault threat, Parry and Brace, Cumbersome, Indirect on a Hold,
  the range / charge-distance rules, Ravage, Hazardous, Scourge and Tag tokens. It uses Spell Drain and prices its
  Versatile picks and the 3.6 activation-start moves.
- The Combat Calculator takes a charge distance and shows Ravage.
- The Strategist's learned leaf was trained on 3.5 armies and has not been re-trained yet (planned after the release).

## Known gaps

- **3.6 terrain types** (Dense, Unstable, Steep, Climbable) are not in this release; terrain keeps the existing types.
- **Combat Possession** (Age of Fantasy Shadow Stalkers) is not imported: OPR's own 3.6.0 data for it is cut off
  mid-sentence. The other 527 spells are in.
- Some 3.6 rule descriptions read differently from what the engine does, found during the upgrade and queued as
  follow-ups: Teleport's 3" / 6" by action (the game uses a flat 6"), Swift, the Predator family's "+1 attack", Extended
  Buff Range, Protection Feat, Versatile Defense, Mend, Re-Deployment, and a few Mark lifetimes.
- Spell edge cases: Hazardous on spell hits does not wound the caster on 1s (Ratmen Power Surge, Goblin Pow!, AoF
  Ratmen Lightning Blast); a one-shot "when shooting" grant can be spent by its holder's damage spell; a claimed mark
  can linger to the claimer's next damage spell; the Tactician may drain an enemy caster holding no tokens.
- [Delete or update before release] Army Forge and glossary display questions F1-F9 (#441) were still open for Chris
  when this draft was written, and the GUI hand-verifies of the upgrade items were pending.
