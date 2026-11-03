# 070 — Save/load rename fragility

**Status**: Done 2026-07-03 — merged to master (engine `9b78d5b`, rebased onto the diverged master in reconciliation 10; superproject bump to follow). Suite 1124/0 post-rebase (was 1033/0 pre-rebase, before the 23 parallel commits landed).
**Related**: #052 (save/load — owns the format), #039 (`CreateFromTypeMap` — found stale, see below), #075 (network type-map hash), #062 (store simplification — the store's *internal* positional TypeID, deliberately out of scope), audit §9/§2.

## Goal
`GameSaveSerializer` recorded types by raw `Type.FullName`, so renaming or moving any persisted type
silently broke every existing save. Decouple the save format from C# type names via stable string IDs.

## Decisions (with the user)
- **Scope = comprehensive (both rename surfaces).** There are two independent places a FullName was the
  format: (a) the save's top-level type-map fingerprint, and (b) every polymorphic `$type` Newtonsoft
  records inside store entries under `TypeNameHandling.Auto`. Fixing only (a) would leave base shapes,
  token payloads, token clear-triggers, and terrain zones rename-fragile. So #070 covers both.
- **No `NetworkProtocol.Version` bump.** The binder rides the store's shared JSON settings, so it also
  changes the on-wire `$type` strings for the full-state sync — normally a wire-format change. The user
  waived the protocol bump: nothing is distributed, so both ends always run the same build.
- **Migration = a FullName fallback, not a migrator.** Zero `.fdgsave` files exist anywhere and save/load
  isn't even GUI-verified yet, so there are no v1 saves to migrate. Bumped `CurrentVersion` 1→2; the
  registry-miss→FullName fallback (in both `ResolveType` and the binder) is cheap insurance, not a live
  compat path. No `IGameSaveMigrator`.
- **`GetTypeMapFingerprint` (the #075 network build-skew hash) stays FullName-based.** It exists to detect
  that a joining client was built against a different store shape — a same-build concern, not a
  persistence-rename concern — so it's orthogonal to #070 and left untouched.
- **The store's internal positional TypeID is NOT touched.** Re-keying the store's live type map by name
  is the audit's separate #062a recommendation. #070 only changes the *string identity* written to the
  save/wire, not the positional scheme.

## What was built (engine)
- **`SaveLoad/SaveTypeRegistry.cs`** — one bidirectional `Type ↔ stable-string-ID` map, the single source
  of truth for both consumers. Covers the 16 registered store types (incl. the TypeID-0 placeholder, newly
  exposed as `GameDataStore.PlaceholderType`) and every persisted polymorphic concrete: `IBaseShape`
  (`baseShape.circle/rect`), `IZone` (`zone.circular/rotated/composite` + `rectangularZone` reused from the
  store-type id + `zone.list` for the one persisted `List<IZone>` collection wrapper), `TokenPayload`
  (`tokenPayload.*`), `TokenClearTrigger` (`clearTrigger.*` — non-nullable, so on **every** saved token).
  `GetIdOrFullName` falls back to `Type.FullName` for anything unregistered. **IDs are permanent** — add,
  never rename.
- **`SaveLoad/StableTypeSerializationBinder.cs`** — an `ISerializationBinder` installed on
  `GameDataStore`'s JSON settings. Emits/reads registry IDs for `$type` payloads (assembly-less name = one
  of ours), delegating to Newtonsoft's `DefaultSerializationBinder` for anything unregistered — purely
  additive, so an omission degrades to today's FullName behavior, never to a load failure.
- **`GameSaveSerializer`** — writes stable IDs into the type map; `ResolveType` resolves registry-first,
  FullName fallback; `CurrentVersion` 1→2. **`SavedTypeEntry.FullName` → `TypeId`.**
- **`GameDataStore`** — binder added to `_jsonConvertSettings`; `PlaceholderType` exposed.

## Verification
- Engine suite **1033/0** (was 1027 pre-change; +6 net). Targeted: `GameSaveLoadTests` (+4: stable-IDs-not-
  FullNames, polymorphic `$type` round-trip incl. nested composite/rotated zones + tokens, FullName-fallback
  load), `SaveTypeRegistryTests` (reflection pins: every store type + every `IBaseShape`/`TokenPayload`/
  `TokenClearTrigger`/`IZone` concrete has an ID; bidirectional). `BaseShapeTests`/`NetworkProtocolTests`/
  rule-rehydration/game-progress all still green (binder is on the shared settings).
- Full `dotnet build` clean; headless smoke exits 0 (tie after 4 rounds).
- The "no `$type` still carries `, FutureOfDarkGrimness`" scan drove out the one non-obvious case: the
  `CompositeZone.Parts` `List<IZone>` collection wrapper (`List\`1[[FDG.IZone, …]]`), now registered as
  `zone.list`.

## Notes / residuals
- **#039 is stale — CreateFromTypeMap is fully implemented** (both overloads, no NIE; done as #052 Phase 2,
  tested by `GameSaveLoadTests`). Its index line was moved to Done alongside this item.
- **STJ rule blobs are already rename-safe.** `ModelData/UnitData/Weapon._ruleDefinitionsJson`
  (`RuleAttachmentPersistence`, #059/#095) is a *separate* System.Text.Json path, not the Newtonsoft store
  settings — and it's `kind`-tagged (a stable discriminator string, not a C# type name), so it was never
  part of this rename surface.
- The reflection pins guard the four polymorphic *families*; a future stored type introducing a new
  polymorphic collection (like `IReadOnlyList<IFoo>`) isn't caught by the family pins but is caught by the
  `PolymorphicPayloads_…` scan if it exercises that type. Documented, not silently cut.

## Outcome
Saves are decoupled from C# type names: rename or move any registered store type, base shape, token
payload, token clear-trigger, or terrain zone and existing saves still load. Delivered comprehensively
(top-level type map + `$type` payloads) via one shared stable-ID registry and a Newtonsoft binder, with a
FullName fallback and reflection pins that fail CI if a new persisted type ships without an ID. Engine-only;
no app change.
