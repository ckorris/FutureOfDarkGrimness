# 236 — Freeform builder silently strips a Forge army's embedded block

**Status**: done (logic tested; the modal itself awaits GUI hand-verification)
**Related**: #156 (Army Forge - decision 4 predicted exactly this: STJ serializes by declared type), `ArmyBuilderScreen.Load/Save`, `BuiltArmyFile`

## Goal
Opening a Forge-built `.fdgarmy` in the freeform Army Builder and saving silently dropped the embedded `Book`+`Selections` block (Load deserialized as plain `ArmyListFile`, discarding the block; Save serialized the declared base type). The file kept playing but the Forge could never re-edit it. Hit in the wild 2026-07-15: "Battle Brothers 2k - Elite Shooting" lost its 10,500-line block in a routine base-size edit (restored from git history, base fixes preserved, commit on master). Done = the strip can never happen silently.

## Decisions
- **Detach-with-consent, not preserve.** Save on a Forge-built army now opens a "Detach from Army Forge?" modal (Cancel focused as the safe default) explaining the block is dropped and the Forge can't re-edit; only "Save detached" writes. Rejected: silently carrying the block through the freeform save - freeform edits desync the selections, and a later Forge re-open would recompile from them, silently discarding the freeform edits (the same data loss, reversed).
- Load now reads every file as `BuiltArmyFile` (a superset; plain armies leave the block null) purely to RECOGNIZE Forge files - the freeform editor still edits only the base fields. After a confirmed detached save (and on New), the flag clears - the on-disk result is a plain freeform army.

## Notes
- 2026-07-15: Implemented + tested (`HasForgeBlock` detection pinned in `ArmyBuilderScreenTests`, incl. the RuleJson round-trip both ways). The modal itself is GUI - hand-verify by loading a Forge army in the freeform builder and hitting Save.

## Outcome
Shipped same-day with the Battle Brothers restore. Freeform Save on a Forge-built army requires an explicit "Save detached" confirm; nothing is stripped silently. Follow-up ideas if it ever matters: a Forge-side "reclaim" (rebuild selections from a detached file) - not planned.
