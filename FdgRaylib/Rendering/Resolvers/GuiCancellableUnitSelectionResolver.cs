using System.Numerics;
using FDG;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// A <see cref="GuiCancellableSelectionResolver{UnitData}"/> that also lets the player click a valid unit on
/// the table canvas — the cancellable counterpart of <see cref="GuiUnitSelectionResolver"/>. Used for the
/// #100 pre-attack cross-unit targeting picker (grant/mark/debuff a unit). Valid units are ringed; clicking
/// one selects it; the dialog's Back button (Cancel) is unaffected.
/// </summary>
public class GuiCancellableUnitSelectionResolver
    : GuiCancellableSelectionResolver<UnitData>, IGuiCanvasOverlay, ICanvasInteractionHandler
{
    private float _scale = 10f;
    private float _tableH = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
    private int _originX, _originY;
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
        CancellableSelectionRequest<UnitData>? request;
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
            if (opt.Option.GetValue() == unit) return $"Invalid: {opt.Reason}";
        }
        return null;
    }

    public void HandleClick(IUnit unit, IModel model)
    {
        CancellableSelectionRequest<UnitData>? request;
        TaskCompletionSource<CancellableResult<DataBinding<UnitData>>>? tcs;
        lock (_lock) { request = _request; tcs = _tcs; }
        if (request == null || tcs == null) return;

        foreach (var opt in request.ValidOptions)
        {
            if (opt.Option.GetValue() == unit)
            {
                Complete(tcs, new Selected<DataBinding<UnitData>>(opt.Option));
                return;
            }
        }
    }

    public override void Draw(int screenW, int screenH)
    {
        DrawTargetRings();
        base.Draw(screenW, screenH);
        _hoveredValidRef = null;
    }

    private void DrawTargetRings()
    {
        CancellableSelectionRequest<UnitData>? request;
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
                if (pos.x == 0f && pos.z == 0f) continue;
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
