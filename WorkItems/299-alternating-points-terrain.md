# 299 — "Alternating: Points" terrain placement mode

**Status**: in-progress (implemented + tested; awaiting GUI hand-verify)
**Related**: #268 (palette), #002 (Alternating), #280 (terrain preview)

## Goal
A fourth terrain placement mode fixing "small piece = small impact" turns in Alternating: every
piece carries a permanent point cost (1-3 today, not hard-capped), each turn a player spends the
per-turn allowance (default 3) from a personal share of the shared total (default 30), dealt out in
placing order at phase start (20/3 across two players = 11/9). A turn's FIRST piece may exceed the
turn budget; the difference is debt taken from the next turn(s). Chris's rule: no new debt while a
turn is paying debt off. Nothing may exceed the personal total; a debt-consumed turn is skipped with
a Toast; the old mode is relabeled "Alternating: One Per".

## Notes
- 2026-07-28 (later): Chris's lobby test found settings rows overflowing the window - the panel used a
  blanket `PushItemWidth(panelW)` after each label+SameLine, so every control overran by its label
  width. Rows now size to the space left in the row (`FillRowItemWidth`), Copy buttons pin to the
  panel edge, the connection header wraps, and the settings column got a 280px floor. Also per
  Chris: `GetPalette()` now sorts by cost (cheap first, stable) and dedupes position-only template
  duplicates (the two Forests / two Sandbag lines) - palette is 29 entries, both alternating modes
  inherit it; sortedness + uniqueness pinned in `DefaultTerrainPoolTests`.
- 2026-07-28: Implemented end to end. Engine: `TerrainPieceEntry.Points` (default 1 for old files) +
  values across all 31 palette entries; `ETerrainPlacementMode.AlternatingPoints` (appended - wire
  values stable, combo order fixed app-side via `explicitOrder`); `GameSettings.TerrainPointsTotal`/
  `TerrainPointsPerTurn` (defaults 30/3, caps 60/6 on `PlaceTerrainStage`); `TerrainPointsLedger`
  (dealing + spend/debt state) + `TerrainPointsBudget` (wire snapshot on `PlaceOneTerrainRequest`,
  composes ALL affordability copy so GUI/CLI/AI/server never drift); `RunPointsPlacement` loop with
  authoritative server-side affordability re-prompt; lobby VM sync (host setters accept 0 total -
  deliberately not repeating `SetTerrainCount`'s >0 quirk). App: CLI menu shows costs/reasons and
  re-prompts unaffordable picks (EOF fallback prefers debt-free affordable); GUI picker rows show
  cost, gray blocked rows with tooltip reason, amber debt warnings + header notice; lobby gets the
  two sliders, mode relabel, tooltip rewrite.
- Tests: `TerrainPointsLedgerTests` (15: dealing incl. 11/9 example + 2v2 cursor order, debt,
  no-debt-while-paying-debt, exact user copy), `TerrainPointsPlacementIntegrationTests` (full AI
  game, per-turn 1 => piece count == total), pool cost floor, settings round-trip/resume extensions,
  `PlaceOneTerrainResolverTests` (CLI budget honor, 3). Engine 2254 green, app 667 green, smoke green.

## Decisions
- **Debt rule generalized with a brake** (Chris, 2026-07-28): first piece of a turn may be anything
  within the remaining personal total, overflow becomes debt - but a turn that is repaying debt may
  not take new debt (blocks debt two turns in a row). Deep debt (cost > 2x per-turn) legally skips
  whole turns with a Toast each time.
- **Mandatory spend, no pass button** (Chris): a turn runs until its budget is 0. Safety valve: if no
  affordable piece fits anywhere (2" grid, 0/90 rotations - conservative on purpose), the player's
  remaining points are forfeited with a Toast rather than re-prompting forever.
- **Point values** (Chris signed off, tweakable): 3 = Central building, Forests, Collapsed wall +
  the multi-large-block composites (Rocky outcrop, Wreckage, Crater rim, Tank trap cluster);
  2 = Mine field, Rubble, Bunker, Shipping container, Long wall, Rock cluster, Copse, Ruined
  building, Marsh; 1 = the rest (walls, sandbags, small blockers, dangerous/difficult strips).
- **Enum appended, not inserted**: numeric wire/save values of the old modes stay stable; the lobby
  combo gets an `explicitOrder` parameter so the two Alternating modes still sit together.
- `TerrainPointsBudget.CostOf` floors at 1 so a hand-authored 0-point piece can't loop forever.
- AI stays debt-free (picks only within-budget templates) - keeps determinism (same RNG consumption
  when no budget) and avoids the dumb AI mortgaging turns.
- Deferred: no headless path exercises the mode interactively (same pre-existing gap as One Per,
  noted in #268 - `GameSettings` defaults to AutoFromLayout in piped runs); covered instead by the
  full-AI-game integration test. GUI hand-verify still owed.

## Outcome
(pending)
