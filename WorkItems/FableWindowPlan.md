# Fable window plan / handoff (2026-07-08)

Chris had Fable (top-tier model) access ending ~2026-07-11. The agreed priority plan was largely
executed during the window; this is the handoff state for any session, any model, any worktree.

## Done during the window (all pushed to origin/master)

- **#183** hero-join Subject-seat attribution (Option C: all-models gate + validator enforcement +
  Subject-seat model visibility) - done, archived; spawned #184 (Counter strike sequencing,
  deferred by design).
- **#185** RuleParticipant struct refactor - done, archived.
- **#166a** rule fire-lint + **#163** rule-trace channel (`--trace-rules`) - done.
- **#167** T1 scenario compiler (`--make-scenario`), T4 seeded dice (`GameSettings.DiceSeed`), and
  the lobby-skip `--scenario` launch (GUI + headless) - done. See `Scenarios/README.md`.

## Remaining, in priority order (fine on any model)

1. ~~**#169 Transport Rout occupant spill**~~ DONE 2026-07-08 (Option B: spillout at the
   `UnitDestructionNotifier` choke point; signed off, GUI hand-verified, archived).
2. ~~**GUI `--scenario` hand-verify**~~ DONE 2026-07-08 (segfaulted on first run - GL resources
   pre-window; fixed via `RaylibRenderer.OnWindowReady`, then verified in-window).
3. **#175 Fear/Fearless rulebook check** - rules research, not engineering.
4. **#065 networking loopback tests** (zero TCP transport tests exist) and **#166 residuals**
   (RuleInteractionTests, SaveLoadRoundTrip helper).
5. **#167 residuals**: `--gen-ledger` manual-test ledger generator, OPR import reconciliation report.

## Not worth top-tier model time

GUI hand-verification backlog (human-only), mechanical chores (#179/#180/#068/#170/#176), further
audits (three done July 6-7; residue filed as #163-#185), #162 tactical overlay (good work but
polish; loses to correctness + tooling).

## How to apply

Start from item 1; present options + recommendation and get sign-off before implementing #169.
The scenario compiler exists now - lean on `Scenarios/` + `--trace-rules` when verifying rule
behavior by hand.
