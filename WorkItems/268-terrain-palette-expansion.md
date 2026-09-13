# 268 — More terrain options, especially smaller impassible objects

**Status:** implemented 2026-07-23, awaiting GUI hand-verify
**Related:** #002 (terrain placement), #044 (externalize the pool to a JSON asset), #049 (lobby picker for
which layout file feeds each mode)

## Report

Add more terrain options, especially smaller impassible objects.

## The fork (resolved 2026-07-23)

`DefaultTerrainPool.Get()` fed **two** different things: AutoFromLayout placed its pieces *verbatim at
their design-time positions* (so the list literally is the generated map), and the Alternating-mode picker
used the same list as its template palette (where only shape/size/type matter). Appending to it would have
silently made every auto-generated map denser.

Owner chose **split the palette from the auto layout**: the auto map is unchanged, the picker gets the
full set.

## Fix

- `DefaultTerrainPool.Get()` — the auto layout, still the same 12 pieces.
- `DefaultTerrainPool.GetPalette()` — those 12 plus 18 new templates; `PlaceTerrainStage`'s Alternating
  branch now reads this instead of `Get().Pieces`.
- `TerrainPieceEntry.Name` — a new optional display name, defaulted empty so existing `.fdgterrain`
  layouts and hand-authored ones are unaffected. Both the GUI picker and the CLI list lead with it and
  fall back to the old type + dimensions label when it is absent. Added because a 30-row picker of
  "Blocking, Impassible (3.0"x3.0")" labels is unreadable. Purely cosmetic - nothing keys off the name.

**The 18 new templates.** 10 small solid obstacles (Blocking|Impassible - block movement *and* sight):
standing stone, boulder, watchtower, wrecked vehicle, shipping container, bunker, wall segment, long wall,
corner wall, rock cluster. 2 that are **impassible without Blocking** - go around, shoot over - which the
built-in set had none of: tank traps, water pool. 6 for general variety: copse, ruined building, sandbag
corner, crater, marsh, stream, barbed wire.

**`ETerrainType.Elevated` deliberately unused.** The flag is declared but no engine code reads it, so a
piece carrying it would look meaningful in the picker and do nothing. A test pins that no palette piece
uses it; delete that test when Elevated is implemented.

## Notes

- 2026-07-23 — implemented. Engine (pool + `TerrainPieceEntry.Name`) + app (two resolver labels).
  `DefaultTerrainPoolTests`, 9 tests - the auto layout's 12-piece count is pinned so a future append can't
  quietly change generated maps; the palette is a superset; there are >= 6 small impassibles and at least
  one non-blocking one; every piece fits the table, has an ASCII name and a real terrain type; and
  **every piece places Valid through the real rotate -> translate -> `TerrainPlacementValidator` path at
  0/45/90 degrees**, which is what would otherwise ship a picker row that rejects every click.
  Engine 2047/2047 (was 2038), app 557/557, build clean, headless smoke exit 0.
- **Not covered by the headless smoke:** `GameSettings` defaults to AutoFromLayout, so a piped run never
  enters the Alternating branch. The per-template placement test above is what stands in for it.
- **Needs a GUI hand-verify:** start a game with terrain mode Alternating, confirm the picker lists ~30
  named pieces with correct thumbnails and scrolls, place a boulder and a wall segment, and confirm a
  model can't move through them but *can* shoot over the tank traps.
