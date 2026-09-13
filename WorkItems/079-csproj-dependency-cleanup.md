# 079 — csproj dependency cleanup

**Status:** Done

## Goal

Audit §1 cleanup of `FutureOfDarkGrimness.csproj`:
- Drop `System.Drawing.Common` (8.0.1) — Windows-only on .NET 8 (GDI+); throws at runtime on Linux if ever exercised.
- Delete the commented-out duplicate `SixLabors.ImageSharp` line and its `//TODO: Put this back.`

## Notes

### 2026-06-13
- Verified `System.Drawing` usage in the engine before removing the package: only `System.Drawing.Color` is referenced (5 files under `TempVisuals/` + one stray `using` in `SerializableVisuals/IMeshProvider.cs`). `Color` lives in `System.Drawing.Primitives`, which ships in the .NET 8 base framework — it does **not** require the `System.Drawing.Common` package. No `Bitmap`/`Graphics`/GDI+ usage anywhere, so the drop is safe.
- Removed both lines (the dead ImageSharp comment + `System.Drawing.Common`). `SixLabors.ImageSharp` 3.1.11 remains as the single live reference.
- Verified: `dotnet build` clean (0 errors), engine suite 424/424 green, headless smoke (`printf "2\n2\n" ... --headless`) exits 0 and runs to "Game ended: It's a tie!".

## Decisions

- Left the unused `using System.Drawing;` lines in place (they resolve fine against the base framework and removing them is out of scope for a dependency-cleanup item).

## Outcome

`System.Drawing.Common` and the dead ImageSharp comment removed from the engine csproj. Submodule committed first, superproject pointer bumped in a follow-up commit per the submodule-first cadence.
