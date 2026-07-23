# 265 — Untrusted content files deserialize with permissive $type handling (RCE-class)

**Status**: todo
**Related**: #186 (same hole on the wire path), #264 (the server browser makes stranger-to-stranger
sharing likely), #160/#070 (stable-type binder background), #058 (STJ migration would subsume this)

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

Before #264, content files came from friends and the risk was theoretical. A public server browser
creates a community of strangers, and "here, try my army list" is the natural next interaction —
the file format must not be a code-execution vector when that happens. Filed during #264's
security review; owner is aware they are less familiar with security specifics, so the summary
above spells out the mechanism.

Severity: high impact (arbitrary code execution on the opening player's machine), low current
exposure (no file-sharing channel in the app yet). Should land before any in-app or community
file-sharing feature, and ideally before publicly announcing the server browser.

## Notes

- 2026-07-23: Filed from #264's security review. The engine is a submodule (read-only by default)
  — the binder change needs the same authorization/cadence as #186.

## Decisions

(none yet)

## Outcome

(open)
