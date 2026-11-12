# 403 — Army Forge: lead with the importer, and size the toolbar from the font

**Status**: done
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

## Notes (continued)

- 2026-09-12, round 2 (owner): recommendation copy replaced with the owner's own wording (two lines,
  the second saying the Forge's editor "exists for convenience but is not the intended way to make a
  list"); roster rows made hoverable across the whole block, not just the title; the indent before a
  roster unit's stat line removed; spell NAMES in the SPELLS lists turned white against grey
  descriptions.

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

- **2026-09-12 (round 2) — the roster row uses `DrawListRow`'s hit-target shape.** The Selectable
  carried the unit NAME as its label, so it was exactly one line tall: the "Qua X+ Def Y+" line beneath
  looked part of the row and was dead space. It is now an empty-label Selectable spanning both lines,
  drawn first with `SetNextItemAllowOverlap`, with the name, points and stats painted back over it -
  the same idiom the list pane already used, so the two panes now behave alike.

- **2026-09-12 (round 2) — one spell-list renderer.** The book's spells were drawn by two identical
  loops (roster pane, and again per caster unit in the config pane). Rather than colour both, they
  became `DrawSpellList`, so the white-name/grey-description split cannot drift between them.

## GUI hand-verify checklist

1. Enter the Forge: the Import popup is already open. Close it, go Back, enter again: open again.
2. The popup leads with the blue recommendation naming army-forge.onepagerules.com, wrapped, no '?',
   with the "not the intended way to make a list" line under it.
3. Paste sits to the LEFT of Fetch. Paste a link, then Fetch, and the import still previews.
4. Back / Save / Load / Import Link are visibly bigger and click with a sound.
5. The pts-limit field shows a five-digit number in full, with both step buttons.
6. The toolbar's combos, status line, badge and points header sit centred against the buttons.
7. Roster pane: hovering anywhere over a unit's two lines highlights the whole block, clicking
   anywhere in it selects, and double-click still adds. The stat line sits flush under the name.
8. SPELLS (roster pane, and on a caster unit in the config pane): names white, descriptions grey.
9. Repeat 4-6 at another resolution (F11 windowed, or a different monitor) - proportions hold.

## Outcome

**Closed 2026-09-12, hand-verified by the owner.** The Army Forge opens with the import popup on every
entry, led by the owner's recommendation to build on army-forge.onepagerules.com and paste the share
link, with the second line saying plainly that editing here "exists for convenience but is not the
intended way to make a list". Paste moved left of Fetch.

Every fixed pixel size on the screen is now expressed in the loaded font through a new `UiChrome.Em`,
and every button uses #398's shared `UiChrome.ButtonSize` + `UiButton` (the private
`ButtonSize(label, minWidthPx)` helper, whose pixel floor was the half that never scaled, is gone). The
reported points-limit cut-off had a specific cause: `InputInt` draws its two step buttons INSIDE the
item width, so a flat 110px was digits-plus-buttons and the digits lost once the display scale grew the
buttons; it now asks for five digits and both buttons explicitly. The toolbar centres its combos,
status line, badge and points header against the taller buttons.

Second round, same day: roster rows take `DrawListRow`'s full-block hit target (the Selectable carried
the unit NAME as its label, so it was one line tall and the stat line beneath was dead space), the stat
line lost its indent, and the book's two identical spell loops became one `DrawSpellList` with names in
white against grey descriptions.
