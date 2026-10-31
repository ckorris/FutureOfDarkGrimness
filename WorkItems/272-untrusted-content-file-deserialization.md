# 272 — Untrusted content files deserialize with permissive $type handling (RCE-class)

**Renumbered 265 -> 272 on 2026-07-23 (reconciliation 22)** - origin/master had independently used
#265 for the lobby Battlefield dropdown. Pre-renumber engine/app commit messages keep #265.

**Status**: done
**Related**: #186 (same hole on the wire path — this shares its binder), #271 (the server browser
makes stranger-to-stranger sharing likely), #160/#070 (stable-type binder background), #058 (STJ
migration would subsume this). Engine commit `f432c03` + superproject app-side terrain loader.

## Goal

Opening a content file that came from a stranger (`.fdgarmy`, `.fdgsave`, terrain layout JSON) must
not be able to execute code. Today all three load through Newtonsoft with
`TypeNameHandling.Auto` and a binder that falls back to `DefaultSerializationBinder` for any
unregistered `$type` (`StableTypeSerializationBinder.cs` — registered names resolve stably, but an
*unknown* `$type` resolves by assembly-qualified name). That fallback is the textbook Newtonsoft
deserialization-gadget vector: a hand-crafted file naming a gadget type can run code at load time.
Army files additionally use `TypeNameHandling.Auto` directly (`FdgRaylib/Cli/TerrainLoader.cs:11`,
army list load path per CLAUDE.md).

Done looks like: file loading rejects unregistered `$type` tokens (allowlist binder, same approach
as #186's wire fix) — with a readable "this file references unknown content" error instead of a
crash — or the loaders migrate to a closed-vocabulary format. Old saves that only use registered
types keep loading; only unknown-type payloads are refused.

## Why filed now (2026-07-23)

Before #271, content files came from friends and the risk was theoretical. A public server browser
creates a community of strangers, and "here, try my army list" is the natural next interaction —
the file format must not be a code-execution vector when that happens. Filed during #271's
security review; owner is aware they are less familiar with security specifics, so the summary
above spells out the mechanism.

Severity: high impact (arbitrary code execution on the opening player's machine), low current
exposure (no file-sharing channel in the app yet). Should land before any in-app or community
file-sharing feature, and ideally before publicly announcing the server browser.

## Notes

- 2026-07-23 (later): Implemented (owner authorized; owner explicitly accepted save breakage and
  asked for this sooner because it touches saves). Engine `f432c03` + the app-side terrain loader
  in the superproject bump.
- 2026-07-23: Filed from #271's security review. The engine is a submodule (read-only by default)
  — the binder change needs the same authorization/cadence as #186.

## Decisions

- **One shared binder, not a second file-specific one.** Files and the wire have the same threat
  model (attacker-controlled `$type`) and the same legitimate type set (engine types / registered
  IDs / benign collections). Promoted #186's binder to `AllowlistSerializationBinder` in
  `FDG.SaveLoad`, deleted `StableTypeSerializationBinder` (its writes were already identical), and
  pointed the store settings, both terrain loaders, and the wire factory at it. `WireSerializationBinder`
  folded away; `WireJsonSettings` kept (it isolates the wire's explicit MaxDepth).
- **The store binder was the load-bearing find, and it also closes a #186 gap.** The scariest path
  isn't the file DTO — it's `GameDataStore.CreateFromReferenceAndJson`, which rebuilds store entries
  from JSON using the store's settings. That path is shared by save/load AND the network full-state
  sync (`GameDataUpdateReceiver` via `StoreReplay`), so #186's envelope hardening did NOT cover the
  sync's entry payloads. Swapping the store binder fixes save/load and the full-state sync together.
- **Army/scenario files are NOT in scope — and that's correct, not a silent cut.** They load via
  System.Text.Json (`RuleJson.Options`), which uses closed kind-tagged polymorphism (no
  `PolymorphismOptions`/open `$type`), so they can't resolve an arbitrary CLR type. Not the
  Newtonsoft-gadget class. (If a custom STJ converter ever maps a kind string to a type by
  reflection, revisit.)
- **ResolveType (save type-map) hardened without breaking legacy primitive FullNames**: resolve in
  the engine assembly, or as an UNQUALIFIED core name (the no-comma guard never loads an arbitrary
  assembly the way a qualified `Type.GetType` would), then gate the result through `IsAllowed`.
  `LegacyFullNameTypeMap_StillLoads` (System.Int32) still passes.
- **Saves lose the permissive fallback** (owner-approved). A legit save from this build still loads
  (all its types are engine/registered/primitive); only a crafted unknown-type payload is refused.

## Outcome

Shipped `f432c03` (engine) + superproject bump (app-side `FdgRaylib/Cli/TerrainLoader.cs`). Every
untrusted Newtonsoft `$type` surface — store rebuild (save/load + network full-state sync), both
terrain loaders, and the save type-map resolver — now resolves only engine types, registered stable
IDs, or benign collections; anything else throws (a readable load error for files, a disconnect on
the wire). One shared `AllowlistSerializationBinder`. 6 new tests (store binder round-trip + hostile
payload, terrain legit-incl-legacy + hostile); full suite 2013/2013; real save round-trip verified
end-to-end via `--make-scenario` -> `--scenario`. Army/scenario files (STJ, closed polymorphism)
are out of the gadget class and untouched. This also retroactively closes the full-state-sync entry
gap that #186's envelope pass didn't reach.
