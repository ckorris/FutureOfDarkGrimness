# 106 — Army builder authoring UX: stat-block headers, duplicate, auto-unfold

**Status**: in-progress
**Related**: #149 (base-shape army-builder UI this builds on; the mm-input amendment lives there), #107 (combined/"doubled up" squads — spun off), #003 (force-org warnings shown in the same screen)
**Branch**: `149-base-shapes` (committed alongside the in-flight base-shape work; app-side only, no engine change)

## Goal
A batch of army-creator (`ArmyBuilderScreen`) usability edits requested 2026-06-26:
1. **Always-visible unit stat block.** Above each unit, a read-only profile: stat line `Name [models] - Qua X+ Def Y+` with right-aligned points, the unit's weapons in army-book form, and its special rules. The editable fields move into a collapsible "Edit" node below.
2. **Duplicate button** per unit — fast "second identical squad".
3. **Auto-unfold on create.** A newly added (or duplicated) unit / weapon / spell opens its node automatically so you can edit it immediately.

Done = all three live in the GUI, build clean, headless smoke green, and no saved `.fdgarmy` regresses.

## Notes
- 2026-06-26: **Built (app-side only).** All in `FdgRaylib/Rendering/ArmyBuilderScreen.cs`:
  - **Stat block** — `DrawUnitSummary` renders the always-visible profile; `WeaponSummary` formats each weapon army-book style (`4x Name (24", A6, AP(4), Reliable)` — range shown only when >0, AP only when ≠0, rule names via `SpecialRuleEntry.PrintableName`); special rules joined with `PrintableName`. Points are right-aligned via `GetContentRegionAvail` (clamped so a long name never pushes them left). User-authored names render through a new `TextColoredUnformatted` helper so a `%` in a name can't be mis-read as a printf token by `ImGui.TextColored`.
  - **Duplicate** — `DuplicateUnit` deep-copies through the save/load STJ pipeline (`RuleJson.Options`), so polymorphic special rules / weapons / base all clone; clears the copy's cross-file `Id` so a Hero's `JoinsUnitId` can't resolve to two units; inserts after the original (deferred past the draw loop so the insert can't shift the iterated index) and unfolds it.
  - **Auto-unfold** — pending-open tracked by `StableID` for units (`_pendingOpenUnitId`) and weapons (`_pendingOpenWeaponId`), and a bool for spells (`_pendingOpenSpell`, opens the last spell — records have no stable identity); `SetNextItemOpen(true, Always)` fires for one frame then clears.
  - **Drive-by fix:** the unit/weapon/spell delete buttons now `TreePop()` before removing when the node was open (the old `continue` skipped it — a latent ImGui tree-stack imbalance, reachable by deleting an expanded item).
- Verified: `dotnet build` clean (2 pre-existing warnings); headless EOF smoke exit 0; all six root `.fdgarmy` files load + play to completion headless (exit 0) — **no format change, nothing to fix**. **App test suite 39/0** — 8 new in `FdgRaylib.Tests/ArmyBuilderScreenTests.cs` over the extracted pure seams (`WeaponSummary` count/range/AP-omit/numeric, `UnitStatLine`, `CloneUnit` Id-clear + deep-copy independence), made `internal static` for `InternalsVisibleTo`.
- **Awaiting GUI hand-verification** in the running window: open the army builder, confirm each unit shows its stat block above a collapsible "Edit"; add/duplicate a unit/weapon/spell auto-opens it; Duplicate makes an independent second unit; base sizes read/edit in mm (28mm default).

## Decisions
- **Stat block always visible (not only on expand).** The user picked the at-a-glance roster view: every unit shows its full profile without expanding; editing is one collapsible level down. (The alternative — summary only when expanded — was rejected.)
- **Weapon line = army-book style with count + range.** `4x Name (24", A.., AP(..), rules)`, count always shown even for `1x`. Chosen over the count-less literal of the original example because "total of each" implies the quantity.
- **Duplicate = units only.** Matches the request's "doubled up squads" framing. Per-weapon / per-spell duplicate was offered and declined; the broader "combine two squads into one doubled unit" feature is its own item (#107).
- **mm base input is tracked under #149, not here** — it amends #149's "inches input" decision (the UI converts mm⇄inches; the `.fdgarmy` still stores inches).

## Outcome
All three edits + the mm-input amendment (#149) and a TreePop drive-by fix shipped app-side in `ArmyBuilderScreen.cs`, with 8 new tests (app suite 39/0), clean build, headless smoke exit 0, and all six root `.fdgarmy` confirmed loading — **no save format change, no regressions**. Held in *Awaiting verification* until the visual behaviour is eyeballed in the running window. The broader "combine two squads into one doubled unit" feature is **#107**.
