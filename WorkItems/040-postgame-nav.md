# 040 — Post-game navigation back to main menu (GUI)

**Status**: in-progress (awaiting GUI hand-verification)
**Related**: branch `040-postgame-nav`

## Goal
When a GUI game finishes, the player should be able to return to the main menu instead of being
left with a frozen window they have to close. Show a "Game Over" card with the result and a
**Return to Main Menu** button; clicking it tears down in-game state and navigates back. The user
must press the button — no auto-return.

## Notes

- 2026-06-21: Implemented host/local path.
  - **Detection gap found**: the GUI never received a game-end signal. Game-end flows
    `GameContext.NotifyGameEnded` → `FDGServer.OnGameEnded`; `CliApp` subscribes to that for headless,
    but the GUI lobby creates the `FDGServer` as a local and only hands the renderer an `IFDGGame`
    (no game-ended event). The only thing reaching the GUI at game-end was the replicated `BannerBeat`
    ("X wins!"), which would be fragile to string-match.
  - **Engine change (submodule)**: added `event Action<string>? OnGameEnded` to `ILobbyViewModel`;
    `LobbyViewModel_Host` forwards `server.OnGameEnded` in both the launch and resume paths;
    `LobbyViewModel_Client` declares it but never raises it (deferred — see below).
  - **App change**: `LobbyScreen.OnGameEnded` forwards the VM event; `Program.cs` wires it to
    `RaylibRenderer.ShowGameOver(result)`. `ShowGameOver` only stores the result (engine thread);
    `DrawGameOverOverlay` draws the centered card on the main thread; the button calls `ExitGame()`
    then `NavigateTo(MainMenu)`. `ExitGame()` unsubscribes the table-state handlers wired in
    `TransitionToGame`, clears the model/terrain/objective collections, and drops every per-game ref so
    a later launch starts clean. `OnModelPlaced` now null-guards `_tableState` (the per-model
    `OnPositionChanged` lambda can't be unsubscribed, so it must tolerate firing after teardown).
  - Engine suite 602/0; full build clean; headless smoke exits 0 (`Game ended: It's a tie!`).
  - GUI overlay needs hand-verification in the running app (display required).

## Decisions

- **Surface the result via the lobby VM, not by string-matching a banner.** The banner beat is the only
  app-side game-end signal today, but matching its text couples the front end to presentation wording and
  breaks silently if it changes. Forwarding `FDGServer.OnGameEnded` through `ILobbyViewModel` mirrors the
  existing `CliApp` path and is the only clean signal.
- **No auto-return.** Per the user: the player must click to leave so they can read the result / inspect
  the final board.
- **Networked client deferred.** A non-host client has no clean game-end signal — it only sees the
  replicated banner beat, not `FDGServer.OnGameEnded`. Returning a networked client to the menu needs a
  new game-ended network message (host broadcast on `NotifyGameEnded`). Explicitly out of scope this
  round; the client's `OnGameEnded` is declared (interface requirement) but never raised.

## Outcome
_(written when closed)_
