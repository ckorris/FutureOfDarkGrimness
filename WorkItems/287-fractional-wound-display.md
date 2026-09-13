# 287 — Fractional wounds display rounded to hundredths everywhere

**Status**: in-progress
**Related**: #199 (wound quantities are float chains), #090 (probabilistic mode)

## Goal
Under the probabilistic roller wound counts are floats. Two display bugs fall out:

1. Raw interpolation prints the full float — the unit hover tooltip shows `Wounds: 8.666667/12`.
2. `F0` formatting TRUNCATES the information — the Assign Wounds panel shows "3 / 3 wounds assigned"
   when the pool is 3.4, and per-model counters lose their fraction too.

Every player-facing wound quantity must round to the nearest hundredth and drop trailing zeros
("8.67", "3.4", "12"), via one shared formatter so the surfaces cannot drift apart again.

Done when: no wound display uses `F0` or bare float interpolation; unit tests cover the formatter's
rounding/trailing-zero behavior.

## Notes
- 2026-07-26: filed from a play session. Two of the reported issues (the hover tooltip's long decimal
  and Assign Wounds hiding the ".4") are the same class of bug, so they are one item.
- Sites: `TableTooltipOverlay` (unit + model sections), `GuiAssignWoundsResolver` (header, per-row
  counters, canvas hover label), `GuiModelSelectionResolver`, CLI `AssignWoundsResolver`.

## Decisions
- `WoundFormat.Format` rounds explicitly (`MathF.Round(v, 2, AwayFromZero)`) before formatting rather
  than relying on `"0.##"` alone, so it can collapse **negative zero**. Remaining-wound counters are
  subtraction chains that land on tiny negatives routinely (#199's epsilon territory) and `"-0"` on a
  wound counter reads as a bug. A test pins this.
- Invariant culture, so the decimal separator can never come out as a comma (ASCII rule, and a comma
  would read as a thousands separator).

## Outcome
Shipped 2026-07-26 (`d62804c`). New `FdgRaylib/Rendering/WoundFormat.cs` (`Format` / `Fraction`) applied
at every wound display: `TableTooltipOverlay` (unit + model sections), `GuiAssignWoundsResolver` (header,
per-row counters, canvas hover label), `GuiModelSelectionResolver` (both sites), CLI `AssignWoundsResolver`.
`HealthBarRenderer` needed no change - it draws a bar, never text. 6 new `WoundFormatTests`; app suite
621/621 green, engine 2196/2196, headless smoke exits 0. Awaiting GUI hand-verify in a probabilistic game.
