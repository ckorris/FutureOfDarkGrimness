# Future of Dark Grimness v0.4.0 (OPR 3.6.0)

<!-- Release body for the v0.4.0 GitHub release (player-facing, v0.3.0 style; edited by Chris 2026-11-02). Paste by
     hand after the v0.4.0 tag; the CI build attaches the archives. -->

**Future of Dark Grimness** is GrimDark Future and Age of Fantasy as a video game - import armies from the official Army Forge and play a full 2k solo game against the bot in about 20 minutes, or battle your friends online. The engine enforces every rule from terrain placement to the final victory check, so you spend your time on strategy instead of bookkeeping.

It's a fan project and is not officially affiliated with OnePageRules.

**Download:** [Windows](https://github.com/ckorris/FutureOfDarkGrimness/releases/download/v0.4.0/FdgRaylib-win-x64-v0.4.0-OPR_3_6_0.zip) | [Linux](https://github.com/ckorris/FutureOfDarkGrimness/releases/download/v0.4.0/FdgRaylib-linux-x64-v0.4.0-OPR_3_6_0.tar.gz) | [macOS - Apple Silicon](https://github.com/ckorris/FutureOfDarkGrimness/releases/download/v0.4.0/FdgRaylib-osx-arm64-v0.4.0-OPR_3_6_0.tar.gz) | [macOS - Intel](https://github.com/ckorris/FutureOfDarkGrimness/releases/download/v0.4.0/FdgRaylib-osx-x64-v0.4.0-OPR_3_6_0.tar.gz)
(running instructions are at the bottom)

## New: OnePageRules 3.6

The whole game now plays the new **v3.6** rules for both Grimdark Future and Age of Fantasy. That includes changes to all 88 army books, plus all the core rules.


## New: Replays

Every game you play is now recorded automatically, whether you're hosting, joining, or playing the bot. Open **Replays** from the main menu to watch any game back, like a battle report.

## New: Main Menu

The main menu has an actual background now! It's a holographic table of things fighting, like a futuristic war room. I'm pumped, go take a look!

## General/Quality of Life Improvements

- Import an Army Forge share link straight from the lobby with the new **Import** button. No need to go through the Army Forge screen first.
- The pre-built army lists were rebuilt by hand for 3.6, so every one is legal under the new army-building rules with sensible loadouts.
- Rule tooltips wrap properly for the longer 3.6 rule texts, and the Army Forge shows which mount or crew a weapon belongs to.
- While moving, there's a new option to automatically re-order your formation to maximize how far they can move.

## Bug Fixes

- A move the game rejected ("Breaks cohesion") could end the whole game with a Game Over screen. It now just asks you again. Thanks to akagiz on Discord for reporting it.
- After a unit countered a charge, the game sometimes shuffled the chargers forward instead of the defenders. Found while play-testing 3.6.
- A hero's aura wasn't applying to the unit it had joined in some cases.
- Weapon teams were giving their Tough value to every model in the squad instead of just the team. Thanks to rckwzrd for reporting it.
- Units waiting in Ambush showed as "Activated" in the army list.
- Picking a special ability at the start of an activation locked you in; you can now back out. Thanks to rckwzrd for reporting it.
- You can now deploy a unit inside a transport that's still in reserve. Thanks to Neremsa for reporting it.
- Cavalry and other long bases no longer stack on top of each other when you drag a packed group at an angle. This was the cause of some issues that vic reported via the bug tracker, so thanks, vic! (Also you're the first to ever use the bug reporting feature!)


## Privacy note

The game now collects optional, anonymous usage stats (how often it's launched, which screens get used, games started and finished) so I can see what people actually play. You'll see a notice the first time you run this version to give you the chance to opt out, and you can switch it off in Options any time. Nothing about your armies, your name, or your machine is sent. Details are in the [README's privacy section](https://github.com/ckorris/FutureOfDarkGrimness#privacy).

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
