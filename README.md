# Future of Dark Grimness: A New Way to Play GrimDark Future

This is GrimDark Future as a video game. Use armies imported from the official Army Forge to play a quick 2k solo game in 20 minutes with the bot, battle your friends online, or team up with them for a comp-stomp. It’s the simplest, fastest way to play. 

This is a fan project that’s not officially affiliated with OnePageRules.



## More than a Virtual Tabletop

[Another gif of gameplay]

The game enforces all rules from start to finish, prompting every stage of play, from placing terrain to blasting your opponents into oblivion, to announcing the victor. With all the setup, measurements, and rules calculation handled, you spend your time analyzing the board and executing strategies. 

The result is a highly-streamlined form of play. Games against humans can take less than half the time, and games against the bot can take as little as 15-30 minutes.

Playing in-person with a real table and real models will always be the best experience. Computers can never replace that. But this is the next best thing. 


## Play Against Bots

[TacticianBot doing something like spells or whatever]

TacticianBot is smart enough to give a human a decent challenge. It’s perfect for trying new armies or practicing against a rough match-up. It can play with any army list, and factors in the strengths and weaknesses of each unit on the table when making decisions.

The game also includes DerpBot, which emulates the solo AI rules from OnePageRules. It’s good for learning the game, but it won’t be winning any tournaments.

Bots can join singleplayer or multiplayer games. 


## Play with your Friends

[Multiplayer lobby, chat, adding bot]

You can host a game that’s findable via a server list, or connect directly via the IP address listed in the lobby. (If your server doesn’t get listed, try forwarding port 6389.)

You can also add local players for hotseat-style, or combine them with more than one person playing on the host computer.


## Other features:

- An option to randomize terrain/objective placement to get into the fight faster.
- “Probabilistic” mode that uses the most likely outcome in every calculation. 5 attacks and Quality 4 will always deal 2.5 wounds. There’s a few exceptions made for binary outcomes like morale tests, because there’s no practical way to split that result.
- UI that previews what you can hit (and what can hit you) before every move.
- Options for common house rules, like having to declare all your shooting all at once, or being able to ignore thin cover that you’re right up against.
- Saving and loading games.


## Bug Reporting

I’ve playtested this a lot, but there’s bound to be bugs. If you see a bug, please use the Report Bug option in the escape menu to tell me. Or you can ping me on Discord [LINK] or open up a Github issue.

Here’s places where I most expect bugs:
Army-specific special rules, especially more complex ones.
Distances/collisions with rectangular bases.
Games that were saved and then loaded.
The client-side experience over a network.
Bot behavior in a really crowded game.

## Dev Stuff:

This is the front-end application implementing the FutureOfDarkGrimness rules engine, made with Raylib. See the [rules engine repo](https://github.com/ckorris/FutureOfDarkGrimness-RulesEngine). The rules engine is front-end agnostic; it was originally targeting the Stride 3D engine until I switched to Linux, and decided to make something more streamlined over stylish.

If you want to build your own front-end, or use the rules engine to power an existing VTT interface, or create an entirely different tool, please feel free! 
