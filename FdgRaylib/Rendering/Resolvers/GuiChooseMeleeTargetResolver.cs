using System.Numerics;
using FDG;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

public class GuiChooseMeleeTargetResolver
    : IStageResolver<ChooseMeleeDefenderRequest, DataBinding<UnitData>>,
      IGuiResolver, IGuiCanvasOverlay, ICanvasInteractionHandler
{
    private readonly ITableState _tableState;
    private readonly object _lock = new();
    private ChooseMeleeDefenderRequest? _request;
    private TaskCompletionSource<DataBinding<UnitData>>? _tcs;

    // Layout — main-thread only, no lock needed
    private float _scale   = 10f;
    private float _tableH  = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
    private int   _originX, _originY;

    // Hover state — set by GetHoverLabel (tooltip pass), cleared at end of Draw
    private IUnit? _hoveredUnit;

    public GuiChooseMeleeTargetResolver(ITableState tableState) => _tableState = tableState;

    public void UpdateLayout(float scale, int originX, int originY, float tableH)
    {
        _scale   = scale;
        _originX = originX;
        _originY = originY;
        _tableH  = tableH;
    }

    public bool HasPendingRequest { get { lock (_lock) return _request != null; } }

    public Task<DataBinding<UnitData>> Resolve(ChooseMeleeDefenderRequest request)
    {
        var tcs = new TaskCompletionSource<DataBinding<UnitData>>();
        lock (_lock) { _tcs = tcs; _request = request; }
        return tcs.Task;
    }

    // ICanvasInteractionHandler -----------------------------------------------

    public string? GetHoverLabel(IUnit unit, IModel model)
    {
        _hoveredUnit = unit;

        ChooseMeleeDefenderRequest? request;
        lock (_lock) request = _request;
        if (request == null) return null;

        if (request.ValidOptions.Any(o => o.Option.GetValue().Name == unit.Name))
            return "✓ Valid target";

        var invalid = request.InvalidOptions.FirstOrDefault(o => o.Option.GetValue().Name == unit.Name);
        if (invalid != null)
            return "✗ " + invalid.Reason;

        return null;
    }

    public void HandleClick(IUnit unit, IModel model) { }

    // IGuiResolver + IGuiCanvasOverlay ----------------------------------------

    public void Draw(int screenW, int screenH)
    {
        ChooseMeleeDefenderRequest? request;
        TaskCompletionSource<DataBinding<UnitData>>? tcs;
        lock (_lock) { request = _request; tcs = _tcs; }
        if (request == null || tcs == null) return;

        var dl = ImGui.GetBackgroundDrawList();

        var validNames   = new HashSet<string>(request.ValidOptions.Select(o => o.Option.GetValue().Name));
        var invalidNames = new HashSet<string>(request.InvalidOptions.Select(o => o.Option.GetValue().Name));

        uint validNormal  = ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 1.00f, 0.20f, 0.55f));
        uint validHovered = ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 1.00f, 0.20f, 1.00f));
        uint validFill    = ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 1.00f, 0.20f, 0.15f));
        uint invalidNorm  = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.25f, 0.25f, 0.40f));
        uint invalidHover = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.25f, 0.25f, 0.80f));

        // ---- Highlight valid and invalid defender units ----
        foreach (var unit in _tableState.Units.Objects)
        {
            bool isValid   = validNames.Contains(unit.Name);
            bool isInvalid = !isValid && invalidNames.Contains(unit.Name);
            if (!isValid && !isInvalid) continue;
            if (!unit.GetIsAlive()) continue;

            bool isHovered  = unit == _hoveredUnit;
            uint ring       = isValid
                ? (isHovered ? validHovered : validNormal)
                : (isHovered ? invalidHover : invalidNorm);
            float thickness = isHovered ? 2.5f : 1.5f;

            foreach (var m in unit.Models)
            {
                if (!m.GetIsAlive()) continue;
                var pos = m.Position;
                if (pos.x == 0f && pos.z == 0f) continue;

                var (px, py) = InchesToPixel(pos.x, pos.z);
                float r = m.BaseRadiusInches * _scale;
                if (isValid && isHovered)
                    dl.AddCircleFilled(new Vector2(px, py), r, validFill);
                dl.AddCircle(new Vector2(px, py), r, ring, 32, thickness);
            }
        }

        // ---- Melee lines: attacker model → nearest defender model, if within melee range ----
        if (_hoveredUnit != null && validNames.Contains(_hoveredUnit.Name))
        {
            var attackerData = request.AttackingUnit.GetValue();
            var defModels = _hoveredUnit.Models
                .Where(m => m.GetIsAlive() && (m.Position.x != 0f || m.Position.z != 0f))
                .ToList();

            if (defModels.Count > 0)
            {
                uint meleeLine = ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 1.00f, 0.20f, 0.65f));

                foreach (var binding in attackerData.ModelBindings)
                {
                    var am = binding.GetValue();
                    if (am.Position.x == 0f && am.Position.z == 0f) continue;

                    var nearest = defModels.MinBy(dm =>
                    {
                        float dx = dm.Position.x - am.Position.x;
                        float dz = dm.Position.z - am.Position.z;
                        return dx * dx + dz * dz;
                    })!;

                    float dx2  = nearest.Position.x - am.Position.x;
                    float dz2  = nearest.Position.z - am.Position.z;
                    float dist = MathF.Sqrt(dx2 * dx2 + dz2 * dz2) - am.BaseRadiusInches - nearest.BaseRadiusInches;
                    if (dist > GameWideConstants.MELEE_RANGE_INCHES_HORIZONTAL) continue;

                    var (ax, ay) = InchesToPixel(am.Position.x, am.Position.z);
                    var (bx, by) = InchesToPixel(nearest.Position.x, nearest.Position.z);
                    dl.AddLine(new Vector2(ax, ay), new Vector2(bx, by), meleeLine, 1.5f);
                }
            }
        }

        // ---- Dialog window (moveable) ----
        int   validCount   = request.ValidOptions.Count;
        int   invalidCount = request.InvalidOptions.Count;
        float rowH   = 32f;
        float pad    = 16f;
        float instrH = 48f;
        float dw = MathF.Min(screenW * 0.45f, 560f);
        float dh = MathF.Min(instrH + pad + (validCount + invalidCount) * rowH + rowH + pad * 3, screenH * 0.80f);

        ImGui.SetNextWindowPos(new Vector2((screenW - dw) * 0.5f, (screenH - dh) * 0.5f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(dw, dh), ImGuiCond.Always);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.15f, 0.15f, 0.20f, 0.97f));
        ImGui.Begin("Choose Melee Target##MeleeDialog",
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleColor();

        ImGui.PushTextWrapPos(dw - pad);
        ImGui.TextUnformatted(request.Instructions);
        ImGui.PopTextWrapPos();
        ImGui.Spacing();

        float listH = ImGui.GetContentRegionAvail().Y;
        ImGui.BeginChild("##MeleeList", new Vector2(dw - pad * 2, listH),
            ImGuiChildFlags.None, ImGuiWindowFlags.HorizontalScrollbar);

        float btnW = ImGui.GetContentRegionAvail().X;
        for (int i = 0; i < validCount; i++)
        {
            var opt = request.ValidOptions[i];
            if (ImGui.Button(opt.Name + $"##{i}", new Vector2(btnW, rowH - 4f)))
                Complete(tcs, opt.Option);
        }

        if (invalidCount > 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1f));
            for (int i = 0; i < invalidCount; i++)
            {
                var opt = request.InvalidOptions[i];
                ImGui.BeginDisabled(true);
                ImGui.Button($"{opt.Name} ({opt.Reason})##{validCount + i}", new Vector2(btnW, rowH - 4f));
                ImGui.EndDisabled();
            }
            ImGui.PopStyleColor();
        }

        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.25f, 0.25f, 0.30f, 1f));
        if (ImGui.Button("Back##back", new Vector2(btnW, rowH - 4f)))
            Complete(tcs, null!);
        ImGui.PopStyleColor();

        ImGui.EndChild();
        ImGui.End();

        _hoveredUnit = null;
    }

    // Helpers -----------------------------------------------------------------

    private void Complete(TaskCompletionSource<DataBinding<UnitData>> tcs, DataBinding<UnitData> option)
    {
        lock (_lock) { _request = null; _tcs = null; }
        _hoveredUnit = null;
        tcs.SetResult(option);
    }

    private (float px, float py) InchesToPixel(float x, float z) =>
        (_originX + x * _scale, _originY + (_tableH - z) * _scale);
}
