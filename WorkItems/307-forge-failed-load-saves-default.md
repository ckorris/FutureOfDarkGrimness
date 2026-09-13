# 307 — A rejected Forge load leaves the default list in place, and Save then writes it out

**Status**: todo
**Related**: #236 (the mirror-image defect on the Army Builder side — already fixed with an explicit confirm),
#241 (share-link import reconstructs a session against a bundled book — the machinery a fix could reuse),
#220 (Forge list version control)

## Goal

Loading an army into the Forge either takes effect or fails loudly enough that the next Save cannot be
mistaken for "save what I just loaded". Done = a rejected load is unmissable, Save cannot silently write a
pristine default list over a path the user picked to hold a real army, and a test pins the sequence
load-a-plain-army -> Save.

## What happened

Reported 2026-07-31. The user built an army, saved it to the wrong directory, opened the Forge, loaded that
file, pressed Save, and picked a new path. What landed there was a 288 KB **empty Alien Hives 1000-pt list**
under the filename `Eternal Dynasty 3k.fdgarmy`:

```
name: "Alien Hives"   faction: "Alien Hives"   pointsLimit: 1000
selections: { name: "", bookName: "Alien Hives", pointsLimit: 1000, units: [] }
book: <the full 41-unit Alien Hives book>
```

Their read was "the state we loaded didn't end up being what was saved". Closer: **the load never took
effect at all**, and Save wrote out the screen's untouched startup state.

## The mechanism

`Load()` (`FdgRaylib/Rendering/ArmyForgeScreen.cs:1028`) hands the file to `AdoptLoaded`
(`:370`), which bails on the first line when the file has no embedded editing block:

```csharp
if (loaded.Selections is null || loaded.Book is null) return false;
```

That is by design — a plain army is not catalog-editable. But on `false` **nothing is mutated**: `_book`,
`_list`, `_bookIndex` all keep their startup values, and the only signal is a status string.

The file the user loaded was Army-Builder-shaped: top-level keys `name`, `faction`, `pointsLimit`, `units`,
`ruleDefinitions`, `spells`, `unattributedPoints` — **no `selections`, no `book`**. So it was rejected. Note
that *every* tracked `armies/*3k.fdgarmy` has that same shape, so this is the common case, not an exotic one.

What remained on screen was the pristine default, and it accounts for the artifact byte for byte:

| startup state | source | matches the saved file |
|---|---|---|
| `_bookIndex = 0`, `UseBook(_library[0])` | `:62-66` | book = Alien Hives |
| library sorted by path, `AlienHives.fdgbook` sorts first | `:99` (`OrderBy(p => p)`) | " |
| `PointsLimit = DefaultPointsLimit` | `:27` (= 1000) | limit 1000 |
| `_list.Units` never touched | — | `units: []` |

`Save` (`:1015`) takes the `compiled` value `Draw` recomputes every frame from `(_book, _list)`, so it
faithfully serialized that default. Save itself is not buggy; it was handed exactly what the screen held.

## Why this is worse than one junk file

The user got lucky: they saved to a *different* path, so the source army survived in the other clone. The
Save dialog is a normal overwrite-capable file picker — pick the file you just failed to load and the
default empty list replaces a real army. **Silent data loss, one click after a failure the UI barely
mentioned.**

The failure notice is `ImGui.TextDisabled(_statusHint)` in the toolbar (`:434-437`) — greyed-out small text
on the *same channel and same styling* as `"Saved X.fdgarmy"`. Failure and success are typographically
indistinguishable, and the hint is easy to walk past on the way to the Save button.

## Suggested shape

Not a single fix; pick from these, and the first is the cheap floor:

1. **Make the rejection unmissable.** A modal, or at minimum a distinct colour and a persistent banner
   rather than a disabled-text line that reads like a success. #236 set the precedent on the Army Builder
   side: the same class of mismatch (save writes state that does not match what was loaded) was gated
   behind an explicit "Save detached" confirm rather than a hint.
2. **Guard Save against writing a pristine default.** If the list is empty and untouched since startup,
   confirm before writing — especially over an existing file. Cheap, and it catches the whole family
   (rejected load, wrong book selected, stray click) rather than this one path.
3. **Let the Forge reopen plain armies.** The deeper fix, and #241 already built most of it: its "Open in
   Forge" path reconstructs an editable session against a bundled book and discloses the units the book
   does not know (`ListCompiler.Compile(outcome.BundledBook, session.Selections)`, `:309-315`). A plain
   `.fdgarmy` names its faction, so the same reconstruction is reachable. Decide whether that is worth it
   or whether the Army Builder stays the right home for hand-authored lists — a design fork, surface it
   before building.

