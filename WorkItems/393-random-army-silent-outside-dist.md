# 393 — Random Army does nothing outside a dist build (silent empty catalog)

**Status**: todo — **HIGH PRIORITY (Chris, 2026-09-04)**
**Related**: #372 (starter armies), #388 (human slots get a starter army), `scripts/build-dist.sh`

## Goal
The lobby's Random Army button (and the automatic starter-army roll for bots) works from ANY way the
app is launched — a `dotnet run`, a Visual Studio F5 on Windows, a dist archive — and when it cannot
work it says so in the lobby instead of doing nothing. Done means: pressing Random Army from a fresh
VS build on Windows assigns an army to the row, and to an added bot, with no manual folder copying.

## Notes
- 2026-09-04: Reported by Chris from his Windows laptop, running the `tactician-bc` checkout from
  Visual Studio: "The Random Army button isn't working for me or the bot." Filed by Claude from the
  Linux box without a Windows repro. Leading cause found by READING, not yet by running:
  - `LobbyScreen.SharedArmyCatalog` -> `ArmyCatalog` scans one folder for `*.fdgarmy`; when the
    folder is missing it returns an empty list, and `AssignRandomArmy` is documented to "silently do
    nothing when the folder holds no readable armies" (`LobbyScreen.cs` ~L496).
  - `scripts/build-dist.sh` L113-116: the app "looks for an armies folder beside the executable", and
    the DIST build copies the repo's `armies/` there. `FdgRaylib.csproj` copies only `Assets\**` to the
    output. A Debug/Release build tree (`FdgRaylib/bin/<cfg>/net8.0/`) has **no `armies/` folder** -
    confirmed on the Linux box. So every non-dist launch has an empty catalog, and the button is a
    silent no-op for humans and bots. Not Windows-specific; Windows-from-VS is just how it was noticed.
  - Immediate workaround for a build-tree run: copy the repo's `armies/` folder next to the built
    `FdgRaylib.exe` (e.g. into `FdgRaylib\bin\Debug\net8.0\`), or use Load Army and pick a file from
    the repo's `armies/` folder by hand.
- Candidate fixes (pick at implementation): (a) `<Content Include="..\armies\**">` with
  `CopyToOutputDirectory` so build trees match dist; (b) fall back to the repo `armies/` when the
  beside-the-exe folder is absent (dev-tree detection); (c) either way, surface "no armies folder found
  at <path>" in the lobby's launch-problems line rather than staying silent - the silence is the part
  that cost a flight's worth of games.

## Decisions
- Filed as its own number rather than a note on #372/#388: it is a launch-path/packaging gap, not a
  starter-army rule, and other items (the C-side random-army extraction in campaign step 12) will
  want to reference it.

## Outcome
(open)
