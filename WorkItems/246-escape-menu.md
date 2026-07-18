# 246 — In-game escape menu; retire the bottom-left toolbar

**Status:** Plan written 2026-07-18 (design session with Fable). Awaiting Chris's sign-off on the
decision forks below, then implement slice by slice. Number 246 verified free against the index,
archive, and Reconciliations.md as of 2026-07-18 — re-verify against origin before first push.

## Goal

The bottom-left in-game toolbar (the "sidebar": a 7-button vertical stack + 3 hotkey-hint lines in
`TableTooltipOverlay.Draw`, `FdgRaylib/Rendering/TableTooltipOverlay.cs:102-151`) is ugly and
overloaded. Replace it with an Escape-opened in-game menu (Save / Load / Options / quit paths) and
collapse the always-visible footprint to a single small "Menu" button. Delete the Field GPU/CPU
button outright (GPU path is proven; auto-fallback stays).

This also fills a real gap: today there is **no way to leave a game in progress** — "Return to Main
Menu" only exists on the game-over card (`RaylibRenderer.cs:907`).

## Current state inventory

Always-visible in-game UI today:

| Element | Where | Fate |
|---|---|---|
| Status HUD (round, scores) | top of table area | stays |
| Dice caption strip (#245) | bottom center | stays |
| Resolver panel + console (Chat/Log/Debug) | right column | stays |
| Toolbar: Labels ON/OFF (hotkey L) | bottom-left stack | -> Options; hotkey stays |
| Toolbar: Grid ON/OFF | bottom-left stack | -> Options |
| Toolbar: Tokens ALL/std (dev, hotkey T) | bottom-left stack | -> Options (dev row); hotkey stays |
| Toolbar: Save Game (host only) | bottom-left stack | -> Escape menu |
| Toolbar: Threat ON/OFF (hotkey F) | bottom-left stack | -> Options; hotkey stays (primary access) |
| Toolbar: Field GPU/CPU | bottom-left stack | **deleted** (UI only — see below) |
| Toolbar: Anchor Target/Self | bottom-left stack | -> Options |
| Hotkey hint lines (measure/zoom/pan) | bottom-left stack | -> Controls list inside the menu |

Rationale: everything in that stack is either infrequent (Save), set-once (Grid, Anchor, Tokens), or
already hotkeyed for mid-decision use (L, T, F). None of it earns permanent screen space. After this
change the only always-visible addition is one small **"Menu" button** pinned bottom-left (same spot,
one button instead of seven) for discoverability; Esc is the primary path.

## Design

### The menu is not a pause

Multiplayer + the engine running on a background thread means the game does NOT stop while the menu
is open. Title it "Menu", not "Pause". The table stays visible behind a translucent dim; animations
keep playing. While open, gameplay input is suppressed (see Escape routing).

### Menu contents (centered modal, ~360px, full-width buttons)

1. **Resume** — close (Esc does the same).
2. **Save Game** — host only (`_saveGameToJson` non-null only on host, `TableTooltipOverlay.cs:38-40`).
   Reuses the existing `HandleSaveGame` TinyDialogs flow (`TableTooltipOverlay.cs:154-166`) — move it
   (and `SaveFilter`) into the new overlay. Clients see a disabled row with "Host controls saving"
   (client-initiated save is #054, unchanged).
3. **Load Game** — host only. Confirm ("Ends the current game for all players"), then `ExitGame()` +
   `NavigateTo(MainMenu)` + invoke the existing main-menu load flow (`Program.cs:329-336`). Refactor
   that lambda into a named method assigned to both `MainMenu.OnLoadGameClicked` and the new menu's
   `OnLoadGame` — do not duplicate the dialog/resume logic.
4. **Options** — swaps the window content in place (Back button returns). Sections:
   - *Display*: Labels (shows "L"), Grid, Token chips std/all (shows "T", marked dev).
   - *Tactical overlay*: Threat frontiers (shows "F"), Field anchor Target/Self (one-line explanation;
     `TacticalOverlayConfig.GhostAnchoredField`, `TacticalOverlayConfig.cs:80-84`).
   - *Audio*: master volume slider -> `Raylib.SetMasterVolume` via a new `AudioManager.SetMasterVolume`
     (no volume API exists today).
   - *Controls* (read-only): Ctrl+drag measure, Ctrl+wheel zoom, middle-drag pan, L/T/F toggles,
     G formation cycle during moves, Enter auto-assign, Esc cancel/menu.
5. **Return to Main Menu** — confirm, then `ExitGame()` + `NavigateTo(MainMenu)` (same pair the
   game-over card uses, `RaylibRenderer.cs:907-911`). Host leaving ends the game for clients via the
   existing host-loss detection; say so in the confirm text on the host.
6. **Quit to Desktop** — confirm, then request window close (`_closeRequested`).

All new strings ASCII-only (project convention — no arrows/ellipsis/em-dashes in UI text).

### Escape routing (the load-bearing design problem)

Esc already means "cancel the thing I'm doing" in four places, and the raylib exit key is
deliberately disabled (`RaylibRenderer.cs:338-341` — do not change that):

- `GuiYesNoResolver.cs:77` — Esc = "No" while a yes/no dialog is up
- `GuiPlaceObjectiveResolver.cs:86`, `GuiPlaceOneTerrainResolver.cs:103,132` — Esc cancels an armed placement
- `TacticalOverlayController` — Esc clears pins (`TacticalOverlayConfig.ClearPinsKey`, config line 89)

**Rule: innermost context wins; the menu opens only when nothing consumed Esc.** Implementation:

- New `EscapeRouter` static (Rendering/): `BeginFrame()` at the top of the in-game frame;
  `TryConsume()` returns true once per frame and always false while the menu is open.
- Convert the four consumers to call `TryConsume()` **only when their condition actually holds**
  (dialog visible / placement armed / pins exist) — they already check those conditions; this is a
  one-line wrap at each site, not a redesign.
- All four consumers run before the end-of-frame in the existing draw order
  (`RaylibRenderer.cs:455-497`: tactical `UpdateInput` -> tooltip/toolbar -> resolver draw), so after
  the resolver draw the renderer checks: Esc pressed && nothing consumed && menu closed -> open menu.
  Menu open -> menu itself handles Esc as close.
- While the menu is open: skip canvas click routing (the `interactionHandler` click in
  `TableTooltipOverlay.Draw`) and gate the L/T/F hotkeys — explicit `EscapeMenuOverlay.IsOpen` checks;
  don't rely on ImGui modal focus for correctness.

### Field button removal

UI only. Keep `TacticalOverlayConfig.UseGpuField` (default true), the CPU compositor path, the
GPU-init auto-fallback, and `FieldHarness` (the CPU picture is the pixel-diff reference — see the
comment at `TacticalOverlayConfig.cs:75-78`). Only the toolbar button and its cache-invalidation
click handler go away. If a driver problem ever needs the manual toggle back it can land in Options
under a dev row.

## Decision forks (recommendations baked into the plan; flag if Chris disagrees)

1. **Esc priority**: cancel-first, menu-on-idle (recommended, encoded above) vs menu-always.
2. **Load Game in the menu**: included as confirm-then-quit-and-load (recommended) vs omitted.
3. **Always-visible remainder**: single Menu button (recommended) vs a slim icon strip of view toggles.
4. **Threat toggle**: hotkey F + Options entry (recommended) vs keeping a dedicated on-screen button.
5. **Settings persistence** (S4): small `fdg-settings.json` beside the exe persisting Labels, Grid,
   Anchor, volume (recommended, optional slice) vs statics-only as today.

## Implementation slices (one at a time, verify + commit each)

- **S1 — Menu shell + Esc routing.** `EscapeRouter`, `EscapeMenuOverlay` (Resume / Return to Main
  Menu / Quit to Desktop, both confirmed), the small Menu button, input suppression while open.
  App-side only; no engine changes.
- **S2 — Save + Load.** Move save into the menu (host gating), wire Load via the shared Program.cs
  method, remove the toolbar Save button.
- **S3 — Options + toolbar retirement.** Options panel (Display / Tactical / Audio / Controls),
  `AudioManager.SetMasterVolume`, delete the toolbar window, delete the Field button.
- **S4 (optional) — Persistence.** `fdg-settings.json`, load at startup, save on change.

## Verification

GUI-only work; the headless path is untouched. Per slice: engine suite green
(`dotnet test FutureOfDarkGrimness/FutureOfDarkGrimness.csproj`), full `dotnet build`, headless smoke
(`printf "2\n2\n" | dotnet run --project FdgRaylib/FdgRaylib.csproj -- --headless`) exits 0.
Hand-verify checklist (record results here):

- [ ] Esc with nothing armed opens the menu; Esc again closes it.
- [ ] Esc with a yes/no dialog up answers No and does NOT open the menu (same for armed placement, pins).
- [ ] Menu open: table clicks do nothing, animations keep playing behind the dim, L/T/F do nothing.
- [ ] Save from menu (host) writes a loadable .fdgsave; client sees the disabled row.
- [ ] Load from menu: confirm -> main menu -> file dialog -> lobby resume (existing #052 flow).
- [ ] Return to Main Menu mid-game: host confirm ends game for a connected client cleanly.
- [ ] Quit to Desktop confirm closes the window.
- [ ] Options toggles behave identically to the old toolbar buttons; volume slider audibly works.
- [ ] Field button gone; opportunity field still renders (GPU), auto-fallback path still compiles.

## Relations

- Substantially resolves the toolbar half of #229 (exploratory "bottom in-game menu should be its own
  panel") — close or re-scope #229 when this lands.
- #054 (client-initiated save) slots into the menu's disabled Save row when built.
- Not in scope: pausing the engine, options on the main menu screen, key remapping.
