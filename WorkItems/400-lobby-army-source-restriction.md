# 400 — Host setting: which game systems' armies a lobby accepts

**Status**: in-progress
**Related**: #378 (GameSystem field + Forge system filter + the mixed-system warning this replaces),
#153 (launch gate), #372/#388 (Random Army + BotArmyPicker), #395 (armies folder discovery)

## Goal

A synced, host-owned lobby setting — **Army Source: All / Grimdark Future / Age of Fantasy** — that
decides which game systems' armies the lobby accepts, changeable in the lobby like Army Points. Armies
that break it are highlighted the way over-points armies are, and a launch is BLOCKED (not merely
warned) while any slot is illegal. Random Army only ever rolls a legal army, and the armies folder is
scanned recursively so it can be organised into `GDF/` and `AoF/` subfolders.

Done when: the combo syncs host -> client, the Faction cell reads red with an explaining tooltip on a
wrong-system army, the Pts cell explains itself on both its colours, LAUNCH greys out with a tooltip
listing every blocker, Random Army never hands out an illegal list, and `#378`'s advisory mixed-system
line is gone.

## Notes

- 2026-09-12: Filed. Numbering checked against `origin/master` after `git fetch` + pull (superproject
  `cd7fd12` -> `cbab372`, engine -> `173cb5a`): index high-water **398**, archive max **399**, detail
  files max **399**, no `400-*` branch on origin. No collision.

## Decisions

- **2026-09-12 — `All`, not `Both` (owner).** The enum's permissive value is `EAllowedGameSystems.All`
  rather than `Both`, because OPR's custom-book tool can produce armies belonging to neither
  collection, and a two-valued name would have to be renamed the day one of those shows up. `All = 0`
  so a pre-#400 save (field absent from the JSON) resolves to today's behaviour.

- **2026-09-12 — the gate blocks, it does not warn (owner).** Three conditions hard-block a launch:
  wrong game system, over the points limit, and **no army assigned at all** (previously the host
  silently substituted a 100-pt stub). Deliberately NOT blocking: an *underbuilt* army (50+ pts under
  the limit, the yellow Pts state) stays a legal advisory — blocking it would reject perfectly legal
  lists and kill quick small-army test games in a big-points lobby.

- **2026-09-12 — Forge catalog errors keep their #153 override (owner).** Force-org problems (too many
  Heroes for the points, etc.) still raise the "Launch anyway?" confirm rather than blocking, so the
  house-rules escape hatch #153 was built for survives. This is what splits `LaunchGate` into two
  results: `BlockingProblems` (cheap, summary-level, computed every frame to drive the button) and
  `OverridableProblems` (runs the full `ListValidator` over a Forge army's embedded book, so it is
  computed only on click).

- **2026-09-12 — blocked launch reads as a disabled LAUNCH + tooltip (owner).** Not a modal: the
  button greys the moment anything is wrong and hovering it lists every blocker. The per-row red cells
  are the at-a-glance half of the same information.

- **2026-09-12 — the mixed-system warning is deleted, not kept (owner).** #378's advisory line
  ("! Mixed game systems ... launch if that is the plan") is superseded by the setting. Chris hadn't
  known it existed; two overlapping system notices in one panel is worse than one rule.

## Outcome

_(pending)_
