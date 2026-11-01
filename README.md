# Future of Dark Grimness: A New Way to Play GrimDark Future



<img width="3829" height="2066" alt="Screenshot from 2026-08-15 15-29-15" src="https://github.com/user-attachments/assets/57c96d44-4360-4ef7-9077-51a42c22e0f0" />


This is GrimDark Future (and Age of Fantasy) as a video game. Use armies imported from the official Army Forge to play a quick 2k solo game in 20 minutes with the bot, battle your friends online, or team up with them for a comp-stomp. It’s the simplest, fastest way to play. 

This is a fan project that’s not officially affiliated with OnePageRules.

<br>

## Rules Version: OPR 3.6.0

This release plays the OnePageRules **v3.6.0** core rules, for both Grimdark Future and Age of Fantasy. The Army Forge screen comes with all **48 Grimdark Future and 40 Age of Fantasy** official army books, under their 3.6 names: **Voidspace Stalkers** is new, the Wormhole Daemons of Change / Lust / Plague / War books (Rift Daemons in Age of Fantasy) are now **Daemons of Change / Lust / Plague / War**, and Saurian Starhost is now **Saurian Starhosts**.

**What 3.6 changed for players:** melee reach is 3" instead of 2", and the side that *took* the most wounds loses the melee. Impact is rolled per model before anyone strikes, and Counter's "fewer Impact dice" half is now its own rule, Brace. Fatigued units no longer trigger "on a 6" rules with their 6s. There are new core rules: Parry (Age of Fantasy hand weapons), Assault weapons that count as both ranged and melee, Cumbersome artillery, and Spell Amplifier. Indirect only ignores line of sight when holding, but no longer gets -1 to hit after moving. Scouts deploy within 9" of their zone and over 9" from enemies. Unit-wide rules like Fearless, Stealth and Regeneration need *most* models to have them instead of all. Tough(9)+ Heroes lend their Quality for morale tests to units within 6". Dangerous terrain rolls as many dice as the wounds it would take to destroy the unit. Transports are entered by touching them. Each spell now says for itself whether it needs line of sight. Army building adds max. 1 unit per 200pts and max. 1 model or Tough per 20pts. Every army book was re-priced, with many weapons renamed and new spells.

**Older files:** army files (`.fdgarmy`) made for 3.5 still load and play, but with 3.6 rules. The lobby shows an amber "built for OPR v3.5.x; it plays with 3.6 rules" notice when one is picked. Army Forge share links import as 3.6 lists, since Army Forge now serves every list with its 3.6 books. Saved games (`.fdgsave`) from earlier versions load too and play with 3.6 rules, keeping the 3.5 armies they were saved with; the resume lobby shows an amber "This save predates the OPR 3.6 upgrade; it plays with 3.6 rules" notice (saves now carry the rules version they were written under, so a save from another ruleset is named the same way). It never blocks the resume, but a fresh game is still the cleaner option.

<br>

## More than a Virtual Tabletop

<img width="712" height="401" alt="TerrainObjZonePick_small" src="https://github.com/user-attachments/assets/90da9357-68bf-46c4-8ebf-fbb84a56ee9d" />




The game enforces all rules from start to finish, prompting every stage of play, from placing terrain, to blasting your opponents into oblivion, to announcing the victor. With all the setup, measurements, and rules calculation handled, you spend your time analyzing the board and executing strategies. 

The result is a highly-streamlined form of play. Games against humans can take less than half the time, and games against the bot can take as little as 15-30 minutes.

Playing in-person with a real table and real models will always be the best experience. Computers can never replace that. But this is the next best thing. 

<br>

## Play Against Bots

<img width="500" height="401" alt="CombatCloseUp_1_cropped_small" src="https://github.com/user-attachments/assets/1431bbd2-3a3d-4e8a-a44a-41dda719f6d4" />


TacticianBot is smart enough to give a human a decent challenge. It’s perfect for trying new armies or practicing against a rough match-up. It can play with any army list, and factors in the strengths and weaknesses of each unit on the table when making decisions.

The game also includes DerpBot, which emulates the solo AI rules from OnePageRules. It’s good for learning the game, but it won’t be winning any tournaments.

Bots can join singleplayer or multiplayer games. 

<br>

## Play with your Friends

<img width="1032" height="610" alt="Screenshot from 2026-08-15 15-12-08" src="https://github.com/user-attachments/assets/6f634a27-ecc1-44da-9fa9-cd8dcc67269c" />

