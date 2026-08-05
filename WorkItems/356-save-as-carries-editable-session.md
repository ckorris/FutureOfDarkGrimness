# 356 - "Save As" on an Army Forge import writes a re-editable file

**Status**: implemented 2026-08-05, awaiting GUI hand-verify
**Related**: #241 (share-link importer, both exits), #307 (the save-side harm this mirrors), #218/#219
(the pricing deltas that make the two halves disagree), #354 (`BundledBookRulebook`, the faction -> book
lookup)

## Goal

An army imported from an Army Forge share link and saved via **Save As** can be reopened in the Forge for
editing. Done = the file both plays with Army Forge's authoritative numbers AND carries the editable
session, and reopening it can never silently change the army.

## Why

The import modal has two exits, and only one of them produced an editable file:

| Exit | Wrote | Reopens in the Forge |
|---|---|---|
| Save As | plain `ArmyListFile` (verbatim OPR data, their points) | no |
| Open in Forge -> Save | `BuiltArmyFile` (selections + book embedded) | yes |

Nothing online is needed for the editable half - OPR's share JSON *is* a list of picks, and
`ReconstructSelections` maps them onto the **bundled** book (`Assets/Books`, read off disk). By the time
Save As runs, `outcome.ForgeSession` already holds that reconstruction. We were throwing it away.

Owner's framing (2026-08-05): Save As should do whatever it takes to stay editable; the freeform Army
Builder is explicitly out of scope.

This also matters for the OPR version cliff. `OprListImporter.SupportedVersionPrefix = "3.5"` refuses a
book outside 3.5.x (designed behavior, signed off 2026-07-16 - OPR retires old book versions, so importing
a 3.6 book into a 3.5 rules implementation would mix rule generations). Local `.fdgarmy` loads never touch
the API and are unaffected, but the *re-import* escape hatch for a non-editable file closes the day OPR
bumps. Every Save As from now on produces a file that never needs the hatch.

## The catch, and the ruling

Such a file carries **two derivations of the same list that need not agree**: `Units` is Army Forge's
verbatim data; `Selections` + `Book` compile through OUR `ListCompiler`. They diverge in exactly the three
ways the import preview already discloses - units the bundled book does not know (excluded), upgrade
choices that did not match (dropped), and per-unit pricing (#218/#219, plus options OPR publishes no price
for at all). So reopening recompiles into something that can differ from the army as saved and played.

- **Owner ruling 2026-08-05: warn-then-adopt.** Measure the gap at reopen. No difference -> adopt silently
  (the Forge's own files always land here, since both halves came from one compile). A difference -> one
  modal stating exactly what changes, with Open for editing / Cancel. Rejected: silent adopt (the #307
  harm shape on the load side), and refusing to open (leaves the file a dead end, which is the whole point
  of the item).
- **Cancel arms the #307 Save guard.** Declining leaves the previous list on screen, which is precisely
  the "Save would write something other than what you just picked" state, so it re-uses that guard. It is
  reported as a neutral status, not `LOAD FAILED` - the user chose it.

## Notes

### 2026-08-05 - implemented (engine `<pending>`, superproject `<pending>`)

**Engine** - `ArmyBuilding/EditableSessionDrift.cs`, the army-data half:
- `EditableSession.Attach(playable, selections, book)` builds the hybrid file. The playable half is copied
  by **serialization round-trip, not field by field**, so a future `ArmyListFile` field cannot be silently
  dropped here - and it is the same round-trip the file already survives on save/load. (A hand-written copy
  would have had to remember `UnattributedPoints`, `DefaultRangedEffectSet`, `AuxiliaryUnits` ... exactly
  the fields an import populates.)
- `EditableSession.Measure(file)` compiles the embedded selections against the embedded book and reports
  `SavedUnitCount`/`RebuiltUnitCount`, `SavedPoints`/`RebuiltPoints`, and `DroppedUnits` (multiset by name,
  so two copies saved and one rebuilt reports one drop). Null when the file has no session.

**App** (`ArmyForgeScreen`):
- `ImportedFileToWrite` attaches the session when `outcome.ForgeSession` + `outcome.BundledBook` exist,
  otherwise writes the plain army exactly as before.
- `SerializeArmy` serializes at the **runtime** type. This is load-bearing: `JsonSerializer.Serialize`
  through a base-typed reference writes only the base properties, which would have silently dropped the
  embedded block and made the whole item a no-op. Pinned by a test.
- `TryAdopt` returns `ELoadOutcome` (Adopted / Rejected / NeedsDriftConfirm) instead of a bool; `Load`
  raises the matching modal. The embedded-block check moved ahead of `AdoptLoaded` so nothing is adopted
  before the drift question is answered.
- The preview's two-exit caption now tells the truth, and says so explicitly when no bundled book matched
  (the honest residual - that import is still not reopenable).

**Tests:** engine `ArmyForgeSerializationTests` +7 (attach keeps the optional fields, the runtime-type
serialization trap, no drift for a Forge-authored file, points-only drift = the real #219 case, a unit the
rebuild cannot produce, duplicate-name multiset). App `ArmyForgeScreenTests` +5 (attach/passthrough,
silent adopt, ask-first-and-change-nothing-yet, the message shows only what actually changes, ASCII).
47/47 in that fixture.

**Verified:** `dotnet build` clean, engine 2869 green, app 1125 green, headless smoke exits 0.

**Not verified / not done:**
- **GUI hand-verify** - the reopen modal and the reworded preview caption were only exercised headless.
  Repro: import a share link, Save As, then Load that file in the Forge.
- **Existing plain files are not retrofitted.** This changes what Save As writes from now on; files already
  on disk stay plain. For OPR-derived ones the conversion is re-import -> Save As (or Open in Forge -> Save)
  while 3.5.x is still live. Hand-authored Army Builder lists have no share link and remain out of reach -
  that is the residue #307's deferred option 3 would address.
- **No OPR 3.6 readiness work.** Filed separately if the owner wants it.

## Decisions

See "The catch, and the ruling" above.

## Outcome

_(written when the item closes)_