Worth checking while in here: `Load()` also returns silently when the file is missing (`:1033`) or
deserializes to `null` (`:1036`) with **no status hint at all** — the same trap with even less signal.

## Notes

### 2026-08-05 - implemented (options 1 + 2; option 3 deferred, see Decisions)

All of it app-side (`FdgRaylib/Rendering/ArmyForgeScreen.cs`) - no engine change.

**1. The rejection is a modal.** `Load()` no longer reports through the status line alone. Every failure
path routes to `TryAdopt`, which records the outcome and returns false; the caller raises a `Load failed`
popup. Its text closes the exact gap the report exposed - it names the file, says it was NOT loaded, gives
the reason, and then states the thing the user had no way to know: *"The list on screen has not changed -
it is still whatever was here before. Saving now would write THAT list, not the file you just picked."*

**2. The status line is typed.** `EForgeStatusKind` (Info/Success/Error) drives colour - red for failure,
green for success, disabled-grey for neutral - so failure and success no longer share `TextDisabled`.
Drawn via `PushStyleColor` + `TextUnformatted` rather than `TextColored`, because the line can carry a
file name or an exception message and a stray `%` would be eaten as a printf directive.

**3. Save is guarded.** `ESaveGuard` / `EvaluateSaveGuard`, evaluated after the file dialog so the confirm
can name the target file:
- `UnchangedAfterFailedLoad` - a load was rejected and the list has not changed since. Detected with
  `ListFingerprint` (a JSON snapshot of `BuilderList` taken at the moment of failure), so **no mutation
  hooks were needed** on the dozen-plus edit sites, and an edit after the failure silently disarms it.
- `EmptyList` - the list has no units; catches the pristine-default artifact from the report directly.
- `None` - ordinary saves are untouched, no extra click.
Confirming ("Save anyway") clears the failed-load state, so the same failure never re-warns.

**Caught while in here** (the item's "worth checking" note): `Load()` returned in silence on a missing
file and on a null deserialize, and a **malformed .fdgarmy threw `JsonException` straight out of `Draw`**
and took the renderer down (it only ever reached `crash.log`). All three now report through the modal.
`File.WriteAllText` in both save paths is wrapped too - a read-only target used to look like a success.

**Tests:** `ArmyForgeScreenTests` +10, 42/42 green. They pin the reported sequence (pristine screen ->
rejected load -> guard fires), the nastier variant an empty-list check alone would miss (army A on screen,
army B's load rejected, Save would write A over B's path), the disarm-on-edit and disarm-on-successful-load
paths, both message texts, and an ASCII pin on all new user-facing strings.

**Verified:** `dotnet build` clean, app suite 1120 green, engine suite 2862 green, headless smoke exits 0.
**Not verified:** the GUI itself - both modals, the status colours, and the guard flow need a hand pass
(repro: launch Forge, Load any `armies/*3k.fdgarmy`, then press Save).

- 2026-07-31: Filed from a live report. Root cause read off the artifact plus the screen source; the
  evidence chain is closed (Forge saves carry `selections`+`book`, proven by the artifact having them; the
  loaded file has neither key; `AdoptLoaded` rejects on that; rejection mutates nothing; the startup
  constants reproduce the artifact exactly). Not reproduced through the GUI — worth one hand-verify pass
  before fixing, and the repro is: launch Forge, Load any `armies/*3k.fdgarmy`, press Save.

## Decisions

- **2026-08-05: shipped options 1 + 2; option 3 (let the Forge reopen plain armies) is DEFERRED and still
  needs a ruling.** 1 + 2 are right whichever way 3 lands - they also cover the other failure paths (missing
  file, malformed JSON, wrong book) that a plain-army reader would not touch. The open fork is unchanged:
  reconstruct an editable session against a bundled book the way #241's "Open in Forge" already does
  (`ListCompiler.Compile(outcome.BundledBook, session.Selections)`), or rule that the Army Builder stays the
  home for hand-authored lists. Worth noting the fork got cheaper since filing: #354 installed
  `BundledBookRulebook`, so faction -> bundled book is already a solved lookup on the app side.
- **2026-08-05: the Save guard detects "unchanged" by fingerprint, not by dirty-tracking.** A JSON snapshot
  of `BuilderList` compared at Save time is O(list) once per save and needs no hooks in `AddToList`,
  `RemoveFromList`, `SetChoice`, `ApplyChoice`, the model-count steppers, or the combine/join edits - a
  dirty flag would have had to be threaded through all of them and would rot the first time a new edit site
  was added.
- **2026-08-05: confirm, never block.** The Forge is advisory everywhere else (#003 force-org warnings never
  block a save or a launch), so the guard is a confirm dialog, not a refusal.

## Outcome

_(written when the item closes)_
