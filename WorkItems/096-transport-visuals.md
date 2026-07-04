# 096 — Transport visuals: occupancy indicator + spillout presentation beats

**Status**: todo
**Related**: #035 (Transport), #056 (presentation beat stream), #053 (sound)

## Goal
Make Transport legible and lively in the GUI. Two deferred-from-#035 visual gaps, combined here:

1. **Occupancy indicator.** Because embarked units are off-table (at origin), the player can't *see* what's loaded in a transport. Add an on-table / panel indicator on a transport showing which (or how many) units are aboard and the remaining capacity — driven by the token-derived occupancy query (`TransportUtilities.GetOccupants` / `GetRemainingCapacity`). No engine state needed; it's a render of existing data.
2. **Spillout presentation beats.** The mid-combat destruction spillout (#035 slice E, `SpilloutOccupantsStage`) currently only logs. Give it presentation beats so it animates in lockstep with the visuals — occupants emerging from the wreck, the dangerous-terrain test rolls, the Shaken application — rather than appearing instantly. Hooks into the #056 beat stream / #053 sound cues.

"Done" = at a glance you can tell a transport is carrying units and what its remaining capacity is, and a transport blowing up plays a visible spillout sequence instead of a silent teleport.

## Notes
- 2026-07-03: **Facet 1 (occupancy indicator) built — app-only, awaiting GUI hand-verify.** A compact cyan `Carrying X/Y` badge (X = occupied spaces, Y = capacity) is drawn above every Transport unit in `TableTooltipOverlay.DrawUnitOverlays`, slotted into the existing chip/name/health stack between the name and the health bar; shown regardless of the Labels toggle (status at a glance, like chips/health) and shown even when empty (`Carrying 0/6`) so remaining capacity always reads. The hover tooltip gains a cargo section: header `"N units aboard (X/Y spaces):"` (or `"Empty (0/Y spaces)"`) + one line per occupant `"Name (K space[s])"`. All counts read live from `TransportUtilities` (`IsTransport`/`GetOccupiedSpaces`/`GetCapacity`/`GetOccupants`/`GetUnitSpaceCost`) over `_tableState.Units.Objects` each frame — no engine change (occupants ride off-table at origin, so the badge is the only on-table cue). Text formatting factored into a pure `TransportBadgeRenderer` (mirrors `HealthBarRenderer`); `TransportBadgeRendererTests` (4) pin the ASCII-safe wording + pluralization. App suite 82/0, build clean, headless smoke exit 0. **Chosen with the user:** compact badge + hover (over names-inline / drawn-chip). Facet 2 (spillout beats) next.
- 2026-06-21: Opened, combining two #035 deferrals (the user asked to merge "visual occupancy indicator" + "spillout presentation beats" into one item). Both are presentation-only (no engine/state change); the occupancy data and the spillout events already exist.

## Decisions
- 2026-07-03: **Occupancy indicator = compact `Carrying X/Y` badge + hover breakdown** (decided with the user, over names-inline and a drawn chip). X/Y counts *spaces* (a Tough model costs 3), which the badge leaves implicit; the hover spells out "spaces" and per-occupant cost to disambiguate. Shown for any transport incl. empty. App-only render of existing token-derived occupancy — no engine state.

## Outcome
