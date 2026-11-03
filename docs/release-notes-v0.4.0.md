# Future of Dark Grimness v0.4.0 (OPR 3.6.0)

<!-- DRAFT for the GitHub release body, written 2026-11-02 in the style of the v0.3.0 notes. Paste by hand after the
     v0.4.0 tag; the CI build attaches the archives. Screenshot spots are marked with [SCREENSHOT: ...] - drop in
     images the way v0.3.0 did (docs/images/, raw.githubusercontent.com links), or delete the markers. -->

**Future of Dark Grimness** is GrimDark Future and Age of Fantasy as a video game - import armies from the official Army Forge and play a full 2k solo game against the bot in about 20 minutes, or battle your friends online. The engine enforces every rule from terrain placement to the final victory check, so you spend your time on strategy instead of bookkeeping.

It's a fan project and is not officially affiliated with OnePageRules.

**Download:** [Windows](https://github.com/ckorris/FutureOfDarkGrimness/releases/download/v0.4.0/FdgRaylib-win-x64-v0.4.0-OPR_3_6_0.zip) | [Linux](https://github.com/ckorris/FutureOfDarkGrimness/releases/download/v0.4.0/FdgRaylib-linux-x64-v0.4.0-OPR_3_6_0.tar.gz) | [macOS - Apple Silicon](https://github.com/ckorris/FutureOfDarkGrimness/releases/download/v0.4.0/FdgRaylib-osx-arm64-v0.4.0-OPR_3_6_0.tar.gz) | [macOS - Intel](https://github.com/ckorris/FutureOfDarkGrimness/releases/download/v0.4.0/FdgRaylib-osx-x64-v0.4.0-OPR_3_6_0.tar.gz)
(running instructions are at the bottom)

## New: OnePageRules 3.6

The whole game now plays the new **v3.6** rules for both Grimdark Future and Age of Fantasy, released by OPR at the end of October. If you've read the new rulebook, everything in it is here: the 3" melee range, Impact hits landing before anyone swings, the new Brace and Parry rules for spear walls and hand weapons, heroes lending their Quality to nearby units for morale, the reworked artillery and indirect fire, the new deployment distances, the new army-building limits, and every faction's updated special rules. All 88 army books (48 Grimdark Future, 40 Age of Fantasy) were re-imported from the 3.6 Army Forge data, including the new **Voidspace Stalkers** and the renamed Daemon books.

Your old 3.5 army files and saved games still load - the game just plays them with the 3.6 rules and tells you so in the lobby.

[SCREENSHOT: an Impact Hits roll followed by the "counters the charge - strikes first!" banner]

## New: Replays

Every game you play is now recorded automatically, whether you're hosting, joining, or playing the bot. Open **Replays** from the main menu to watch any game back: play, pause, step through one decision at a time, jump around, and speed up or slow down. A saved game keeps its replay too, so a game you save and finish later plays back as one piece.

This is also a big help if you hit a bug - send me the replay file from your `Replays` folder and I can see exactly what happened.

[SCREENSHOT: the replay viewer mid-game with the decision list open]

## New: Main Menu

The main menu is now a holographic war table with a fresh random battle playing out behind the buttons every half minute. If you'd rather not have it moving, Options lets you freeze it or turn it off. Options also moved to the main menu and remembers your settings between launches.

[SCREENSHOT: the new main menu]

## General/Quality of Life Improvements

- Import an Army Forge share link straight from the lobby with the new **Import** button - no need to go through the Army Forge screen first.
- The Random Army lists were rebuilt by hand for 3.6, so every one is legal under the new army-building rules with sensible loadouts.
- The lobby gives a friendly amber heads-up when an army is over the optional 3.6 unit and hero limits for the points you're playing at. It never stops you launching.
- The Combat Calculator learned the 3.6 rules too: you can set how far a charge came from, and it shows Parry and Brace at work.
- The dice panels and the game log explain the new rules as they fire: Impact rolls, who counters, what Parry blocked, and whose Quality a morale test used.
- Rule tooltips wrap properly for the longer 3.6 rule texts, and the Army Forge shows which mount or crew a weapon belongs to.
- Moving a group of models keeps your formation as it is by default; the old "re-form to face the way you're going" behaviour is now an option you turn on.

## Bug Fixes

- A move the game rejected ("Breaks cohesion") could end the whole game with a Game Over screen. It now just asks you again. Thanks to the folks on Discord who reported it.
- After a unit countered a charge, the game sometimes shuffled the chargers forward instead of the defenders. Found while play-testing 3.6.
- A hero's aura wasn't applying to the unit it had joined in some cases.
- Weapon teams were giving their Tough value to every model in the squad instead of just the team.
- Units waiting in Ambush showed as "Activated" in the army list.
- Picking a special ability at the start of an activation locked you in; you can now back out.
- You can now deploy a unit inside a transport that's still in reserve.
- Cavalry and other long bases no longer stack on top of each other when you drag a packed group at an angle.
- A handful of log lines read oddly ("Warriors's", or Rending claiming to ignore a rule the target didn't have).

## Privacy note

The game now collects optional, anonymous usage stats (how often it's launched, which screens get used, games started and finished) so I can see what people actually play. You'll see a notice the first time you run this version, and you can switch it off in Options any time. Nothing about your armies, your name or your machine is sent. Details are in the [README's privacy section](https://github.com/ckorris/FutureOfDarkGrimness#privacy).

## Known gaps

- The new 3.6 terrain types (Dense, Unstable, Steep, Climbable) aren't in yet; terrain works as before.
- One Age of Fantasy spell, Combat Possession (Shadow Stalkers), is missing because OPR's own data for it is incomplete. The other 527 spells are in.
- The Strategist bot was trained on 3.5 armies and hasn't been retrained yet, so it may play a little differently than it used to.

## Which download do I want?

| Platform | File |
|----------|------|
| Windows (64-bit) | `FdgRaylib-win-x64-v0.4.0-OPR_3_6_0.zip` |
| Linux (64-bit) | `FdgRaylib-linux-x64-v0.4.0-OPR_3_6_0.tar.gz` |
| macOS (Apple Silicon) | `FdgRaylib-osx-arm64-v0.4.0-OPR_3_6_0.tar.gz` |
| macOS (Intel) | `FdgRaylib-osx-x64-v0.4.0-OPR_3_6_0.tar.gz` |

Each archive is self-contained - no .NET install required. Extract it anywhere and run.

## Running it

- **Windows:** unzip and run `FdgRaylib.exe`. The first launch may show a SmartScreen "Windows protected your PC" dialog, because the app is new and unsigned - click **More info**, then **Run anyway**.
- **Linux:** untar and run `./FdgRaylib`.
- **macOS:** untar, then from a Terminal in that folder run `xattr -dr com.apple.quarantine .` followed by `./FdgRaylib` (a one-time step, since Gatekeeper blocks unsigned apps). Note: this build hasn't been tested on real Mac hardware.

`SHA256SUMS.txt` lists the SHA-256 hash of each archive if you'd like to verify your download.

Builds are unsigned on every platform, so the warnings above are expected - see [Windows will warn you about this app](https://github.com/ckorris/FutureOfDarkGrimness#windows-will-warn-you-about-this-app) in the README.

See the [README](https://github.com/ckorris/FutureOfDarkGrimness#readme) for importing armies, multiplayer setup, and bug reporting.

**Full Changelog**: https://github.com/ckorris/FutureOfDarkGrimness/compare/v0.3.0...v0.4.0
