# 449 — Replay system umbrella

**Status**: in-progress (planning done 2026-10-30; no code yet)
**Related**: plan `docs/replay-plan.md`; sub-items #450-#457; parallel effort #415 (OPR 3.6 upgrade, `feature/opr-3.6`)

## Goal
A replay of every game, recorded on host and client, auto-saved at game end, embedded in `.fdgsave` so a
resumed game keeps one continuous replay, playable in the GUI (pause / step per decision / autoplay) and
inspectable from the command line. Done when #457 has merged `feature/replay` into `master` in both repos
and Chris has hand-verified a networked game's replay on both machines.

This item owns: the plan document, the branch (`feature/replay` in both repos, from `master`), the
periodic `master` -> `feature/replay` merges (after each sub-item closes), the item table in the plan
(section 6), and the open forks (section 7).

## Notes
- 2026-10-30: Plan written after a feasibility pass (see the plan's section 3 for what already exists).
  Size measured with a throwaway FdgLab probe (plan section 5; probe archived at
  `~/Projects/fdg-lab-scratch/replay-probe/`, deliberately not in the repo): ~0.35-0.5 MB per 2k 1v1
  game compressed, saves roughly 1.5-2x their current size once a replay is embedded. Beats were not
  measurable in the lab (no presenter) - #450 measures them on the first real recording and updates the
  table. Numbers 449-457 reserved on `master` (Reconciliations entry dated 2026-10-30). Local branches
  `feature/replay` created in both repos from `master` / engine `479888d`; not pushed yet.

## Decisions
- D1-D5 in the plan (record-what-happened, broadcast replies, both containers, AI steps count, always a
  concrete seed) are Chris's, 2026-10-30. Do not reopen them in sub-items.
- Feature branch rather than slice-by-slice on `master`: the work is red for its middle third (format +
  viewer) and `master` must stay shippable while the 3.6 upgrade also runs; see plan section 8 for why
  the two efforts do not collide.

## Outcome
(open)
