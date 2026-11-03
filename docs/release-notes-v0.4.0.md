# Future of Dark Grimness v0.4.0 (OPR 3.6.0)

DRAFT for the GitHub release page (rewritten 2026-11-02; the 2026-10-31 itemised draft is in git history). Plain
ASCII, no in-page links. Paste into the release body by hand; the `v0.4.0` tag triggers the CI build that attaches
the four platform archives `FdgRaylib-<rid>-v0.4.0-OPR_3_6_0.{zip,tar.gz}` and `SHA256SUMS.txt`. Builds are unsigned.

## OnePageRules 3.6

This release moves the game from the OnePageRules v3.5 rules to **v3.6.0** for both Grimdark Future and Age of
Fantasy, with no 3.5 mode. Everything the new edition changed is in: the 3" melee range and the "took the most wounds
loses" melee result, Impact rolled per model before anyone strikes, Counter trimmed to strike-first with the new
Brace(X) taking its anti-Impact half, the new core rules Parry(X), Assault, Cumbersome and Spell Amplifier, the
reworked Fatigue, Indirect, Artillery, Hero morale aura, Dangerous terrain, Transport, Aircraft and Unstoppable rules,
"most models" instead of "all models" for unit-wide rules, per-spell line of sight and the 3.6 casting modifiers, the
3.6 deployment distances, the new force-organisation caps, and every reworded or new faction rule including the 112
"Enhanced X" rules. The rulebook is the reference; where a 3.6 sentence allows two readings the game documents the one
it took in `docs/opr-3.6-upgrade-plan.md`.

## Replays

- Every game is recorded as it is played, on the host and on remote clients, and saved to `Replays/` beside the
  executable however the game ends. A saved game carries its replay, so a save that is resumed keeps one continuous
  record.
- Main menu -> **Replays** opens the viewer: the table with the real animations, play / pause / step by decision /
  seek / speed, a decision list with details, and a full-board mode.
- Headless tools for bug reports: `--replay game.fdgreplay --dump | --events | --stats | --verify | --export`, and
  `--at N --make-save` cuts a resumable save at any step.

## Army books and lists

- The Army Forge ships all **48 Grimdark Future and 40 Age of Fantasy** official books from Army Forge's 3.6.0 data.
  New: **Voidspace Stalkers**. Renamed: the Wormhole / Rift Daemons books are **Daemons of Change / Lust / Plague /
  War**; Saurian Starhost is **Saurian Starhosts**.
- An Army Forge share link can be imported straight into a lobby slot (roster **Import**), as well as in the Forge.
- The bundled Random Army lists were hand-rebuilt for 3.6: legal force organisation, full point brackets, redesigned
  loadouts.

## Older armies and saves

- A `.fdgarmy` built for 3.5 still loads and plays, with 3.6 rules; the lobby and the Forge say so in amber. It never
  blocks a launch.
- Saved games from v0.3.x load and play with 3.6 rules; the resume lobby shows the same kind of notice. A fresh game is
  the cleaner option.

## Interface

- New main menu: an animated holographic war table behind a command-panel menu (Animated / Still / Off in Options).
- Options moved to the main menu and persist between runs; the in-game Options panel is larger.
- Lobby: amber force-organisation advice per army (never blocking); the Army Source setting adjusts the points limit.
- Army Forge: lists books under their 3.6 names, flags older lists, names the mount or crew a weapon belongs to, warns
  about Enhanced rules without their base rule, and no longer greys out combined upgrades after a weapon swap.
- Combat Calculator: a charge distance for melee, a "Defender moved this round" tick when the defender's rules care
  (Artillery), Parry shown as a chip on the save line, Ravage shown as its own row.
- Rule hovers wrap long texts everywhere; an Enhanced rule whose official text does not name its base ends with
  "Base rule: X.".
- Dice panels and the log explain the new rules as they fire: "Impact Hits" before the fight, the Counter banner,
  Parry beats, "fatigued - 6s do not count", a morale test's "Quality 3+ (Hero aura: Captain)" chip.

## AI

- The Tactician plays the 3.6 rules (3" reach, Assault, Parry and Brace, Cumbersome, Indirect on a Hold, Spell Drain)
  and prices its own moves against rules that care whether it moved.
- The Strategist's learned leaf was trained on 3.5 armies; re-training is planned after this release.

## Fixes

- A rejected move or placement no longer ends the game ("Breaks cohesion" Game Over in v0.3.0): the game re-asks, and
  placement gained an Auto-place for spots with no legal position.
- After a Counter, the charged unit piles in, not the charger.
- Group moves keep the formation rigid by default; "Optimize formation for distance" is the opt-in re-form.
- A one-model item's Tough (weapon teams) applies to that model only; a joined hero's aura fires on combined units;
  units held in Ambush read "In Reserve", not "Activated"; activation-start picks can be backed out of; units can
  deploy inside a transport that is still in reserve.
- Log lines name the roll a morale test really needed, use "Warriors'" not "Warriors's", and no longer claim Rending
  ignored a Regeneration the defender did not have.

## Privacy

- Optional, anonymous usage analytics (launches, features opened, games started and ended). A notice explains it on
  first launch; it can be turned off in Options at any time. Details in the README's privacy section.

## Known gaps

- The 3.6 terrain types (Dense, Unstable, Steep, Climbable) are not in this release.
- Combat Possession (Age of Fantasy Shadow Stalkers) is not imported; OPR's own 3.6.0 data for it is cut off. The
  other 527 spells are in.
- A few rule descriptions read differently from what the engine does (Teleport's 3"/6", Swift, the Predator family's
  "+1 attack", Extended Buff Range, Protection Feat, Versatile Defense, Mend, Re-Deployment, some Mark lifetimes), and
  some spell edge cases (Hazardous on spell hits, one-shot shooting grants spent by a damage spell). All are filed as
  follow-ups.
- A networked client that joins a resumed scenario save may draw unit labels without model bases (#463).
