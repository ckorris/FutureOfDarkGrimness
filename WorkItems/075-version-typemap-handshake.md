# 075 — Version / type-map handshake on client join + enums-as-strings

**Status**: done (engine + app; awaiting GUI hand-verification of a real mismatch)
**Related**: audit §6; branch `082-network-robustness` (second of #082/#075/#076/#077); composes with #076 (host detects the client's self-disconnect after rejection) and #065 (loopback fixture would add an end-to-end dispatch test)

## Goal

Both ends currently assume identical builds — same store type map (positional `TypeID` baked into every serialized `DataReference`), same message registry, same enum ordinals. A drifted build silently corrupts replicated data. Add a join handshake that refuses an incompatible client with a readable error, and serialize enums by name so reordering members stops being a silent wire/save format change.

## Decisions

- **2026-06-25** — **Host-validates, not the audit's literal "client validates the greeting reply."** The client sends its `(ProtocolVersion, TypeMapHash)` in `NewLobbyClientGreeting`; the host checks them at the top of `OnReceiveNewClientGreeting` (before either the new-game or resume branch) and, on mismatch, sends a single-connection `LobbyJoinRejectedMessage(reason)` and returns **without adding the client to the roster**. Rationale: the host is authoritative — rejecting before roster mutation avoids a half-joined client, and the host produces one canonical reason string. The client surfaces it.
- **2026-06-25** — **`ClientModal` awaits the join outcome before navigating to the lobby.** `LobbyViewModel_Client` exposes `Task<string?> JoinResultTask` that completes `null` on `LobbyPlayerIDAssignment` (accept) or with the reason on `LobbyJoinRejectedMessage` (reject). The modal `Task.WhenAny`s it against an 8 s timeout; on reject/timeout it shows the reason in the existing `_status` line, `Dispose()`s the VM, and `client.Disconnect()`s. So a mismatch is visible exactly where the user initiated the connection, instead of bouncing them out of a half-loaded lobby. The client's self-disconnect is what the host's read loop sees (composing cleanly with #076).
- **2026-06-25** — **Handshake fields are primitives (`int`, `string`).** The greeting and rejection must deserialize even when the rest of the wire format differs between builds, so they carry no enums/records. An older pre-handshake client sends no version/hash → they default to `0`/`null` → treated as a mismatch and rejected (verified by test). The bootstrap gap (a *new* client joining an *old* host that has no check) is inherent to introducing a handshake and acceptable pre-release.
- **2026-06-25** — **Type-map fingerprint = SHA-256 of the ordered `Type.FullName`s**, not `string.GetHashCode` (per-process randomized → unstable across machines). Computed once and cached in `NetworkProtocol.LocalTypeMapHash`. The `Version` const is the manual catch-all for format changes the hash can't see (message registry, enum encoding); bumped implicitly to its inaugural value `1` with the enum change.
- **2026-06-25** — **`StringEnumConverter` added to `GameDataStore.GetJsonSettings()`** (the central Newtonsoft settings shared by the wire and store-value blobs). It reads **both** string and integer tokens, so old int-encoded saves still load and a new build's string output is read by either — i.e. the save format change is bidirectionally tolerant, so **no save `CurrentVersion` bump needed**. The save *envelope* uses default settings (not these), so only embedded store values change. Confirmed: full suite incl. `GameSaveLoadTests`/`MessageSerializationTests` stayed green.

## Notes

- **2026-06-25** — Implemented + verified. Engine suite **776/0**, full `dotnet build` clean, headless exit 0.
  - Engine: `Data/Containers/GameDataStore.cs` (`StringEnumConverter` in settings; new `GetTypeMapFingerprint()`); `Network/NetworkProtocol.cs` (new — `Version`, cached `LocalTypeMapHash`, `TryValidateJoin`); `Network/Messages/LobbyMessages/NewLobbyClientGreeting.cs` (+`ProtocolVersion`/`TypeMapHash`); `Network/Messages/LobbyMessages/LobbyJoinRejectedMessage.cs` (new); `Network/Connection/Lobby/LobbyViewModel_Host.cs` (validate + reject); `Network/Connection/Lobby/LobbyViewModel_Client.cs` (send version/hash, handle rejection, `JoinResultTask`).
  - App: `FdgRaylib/Rendering/ClientModal.cs` (await handshake, surface reason / timeout, disconnect on failure).
  - `Tests/NetworkProtocolTests.cs`: 9 tests (validate accept/version-mismatch/hash-mismatch/legacy-defaults, fingerprint stable + differs, greeting + rejection round-trip, enum-as-name + integer-tolerant read).
  - End-to-end host→reject→client-surfaces dispatch is **not** an automated test (needs the #065 loopback fixture); the logic is unit-tested and the wiring is GUI-hand-verifiable.

## Outcome

Shipped the version + type-map handshake (host rejects incompatible clients before roster add, modal surfaces the reason) and enum-as-strings on the wire/store-values. Save format is tolerant in both directions, so existing saves still load. Deferred: message-registry hashing (the `Version` counter covers it manually); end-to-end loopback test (#065). **Awaiting GUI hand-verification** of a real cross-build rejection.
