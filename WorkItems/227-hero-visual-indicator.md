# 227 — Visual flag indicating which model/unit is a Hero

**Status**: in-progress (implemented + tested; awaiting GUI hand-verification)
**Related**: #006 (Hero rule, engine-side, done), #183 (hero-subject seat attribution), #096 (transport visuals — precedent for a small badge overlay)

## Goal
Once a Hero has joined a unit (#006), there's no visual indicator in the GUI of which model is the hero or that the unit has one joined. Add some marker (badge/icon on the model, or a label on the unit panel) so it's visible during play, similar in spirit to #096's cyan "Carrying X/Y" transport badge.

## Decisions
- 2026-07-15: **Design fork resolved with the user.** Marker = a small **white star with a dark outline, drawn centred ON the hero model's base** (not floating nearby). Chosen over a gold outline/ring: gold would blend with the selection/hover halos and the weapon/charge range rings. The star sits INSIDE the base overlaying the player-colour fill, so it never collides with those cues (they all live AROUND the base). White+dark-outline reads on any player colour, light or dark. Rejected: gold anything (halo collision), a drawn crown/diamond above the base (clutters at low zoom), a permanent ring (collides with halos). Also chosen: a **hover-tooltip tag** showing the hero's OWN Quality/Defense (which diverge from the host unit). Declined the always-on "Hero" name-badge above the unit name.
- **No engine change.** Hero identity comes from `IUnit.JoinedHeroModelId` (already on the interface, #006). The tooltip's divergent Q/D come from `UnitData.HeroAttachment` (public, off the concrete type) via an `is` pattern that degrades to a bare "Hero" for any non-`UnitData` `IUnit`.
- Star fill drawn as centre-anchored triangles with both windings (like `ModelBaseRenderer`'s heading marker) to sidestep Raylib back-face culling with no double-blend; outline via `DrawLineEx` over the 10 perimeter points. Size = `clamp(baseRadiusPx * 0.5, 4, 14)` so it stays legible zoomed out and never swamps a large base.

## Notes
- 2026-07-15: **Implemented.** New `FdgRaylib/Rendering/HeroMarkerRenderer.cs` (pure `IsHeroModel`, `StarPoints`, `OuterRadiusPx`, `FormatHeroTag` + the Raylib `DrawStarRaylib`). Wired the star into `RaylibRenderer.DrawModels` (drawn last, per hero model, faded by the presentation alpha) and the tag into `TableTooltipOverlay.DrawUnitTooltip` (gold text right after the model section). Tests: `FdgRaylib.Tests/HeroMarkerRendererTests.cs` (5 cases — star vertex radii/orientation/centring, radius clamp, ASCII tag) mirroring `TransportBadgeRendererTests`/`HealthBarRendererTests`. Verify: `dotnet build` clean (0/0), full app suite **332 passed / 0 failed**, headless smoke exit 0. **Remaining: GUI hand-verification** (star + tooltip can't be driven headless) — build a Hero-joined army, confirm the star sits on the hero model and the hover tag shows the hero's Q/D.
- 2026-07-15: Filed from user playtest feedback.

## Outcome