You can host a game that’s findable via a server list, or connect directly via the IP address listed in the lobby. (If your server doesn’t get listed, try forwarding port 6389.)

You can also add local players for hotseat-style, or combine them with more than one person playing on the host computer.

<br>

## Other features:

- An option to randomize terrain/objective placement to get into the fight faster.
- “Probabilistic” mode that uses the most likely outcome in every calculation. 5 attacks at Quality 4 will always deal 2.5 wounds. There’s a few exceptions made for binary outcomes like morale tests, because there’s no practical way to split that result.
- UI that previews what you can hit (and what can hit you) before every move.
- Options for common house rules, like having to declare all your shooting all at once, or being able to ignore thin cover that you’re right up against.
- Saving and loading games.

<br>

## Installation

Download the latest release for your platform from the [Releases page](https://github.com/ckorris/FutureOfDarkGrimness/releases) and extract it anywhere. 

- On **Windows**, unzip and run `FdgRaylib.exe`. 
- On **Linux**, untar and run `./FdgRaylib`. 
- On **macOS**, grab `osx-arm64` for Apple Silicon or `osx-x64` for Intel, untar, then from a Terminal in that folder run `xattr -dr com.apple.quarantine .` followed by `./FdgRaylib` (one-time step, since Gatekeeper blocks unsigned apps). Note that I haven't actually tried this as I don't own a Mac.

### Windows will warn you about this app

The first time you run `FdgRaylib.exe`, SmartScreen may show a "Windows protected your PC" dialog. That's because the app is new and hasn't built up a download reputation yet, not because anything is wrong with it. Click **More info**, then **Run anyway**.

<br>

## Uninstalling

The app is portable - there's no installer. To remove it completely:

- Delete the folder you extracted.
- Delete its settings folder: `%APPDATA%\FDG` on Windows, or `~/.config/fdg` on Linux and macOS. It holds a single `config.json` (your player name and last-used lobby settings).

Any games you saved stay wherever you chose to save them; delete those `.fdgsave` files yourself if you want them gone.

<br>

## Importing an army

If you want to use an army from OnePageRules' Army Forge, then open that army. In the top right, click the three dots, followed by Share List, and Share as Link. 

<img width="449" height="581" alt="Screenshot from 2026-08-15 15-53-58" src="https://github.com/user-attachments/assets/d3fbf82b-5760-4df4-8529-67c801a717dc" />
<img width="1219" height="241" alt="Screenshot from 2026-08-15 15-58-12" src="https://github.com/user-attachments/assets/7190af7c-afeb-4d4b-aee3-2babb320e1ec" />


Copy the link, then in Future of Dark Grimness, open the Army Forge. Press Import in the top right, and paste the link in the prompt. Then press Save As and put it in your Armies folder.

<img width="1090" height="442" alt="Screenshot from 2026-08-15 15-59-35" src="https://github.com/user-attachments/assets/150a5b67-9591-4d37-914f-14734689c659" />
<img width="925" height="651" alt="Screenshot from 2026-08-15 15-59-46" src="https://github.com/user-attachments/assets/feaaa640-31a6-4e59-8d67-6fce54077578" />


## Playing your First Game

The best way to try it out is to play a solo game against a bot. Launch the game, open Host, and choose a point amount on the right hand side. Press the button to add a "Tactician Bot. Then load an army for each of you from one of the premade armies in the "Armies" folder. Press Launch, and enjoy!


<br>



## Bug Reporting

I’ve playtested this a lot, but there’s bound to be bugs. If you see a bug, please use the Report Bug option in the escape menu to tell me. Or you can ping me on Discord [here](https://discord.com/channels/610199287346888743/1538297424865403032) or open up a Github issue.

Here’s places where I most expect bugs:

- Army-specific special rules, especially more complex ones.
- Distances/collisions with rectangular bases.
- Games that were saved and then loaded.
- The client-side experience over a network.
- Bot behavior in a really crowded game.

<br>

## Unsupported

There's a few things yet to come: 

- **3.6 terrain types:** Dense, Unstable, Steep and Climbable terrain from the 3.6 Beginner's Guide are not in yet. Terrain plays with the existing types.
- **3D movement**: The engine _mostly_ supports three dimensions, but the UI doesn't have a good way of doing that since it's all rendered in 2D. That said, if someone used the rules engine with a 3D game engine, it's mostly a matter of implementing colliders.
- **Campaign Rules and Multi-Army Lists:** I intend to do both. 
- **Replays:** Would also be great to make battle reports.
- **Observer Mode:** Shouldn't be that hard, but I haven't done it yet.  

<br>


## Dev Stuff:

This is the front-end application implementing the FutureOfDarkGrimness rules engine, made with Raylib. See the [rules engine repo](https://github.com/ckorris/FutureOfDarkGrimness-RulesEngine). The rules engine is front-end agnostic; it was originally targeting the Stride 3D engine until I switched to Linux, and decided to make something more streamlined over stylish.

If you want to build your own front-end, or use the rules engine to power an existing VTT interface, or create an entirely different tool, please feel free! 

<img width="3831" height="2067" alt="Screenshot from 2026-08-15 15-04-22" src="https://github.com/user-attachments/assets/15ef7a93-7978-4a2a-8d03-66dc24e02e68" />

<img width="3834" height="2062" alt="Screenshot from 2026-08-15 15-01-03" src="https://github.com/user-attachments/assets/396c3309-ba5d-403d-bb13-cf2603029549" />


## Similar Projects

I'm not the first person to do something like this. Check out some other projects that bring OnePageRules to the screen: 

**Niemandsland - A 3D Tabletop Simulator for OnePageRules Game Systems**

Discord post [here](https://discord.com/channels/610199287346888743/1520340158409605242/1520340158409605242), GitHub [here](https://github.com/DutchMaxwell/Niemandsland/releases/tag/v0.3.12.0-alpha).

**Udo's Wargaming Simulator**

Discord post [here](https://discord.com/channels/610199287346888743/1523007809866957030/1523007809866957030), website [here](https://udos3dworld.com/WargamingSimulator/).

**Grimdark Future Tactics**

Discord post [here](https://discord.com/channels/610199287346888743/1523023020615143655/1523023020615143655), website [here](https://www.joshuaates.com/OnePageRulesGame/index.html).


## Privacy

The app sends anonymous usage statistics, described below, which you can turn off. Apart from those, nothing is transferred over the network except as a result of these user-initiated actions:

- **Hosting a game** opens a listening TCP port (default 6389), asks your router on your local network (via UPnP/NAT-PMP) to forward that port, and fetches your public IP address from api.ipify.org so the lobby can show you the address other players should connect to.
- **Hosting with "List publicly" checked** registers the game with the project's public server list (a small service run by the maintainer) and refreshes the entry every 30 seconds: the server name you typed, port, player count, game state, protocol/compatibility version, and whether a password is set (never the password itself). The list service records your public IP so other players can connect to you; the entry is visible to anyone browsing the server list and expires about 90 seconds after you stop hosting. Browsing the server list downloads that list and sends nothing about you beyond the request itself.
- **Joining a game** connects directly to the host address you enter or pick from the list.
- **Importing an Army Forge share link** fetches that list and its army books from OnePageRules' Army Forge API.
- **Report Bug** (in the escape menu) saves a report locally and uploads it to the maintainer: your description, build version, OS, in-game player name, the game log (including the game's chat), the tail of a crash log if one exists, and - when you are the host - a save of the current game, which includes every player's army list. All of this is disclosed on the report screen before you send, and the other players in the game are notified that a report was sent.

### Analytics

The joy of making a game like this is knowing that people are playing and enjoying it. To help me understand that, I added some anonymous analytics. You'll get a popup on first launch offering to opt out (or you can do it in Options), but if not, I'll get stats on:

- How many people have played, with unique players tracked by a random ID that can't be traced to you.
- How long your previous session lasted, and whether it closed normally.
- What game version, operating system, and CPU type people are using.
- If people are actually using the Combat Calculator.
- Game points, game system (GDF vs. AoF), each army's faction, how many human, AI and online players, which AI types, teams, the lobby settings, and how and when the game ended.



This project is licensed under the [MIT License](LICENSE). The same applies to the [rules engine repo](https://github.com/ckorris/FutureOfDarkGrimness-RulesEngine).

**Scope:** the MIT license covers the source code and the original assets authored for this project. GrimDark Future, Army Forge, and all associated game names, faction and unit names, rules, and game data are the property of OnePageRules, used here unofficially and without affiliation or endorsement — nothing in the MIT license grants any rights to that material. The contents of the `armies` folder are Army Forge exports provided for convenience and are not relicensed.



