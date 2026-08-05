# 357 - Retrofit already-saved armies so the Forge can reopen them

**Status**: implemented 2026-08-05, awaiting GUI hand-verify
**Related**: #356 (Save As carries the session going forward - this is its retroactive half), #307 (the
dead end being removed), #107 (combined pairs), #323 (starved replace, which the bound had to learn),
#218/#219 (why price cannot be a match criterion)

## Goal

A `.fdgarmy` already on disk becomes catalog-editable again without re-importing it. Done = a CLI pass over
`armies/` converts every file it can, never alters an army's playable half, and says exactly what it could
not do.

## Why this is not just "attach the session"

#356 made *future* Save As files editable, but a file already saved stores the RESULT of its upgrade picks
and never the picks themselves - `BuilderList` is what the Forge edits, and a plain army has none. So the
retrofit has to run `ListCompiler` backwards.

Rather than re-derive the replace-chain algebra in reverse (#218/#261/#323/#324 got it right forwards over
four items), `SelectionSolver` enumerates each roster unit's option space depth-first and compiles every
candidate through the **real** compiler, accepting only a match. The compiler stays the single authority on
what a pick-set means, and a wrong guess cannot pass. Measured corpus: median 16 candidates per unit, worst
~270k, so an exhaustive search with a 100k budget clears effectively everything.

## Decisions

- **Match on size + weapon loadout, NOT price.** These armies are OPR imports: the saved per-unit cost is
  Army Forge's, and our compiler is known to disagree on some units (#218) and to count some options free
  because OPR publishes no price at all (#219). Requiring price equality rejected the correct picks on every
  file - the first run converted **0 of 11**. Price is now the tie-break between loadout-equal candidates,
  and the residual delta is reported.
- **All-or-nothing per file.** `ArmySolve.Selections` is populated only when every unit solved; a partial
  session would make an army's editable half quietly disagree with the army. Nine of the eleven files were
  partial at some point during development - none of them were written.
- **The playable half is compared before and after, and a mismatch refuses the write.** The tool adds a
  session; it never re-prices or re-shapes. Belt and braces on top of `EditableSession.Attach`'s round-trip.
- **Combined pairs are tried, not detected.** Some merge under a "(Combined)" suffix and some keep the plain
  name (Robot Legions Warriors: 10 models out of a 5-model roster entry with no add-models section), so the
  pair reading is simply attempted whenever the single-copy one fails. Only SYMMETRIC pairs are solved -
  both copies taking the same picks, which is what a doubled squad is in practice. An asymmetric pair would
  square the search space and is left unsolved rather than guessed.
- **Joined heroes (#006) carry across as-is.** A hero is its own entry in the file and its own unit in the
  list; the link is an id reference, so once the hero itself solves, the reference copies over.

## Notes

### 2026-08-05 - implemented (engine `<pending>`, superproject `<pending>`)

**Engine** - `ArmyBuilding/SelectionSolver.cs`: `Solve(book, army)` -> per-unit `UnitSolve` (picks, points
delta, or the reason it failed) plus a whole `BuilderList` when every unit solved. Also
`EditableSession.NormalizeUnitName`, because the drift measure compared "Warriors (Combined)" against a
rebuilt "Warriors" and reported a phantom dropped unit - that fix lands in **#356's reopen modal too**,
where it would have told users a unit was being lost when nothing was.

**App** - `--retrofit-editable <fileOrDir> [--dry-run]` in `Program.cs`, mirroring `--retrofit-effects`:
loads the bundled books, matches faction, solves, attaches, writes in place. Reports per file, and for a
converted one discloses what reopening would change (the same figures #356's modal shows).

**One real bug found by the corpus.** `CountedBound` capped a counted Replace at the copies present in the
BASE loadout, so a section whose targets another section GRANTS could never reach its true application
count - the #323 starved-replace shape (a Titan whose shield swaps into a second Heavy Hammer, after which
"Replace any Heavy Hammer" applies twice). The correct answer was outside the search. Now bounded by base
availability plus what other sections can grant.

**Result on the real corpus - 11 of 11 plain files converted, 22 of 22 now editable:**

| Stage | Converted |
|---|---|
| exact-price match, no pair/hero handling | 0 / 11 |
| price as tie-break, heroes carried | 3 / 11 |
| combined pairs tried on fallback | 10 / 11 |
| grant-aware counted bound | **11 / 11** |

Verified per file: the playable projection (`name`, `faction`, `pointsLimit`, `unattributedPoints`, `units`,
`auxiliaryUnits`, `spells`) is byte-identical to `HEAD`, and every points total is unchanged. Two converted
armies played to a result headless with zero rule drops. Re-running converts nothing (idempotent).

The only on-disk change outside the added block is schema-default backfill in embedded rule definitions
(`minArrivalRound`, `mandatoryArrival` now written explicitly) - the same values a missing key already
deserialized to, so no behavior moves.

Reopening these in the Forge rebuilds them 15-110 pts lighter than saved (Army Forge's totals vs ours,
#218/#219), which #356's modal discloses before adopting. Unit counts match exactly on all 11.

**Tests:** `ArmyForgeSerializationTests` +6 (compile -> discard picks -> solve -> identical army; loadout
match survives a price disagreement and reports it; an unplaceable unit refuses a partial list; a combined
pair solves to two linked entries; name normalization). 15/15 in that fixture.

**Verified:** `dotnet build` clean, engine 2874 green, app 1125 green, headless smoke exits 0.

**Not done:**
- **Asymmetric combined pairs** - the two halves upgraded differently. None in the corpus; would square the
  search space. Reported unsolved.
- **Hand-authored armies remain out of reach when their unit names are not in any bundled book** - the
  solver needs a roster entry to search. Nothing in `armies/` hit this, but a freeform list would.
- **No GUI hand-verify** that a retrofitted file actually opens in the Forge - only the engine round-trip
  and the drift measure were exercised. Repro: launch the Forge, Load `armies/Titan Lords 3k.fdgarmy`.

## Outcome

_(written when the item closes)_
