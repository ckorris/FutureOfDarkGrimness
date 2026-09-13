## 047 — Deployment zone selection: labels, canvas click, reading-order numbering

**Status**: done

### Goal

The Choose Deployment Zone dialog showed `Zone 1` / `Zone 2` buttons but the player had no way to tell which zone on the table each button referred to. Make zone identification obvious, let the player click a zone directly, and number the zones in Western reading order.

### Changes

All in `FdgRaylib/Rendering/Resolvers/GuiChooseDeploymentZoneResolver.cs`. No engine changes.

- Implements `IGuiCanvasOverlay` so the resolver receives table layout (scale + origin) each frame.
- Draws available zones on the canvas (`ImGui.GetBackgroundDrawList()` via `ZoneRenderer.DrawFilled`) using the same blue tint as the deploy stage. Unavailable zones are drawn grey.
- Renders a large `Zone N` label centred in each available zone, with a translucent dark backdrop for legibility.
- Hover state is unioned from two sources and applied to both the canvas zone (brighter fill + thicker outline) and the matching dialog button (forced `ButtonHovered` style):
  - Canvas hover: geometric hit-test of the cursor against the zone rect (gated by `WantCaptureMouse`).
  - Button hover: `ImGui.IsItemHovered()` carried forward one frame so canvas highlight follows the button.
- Clicking a zone directly on the canvas resolves the request with the corresponding `DataBinding<RectangularZone>`, same as clicking the button.
- Zones are renumbered in reading order at the resolver layer via `SortReadingOrder`: primary key = centre Z descending (top first); zones whose centre Z is within 1″ are treated as the same row and tie-broken by centre X ascending (left first). The sorted view is used for hit-test, draw, and click resolution so display order and click order can never disagree.

### Why GUI-side and not engine-side

`RectangularZone` is pure geometry (Left/Right/Bottom/Top, no name field). The engine doesn't track or care about zone ordering — `ChooseDeploymentZoneRequest` ships `AvailableZones` / `UnavailableZones` and the player just hands back a binding. The "Zone N" string is invented by each resolver from its list index, so reordering is a presentation concern and lives with the GUI. If a second consumer ever needs the same numbering (CLI prompts, AI logs), promote `SortReadingOrder` to a shared helper at that point.

### Outcome

Zone selection dialog now shows labelled zones on the table, supports direct-canvas clicks, and synchronises hover state between dialog and table. Numbering follows reading order (top-to-bottom, left-to-right) regardless of the order the engine emits the zones.
