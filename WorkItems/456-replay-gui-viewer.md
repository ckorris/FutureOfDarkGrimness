# 456 — GUI replay viewer

**Status**: todo (forks F1, F2 answered 2026-10-30 - see Decisions)
**Related**: plan `docs/replay-plan.md` sections 4.3, 4.4, 7; depends on #454, #452; reuses the in-game canvas (`RaylibRenderer.TransitionToGame`), `PresentationPlayer`, `GameLog`

## Goal
From the main menu, open a replay (from the `Replays/` list or a file dialog, `.fdgreplay` or `.fdgsave`),
watch it on the real table view with the real animations, pause, step forward and back one decision at a
time, jump via a decision list, autoplay at a chosen speed, and see for each step who decided what. Done
when Chris has hand-verified it on a replay of a GUI game and on a client-recorded replay of a networked
game, and the acceptance list below is ticked.

## Scope (`FdgRaylib/Rendering/ReplayBrowserScreen.cs`, `ReplayScreen.cs`, wiring in `Program.cs`)
- Main menu: `Replays` button (between `Load Game` and `Options`; `MainMenuScreen.OnReplaysClicked`).
- `ReplayBrowserScreen` (`IAppScreen`): table of `ReplaySave.List()` newest first - date, armies,
  players (controller tags), rounds, steps, outcome, size - from `ReplaySummary` (header only; do not
  decompress every file on open: #450's header is the first JSONL line, read just that). `Open file...`
  (`TinyDialogs`, filter `*.fdgreplay;*.fdgsave`), `Delete`, `Back`. Parsing failures show the
  `ReplayFormatException` message inline, never a crash.
- `ReplayScreen`: a new `IAppScreen` (F2) that calls the same wiring `TransitionToGame` uses. It needs:
  - A `ReplayPlayer` (from #454); the renderer draws `player.TableState`; on `OnStoreReplaced` the
    screen re-runs the table-state subscription setup (`TransitionToGame`'s model subscription code; the
    #052 note about seeding `_placedModels` from already-positioned models applies - restored stores do
    not fire `OnPositionChanged`).
  - Player colours from the header slots (palette-indexed as `LobbyScreen.HandleLaunch` does).
  - `PresentationPlayer` fed the span's beats on each step; `GameLog` fed the span's log lines; the
    tactical overlay / hover tooltips in read-only mode if they come for free, otherwise off.
  - No resolvers, no outstanding-task display, no save button, no chat.
- Controls (ImGui bar, bottom): `|<`  `<`  `Play/Pause`  `>`  `>|`, speed `0.5x 1x 2x 4x Instant`, a
  step slider, and the label `Round 2 - Activation 5 - P1 (AI) - step 12 / 341`. Keys: Space
  play/pause, Left/Right step, Home/End, Esc back to the browser (see `ResolverKeybinds` for how
  advertised keys are centralised; do not collide with its in-game bindings - the replay screen has
  none of them active).
- Decision list (right panel, scrollable): one row per step `#012 R2 A5 P1 Warriors: Shoot`
  (`ReplayStep.Summary`), current row highlighted and kept in view, click = seek. A `Show folded`
  checkbox adds folded requests greyed. Markers (`Round 3`, `Saved`, `Resumed`, `Game over`) as
  separator rows.
- Details (below the list, for the current step): request `DisplayName`, who answered (controller), the
  describer summary, and a `Raw` toggle printing the reply JSON trimmed to ~20 lines.
- Autoplay (F1, decided): after a step's span beats finish (`PresentationPlayer` idle) plus a dwell
  of 0.75 s / speed; `Instant` = no beats, 0.1 s dwell. Autoplay stops at the end and at a `Resumed`
  marker (shows a one-line banner "Game was saved and resumed here; the unfinished activation was
  replayed").
- Step-back cost: a seek rebuilds the store (plan 4.3). Show a brief "seeking..." if it exceeds 100 ms;
  no frame may block the UI for more than that - do the `Load` on a worker and swap on the main thread
  (same lock-and-handoff discipline the resolvers use; see `docs/ResolverGuide.md`).
- ASCII-only in every string (CLAUDE.md).

## Tests (`FdgRaylib.Tests/ReplayViewerTests.cs`)
- Browser row formatting from a `ReplaySummary`; sort order; a corrupt file yields an error row.
- Control state machine (pure class `ReplayTransport`): play/pause/step/seek transitions, autoplay
  advance gating on "beats idle + dwell", stop at end and at `Resumed`.
- Decision-list row text for each marker kind and for a folded step.

## Acceptance (Chris, hand-verify)
- [ ] Open a replay of a local GUI game; models and terrain appear at step 0; stepping forward plays
      shooting beams / dice / wounds exactly as in play; log scrolls in sync.
- [ ] Step back from the middle; the table reverts; step forward again replays the same beats.
- [ ] Autoplay at 2x runs to the end without stalling; `Instant` finishes a 300-step game in under 30 s.
- [ ] Open a `.fdgsave` with an embedded replay; the `Saved`/`Resumed` separators appear.
- [ ] Open a client-recorded replay of a networked game; other players' decisions are listed.
- [ ] Esc returns to the browser; the browser lists the file just watched; Delete removes it.

## Verification
`dotnet build`; FdgRaylib.Tests green; headless smoke unaffected; acceptance list above.

## Notes
- (none yet)

## Decisions
- F1 (Chris, 2026-10-30): beat-paced autoplay + 0.75 s dwell. F2: new `ReplayScreen` + main-menu `Replays` button.
- (record the subscription re-seeding approach on `OnStoreReplaced`, any overlay left off)

## Outcome
(open)
