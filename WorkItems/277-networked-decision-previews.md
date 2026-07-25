# 277 — Networked decision previews (live ghosts/paths for other players)

**Status**: in-progress
**Related**: #088 (request routing/notify lifecycle), #076 (disconnect resolved-broadcasts, reused as cleanup signal), #186 (wire allowlist — relay never deserializes payloads), #162 (movement resolver snapshots to be tapped in slice 2)

## Goal

While one networked player is contemplating a decision (moving ghosts, planning paths), the other
players currently stare at a frozen board with only the "waiting on Player N" text. Ship a reusable
preview channel: the deciding player's GUI resolver streams its transient visual state to every
other player, who see it drawn live (ghost bases + planned paths in the actor's color). The
transport must be payload-agnostic so future, visually different resolvers (targeting aim lines,
wound-assign pips) can join by defining a new payload + presenter with no engine changes. Done =
movement-family previews visible across the network, verified live host+client, with the transport
covered by engine tests.

## Slice plan

1. **Engine transport** (DONE 2026-07-24): wire messages, host relay w/ spoof+flood guards,
   channel/feed on `IFDGGame`, 15 tests.
2. **App side**: `PreviewPublisher` (~10 Hz, dedup, clear-on-end) + `RemotePreviewOverlay` +
   payload registry + `GhostPathPreview` (base/ghost slot split, roster indexing, 0.01" float
   quantization, omit ghosts equal to committed) + `GuiDefineMovementResolver` as first source.
   Manual verify: two instances on localhost.
3. **Follow-ups**: other movement-family resolvers (consolidation, aircraft advance, placements)
   implement the same payload; then non-movement request types on demand.

## Notes

- 2026-07-24: Slice 1 landed in the engine (submodule): `Network/Messages/StagePreviewMessages/`
  (Submit/broadcast x update/clear, chat-relay pattern so the host's own re-broadcast can't
  re-enter the relay), `StageResolution/Previews/` (`IPreviewChannel`/`PreviewChannel`,
  `IPreviewFeed`/`PreviewFeed`, internal `PreviewRelayer`), `IFDGGame.PreviewChannel/PreviewFeed`
  properties (ctor-created in both game classes), relayer constructed in
  `FDGServer.BuildContextAndLaunch`. 2129/2129 tests green (15 new in `StagePreviewTests`), full
  build + headless smoke green.

## Decisions

- **Typed semantic payloads over generic draw primitives.** The engine transports opaque
  `(slot, typeName, json)`; payload records + presenters live app-side (FdgRaylib owns all
  pixels). A draw-primitive wire vocabulary would bake styling into the sender and build a second
  drawing API; the movement family collapses into ONE payload+presenter anyway. TempVisuals was
  considered and rejected: host-originated, 3D-mesh oriented, no GUI drawer exists.
- **Slot-keyed latest-wins as the bandwidth cache.** `Slot` subdivides a player's preview so
  publishers split click-cadence state (committed waypoints, model roster — "base" slot) from the
  mouse-driven stream ("ghost" slot); receivers cache per (player, slot). A `BaseVersion` stamp
  inside the slice-2 payloads guards ghost-vs-stale-roster composition. Est. ~5-7 KB/s group-drag,
  ~1-2 KB/s single-drag vs ~25-30 KB/s naive.
- **Host-local players broadcast directly; only remote clients use the Submit path.** This is
  load-bearing for security: locally-dispatched messages reach connection-aware handlers with no
  connection, so a mixed path couldn't tell host-local from spoofed. Because locals never submit,
  the relayer registers connection-aware ONLY and can verify sender-connection-owns-claimed-player
  on every Submit it sees. Plus per-connection rate cap (40/s) + size cap (32K chars); payload
  JSON is re-broadcast untouched, never deserialized host-side (#186 surface unchanged).
- **Previews expire on the player's LAST outstanding task resolving** (reusing the
  notify-awaiting/resolved broadcasts) — covers crash/disconnect, where the publisher can never
  send its clear (#076 broadcasts resolved for each failed task). Scoped to the last task so a
  concurrent second request isn't wiped mid-stream.
- **`IFDGGame` properties, not `AssignInterfaces` params** — the bus exists at ctor time; avoids
  churning the 5-arg signature and its call sites.
- **Open information by design**: previews broadcast to all (GDF is open-info; the awaiting
  notification already broadcasts). Per-resolver opt-in (only `IPreviewSource` implementors
  publish) is the escape hatch for intent-sensitive future requests.

## Outcome

(when closed)
