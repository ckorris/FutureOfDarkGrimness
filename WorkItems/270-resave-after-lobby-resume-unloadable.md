# 270 — A game resumed through the lobby cannot be saved and loaded again

**Status**: todo
**Related**: #052 (save/load), #054 (client-initiated saving), found while hand-verifying #265

## Goal
`Load Game -> Resume -> Save Game` must produce a `.fdgsave` that loads. Today it produces a file that
throws on load, so a session can only survive one save/load cycle: the second load is unrecoverable.

## Notes

- 2026-07-23: Found while verifying #265's resume fix in the running app. Loaded
  `WayTooManyInBack.fdgsave` (round 3/4), resumed it, saved immediately, then reopened the new save -
  it aborts before the window appears:

  ```
  Store replay stalled with 4 unresolved entr(ies)
   ---> InvalidDataReferenceAssignmentException[PlayerSlotInfo]: ... Reason: FutureGeneration
  ```

  The original save loads fine, so this is a property of the RE-saved file, not the loader.

- **Deterministic repro** (no GUI): compile any scenario store, round-trip it through
  `GameSaveSerializer`, destroy the `PlayerSlotInfo` entries and rebuild `PlayerSlot`s exactly as
  `LobbyViewModel_Host.LaunchResume` does, construct the resume `FDGServer`, then
  `GameSaveSerializer.Load(GameSaveSerializer.Save(store))` -> the exception above. Skip the
  destroy step and it passes, which is why `ResumeRoundCountTests` (destroys, but never re-saves) and
  the `--scenario` path (never destroys) both stay green.

- **Cause.** `LaunchResume` destroys the saved `PlayerSlotInfo` entries and lets the rebuilt slots
  create fresh ones (`LobbyViewModel_Host.cs`, "the saved PlayerSlotInfo entries are removed first so
  the rebuilt slots don't create duplicates"). Destroy + create bumps each slot index's generation, so
  the re-saved references carry a generation ahead of where a fresh store starts. On load,
  `ComponentStore.CreateFromReference` rejects them: `_generations[index] < reference.Generation - 1`
  -> `FutureGeneration`. Nothing about the world data is wrong; the reference generations are simply
  unreplayable from zero.

- Confirmed **pre-existing**, not introduced by #265: the repro fails identically with #265's resume
  write-back removed, and the type that fails (`PlayerSlotInfo`) is untouched by that work.

## Decisions

(none yet - not started)

## Outcome

(open)

## Likely directions

Not yet designed. Two obvious candidates, both need a look at what generations are actually for:

1. **Normalize on save.** Rewrite references to a canonical generation when serializing, so any store
   re-saves into something replayable from an empty store. Broadest fix; touches every type.
2. **Re-crew in place.** Have `LaunchResume` update the existing `PlayerSlotInfo` values (`SetValue`)
   instead of destroy + create, so generations never advance. Narrower, but only fixes this one type -
   any other destroy-and-recreate during a resume would reintroduce it.

Option 2 is the smaller change and directly matches the comment's intent ("don't create duplicates");
option 1 is the one that makes the invariant true in general.
