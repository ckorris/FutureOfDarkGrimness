# 270 — A game resumed through the lobby cannot be saved and loaded again

**Status**: done
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

- 2026-07-23 (fix): the bug is one level below where this item first placed it. A test that only
  creates a `TeamData`, destroys it, creates another in the same slot and round-trips the store fails
  the same way — no lobby, no `PlayerSlotInfo`, no resume. Both directions originally proposed here
  were aimed at the symptom; see Decisions for what was actually wrong and the third option that came
  out of it (Chris's call).

## Decisions

- **The generation guard was being asked a question it can't answer.** `Create` pre-increments a
  slot's generation each time it fills it, so a recycled slot is at 2+. `CreateFromReference` demanded
  the incoming generation be exactly one past the store's own — right for the LIVE incremental network
  stream, where sender and receiver are in step and a gap means a missed message; meaningless for a
  whole-store snapshot, where the target store starts every slot at 0. A snapshot's references are
  internally consistent by construction (every binding pointing at that slot carries the same
  generation), so there was nothing wrong with the save at all — only with the gate.

- **Fixed by splitting the two paths** (option A of three, Chris's call): `CreateFromReplay` for
  snapshot rebuilds adopts the entry's generation, keeping the invariants that still mean something
  (real index, right type, slot not already filled by this replay, generation >= 1 — 0 being an unset
  reference). `CreateFromReference` keeps both ordering guards for the live stream. One change fixes
  every type and every resume path, present and future.

- **Rejected: re-crewing slots in place** (the original option 2 in this file) — it would have fixed
  the two known call sites while leaving the store unable to round-trip a recycled slot, so the next
  destroy-and-recreate anywhere brings the bug back. **Rejected: normalizing generations on save** (the
  original option 1) — correct in principle but it means rewriting references inside every serialized
  payload (each `DataBinding<T>` field, nested and polymorphic), which is graph surgery on the save
  format to work around a gate that was simply too strict.

- **Adopted, not flattened.** Replay takes the generation as-is rather than resetting it to 1, so a
  stale reference to a recycled slot is still stale after a load. Flattening would have made old
  references silently valid again.

## Outcome

Fixed at the data layer. `ComponentStore.CreateFromReplay` / `GameDataStore.CreateFromReplayJson` are
the snapshot-rebuild entry points, used by `StoreReplay` (shared by save/load and the join-time network
catch-up); the live incremental path is untouched and keeps its ordering guards. No save-format change,
so existing saves load exactly as before.

Tests: `GameSaveLoadTests` (recycled slot round-trips; survives three save/load cycles),
`GameDataStoreTests` (live path still rejects a skipped generation; replay adopts one, keeps index /
type / already-assigned / generation-0 guards, still grows to fit), `ResumeSettingsOverrideTests`
(the reported shape end to end: resume, save, reopen, slots and settings intact). Engine 2066 green,
app 564 green, headless smoke exit 0.

Verified on the actual artifact: the `.fdgsave` written from the resumed game during #265's hand-verify
— the exact file that aborted with `FutureGeneration` — now loads headless (exit 0) and reopens in the
GUI on its Mars-Like board with the round-3 position intact.
