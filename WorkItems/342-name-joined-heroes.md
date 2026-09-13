# 342 — Name the joined hero in the tooltip and the army list

**Status**: in-progress (implemented + tested; awaiting GUI hand-verify)
**Related**: #006 (hero join), #227 (hero star + Q/D tag), #329 (per-unit points)

## Goal

A hero that has joined a unit is anonymous for the rest of the game: nothing anywhere shows its name.
Done = the tooltip and the in-game army list both name it, and the name survives saves and the network
sync like the rest of the attachment.

## Notes

- 2026-08-05: Implemented. Engine: `HeroAttachment` gains `Name` + `PointCost`, captured in
  `HeroJoinResolver.Apply` at the merge. App: `HeroMarkerRenderer.FormatHeroTag` takes the name
  (`"Hero: Elven Noble  Qua 3+  Def 4+"`); new `FormatHeroNameLine` (`"+ Elven Noble - 150pts"`) drawn under
  the host's name line in both army-list modes; the tooltip's "This model" header becomes the hero's name
  when hovering the hero. Engine suite 2837/0 (2833 before), app 1083/0, `dotnet build` clean, headless
  smoke exit 0 both with the built-in army and with `HeroTest.fdgarmy` (a real hero join).
- 2026-08-05: Root cause was not a display gap — the name did not exist to display. `HeroJoinResolver`
  moves the hero's `ModelData` into the host and discards the hero's `UnitData`; `ModelData` has no name
  field, and `HeroAttachment` carried only id / Quality / Defense / HeroWounds. Every display site was
  already hero-aware (#227) and printing the best it could: a bare "Hero".

## Decisions

- **Name rides `HeroAttachment`, not `ModelData`.** A `ModelData.Name` would be more general (named
  sergeants/champions later) but widens serialization on every model in the game for one consumer. The
  attachment is already the designated home for "facts about the hero that diverge from the rank and file"
  and is already serialized, so it rides saves and the network sync with no new plumbing.
- **App-side lookup from the `.fdgarmy` was rejected outright.** Clients and resumed saves only ever see
  merged units; the list entries are not there to look up.
- **Hero points captured too**, even though #329 already folds them into the host's `PointCost`. The fold is
  lossy — the host's total is right but the hero's share is unrecoverable — and it is the same one-field
  shape as the name, at the same call site.
- **Name on its own line, not appended to the unit header.** Real hero names run long ("Knight Veteran
  Master Brother"); inline would blow out the army-list table row's width.
- **Pre-#342 saves degrade, they don't break.** `Name` null / `PointCost` 0 falls back to the exact strings
  #227 printed, and the points are omitted rather than shown as "0pts" (which would read as "free").
  Pinned by `HeroAttachment_FromPreNameSaveLoadsWithNoNameOrPoints` and the fallback format tests.

## Deferred (explicitly, not dropped)

- **Log lines still name only the host unit.** A joined hero dying, shooting, or taking a wound is invisible
  in the log — every log line says "Retributors". Raised with the user 2026-08-05 and deliberately left out
  of this slice as the larger change; wants its own number when picked up.
- **No permanent on-table label.** Option 3 (naming the hero in the `N`-toggled canvas unit label) was
  offered and declined for now: it adds standing text to every hero unit on the table. The tooltip's
  hero-name header covers the on-table "who is this?" question instead.

## Outcome

_(pending GUI hand-verify)_
