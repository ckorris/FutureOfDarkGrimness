# 460 — Import an Army Forge link from the lobby

**Status**: in-progress - implemented + suites green, awaiting GUI hand-verify
**Related**: #241 (share-link importer + Forge popup), #403 (import-first Forge), #356 (Save As embeds the
editable session), #402 (lobby launch gate: points / Army Source), #153 (catalog-validation confirm)

## Goal
Playing an Army Forge list today takes three trips: open the Forge screen, import + Save As, then Load Army
in the lobby and find the file. Collapse that into the lobby itself. Every roster row that can Load Army also
gets an **Import** button next to it. It opens a slimmed-down version of the Forge's #241 popup: a field to paste
the share link, instructions for getting the link out of Army Forge, the same issue readout (Army Forge list
errors, import warnings, rules the engine won't enforce), a **Save a copy to my armies folder** checkbox, and
only Import / Cancel as actions. A list that breaks the lobby's settings (over the points limit, or a game
system the Army Source setting doesn't allow) can still be imported, behind a confirmation that says what's
wrong - the host may change the settings afterwards. Everything is sized in em / relative to the viewport, so it
holds up across resolutions and window sizes.

Done = the button + popup work for host and client rows (same permission gate as Load Army), the assigned army
is the same object Save As would write (#356 shape), the optional save lands in the armies folder, the
lobby-settings confirm fires on blocking problems, the layout logic and wording have unit tests, and the headless
smoke stays green.

## Notes
- 2026-11-01 (later): Implemented. Engine: `LaunchGate.SettingsProblems(army, limit, allowed)` - the per-army
  half of `BlockingProblems`, which now prefixes the player name onto its lines (output unchanged) + a test
  pinning that the two agree. App: `LobbyImportPopup` (popup), `LobbyArmyImport` (ImGui-free wording, save
  name/folder/no-overwrite) + `LobbyArmyImportTests` (9), `UserConfig.ImportSavesCopy`, a sized
  `UiButton.NavigateSmall` overload, the roster's 2x2 Actions grid + vertical centring, README import steps.
  `ArmyForgeScreen.SerializeArmy` made internal to share the runtime-type serialization rule. Verified: engine
  3394 passed / 1 skipped, app 3149 passed, `dotnet build` clean, headless smoke exit 0 (4 rounds, tie).
  NOT yet verified: anything on screen - the GUI checks below are the owner's.

  GUI hand-verify checklist:
  1. Lobby roster: every row's Actions cell is a 2x2 grid (Load Army | Import / Random Army | Remove), equal
     button widths, no clipping; the other cells sit vertically centred. Check fullscreen and a small window.
     A resume lobby shows Local | Tactician / Strategist | DerpBot in the same grid.
  2. Import on your row -> popup names the row, shows the Army Forge instructions; Paste a share link -> it
     fetches at once, preview lists units + any list errors / warnings / inert rules. Typing a link fetches
     after a short pause; junk text shows the "does not look like a link" hint.
  3. Import on a clean list -> slot gets the army, popup closes. With "Save a copy" ticked, the file appears
     at the path the popup showed (e.g. `armies/GDF/2k - Orks - Name.fdgarmy`); importing the same list again
     writes `... (2).fdgarmy`. The tick is remembered across launches.
  4. Lower the lobby's Army Points below the list (or set Army Source to the other system) -> the preview
     shows the red line(s); Import raises "Import anyway?" (Cancel focused); Import anyway assigns it and the
     roster cell reads red, LAUNCH blocked.
  5. As a client, Import on your own row works; Import is greyed on rows you can't modify.
  6. Shrink the window: popup stays centred, fits, preview scrolls rather than pushing Import off-screen.
- 2026-11-01: Filed from `origin/master` at `170ed4c` (index + archive max 458; both feature branches hold a
  conflicting 459 between themselves, so this took 460). Surveyed: `ArmyForgeShareService.FetchAndImportAsync`
  is already UI-agnostic; the Forge popup is `ArmyForgeScreen.DrawImportModal`; lobby rows are
  `LobbyScreen.DrawPlayerList` (Actions column: Load Army / Random Army / Remove); the per-army settings check can
  reuse `LaunchGate.BlockingProblems` with a single-army list, and both host and client view-models expose
  `ArmyPoints` + `AllowedGameSystems`. Design questions sent to the owner before building.

## Decisions
- **Fetch flow: auto-fetch (owner, 2026-11-01).** No Fetch button: Paste / Enter fetch immediately, typing
  fetches after a 0.6s pause, and a changed link cancels the in-flight fetch. Import stays greyed until a
  preview is in, so issues are always seen before the army is assigned. Re-pasting the same link is the
  retry after a failed fetch (keeps the button set to Import / Cancel).
- **Paste button kept (owner).** Same reason as #403: no right-click paste in an ImGui field.
- **Saved copy: auto-named, never overwrites (owner).** Owner asked for the shipped armies' pattern with the
  points in front. Army Forge lists carry their target (`pointsLimit`), so the name uses that the way the
  shipped files do (`2k - ...`; an odd target is written out, `1500 - ...`) and falls back to the actual total
  (`1985 - ...`) only when there is none. Then the faction (skipped when the list name already contains it),
  then the list name (skipped when Army Forge had none). Goes in `armies/GDF/` or `armies/AoF/` when that
  subfolder exists, else `armies/`. Collisions get ` (2)`, ` (3)`. The checkbox defaults on and is
  remembered (`UserConfig.ImportSavesCopy`); disabled when no armies folder exists. The copy is written
  BEFORE assigning - a failed write leaves the slot alone and says why (#307: never report a write that did
  not happen).
- **Row layout: 2x2 grid with taller rows (owner).** The Actions column is now FIXED-width, measured from the
  labels at the frame's font scale, instead of a 0.26 stretch weight - so the buttons can't clip, and the
  stretch columns absorb a narrow window (their text already shrinks, #405). The old +25% cell padding is
  mostly replaced by the second button line (rows are ~30% taller than before). Resume rows use the same
  grid so the column fits both modes.
- **Which army is assigned:** `ArmyForgeScreen.ImportedFileToWrite` - Army Forge's verbatim units and points,
  with the editable session attached when a bundled book matched (#356). Same object as the saved copy and
  as a Forge Save As, so a lobby import, a reload of its copy, and a Forge-saved file all behave the same at
  launch.
- **"Breaks the lobby settings" = `LaunchGate.SettingsProblems`** (over points, wrong Army Source), extracted
  engine-side so the popup and the LAUNCH gate are one check. Catalog-validation errors (the #153
  "Launch anyway?" half) are NOT part of the import confirm: Army Forge's own list errors are already shown
  in the preview, and the launch confirm still catches ours.
- **Forge pricing reconciliation dropped from this popup.** It is about Open in Forge (our pricing), which the
  lobby doesn't offer; the lobby plays Army Forge's numbers.
- **Popup scale:** font scale follows the lobby's window-to-monitor factor; widths in em capped to the
  viewport; preview height a third of the viewport within [5, 16] em; re-centred every frame so the growth
  when the preview arrives never pushes Import off a short window.
- Not done (minor, unrequested): a saved copy isn't added to the running Random Army catalog until the next
  launch (the catalog scans once per process).

## Outcome
