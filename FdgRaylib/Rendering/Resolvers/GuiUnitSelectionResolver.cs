using System.Numerics;
using FDG;
using FDG.Data;
using FDG.StageResolution.Requests;
using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// A <see cref="GuiSelectionResolver{UnitData}"/> that also lets the player click a valid unit directly on
/// the table canvas — the same interaction the shooting resolver offers — instead of only using the dialog
/// button list. Used for every SelectionRequest&lt;UnitData&gt;: spell targets (#103) and melee defender
/// selection. Valid target units are ringed on the canvas; clicking one (via the shared
/// <see cref="ICanvasInteractionHandler"/> seam driven by <see cref="TableHitTester"/>) selects it. The
/// dialog stays as a fallback and for the Back button.
/// </summary>
public class GuiUnitSelectionResolver : GuiSelectionResolver<UnitData>, IGuiCanvasOverlay, ICanvasInteractionHandler
{
    private float _scale = 10f;
    private float _tableH = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
    private int _originX, _originY;

    // Set by GetHoverLabel (called before Draw each frame) so Draw can emphasise the ring under the cursor;
    // cleared at the end of Draw so it lasts a single frame.
    private DataReference? _hoveredValidRef;

    public void UpdateLayout(float scale, int originX, int originY, float tableH)
    {
        _scale = scale;
        _originX = originX;
        _originY = originY;
        _tableH = tableH;
    }

    public string? GetHoverLabel(IUnit unit, IModel model)
    {
        SelectionRequest<UnitData>? request;
        lock (_lock) { request = _request; }
        _hoveredValidRef = null;
        if (request == null) return null;

        foreach (var opt in request.ValidOptions)
        {
            if (opt.Option.GetValue() == unit)
            {
                _hoveredValidRef = opt.Option.Reference;
                return $"Click to select {opt.Name}";
            }
        }
        foreach (var opt in request.InvalidOptions)
        {
            if (opt.Option.GetValue() == unit) return $"✗ {opt.Reason}";
        }
        return null;
    }

    public void HandleClick(IUnit unit, IModel model)
    {
        SelectionRequest<UnitData>? request;
        TaskCompletionSource<DataBinding<UnitData>>? tcs;
        lock (_lock) { request = _request; tcs = _tcs; }
        if (request == null || tcs == null) return;

        foreach (var opt in request.ValidOptions)
        {
            if (opt.Option.GetValue() == unit)
            {
                Complete(tcs, opt.Option);
                return;
            }
        }
    }

    public override void Draw(int screenW, int screenH)
    {
        DrawTargetRings();
        base.Draw(screenW, screenH); // the dialog window, on top of the canvas rings
        _hoveredValidRef = null;
    }

    // Ring every valid target unit's living models so the clickable targets are obvious; the one under the
    // cursor rings brighter and thicker.
    private void DrawTargetRings()
    {
        SelectionRequest<UnitData>? request;
        lock (_lock) { request = _request; }
        if (request == null) return;

        var dl = ImGui.GetBackgroundDrawList();
        uint colorValid = ImGui.ColorConvertFloat4ToU32(new Vector4(0.30f, 0.80f, 1.00f, 0.75f));
        uint colorHover = ImGui.ColorConvertFloat4ToU32(new Vector4(0.55f, 0.90f, 1.00f, 1.00f));

        foreach (var opt in request.ValidOptions)
        {
            bool hovered = _hoveredValidRef != null && opt.Option.Reference.Equals(_hoveredValidRef);
            uint color = hovered ? colorHover : colorValid;
            float thickness = hovered ? 3f : 2f;

            foreach (IModel model in opt.Option.GetValue().Models)
            {
                if (!model.GetIsAlive()) continue;
                var pos = model.Position;
                if (pos.x == 0f && pos.z == 0f) continue; // not yet placed on the table
                var (px, py) = InchesToPixel(pos.x, pos.z);
                dl.AddCircle(new Vector2(px, py), model.BaseRadiusInches * _scale + 3f, color, 32, thickness);
            }
        }
    }

    private (float px, float py) InchesToPixel(float x, float z)
    {
        float px = _originX + x * _scale;
        float py = _originY + (_tableH - z) * _scale;
        return (px, py);
    }
}
