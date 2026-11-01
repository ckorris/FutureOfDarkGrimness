# 461 — Networked client of a resumed game draws no model bases (labels only)

**Status**: todo
**Related**: found by #457's two-process networked runs; #309 (client model instances replaced per update);
`RaylibRenderer.SubscribeToModel` / `OnModelPlaced`

## Goal
A remote client that joins a resumed save sees every model on the table from the first frame, as the host does.
Done when the repro below shows the model circles on the client at launch, and they stay drawn through the game.

## Notes
- 2026-10-31 (#457): two GUI processes on one machine over real TCP (host in Xephyr `:5`, client in `:6`). The host
  loads a scenario-compiled save (`--make-scenario`, round 3, one Marksmen + one Rending Squad per side) with
  Load Game, the client joins by Direct Connect, the host clicks Resume. On the **client**, every unit's label and
  activation badge is drawn but **no model circles** - not its own, not the host's; the host's table is normal.
  The client can still play (the resolver panels list its units) and its replay records correctly.
  - **Pre-existing, not a replay regression:** the same flow on an `origin/master` build (`46072fd`) shows the
    same thing.
  - **Depends on the save:** the same client, rejoining a save written by the game itself mid-round
    (Esc -> Save Game in a networked game, then Load Game), drew its models normally. A #187 recovery save derived
    from the scenario game showed the bug again. So it looks tied to how a `--make-scenario` save's models carry
    their positions, or to the order the client's full sync creates models vs units, rather than to resuming as
    such.
  - Suspects: `SubscribeToModel` seeds only when `model.Position` is non-zero at subscribe time and otherwise
    waits for `OnPositionChanged` on the instance it captured; #309 notes a client deserializes a NEW instance
    per update, so a position that arrives on a replacement instance never fires on the subscribed one.
  - Repro files (lab, not in the repo): `~/Projects/fdg-lab-scratch/457/net/net457.json` (scenario) and
    `net457.fdgsave` (compiled).

## Decisions
- (none yet)

## Outcome
(open)
