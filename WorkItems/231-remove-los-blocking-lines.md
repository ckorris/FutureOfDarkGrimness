# 231 — Remove LoS blocking lines from the shooting UI

**Status**: todo
**Related**: #046 (built `GetFirstBlockingHit` + the stub/marker visual, done), #045 (cover indication)

## Goal
The line-of-sight blocking indicator (the stub line + marker at the first blocking terrain, from #046) reads as confusing: when a VALID target stands in front of the blocker, the blocked-line visual makes the situation look shootable/unshootable in ways the player misreads. User direction: remove the blocking lines. Scope: strip the blocked-LoS line/marker rendering from the shooting flow (keep the underlying engine API - other overlays may still use it); check what #045's cover indication still needs from it before deleting shared pieces.

## Notes
- 2026-07-15: Filed from user playtest feedback ("can be confusing if something valid is in front of the blocker").

## Decisions

## Outcome
