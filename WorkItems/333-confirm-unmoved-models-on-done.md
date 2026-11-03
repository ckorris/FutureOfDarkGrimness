# 333 — Confirm before finishing a move with models still on the start line

**Status:** implemented + tested; awaiting GUI hand-verify (the popup itself is ImGui).

## Goal

Two halves of the same complaint, from a 2026-08-04 game:

1. Finishing a move (Done) while one or more models have not moved should **ask first**. Single mode
   moves one model at a time and the roster (#326) is the only place a forgotten model shows; clicking
   Done spends those models' move for the activation with no way back.
2. A move that covered no ground should **not** stamp the movement token.

## Decisions (signed off 2026-08-04, before building)

- **Popup trigger: any living model at 0"**, not just "nobody moved". The forgotten-straggler case is the
  one that actually costs a player something, and it is invisible without the warning. It **asks, never
  blocks** — deliberate stragglers exist (screening, a model boxed in behind terrain).
- **Token withheld only when the UNIT travelled 0"**. The literal reading ("no token if any model stayed
  put") was rejected: it would make "leave one model behind" a free way to keep every
  hasn't-moved-this-round bonus, which is an exploit, not a rule. A unit that moved 3 of 5 models moved.
- The Move **action is still spent** either way. Declining to move is a choice made with the move; `Back`
  is what keeps it. Said out loud in both front ends' all-zero wording, since that is the mistake worth
  naming.

## What changed

**Engine** (submodule `88f89c8`, `3abfe95`):

- `MovementStage.ReconcileChildContextBeforeLeaving` returns before the `MovedThisRound` stamp when the
  recorded distance is `<= 0.0001f`. "Skip all" (and a Done with no waypoints placed) submits a real
  zero-length path, so it leaves through `OnFinishedMovement`, not the `MoveCancelled` back-out — the
  guard that already existed covered only one of the two ways to not move. `RegisterMoveFinished` still
  runs, so the activation's move is spent.
- `MovementUtilities.GetTotalMoveDistance(ModelMoveEntry)` made public (the private
  `GetTotalMoveDistances` dictionary builder now calls it), so a front end can ask "did THIS model move?"
  without re-deriving the sum.

**App:**

- `ModelRoster.UnmovedOrdinals` + `ModelRoster.DistanceEpsilon` — the straggler list as pure arithmetic,
  sharing the epsilon that greys a not-yet-started roster row so the two can never disagree.
- `GuiDefineMovementResolver.DrawDoneConfirmation` — modal popup mirroring #319's Done-shooting one:
  names the models ("Model N", the roster's own ordinals) rather than an abstract "are you sure?".
  Mouse-only buttons; while it is up it owns the keyboard and the table (`wantInput` folds in
  `_donePopupOpen`, group-mode input gets `overTable && !_donePopupOpen`, Done itself is muted), so one
  Enter press cannot answer the popup and re-press Done behind it.
- `DefineMovementPathResolver` (CLI) — the same confirmation in this front end's vocabulary. Declining
  re-runs the whole prompt, exactly as an invalid path does. EOF and bare Enter both answer yes (#319's
  rule: a piped script that entered blank lines meant them).

## Verification

- Engine suite 2789/2789 green. New `MovementBackOutTests`:
  `ZeroDistanceMove_SpendsTheMove_ButDoesNotStampMovedThisRound` (both halves at once) and
  `TinyButRealMove_StillStampsMovedThisRound` (pins the epsilon as float drift, not a "barely moved"
  allowance).
- `ModelRosterTests` 22/22, four new: ordering/1-basedness, empty case, epsilon, and an
  agrees-with-the-greyed-rows cross-check.
- Full `dotnet build` clean; headless smoke exits 0 with the tie line.
- CLI hand-driven through `Scenarios/mobile-artillery-defensive.json --headless`: all-zero wording, `n`
  re-prompting the whole move, bare Enter committing, and the 4-of-5 partial wording all confirmed.

## Notes

**2026-08-04.** Deliberately NOT changed: **"Skip all" raises no popup.** Its label and tooltip already
say "Don't move the unit. Every model stays in place." — it is its own confirmation, and a second one
would train players to click through both. It does now benefit from the token half.

Left open: the GUI popup is ImGui and unverified in the running app — same
`awaiting GUI hand-verify` bucket as #295/#326. Consolidation (`GuiConsolidationMoveResolver`) has the
same click-to-select gesture and no equivalent warning; not in scope here, and it is already queued
behind #326 for the roster treatment.
