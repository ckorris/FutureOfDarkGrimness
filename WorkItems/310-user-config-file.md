# 310 — Per-user config file (player name + host settings)

**Status**: in-progress (implemented + tested; awaiting GUI hand-verify)
**Related**: #189 (listen port), #265 (table background), #271 (public listing)

## Goal

An install carries a config file it creates on first run, and the front end reads its defaults from
it instead of hardcoding them:

- **Player name** — one name for both hosting and joining, defaulting to `Newbie` on a fresh install
  (replacing the hardcoded "Mr. Host" / "Mrs. Client"), rewritten whenever the player hosts or joins
  under a different name.
- **Host setup** — the host dialog's server name, port and "List publicly" tick, plus the lobby
  settings panel (army points, terrain mode + its sub-options, objective mode, randomness, turn
  style, cover proximity rules), remembered from the last hosted game.
- **Not** the battlefield / table background: it is deliberately re-randomized every time a lobby
  opens, so remembering it would defeat the point.

## Notes

- 2026-08-02 (2): Two hand-verify findings, both fixed. (a) The Join dialog kept showing the name it
  was constructed with, because the modals seed their fields once at construction and only re-read the
  config in `Reset()` (cancel / success). `IAppScreen` gained an `OnShown()` default-no-op hook that
  `RaylibRenderer.NavigateTo` calls; both modals implement it as `Reset()`, so opening either one
  always shows the current saved name. (b) The ~1s freeze leaving a host lobby was NOT the config
  write (measured: 0.10 ms per save, 30 ms one-time serializer JIT at startup) - it was
  `NatPortMapper.Dispose()` blocking the UI thread for up to 2s on the router round-trip that removes
  the UPnP mapping. `Dispose()` is now non-blocking; app exit calls a new `DisposeBlocking()`, since
  the mapping is created with an indefinite lease and there is no later moment for a background
  removal to finish in. `FDGHost.Stop()` and `PublicListingService.Dispose()` were already
  non-blocking, so nothing else on that path waits.
- 2026-08-02: Implemented. `FdgRaylib/Config/UserConfig.cs` (config + store) and `HostGameSettings`
  (the lobby panel's saved half). Wired into `HostModal`, `ClientModal`, `LobbyScreen`, and
  `Program.cs` (startup `EnsureExists`, plus the Load Game flow's host name). 11 new tests in
  `FdgRaylib.Tests/UserConfigTests.cs`; engine 2550/2550, app 871/871, headless smoke exits 0 and
  writes `~/.config/fdg/config.json` with the defaults on a machine that had none.

## Decisions

- **Location: per-user OS config dir**, not next to the executable (which is where `listserver.url`
  lives). `%APPDATA%\FDG\config.json` on Windows, `~/.config/fdg/config.json` elsewhere — via
  `Environment.SpecialFolder.ApplicationData`, so downloading a fresh dist build into a new folder
  keeps your name and settings. `FDG_CONFIG_DIR` overrides the directory (tests, portable installs).
  Signed off by the user before building.
- **Scope: the host dialog fields ride along** with the lobby settings — server name, port, "List
  publicly". The password is never stored. The client's last host address is deliberately NOT stored
  (offered and declined at the design fork).
- **Written at two points, not on every edit.** The name is saved when a host actually starts
  listening / a join is accepted (so a failed bind or a rejected join doesn't rewrite the config);
  the lobby settings are captured when the lobby ends — LAUNCH or Back. Saving per edit would write
  the file once per value while dragging a slider. **Known gap, accepted:** closing the window
  outright from inside a lobby (no Back, no LAUNCH) doesn't save that lobby's settings. Add an
  app-exit flush if that turns out to bite.
- **Resume lobbies never write.** A resumed game's settings come from the save, not from this
  player's picks, so both `ApplyTo` and `PersistHostSettings` gate on `IsResumeMode` (and on
  `HasHostPrivileges` — a client's panel is a mirror of the host's).
- **Own DTO rather than serializing `GameSettings`.** The struct carries two fields that must not be
  remembered (`TableBackground`, `DiceSeed`) and a Newtonsoft-attributed computed property that STJ
  would emit as a redundant key. A new lobby setting needs a line here, which is the same commit that
  adds its `Set*` call to the panel.
- **Failure is silent-by-design.** Unreadable/corrupt/unwritable config falls back to defaults with a
  console line; it never blocks the app or surfaces an error to the player.

## Outcome

_(open)_

## Verify (GUI)

1. Fresh install (`rm ~/.config/fdg/config.json`) -> launch -> Host: the name reads `Newbie`.
2. Host as "Someone Else" on port 6390, tick List publicly, set Turn Style = Bolt Action, Army Points
   = 1500, Terrain Mode = Alternating: Points -> LAUNCH (or Back) -> quit -> relaunch -> Host: name,
   port, tick and every lobby setting come back as they were.
3. Join dialog leads with the same name; joining as a different name changes what the Host dialog
   leads with next time.
4. The battlefield still changes at random every time a lobby opens, and is not in the config file.
5. Load Game: the resumed lobby's host slot uses the saved name, its settings stay the save's, and
   quitting out of it does not overwrite the config's host settings.
