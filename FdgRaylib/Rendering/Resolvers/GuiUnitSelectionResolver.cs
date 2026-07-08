using System.Numerics;
using FDG;
using FDG.Data;
using FDG.StageResolution.Requests;
using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// A <see cref="GuiSelectionResolver{UnitData}"/> that also lets the player click a valid unit directly on
/// the table canvas — the same interaction the shooting resolver offers — instead of only using the dialog
/// button list. Used for every SelectionRequest&lt;UnitData&gt;: choose-unit-to-deploy, choose-unit-to-
/// activate, spell targets (#103) and melee defender selection. Valid target units are ringed on the canvas;
/// clicking one (via the shared <see cref="ICanvasInteractionHandler"/> seam driven by
/// <see cref="TableHitTester"/>) selects it. Each dialog button carries the unit's stats (models, Quality,
/// Defense, weapons) and hovering a button highlights that unit on the table (and vice versa) — matching the
/// model picker (<see cref="GuiModelSelectionResolver"/>) and the wound-assignment dialog. The dialog stays
/// as a fallback and for the Back button. Units not yet on the table (deployment) simply have no rings.
/// </summary>
public class GuiUnitSelectionResolver : GuiSelectionResolver<UnitData>, IGuiCanvasOverlay, ICanvasInteractionHandler
{
    private float _scale = 10f;
    private float _tableH = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
    private int _originX, _originY;

    // The unit to emphasise this frame — set by a hovered dialog button (OnValidOptionHovered) OR by the
    // canvas hover (GetHoverLabel); cleared at the end of Draw so it lasts a single frame. Main-thread only.
    private DataReference? _hoveredValidRef;

    private static readonly uint HoverCol     = ImGui.ColorConvertFloat4ToU32(new Vector4(0.55f, 0.90f, 1.00f, 1.00f));
    private static readonly uint HoverHaloCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.55f, 0.90f, 1.00f, 0.45f));

    public void UpdateLayout(float scale, int originX, int originY, float tableH)
    {
        _scale = scale;
        _originX = originX;
        _originY = originY;
        _tableH = tableH;
    }

    // Dialog content: a bright heading (unit name) + smaller, dimmer detail lines (models/Quality/Defense,
    // then weapons) so the pick is informed without hunting the canvas — the same treatment the model picker
    // and wounds dialog give. Unit-wide special rules are omitted here (they live in the hover tooltip);
    // opt.Name may already carry a reserve suffix like "(Ambush)", which is kept as the heading.
    protected override (string Heading, IReadOnlyList<string> Details) OptionContent(
        DataBinding<UnitData> option, string name)
    {
        UnitData unit = option.GetValue();
        int liveModels = unit.Models.Count(m => m.GetIsAlive());
        var weapons = unit.AllWeapons()
            .DistinctBy(w => w.Name)
            .Select(w => (w.Name, w.RangeInches))
            .ToList();
        return UnitOptionLabel.Build(name, liveModels, unit.Quality, unit.Defense, weapons);
    }

    protected override void OnValidOptionHovered(SelectionRequest<UnitData>.ValidOption opt) =>
        _hoveredValidRef = opt.Option.Reference;

    public string? GetHoverLabel(IUnit unit, IModel model)
    {
        SelectionRequest<UnitData>? request;
        lock (_lock) { request = _request; }
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
        // Only the hovered unit is ringed -- no persistent ring on every valid unit (that was visual noise
        // during activation, where every one of the player's units is a valid pick). The dialog draws first;
        // DrawHoverHighlight runs after so a hovered dialog button lands the highlight the same frame.
        base.Draw(screenW, screenH);
        DrawHoverHighlight();
        _hoveredValidRef = null;
    }

    // The hovered unit (dialog button or canvas) rings with a halo, connecting the list entry to the figures
    // on the board — same affordance as the model picker and wounds dialog.
    private void DrawHoverHighlight()
    {
        if (_hoveredValidRef == null) return;

        SelectionRequest<UnitData>? request;
        lock (_lock) { request = _request; }
        if (request == null) return;

        var dl = ImGui.GetBackgroundDrawList();
        foreach (var opt in request.ValidOptions)
        {
            if (!opt.Option.Reference.Equals(_hoveredValidRef)) continue;

            foreach (IModel model in opt.Option.GetValue().Models)
            {
                if (!model.GetIsAlive()) continue;
                var pos = model.Position;
                if (pos.x == 0f && pos.z == 0f) continue;
                var (px, py) = InchesToPixel(pos.x, pos.z);
                var c = new Vector2(px, py);
                // Shape-aware highlight: matches the true base outline (rectangle for rectangular bases).
                ModelBaseRenderer.DrawOutlineImGui(dl, model.BaseShape, c, _scale, HoverCol, 3f, 3f / _scale, model.Facing);
                ModelBaseRenderer.DrawOutlineImGui(dl, model.BaseShape, c, _scale, HoverHaloCol, 2f, 7f / _scale, model.Facing);
            }
            return;
        }
    }

    private (float px, float py) InchesToPixel(float x, float z)
    {
        float px = _originX + x * _scale;
        float py = _originY + (_tableH - z) * _scale;
        return (px, py);
    }
}
