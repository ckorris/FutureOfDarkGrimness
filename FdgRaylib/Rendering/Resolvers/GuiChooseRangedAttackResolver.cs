using System.Numerics;
using FDG;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using ImGuiNET;
using static FDG.StageResolution.Requests.ChooseRangedAttackRequest;

namespace FdgRaylib.Rendering.Resolvers;

public class GuiChooseRangedAttackResolver
    : IStageResolver<ChooseRangedAttackRequest, RangedAttackChoice>, IGuiResolver, IGuiCanvasOverlay
{
    private readonly ITableState _tableState;
    private readonly object _lock = new();
    private ChooseRangedAttackRequest? _request;
    private TaskCompletionSource<RangedAttackChoice>? _tcs;

    // Layout — main-thread only, no lock needed
    private float _scale   = 10f;
    private float _tableH  = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
    private int   _originX, _originY;

    // Hover state — main-thread only
    private (int weaponIdx, int targetIdx) _hoveredOption = (-1, -1);

    public GuiChooseRangedAttackResolver(ITableState tableState) => _tableState = tableState;

    public void UpdateLayout(float scale, int originX, int originY, float tableH)
    {
        _scale   = scale;
        _originX = originX;
        _originY = originY;
        _tableH  = tableH;
    }

    public bool HasPendingRequest { get { lock (_lock) return _request != null; } }

    public Task<RangedAttackChoice> Resolve(ChooseRangedAttackRequest request)
    {
        var tcs = new TaskCompletionSource<RangedAttackChoice>();
        lock (_lock) { _tcs = tcs; _request = request; }
        return tcs.Task;
    }

    public void Draw(int screenW, int screenH)
    {
        ChooseRangedAttackRequest? request;
        TaskCompletionSource<RangedAttackChoice>? tcs;
        lock (_lock) { request = _request; tcs = _tcs; }
        if (request == null || tcs == null) return;

        var options = BuildOptions(request);

        // Draw canvas lines for hovered option before the ImGui window
        DrawHoverLines(request, options);

        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("##RangedBackdrop",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoInputs |
            ImGuiWindowFlags.NoBackground);

        float pad     = 16f;
        float headerH = 64f;
        float rowH    = 36f;
        float backH   = rowH + pad;
        float dw = MathF.Min(screenW * 0.55f, 680f);
        float dh = MathF.Min(headerH + pad + options.Count * rowH + backH + pad * 2, screenH * 0.82f);
        float dx = (screenW - dw) * 0.5f;
        float dy = (screenH - dh) * 0.5f;

        ImGui.SetCursorPos(new Vector2(dx, dy));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.15f, 0.15f, 0.20f, 0.97f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f);
        ImGui.BeginChild("##RangedDialog", new Vector2(dw, dh), ImGuiChildFlags.Borders,
            ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();

        // Header
        string attackerName = request.AttackingUnit.GetValue().Name;
        ImGui.SetCursorPos(new Vector2(pad, pad));
        ImGui.PushTextWrapPos(dw - pad);
        ImGui.TextUnformatted($"Shoot: {attackerName}");
        ImGui.Spacing();
        ImGui.TextUnformatted("Choose a weapon and target.");
        ImGui.PopTextWrapPos();

        // Scrollable option list
        float listY = pad + headerH;
        float listH = dh - listY - pad;
        ImGui.SetCursorPos(new Vector2(pad, listY));
        ImGui.BeginChild("##RangedList", new Vector2(dw - pad * 2, listH), ImGuiChildFlags.None,
            ImGuiWindowFlags.HorizontalScrollbar);

        float btnW = ImGui.GetContentRegionAvail().X;
        (int weaponIdx, int targetIdx) newHovered = (-1, -1);
        for (int i = 0; i < options.Count; i++)
        {
            var (label, choice, wIdx, tIdx) = options[i];
            if (ImGui.Button($"{label}##{i}", new Vector2(btnW, rowH - 4f)))
                Complete(tcs, choice);
            if (ImGui.IsItemHovered())
                newHovered = (wIdx, tIdx);
        }
        _hoveredOption = newHovered;

        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.25f, 0.25f, 0.30f, 1f));
        if (ImGui.Button("Back##back", new Vector2(btnW, rowH - 4f)))
            Complete(tcs, null!);
        ImGui.PopStyleColor();

        ImGui.EndChild();
        ImGui.EndChild();
        ImGui.End();
    }

    private void DrawHoverLines(ChooseRangedAttackRequest request,
        List<(string Label, RangedAttackChoice Choice, int WIdx, int TIdx)> options)
    {
        var (wIdx, tIdx) = _hoveredOption;
        if (wIdx < 0 || wIdx >= request.WeaponOptions.Count) return;

        var weaponOption = request.WeaponOptions[wIdx];
        if (tIdx < 0 || tIdx >= weaponOption.WeaponTargetStats.Count) return;

        var targetStats  = weaponOption.WeaponTargetStats[tIdx];
        var attackerUnit = request.AttackingUnit.GetValue();
        var targetUnit   = targetStats.TargetUnit.GetValue();

        var dl = ImGui.GetBackgroundDrawList();

        uint colorCan    = ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 1.00f, 0.20f, 0.80f));
        uint colorCannot = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.30f, 0.30f, 0.55f));
        uint colorTarget = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.85f, 0.10f, 0.65f));

        // Highlight target models
        foreach (var mb in targetUnit.ModelBindings)
        {
            var m = mb.GetValue();
            var (tx, ty) = InchesToPixel(m.Position.x, m.Position.z);
            dl.AddCircle(new Vector2(tx, ty), m.BaseRadiusInches * _scale + 3f, colorTarget, 32, 2f);
        }

        // Lines from each attacker to each target model
        foreach (var attackerBinding in attackerUnit.ModelBindings)
        {
            var attacker = attackerBinding.GetValue();
            var (ax, ay) = InchesToPixel(attacker.Position.x, attacker.Position.z);
            bool canShoot = targetStats.modelsThatCanShoot.Contains(attackerBinding);
            uint lineColor = canShoot ? colorCan : colorCannot;

            foreach (var defenderBinding in targetUnit.ModelBindings)
            {
                var defender = defenderBinding.GetValue();
                var (tx, ty) = InchesToPixel(defender.Position.x, defender.Position.z);
                dl.AddLine(new Vector2(ax, ay), new Vector2(tx, ty), lineColor, 1.5f);
            }
        }
    }

    private static List<(string Label, RangedAttackChoice Choice, int WIdx, int TIdx)> BuildOptions(
        ChooseRangedAttackRequest request)
    {
        var list = new List<(string, RangedAttackChoice, int, int)>();
        for (int wi = 0; wi < request.WeaponOptions.Count; wi++)
        {
            var weaponOption = request.WeaponOptions[wi];
            string weaponStats = weaponOption.Weapon.GetWeaponNameAndStats();
            for (int ti = 0; ti < weaponOption.WeaponTargetStats.Count; ti++)
            {
                var targetStats = weaponOption.WeaponTargetStats[ti];

                // Skip targets no model can actually reach
                if (targetStats.modelsThatCanShoot.Count == 0) continue;

                var targetUnit  = targetStats.TargetUnit.GetValue();
                int canShoot    = targetStats.modelsThatCanShoot.Count;
                int cannotShoot = targetStats.modelsWithWeaponThatCannotShoot.Count;
                int totalModels = targetUnit.ModelBindings.Count;

                string label = $"{weaponStats}  →  {targetUnit.Name}  ({totalModels} models, {canShoot} in range";
                if (cannotShoot > 0) label += $", {cannotShoot} out of range";
                if (targetStats.HasCover) label += ", Cover";
                label += ")";

                list.Add((label, new RangedAttackChoice(weaponOption.Weapon, targetStats.TargetUnit), wi, ti));
            }
        }
        return list;
    }

    private void Complete(TaskCompletionSource<RangedAttackChoice> tcs, RangedAttackChoice choice)
    {
        lock (_lock) { _request = null; _tcs = null; }
        _hoveredOption = (-1, -1);
        tcs.SetResult(choice);
    }

    private (float px, float py) InchesToPixel(float x, float z)
    {
        float px = _originX + x * _scale;
        float py = _originY + (_tableH - z) * _scale;
        return (px, py);
    }
}
