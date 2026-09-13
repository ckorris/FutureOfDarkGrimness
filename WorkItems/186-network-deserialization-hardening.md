# 186 — Harden network deserialization (allowlist binder)

**Status**: done
**Related**: #070 (StableTypeSerializationBinder), QF1 password gate, NetworkingHandoff-2026-07-08.md,
#271 (public listing made this a prerequisite), #273 (transport limits, done), #272 (SAME hole on
files - still open). Engine commits `2ecf201` (envelope) + `e292e18` (request/reply bodies).

## Goal
The networked message path deserializes attacker-controllable JSON with Newtonsoft `TypeNameHandling.Auto`,
and the store's `StableTypeSerializationBinder` falls back to `DefaultSerializationBinder`
(`SaveLoad/StableTypeSerializationBinder.cs:43`), which resolves arbitrary assembly-qualified `$type` names.
On an internet-exposed port that is the classic Newtonsoft gadget surface. Done = the network serializer
(the settings used by `MessageSerializer` / `RequestMessageSender` / the data-sync path) only resolves
`$type` tokens that are either SaveTypeRegistry IDs or types in our own assemblies (incl. generic collection
closures like `List<FDG.X>`); an unknown/foreign type is rejected, not instantiated. Saves keep the current
permissive fallback (local trusted input) — this is network-path-only.

## Notes
- 2026-07-23: Implemented (owner authorized the security pass). Two commits:
  - `2ecf201` — `WireSerializationBinder` + `WireJsonSettings.For`, swapped into `MessageSerializer`
    (the message envelope). 10 tests.
  - `e292e18` — **completion.** During review found the envelope was only half the surface: the
    inner stage request/reply JSON bodies are deserialized SEPARATELY, with the store's permissive
    settings, at `StageResolverRegistry.ResolveRequestAsJson_Typed` (request body -> attacked by a
    malicious HOST, i.e. the #271 stranger-server case) and `RequestMessageSender.DeserializeAndReturnReply`
    (reply body -> attacked by a malicious CLIENT). Both routed through the same factory; +2 tests
    exercising a hostile `$type` in a request body. This was the load-bearing half.
- 2026-07-08: Filed. Deferred from the QF1-10 tonight-batch because a correct allowlist is fiddly (generic
  closures, and getting it wrong silently breaks full-state sync + army-list transfer). Mitigated meanwhile
  by the QF1 password gate and recommending players run over a trusted tunnel (Tailscale).

## Decisions
- Network vs save settings must diverge: saves are local trusted input and legitimately carry a wide type
  set; the wire is untrusted. Don't tighten both with one binder.
- **Allowlist by construction, not a per-type registration list** (owner asked for a security-vs-flexibility
  re-check): resolve only registered `SaveTypeRegistry` IDs, types in the ENGINE assembly, or benign
  collections thereof. Every known Newtonsoft gadget is a framework/library type, so the assembly rule
  kills the whole attack class; strict per-type enumeration's only extra gain is blocking unregistered
  *engine* types (thin - they're data holders) at the cost of a permanent registration treadmill whose
  failure mode is runtime multiplayer breakage for every new request/result/beat (no common base to lint).
  Both ends run the same build (#075 type-map gate), so no extra rename-stable IDs are needed.
- **One source of truth**: `WireJsonSettings.For(store)` builds the hardened settings; all three wire
  sites use it, in BOTH directions, so the outbound `BindToName` can never drift from the inbound
  allowlist. `MaxDepth` pinned to 64 explicitly.
- **Standing invariant left behind**: engine types must never gain side-effectful deserialization
  (file/process/reflection-by-name in ctors or setters) - the binder trusts the engine assembly
  wholesale, so a gadget-shaped engine type would be inside the fence. Cheap to honor; note in review.
- Wire format unchanged (BindToName identical to the stable binder) -> no protocol version bump.
- Deleted the dead `WhitelistedTypeDeserializer.cs` prototype (never referenced; leaked an NUnit using
  into the shipping namespace).
- Newtonsoft 13.0.3 default MaxDepth 64 already bounds the deep-nesting stack DoS.

## Outcome
Shipped `2ecf201` + `e292e18` (2026-07-23). Every wire deserialization - message envelope, stage
request bodies, reply bodies - resolves `$type` only to registered stable IDs, engine types, or benign
collections; framework types (the entire gadget class) throw `JsonSerializationException`, which the
read loop turns into a disconnect. 12 tests (`WireSerializationBinderTests`) incl. hand-crafted hostile
frames and a hostile request body. Full suite 2008/2008; `NetworkedFullStateSyncTests` exercises the
binder against the full store sync. Clears the #271-announcement prerequisite. The identical hole on
FILES (`.fdgarmy`/`.fdgsave`/terrain) is #272, still open - saves deliberately keep the permissive
fallback here.
