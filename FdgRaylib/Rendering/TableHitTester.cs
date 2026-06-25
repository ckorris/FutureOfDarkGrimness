using FDG;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// Performs mouse-vs-table hit testing once per frame and caches the results so that
/// multiple systems (tooltip overlay, resolver canvas overlays) share the same answer
/// without redoing the math.
///
/// Call Update() at the start of the ImGui frame each game tick.
/// </summary>
public class TableHitTester
{
    public IUnit?    HoveredUnit    { get; private set; }
    public IModel?   HoveredModel   { get; private set; }
    public ITerrain? HoveredTerrain { get; private set; }

    /// <summary>Left mouse button clicked this frame, not consumed by an ImGui widget.</summary>
    public bool Clicked { get; private set; }

    public void Update(ITableState tableState, float scale, int originX, int originY, float tableH)
    {
        HoveredUnit    = null;
        HoveredModel   = null;
        HoveredTerrain = null;

        var io        = ImGui.GetIO();
        bool owned    = io.WantCaptureMouse;
        Clicked       = !owned && ImGui.IsMouseClicked(ImGuiMouseButton.Left);

        if (owned) return;

        var   mousePos   = io.MousePos;
        float tableX     = (mousePos.X - originX) / scale;
        float tableZ     = tableH - (mousePos.Y - originY) / scale;

        // Hit-test models — closest centre wins
        float bestDist = float.MaxValue;
        foreach (var unit in tableState.Units.Objects)
        {
            foreach (var model in unit.Models)
            {
                if (!model.GetIsAlive()) continue;
                var pos = model.Position;
                if (pos.x == 0f && pos.z == 0f) continue;

                float dx   = tableX - pos.x;
                float dz   = tableZ - pos.z;
                float dist = MathF.Sqrt(dx * dx + dz * dz);
                // Hit-test the true base shape (#149); dist still breaks ties between overlapping bases.
                if (model.BaseShape.ContainsLocalPoint(dx, dz) && dist < bestDist)
                {
                    bestDist     = dist;
                    HoveredModel = model;
                    HoveredUnit  = unit;
                }
            }
        }

        // Hit-test terrain only when no model is under the cursor
        if (HoveredUnit == null)
        {
            foreach (var terrain in tableState.Terrain.Objects)
            {
                if (terrain.IsPointWithinZone(new Float2(tableX, tableZ)))
                {
                    HoveredTerrain = terrain;
                    break;
                }
            }
        }
    }
}
