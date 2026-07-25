using System.Numerics;
using FDG;
using ImGuiNET;
using Raylib_cs;

namespace FdgRaylib.Rendering.Previews;

/// <summary>
/// Draws the movement family's remote previews (#277): each planning player's committed path lines
/// and ghost bases, in that player's palette color so multiple simultaneous planners stay
/// attributable. Ghost fills carry the actor's band tint (advance/rush/charge - the same color
/// language the planner sees locally, dimmed) inside a player-colored outline. Base shapes,
/// start positions, and facings fall back to the receiver's synced table state via ModelId; a
/// model that can't be resolved (killed or removed since the payload was built) is skipped.
/// </summary>
public class GhostPathPreviewPresenter : IRemotePreviewPresenter
{
    // The local overlay's band palette (GuiDefineMovementResolver) at reduced alpha - remote
    // previews deliberately read dimmer than the viewer's own planning overlay.
    private static readonly uint AdvanceFill = ImGui.ColorConvertFloat4ToU32(new Vector4(0.25f, 0.95f, 0.25f, 0.30f));
    private static readonly uint RushFill    = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.92f, 0.20f, 0.30f));
    private static readonly uint ChargeFill  = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.55f, 0.10f, 0.30f));
    private static readonly uint NeutralFill = ImGui.ColorConvertFloat4ToU32(new Vector4(0.40f, 0.85f, 1.00f, 0.30f));

    // Guid -> live model, lazily rebuilt when a lookup misses (models never change ID; the cache
    // only goes stale when it predates a model's creation, which a rebuild fixes).
    private Dictionary<Guid, IModel> _modelCache = new();

    public void Draw(PlayerID sourcePlayer, IReadOnlyDictionary<string, object> slots, in PreviewDrawContext ctx)
    {
        if (slots.TryGetValue(GhostPathSlots.Base, out object? baseObj) == false
            || baseObj is not GhostPathBase basePayload)
        {
            return;
        }

        // Ghosts compose only against the base they were built with; a mismatched version means
        // the base update is still in flight - draw the committed state alone this frame.
        GhostPathGhosts? ghosts = null;
        if (slots.TryGetValue(GhostPathSlots.Ghost, out object? ghostObj)
            && ghostObj is GhostPathGhosts g && g.BaseVersion == basePayload.BaseVersion)
        {
            ghosts = g;
        }

        ImDrawListPtr dl = ImGui.GetBackgroundDrawList();
        Color playerColor = ctx.ColorForPlayer(sourcePlayer);
        uint lineCol    = PlayerTint(playerColor, 0.80f);
        uint outlineCol = PlayerTint(playerColor, 0.85f);
        uint baseFill   = PlayerTint(playerColor, 0.18f);

        foreach (GhostPathBaseModel entry in basePayload.Models)
        {
            IModel? model = ResolveModel(entry.ModelId, ctx.TableState);
            if (model == null)
            {
                continue;
            }

            // A model still at the (0,0) unplaced sentinel (deployment, reserve arrival) has no
            // meaningful start - draw only endpoint footprints, never a line from the table corner.
            bool onTable = model.Position.x != 0f || model.Position.z != 0f;

            // Committed path polyline from the model's live position through its waypoints
            // (skipping the first leg when the model has no start to anchor it).
            bool anchored = onTable;
            (float prevX, float prevY) = ctx.InchesToPixel(model.Position.x, model.Position.z);
            foreach (GhostPathPoint point in entry.Waypoints)
            {
                (float x, float y) = ctx.InchesToPixel(point.X, point.Z);
                if (anchored)
                {
                    dl.AddLine(new Vector2(prevX, prevY), new Vector2(x, y), lineCol, 2f);
                }
                anchored = true;
                (prevX, prevY) = (x, y);
            }

            // Footprint at the committed endpoint, when the model has actually planned a move.
            if (entry.Waypoints.Count > 0)
            {
                Float2 facing = FacingOrFallback(entry.FacingX, entry.FacingZ, model);
                var center = new Vector2(prevX, prevY);
                ModelBaseRenderer.DrawFilledImGui(dl, model.BaseShape, center, ctx.Scale,
                    baseFill, outlineCol, 1.5f, facing);
                ModelBaseRenderer.DrawHeadingImGui(dl, model.BaseShape, center, ctx.Scale, facing, outlineCol);
            }
        }

        if (ghosts == null)
        {
            return;
        }

        foreach (GhostPathGhost ghost in ghosts.Ghosts)
        {
            if (ghost.ModelIndex < 0 || ghost.ModelIndex >= basePayload.Models.Count)
            {
                continue;
            }
            GhostPathBaseModel entry = basePayload.Models[ghost.ModelIndex];
            IModel? model = ResolveModel(entry.ModelId, ctx.TableState);
            if (model == null)
            {
                continue;
            }

            // Anchor at the committed endpoint (last waypoint, else the live position) - unless
            // the model is unplaced with nothing committed, where there is no anchor at all.
            (float gx, float gy) = ctx.InchesToPixel(ghost.X, ghost.Z);
            bool hasAnchor = entry.Waypoints.Count > 0
                || model.Position.x != 0f || model.Position.z != 0f;
            if (hasAnchor)
            {
                float fromX = entry.Waypoints.Count > 0 ? entry.Waypoints[^1].X : model.Position.x;
                float fromZ = entry.Waypoints.Count > 0 ? entry.Waypoints[^1].Z : model.Position.z;
                (float ax, float ay) = ctx.InchesToPixel(fromX, fromZ);
                dl.AddLine(new Vector2(ax, ay), new Vector2(gx, gy), lineCol, 2f);
            }

            Float2 facing = FacingOrFallback(ghost.FacingX, ghost.FacingZ, model);
            var ghostCenter = new Vector2(gx, gy);
            ModelBaseRenderer.DrawFilledImGui(dl, model.BaseShape, ghostCenter, ctx.Scale,
                BandFill(ghost.Band), outlineCol, 1.5f, facing);
            ModelBaseRenderer.DrawHeadingImGui(dl, model.BaseShape, ghostCenter, ctx.Scale, facing, outlineCol);
        }
    }

    private static uint BandFill(int band) => band switch
    {
        GhostPathBands.Rush => RushFill,
        GhostPathBands.Charge => ChargeFill,
        GhostPathBands.Neutral => NeutralFill,
        _ => AdvanceFill,
    };

    private static uint PlayerTint(Color color, float alpha)
        => ImGui.ColorConvertFloat4ToU32(new Vector4(color.R / 255f, color.G / 255f, color.B / 255f, alpha));

    private static Float2 FacingOrFallback(float facingX, float facingZ, IModel model)
    {
        // A degenerate facing (zero-length after quantization) falls back to the live model's.
        float lengthSquared = facingX * facingX + facingZ * facingZ;
        return lengthSquared > 0.0001f ? new Float2(facingX, facingZ) : model.Facing;
    }

    private IModel? ResolveModel(Guid modelId, ITableState tableState)
    {
        if (_modelCache.TryGetValue(modelId, out IModel? cached))
        {
            return cached;
        }

        // Miss: rebuild from live state, but only when the cache is actually stale (a model was
        // created since) - a payload naming a genuinely absent model must not rebuild every frame.
        int liveCount = tableState.Models.Objects.Count();
        if (liveCount == _modelCache.Count)
        {
            return null;
        }

        var rebuilt = new Dictionary<Guid, IModel>();
        foreach (IModel model in tableState.Models.Objects)
        {
            rebuilt[model.ID.ID] = model;
        }
        _modelCache = rebuilt;

        return _modelCache.TryGetValue(modelId, out IModel? found) ? found : null;
    }
}
