# 445 — Final verification of the OPR 3.6 upgrade

**Status**: todo
**Related**: #415, #444 (merge happens only after this passes), #167 (scenario tools), #196 (`--rule-coverage`)
**Branch**: `feature/opr-3.6`. Model/effort: Opus 5.5 / medium.

## Goal

An evidence-backed statement that the feature branch is releasable, written into this file, before #444
merges. The checklist is plan section 7; this item runs it, records the outputs, and files follow-up items
for anything red instead of patching around it.

## Checklist (record each result with the command and its tail)

1. `dotnet test FutureOfDarkGrimness/FutureOfDarkGrimness.csproj` green; `dotnet build` clean (0 warnings
   introduced by the upgrade - compare against the master build's warning count).
2. Headless smokes exit 0: the default built-in army; `--army` with one GDF and one AoF 3.6 army produced by
   `--book-to-army` from a re-baked book; at least one book from each restructured family (a Prime Brothers
   book, a Disciples book, Infected Colonies, Rebel Guerrillas, Voidspace Stalkers, Chivalrous Kingdoms).
3. `FdgRaylib --rule-coverage FdgRaylib/Assets/Books` -> `Dead: 0`, or every remaining name appears in
   #436's deferred ledger with a reason and a player-visible "not implemented" diagnostic (#168).
4. `--validate-rules` passes on `GdfRuleSupplement.json`, `AofRuleSupplement.json`, and each
   `AofBookOverrides/*.json` (alone and merged).
5. Every bundled `.fdgbook` has `version` = `OPR v3.6.0`; counts 48 GDF + 40 AoF; no file for the 0-unit
   parent entries; the Random Army catalog (`armies/`) regenerated and non-empty for both systems (#395 is a
   separate pre-existing bug about where the catalog is scanned; do not fix it here, but note it).
6. `--import-army` with a fresh 3.6 Army Forge share link succeeds; with a 3.5 link it refuses with the
   message #437 specifies.
7. Each `Scenarios/*.json` compiles with `--make-scenario` and runs headless (`--scenario ... --headless`).
8. Legacy load: the pre-upgrade `armies/` files kept by #440 as fixtures load into a lobby and a headless
   game runs to completion with the Q5 notice logged.
9. GUI hand-verify list for Chris: one line per GUI-touching item (#417, #422, #423, #424, #425, #429, #441)
   naming the screen and what to click; collected from those items' "awaiting hand-verify" notes.
10. `docs/` numbers spot-check: open `docs/ResolverGuide.md` and `docs/EngineNotes.md` and confirm the lines
    #444 changed match the code (melee 3", Brace, Indirect, Scout, Transport, Hero morale).

## Rules for the agent

Plan section 1. This item changes no production code; a red step becomes a new numbered item (fetch
origin first) or a reopen of the responsible item, and this file records which.

## Notes

- 2026-10-31 (from #440): add to the checklist - `FdgRaylib --retrofit-editable armies FdgLab/armies --dry-run` (0
  converted, 40 already editable, 0 left alone); every `armies/` and `FdgLab/armies` list playing `--headless --army`
  with 0 `[rules]` warnings; the 28-scenario headless run (`--headless --scenario <json> --all-ai`, table in #440);
  `Legacy35ArmyTests` green; and Chris's Random Army hand-verify in a GDF and an AoF lobby (the lobby should show no
  amber legacy line for any catalog list, and one for a 3.5 file loaded by hand).
- 2026-10-29: filed with the plan.

## Decisions

(none yet)

## Outcome

(open)
