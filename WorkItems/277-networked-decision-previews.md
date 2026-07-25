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
2. **App side** (DONE 2026-07-25, hand-verified): `PreviewPublisher` (~10 Hz, dedup, clear-on-end)
   + `RemotePreviewOverlay` + payload registry + `GhostPathPreview` (base/ghost slot split, roster
   indexing, 0.01" float quantization, omit ghosts equal to committed) +
   `GuiDefineMovementResolver` as first source.
3. **Follow-ups** (model-based trio DONE 2026-07-25): consolidation, aircraft advance and
   place-objects share the payload. Remaining: non-movement request types on demand.
4. **Marker family** (DONE 2026-07-25): objective + terrain placement previews via their own
   payload family (non-model markers). Awaiting hand-verify alongside slice 3.

## Notes

- 2026-07-25 (slice 4): objective + terrain placement previews - the deferred marker facet. New
  "marker" payload family (`MarkerPreviewPayloads.cs`): `ObjectiveMarkerPreview` (number, center,
  radii, Pending/Valid) and `TerrainFootprintPreview` (ETerrainType as int, footprint flattened to
  wire circles + quads with rotation baked in - never the polymorphic IZone, #186), single
  "marker" slot with NO base/ghost split (whole ghost is a few hundred bytes at 10 Hz, well under
  the movement family's envelope; committed markers/terrain reach clients via synced table state).
  `MarkerFootprints.Flatten` converts `ZoneExtensions.Primitives()` leaves (circle / rect /
  rotated-rect only) to wire primitives. `MarkerPreviewPresenter`: local color language dimmed
  (grey disc + number + seizure ring; terrain-type tint via `TerrainTypeColors`, now internal) in
  a player-tinted outline that goes red when the placer hovers an illegal spot. Both resolvers
  snapshot the drawn ghost in Draw (`_ghostSnapshot` + `_snapshotRequest` guard, same pattern as
  consolidation): live-cursor AND frozen-pending ghosts stream; off-table mouse / template
  selection = null preview = publisher clears. `GhostPathQuantize` renamed `PreviewQuantize` (own
  file) - it is cross-family wire hygiene now. +3 PreviewSourceTests (objective/terrain null
  contracts incl. no-Draw guard, rotated-composite flatten). 2129/2129 engine + 592/592 app,
  build + headless smoke green.
  HAND-VERIFY CHECKLIST (slice 4, two instances): (e) objective placement: hovering ghost with
  number + 3" ring visible remotely, red outline when hovering an illegal spot, frozen ghost while
  the Confirm dialog is up, nothing while mouse is off-table; (f) terrain placement: nothing
  during template pick, footprint ghost with terrain tint + rotation remotely, red when
  overlapping, frozen while confirming.
- 2026-07-25 (later): Slice 2 GUI HAND-VERIFY PASSED (user, two instances on localhost). Slice 3
  landed: consolidation, aircraft advance and place-objects opted into the GhostPath payloads.
  Shared bits: `GhostPathQuantize` (0.01" + ghost epsilon, replaces the movement resolver's
  privates), `GhostPathBands.Neutral` = 3 (cyan fill - consolidation slides / placements have no
  band semantics), presenter suppresses start-anchored lines for models at the (0,0) unplaced
  sentinel (deployment/reserve models aren't on the table yet). Per resolver:
  `GuiConsolidationMoveResolver` mirrors movement (per-frame `_ghostSnapshot` + `_snapshotRequest`
  guard; facing = model facing, group-rotated - #250 no travel rotation);
  `GuiAircraftAdvanceResolver` publishes the living roster with no waypoints + ghosts at
  position + heading x distance (the presenter's anchor line doubles as the approach; no snapshot
  guard needed - `_currentDistance` resets in Resolve under the request lock);
  `GuiPlaceObjectsResolver<ModelData>` rides `_placed[i]` <-> `ModelsToPlace[i]` index pairing
  (same invariant as the reach rings): placements = single-waypoint base entries at click cadence,
  cursor ghost / group phantoms = ghost slot; non-ModelData roster shares nothing.
  NEW: `FdgRaylib.Tests` exists now (arrived from master) - added `PreviewSourceTests` (5 tests:
  quantize, roster order/index pairing, Neutral/Advance bands, BaseVersion pairing, null
  contract). 2129/2129 engine + 589/589 app green, build + headless smoke green.
  DEFERRED (explicitly, not silently): objective + terrain placement previews - non-model markers
  with no ModelId; need a small marker/footprint payload family + presenter of their own.
  AWAITING GUI HAND-VERIFY (slice 3): two instances ->
  (a) consolidation after wipeout/disengage: paths + cyan ghosts visible remotely, group rotation
  included; (b) aircraft advance: ghost bases sliding along the heading remotely; (c) deployment:
  placed models + cursor ghost/formation phantoms remotely, NO lines from the table corner;
  (d) Teleport-style reposition: anchor lines from current positions to placements.
- 2026-07-25: Slice 2 landed (app side): `FdgRaylib/Rendering/Previews/` - `IPreviewSource` +
  `PreviewState`, `PreviewPublisher` (10 Hz, serialize-and-compare dedup, clear on request end /
  player handoff), `RemotePreviewOverlay` (feed-version-gated decode cache, registered-type
  allowlist, per-family presenters), `GhostPathBase`/`GhostPathGhosts` payloads (base/ghost slot
  split, roster indexing, 0.01" quantization, ghosts-equal-committed omitted) +
  `GhostPathPreviewPresenter` (player-color paths/outlines, band-tinted ghost fills).
  `GuiDefineMovementResolver` is the first source (BuildPreviewState mirrors the local
  final-ghost facing math; `_snapshotRequest` guards against streaming a previous move's ghost
  snapshot while animations hold Draw closed). Wired via `GuiResolverOverlay.AttachPreviews`
  (GameGuiWiring) -> renderer picks both up at TransitionToGame, publisher ticks after resolver
  Draw. 2129/2129 green, full build + headless smoke green. AWAITING GUI HAND-VERIFY (below).
  Verify checklist: host + client on localhost, one moves a unit ->
  (a) other player sees committed paths + live ghost in mover's color, band tint on the ghost;
  (b) group mode: whole-unit phantoms + rotation visible remotely;
  (c) preview vanishes when the mover clicks Done/Back/Skip;
  (d) idle mouse = no network chatter (no visible effect - spot-check via debug log if desired);
  (e) mover's own screen shows no double-draw of its own ghosts.
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
