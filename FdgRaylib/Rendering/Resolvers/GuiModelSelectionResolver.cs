using System.Numerics;
using FDG;
using FDG.Data;
using FDG.StageResolution.Requests;
using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// A <see cref="GuiSelectionResolver{ModelData}"/> for picking a single MODEL (Takedown's target, a
/// single-model spell's victim), with the same table-canvas affordances the unit selector and the
/// wound-assignment dialog offer: every candidate model is ringed on the canvas, hovering a dialog button
/// (or the model itself) highlights it, clicking the rendered model selects it, and each dialog button
/// carries the model's stats (wounds remaining, weapons) instead of a bare "Model N".
/// </summary>
public class GuiModelSelectionResolver : GuiSelectionResolver<ModelData>, IGuiCanvasOverlay, ICanvasInteractionHandler
{
    private float _scale = 10f;
    private float _tableH = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
    private int _originX, _originY;

    // The model to emphasise this frame — set by a hovered dialog button OR by the canvas hover; cleared
    // at the end of Draw so it lasts a single frame. Main-thread only (both setters run on the UI thread).
    private ModelData? _hoveredModel;

    private static readonly uint RingCol      = ImGui.ColorConvertFloat4ToU32(new Vector4(0.30f, 0.80f, 1.00f, 0.75f));
    private static readonly uint HoverCol     = ImGui.ColorConvertFloat4ToU32(new Vector4(0.55f, 0.90f, 1.00f, 1.00f));
    private static readonly uint HoverHaloCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.55f, 0.90f, 1.00f, 0.45f));

    public void UpdateLayout(float scale, int originX, int originY, float tableH)
    {
        _scale = scale;
        _originX = originX;
        _originY = originY;
        _tableH = tableH;
    }

    // Dialog content: heading "Model 2  (2/3 wounds)" + a weapon line per group beneath it, so the choice is
    // informed without hunting the canvas. Single-wound models skip the counter (same convention as wounds).
    protected override (string Heading, IReadOnlyList<string> Details) OptionContent(
        DataBinding<ModelData> option, string name)
    {
        ModelData model = option.GetValue();
        string wounds = model.TotalWounds > 1f
            ? $"  ({model.TotalWounds - model.WoundsDealt:F0}/{model.TotalWounds:F0} wounds)"
            : "";
        return ($"{name}{wounds}", GuiAssignWoundsResolver.WeaponLines(model));
    }

    protected override void OnValidOptionHovered(SelectionRequest<ModelData>.ValidOption opt) =>
        _hoveredModel = opt.Option.GetValue();

    // #248: keyboard highlight rings the model like a hover (no tooltip to suppress here).
    protected override void OnValidOptionHighlighted(SelectionRequest<ModelData>.ValidOption opt) =>
        _hoveredModel = opt.Option.GetValue();

    // ICanvasInteractionHandler — hover a candidate model on the table for its stats, click to select it.

    public string? GetHoverLabel(IUnit unit, IModel model)
    {
        SelectionRequest<ModelData>? request;
        lock (_lock) { request = _request; }
        if (request == null) return null;

        foreach (var opt in request.ValidOptions)
        {
            ModelData candidate = opt.Option.GetValue();
            if (!ReferenceEquals(candidate, model)) continue;
            _hoveredModel = candidate;
            string wounds = candidate.TotalWounds > 1f
                ? $" ({candidate.TotalWounds - candidate.WoundsDealt:F0}/{candidate.TotalWounds:F0} wounds)"
                : "";
            return $"Click to select {opt.Name}{wounds}";
        }
        foreach (var opt in request.InvalidOptions)
        {
            if (ReferenceEquals(opt.Option.GetValue(), model)) return $"Invalid: {opt.Reason}";
        }
        return null;
    }

    public void HandleClick(IUnit unit, IModel model)
    {
        SelectionRequest<ModelData>? request;
        TaskCompletionSource<DataBinding<ModelData>>? tcs;
        lock (_lock) { request = _request; tcs = _tcs; }
        if (request == null || tcs == null) return;

        foreach (var opt in request.ValidOptions)
        {
            if (ReferenceEquals(opt.Option.GetValue(), model))
            {
                Complete(tcs, opt.Option);
                return;
            }
        }
    }

    public override void Draw(int screenW, int screenH)
    {
        DrawCandidateRings();
        base.Draw(screenW, screenH); // the dialog window, on top of the canvas rings
        DrawHoverHighlight();
        _hoveredModel = null;
    }

    private void DrawCandidateRings()
    {
        SelectionRequest<ModelData>? request;
        lock (_lock) { request = _request; }
        if (request == null) return;

        var dl = ImGui.GetBackgroundDrawList();
        foreach (var opt in request.ValidOptions)
        {
            ModelData model = opt.Option.GetValue();
            var pos = model.Position;
            if (pos.x == 0f && pos.z == 0f) continue; // not yet placed on the table
            var (px, py) = InchesToPixel(pos.x, pos.z);
            // Shape-aware ring: matches the true base (rectangle for rectangular bases), inflated 3px.
            ModelBaseRenderer.DrawOutlineImGui(dl, model.BaseShape, new Vector2(px, py), _scale,
                RingCol, 2f, 3f / _scale, model.Facing);
        }
    }

    // The hovered candidate (dialog button or canvas) rings brighter with a halo, connecting the list
    // entry to the figure on the board — same affordance as the wounds dialog.
    private void DrawHoverHighlight()
    {
        ModelData? model = _hoveredModel;
        if (model == null) return;
        var pos = model.Position;
        if (pos.x == 0f && pos.z == 0f) return;

        var (px, py) = InchesToPixel(pos.x, pos.z);
        var c = new Vector2(px, py);
        var dl = ImGui.GetBackgroundDrawList();
        // Shape-aware highlight: matches the true base outline (rectangle for rectangular bases).
        ModelBaseRenderer.DrawOutlineImGui(dl, model.BaseShape, c, _scale, HoverCol, 3f, 3f / _scale, model.Facing);
        ModelBaseRenderer.DrawOutlineImGui(dl, model.BaseShape, c, _scale, HoverHaloCol, 2f, 7f / _scale, model.Facing);
    }

    private (float px, float py) InchesToPixel(float x, float z) =>
        (_originX + x * _scale, _originY + (_tableH - z) * _scale);
}
