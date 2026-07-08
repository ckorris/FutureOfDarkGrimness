# 186 — Harden network deserialization (allowlist binder)

**Status**: todo
**Related**: #070 (StableTypeSerializationBinder), QF1 password gate, NetworkingHandoff-2026-07-08.md

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
- 2026-07-08: Filed. Deferred from the QF1-10 tonight-batch because a correct allowlist is fiddly (generic
  closures, and getting it wrong silently breaks full-state sync + army-list transfer). Mitigated meanwhile
  by the QF1 password gate and recommending players run over a trusted tunnel (Tailscale).

## Decisions
- Network vs save settings must diverge: saves are local trusted input and legitimately carry a wide type
  set; the wire is untrusted. Don't tighten both with one binder.

## Outcome
(pending)
