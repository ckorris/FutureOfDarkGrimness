# 297 — Objectives are held per side: backend landed, UI treatment open

**Status**: backend done (2026-07-27); UI facet open - needs Chris's pick on the display treatment
**Related**: #296 (crowded-game drift - surfaced the rule question), #257 (victory pools objectives
per team), #191 (Tactician umbrella)

## Goal

Objectives belong to a SIDE, not a player (Chris, 2026-07-27: "they should not be contested to
neutral [by allies]. Actually objectives should be set to one team or the other, but I'm not sure
how the UI should be for that"). Backend semantics first; the UI shows team ownership once the
treatment is decided.

## Backend (DONE 2026-07-27)

`ITeamExtensions.ReconcileObjectiveOwner` is the single authority, used by BOTH
`ReconcileObjectivesStage` and the AI's `TacticalAnalysis.ProjectObjectives` (so the Tactician's
projection can never drift from the engine rule):

- Exactly one SIDE with players in range holds the marker. Sticky toward the current owner (the
  original seizer keeps it while a teammate guards it - including guarding it alone); otherwise
  the seizing side's first-registered in-range player is recorded.
- Opposing sides in range still contest to neutral; nobody in range leaves the current owner.
- `OwnerID` stays a plain `PlayerID` - no data-model or save-format change. "Team ownership" is a
  rule about WHO can hold/contest, not a new owner type; victory already pools per team (#257).
- With no registered teams every player is their own side, so 1v1 (and every benchmark) is
  bit-identical - the reconcile stage even keeps the exact old log wording.
- Tactician: the #296 ally-contest penalty and step-off bonus became obsolete the moment allies
  stopped contesting each other and were REMOVED same-day; the walk-away penalty now skips when a
  teammate stays in range to hold the marker. 5 new tests (4 stage + 1 projection mirror);
  suite 2233/2233.

## UI facet (OPEN - decide, then build)

Today the marker (`RaylibRenderer.DrawObjectives`) and the scoreboard pips (`StatusHudOverlay`,
one player-colored pip + count per player) are strictly per-player. With per-side holding, a
marker recorded to player A is really TEAM A+B's - the display should say so. Options:

1. **Team-colored markers + team-grouped scoreboard (recommended).** Each team gets a color
   (derive from its first player's color, or introduce team colors in the lobby); markers tint by
   the owning side; the scoreboard groups pips per team with a pooled count (matching how victory
   is actually decided, #257). Per-player detail stays visible in tooltips/logs.
2. **Minimal:** keep per-player marker colors (the recorded owner's), add only a pooled team
   score line to the scoreboard. Cheapest; markers can look "wrong" when a teammate guards them.
3. **Badged:** keep player colors but stamp the team number/icon on the marker ring and pips.
   Most information, most visual noise.

Open questions for Chris: which option; whether teams get their own colors (lobby UI change); and
whether the in-game log should say "held by Team N" instead of the player line.

## Notes

- 2026-07-27: filed; backend landed with #296's session (engine commit alongside reconciliation
  30's renumber). The crowded-2v2 repro (`Scenarios/crowded-2v2-3k.json`) is the natural eyeball
  scene for whatever UI treatment is picked.

## Decisions

- 2026-07-27 (Chris): allies must not contest each other's markers; objectives conceptually
  per-team; backend first, UI treatment to be decided (this item).

## Outcome

(open)
