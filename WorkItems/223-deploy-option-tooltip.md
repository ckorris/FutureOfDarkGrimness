# 223 — Deploy screen: hover unit options for full-spec tooltip

**Status**: in-progress (implemented; awaiting build + GUI hand-verification)
**Related**: #161 (resolver consistency pass), `GuiUnitSelectionResolver.cs`, `UnitStatBlockRenderer.cs`, the weapon-rules-everywhere pass (tooltip / shoot list / deploy panel / picker)

## Goal
When choosing which unit/loadout to deploy, hovering an option should show a tooltip with the unit's full stat block (mirroring the read-only stat-block preview already built for #106's army builder) rather than requiring a separate lookup. Done = hovering a deploy-screen unit option surfaces model count/Quality/Defense/weapons/rules at a glance.

## Decisions
- 2026-07-15: The deploy / activate unit picker is `GuiUnitSelectionResolver` (a `GuiSelectionResolver<UnitData>`). Its base calls `OnValidOptionHovered(opt)` from inside the button-draw loop, while the option's button is the hovered ImGui item -- so a tooltip raised there attaches to the hovered row. Rendered the stat block into an `ImGui.BeginTooltip()` there.
- **Shared `UnitStatBlockRenderer`** (new, `FdgRaylib/Rendering/`): name, model count + Q/D, mobility, each distinct weapon on its own datasheet line (rules included via `GetWeaponNameAndStats`), then unit special rules. One `includeRuleDescriptions` flag: `true` for this hover tooltip (fuller), `false` for the compact deploy-placement panel stat block (which shares the same renderer), so the two never drift.

## Notes
- 2026-07-15 (later): **Verified.** Full build clean; engine suite 1642/0; app suite 343/0; headless smoke exit 0. Remaining: GUI hand-verification only.
- 2026-07-15: **Implemented.** Overrode `GuiUnitSelectionResolver.OnValidOptionHovered` to raise a full-spec tooltip via the new `UnitStatBlockRenderer`. Because the picker backs every `SelectionRequest<UnitData>` (deploy, activate, spell target, melee defender), the tooltip helps in all of those, not just deploy. **Build/test pending** (the app was running and locking the build output). **Deferred:** invalid options (e.g. already-activated units during activation) don't get the tooltip -- the base draws them `BeginDisabled`, so `IsItemHovered` is false and `OnValidOptionHovered` never fires for them; surfacing stats for invalid rows would need an `AllowWhenDisabled` hover hook in the base. Not needed for the deploy case (all deploy options are valid).
- 2026-07-15: Filed from user playtest feedback.

## Outcome
