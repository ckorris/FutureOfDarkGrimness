# 096 — Transport visuals: occupancy indicator + spillout presentation beats

**Status**: todo
**Related**: #035 (Transport), #056 (presentation beat stream), #053 (sound)

## Goal
Make Transport legible and lively in the GUI. Two deferred-from-#035 visual gaps, combined here:

1. **Occupancy indicator.** Because embarked units are off-table (at origin), the player can't *see* what's loaded in a transport. Add an on-table / panel indicator on a transport showing which (or how many) units are aboard and the remaining capacity — driven by the token-derived occupancy query (`TransportUtilities.GetOccupants` / `GetRemainingCapacity`). No engine state needed; it's a render of existing data.
2. **Spillout presentation beats.** The mid-combat destruction spillout (#035 slice E, `SpilloutOccupantsStage`) currently only logs. Give it presentation beats so it animates in lockstep with the visuals — occupants emerging from the wreck, the dangerous-terrain test rolls, the Shaken application — rather than appearing instantly. Hooks into the #056 beat stream / #053 sound cues.

"Done" = at a glance you can tell a transport is carrying units and what its remaining capacity is, and a transport blowing up plays a visible spillout sequence instead of a silent teleport.

## Notes
- 2026-06-21: Opened, combining two #035 deferrals (the user asked to merge "visual occupancy indicator" + "spillout presentation beats" into one item). Both are presentation-only (no engine/state change); the occupancy data and the spillout events already exist.

## Decisions

## Outcome
