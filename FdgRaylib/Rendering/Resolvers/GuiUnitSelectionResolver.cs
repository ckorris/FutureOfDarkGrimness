using System.Numerics;
using FDG;
using FDG.Data;
using FDG.Rules.Dispatch;
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
/// #315: an embarked unit's row rings its transport in amber instead (its own models are off-table), and
/// canvas-hovering a transport emphasises its occupants' rows — so two same-named squads riding two
/// transports can be told apart before committing an activation.
/// </summary>
public class GuiUnitSelectionResolver : GuiSelectionResolver<UnitData>, IGuiCanvasOverlay, ICanvasInteractionHandler
{
    private float _scale = 10f;
    private float _tableH = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
    private int _originX, _originY;

    // The unit to emphasise this frame — set by a hovered dialog button (OnValidOptionHovered) OR by the
    // canvas hover (GetHoverLabel); cleared at the end of Draw so it lasts a single frame. Main-thread only.
    private DataReference? _hoveredValidRef;

    // #315: rows to emphasise this frame — the occupants of a canvas-hovered transport, set by
    // GetHoverLabel (which runs earlier in the same frame) and consumed by the base draw loop via
    // IsRowEmphasized; cleared at the end of Draw like _hoveredValidRef. Main-thread only.
    private readonly HashSet<DataReference> _cargoRowRefs = new();

    private static readonly uint HoverCol     = ImGui.ColorConvertFloat4ToU32(new Vector4(0.55f, 0.90f, 1.00f, 1.00f));
    private static readonly uint HoverHaloCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.55f, 0.90f, 1.00f, 0.45f));

    // #315: an embarked unit's models sit at the off-table sentinel, so hovering its row rings its
    // TRANSPORT instead — in amber, not the hover cyan, so it reads "your ride is here", not "you'd be
    // selecting the transport".
    private static readonly uint TransportCol     = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.80f, 0.35f, 1.00f));
    private static readonly uint TransportHaloCol = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.80f, 0.35f, 0.45f));

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
            .Select(w => (w.Name, w.RangeInches, WeaponStatFormatter.RuleList(w)))
            .ToList();
        return UnitOptionLabel.Build(name, liveModels, unit.Quality, unit.Defense, weapons);
    }

    // #337: the engine appends a status badge to the picker label of a unit whose activation will not be a
    // normal one ("Blade Squad (Shaken - recovers)" - it skips the action menu and spends the activation
    // recovering). Splitting it out here draws that run amber and underlined, with the token catalog's own
    // sentence on hover; a label with no badge returns null and draws exactly as it always did.
    protected override IReadOnlyList<RuleHoverText.Segment>? HeadingSegments(
        DataBinding<UnitData> option, string heading) => UnitStatusBadge.Segments(heading);

    // Hovering a valid option highlights its models on the canvas AND raises a full-spec tooltip (#223): the
    // same read-only stat block the canvas hover shows, so the player can compare units to deploy / activate
    // without hunting the board. Called from the base draw loop while the option's button is the hovered item.
    protected override void OnValidOptionHovered(SelectionRequest<UnitData>.ValidOption opt)
    {
        _hoveredValidRef = opt.Option.Reference;
        // Same translucent well as the canvas hover (ImGuiTheme.TooltipBg) — this tooltip pops next to the
        // picker, over the board, and hovering a row also rings that unit's models underneath it.
        ImGuiTheme.BeginTranslucentTooltip();
        UnitStatBlockRenderer.Draw(opt.Option.GetValue(), includeRuleDescriptions: true);
        ImGuiTheme.EndTranslucentTooltip();
    }

    // #248: keyboard highlight rings the unit like a hover, but skips the tooltip (it would pop at the
    // unrelated mouse position).
    protected override void OnValidOptionHighlighted(SelectionRequest<UnitData>.ValidOption opt) =>
        _hoveredValidRef = opt.Option.Reference;

    public string? GetHoverLabel(IUnit unit, IModel model)
    {
        SelectionRequest<UnitData>? request;
        lock (_lock) { request = _request; }
        if (request == null) return null;

        // #315: hovering a transport on the table emphasises its occupants' rows in the list — the
        // reverse of hovering an occupant's row ringing the transport (#286's two-way rule). Computed
        // here because this runs earlier in the same frame than Draw; empty for non-transports.
        _cargoRowRefs.Clear();
        foreach (DataReference cargo in TransportOptionLookup.CargoOptionRefs(request, unit))
            _cargoRowRefs.Add(cargo);

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

    // #315: paint the canvas-hovered transport's occupants' rows (the base class does the painting).
    protected override bool IsRowEmphasized(DataBinding<UnitData> option) =>
        _cargoRowRefs.Contains(option.Reference);

    public override void Draw(int screenW, int screenH)
    {
        // Only the hovered unit is ringed -- no persistent ring on every valid unit (that was visual noise
        // during activation, where every one of the player's units is a valid pick). The dialog draws first;
        // DrawHoverHighlight runs after so a hovered dialog button lands the highlight the same frame.
        base.Draw(screenW, screenH);
        DrawHoverHighlight();
        _hoveredValidRef = null;
        _cargoRowRefs.Clear();
    }

    // The hovered unit (dialog button or canvas) rings with a halo, connecting the list entry to the figures
    // on the board — same affordance as the model picker and wounds dialog. #315: an embarked unit has no
    // figures on the board (its models park at the off-table sentinel), so its TRANSPORT rings instead, in
    // the distinct amber style — that is where activating it will play out.
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

            UnitData unit = opt.Option.GetValue();
            UnitData? transport = TransportOptionLookup.FindTransportOf(request, unit);
            if (transport != null)
                RingModels(dl, transport, TransportCol, TransportHaloCol);
            else
                RingModels(dl, unit, HoverCol, HoverHaloCol);
            return;
        }
    }

    private void RingModels(ImDrawListPtr dl, UnitData unit, uint col, uint haloCol)
    {
        foreach (IModel model in unit.Models)
        {
            if (!model.GetIsAlive()) continue;
            var pos = model.Position;
            if (pos.x == 0f && pos.z == 0f) continue;
            var (px, py) = InchesToPixel(pos.x, pos.z);
            var c = new Vector2(px, py);
            // Shape-aware highlight: matches the true base outline (rectangle for rectangular bases).
            ModelBaseRenderer.DrawOutlineImGui(dl, model.BaseShape, c, _scale, col, 3f, 3f / _scale, model.Facing);
            ModelBaseRenderer.DrawOutlineImGui(dl, model.BaseShape, c, _scale, haloCol, 2f, 7f / _scale, model.Facing);
        }
    }

    private (float px, float py) InchesToPixel(float x, float z)
    {
        float px = _originX + x * _scale;
        float py = _originY + (_tableH - z) * _scale;
        return (px, py);
    }
}
