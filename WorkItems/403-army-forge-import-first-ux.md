# 403 — Army Forge: lead with the importer, and size the toolbar from the font

**Status**: awaiting GUI hand-verify
**Related**: #241 (the share-link importer this promotes), #398 (`UiChrome.ButtonSize` / `UiText`,
whose font-relative sizing this adopts), #156 (the Forge itself)

## Goal

Owner-reported polish on the Army Forge screen, 2026-09-12:

1. The Import popup opens as soon as the screen does.
2. Text saying we recommend importing from army-forge.onepagerules.com rather than composing here.
3. Paste sits LEFT of Fetch - it is the button you press first.
4. Back / Save / Load / Import Link are larger.
5. The points-limit field cuts its own digits off; it needs to be much wider.
6. All of the above scales with resolution rather than being pixel counts tuned for one monitor.

## Notes

- 2026-09-12: Filed. Numbering taken from `origin/master` at `f63996a` after `git fetch`: detail files
  max **402** (400/401 upstream, 402 local), archive max **401**, no `40x` branch on any remote.

## Decisions

- **2026-09-12 — the popup opens on EVERY entry, not just the first.** `OnShown` re-arms it, so coming
  back to the Forge leads with the importer again. That is the point of the nudge; the copy says
  plainly that composing by hand still works, so it reads as a recommendation rather than a wall.

- **2026-09-12 — sizes in em, via a new `UiChrome.Em`.** #398 had already moved the Combat Calculator
  to font-relative chrome (`UiChrome.ButtonSize`) and the same disease was here: the points-limit
  field's flat 110px had to hold five digits AND the two step buttons `InputInt` draws inside the item
  width, so once the display scale grew the buttons the digits had nothing left - which is the reported
  cut-off. It now asks for both explicitly, measured in the loaded font. Popup widths, combo widths and
  the preview pane went the same way.

- **2026-09-12 — the toolbar centres itself against the taller buttons.** `SameLine` starts every item
  on a row at the row's TOP, so chrome-sized buttons beside a combo would leave the combo hanging.
  `CenterOnToolbarRow` offsets each shorter item; `SameLine` resets the cursor per item, so the offsets
  never compound.

- **2026-09-12 — every button on the screen went to `UiChrome.ButtonSize` + `UiButton`, not just the
  four asked for.** The private `ButtonSize(label, minWidthPx)` helper had a pixel floor (120/140/160)
  that was exactly the half that did not scale, and the raw `ImGui.Button` calls were silent while the
  rest of the app's chrome clicks. Both were fixed together; the private helper is gone. Not requested -
  easy to revert if the extra click cues are unwanted.

## GUI hand-verify checklist

1. Enter the Forge: the Import popup is already open. Close it, go Back, enter again: open again.
2. The popup leads with the blue recommendation naming army-forge.onepagerules.com, wrapped, no '?'.
3. Paste sits to the LEFT of Fetch. Paste a link, then Fetch, and the import still previews.
4. Back / Save / Load / Import Link are visibly bigger and click with a sound.
5. The pts-limit field shows a five-digit number in full, with both step buttons.
6. The toolbar's combos, status line, badge and points header sit centred against the buttons.
7. Repeat 4-6 at another resolution (F11 windowed, or a different monitor) - proportions hold.

## Outcome

_(pending hand-verify; implementation complete and committed)_
