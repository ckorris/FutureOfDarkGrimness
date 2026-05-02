using System.Numerics;
using FDG;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using ImGuiNET;
using static FDG.StageResolution.Requests.ChooseRangedAttackRequest;

namespace FdgRaylib.Rendering.Resolvers;

public class GuiChooseRangedAttackResolver
    : IStageResolver<ChooseRangedAttackRequest, RangedAttackChoice>, IGuiResolver
{
    private readonly object _lock = new();
    private ChooseRangedAttackRequest? _request;
    private TaskCompletionSource<RangedAttackChoice>? _tcs;

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

        // Build flat option list once per frame (cheap — small lists)
        var options = BuildOptions(request);

        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0f, 0f, 0f, 0.55f));
        ImGui.Begin("##RangedBackdrop",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleColor();

        float pad     = 16f;
        float headerH = 64f;
        float rowH    = 36f;
        float dw = MathF.Min(screenW * 0.55f, 680f);
        float dh = MathF.Min(headerH + pad + options.Count * rowH + pad * 2, screenH * 0.82f);
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
        for (int i = 0; i < options.Count; i++)
        {
            var (label, choice) = options[i];
            if (ImGui.Button($"{label}##{i}", new Vector2(btnW, rowH - 4f)))
                Complete(tcs, choice);
        }

        ImGui.EndChild();
        ImGui.EndChild();
        ImGui.End();
    }

    private static List<(string Label, RangedAttackChoice Choice)> BuildOptions(ChooseRangedAttackRequest request)
    {
        var list = new List<(string, RangedAttackChoice)>();
        foreach (var weaponOption in request.WeaponOptions)
        {
            string weaponStats = weaponOption.Weapon.GetWeaponNameAndStats();
            foreach (var targetStats in weaponOption.WeaponTargetStats)
            {
                var targetUnit  = targetStats.TargetUnit.GetValue();
                int canShoot    = targetStats.modelsThatCanShoot.Count;
                int cannotShoot = targetStats.modelsWithWeaponThatCannotShoot.Count;
                int totalModels = targetUnit.ModelBindings.Count;

                string label = $"{weaponStats}  →  {targetUnit.Name}  ({totalModels} models, {canShoot} in range";
                if (cannotShoot > 0) label += $", {cannotShoot} out of range";
                if (targetStats.HasCover) label += ", Cover";
                label += ")";

                list.Add((label, new RangedAttackChoice(weaponOption.Weapon, targetStats.TargetUnit)));
            }
        }
        return list;
    }

    private void Complete(TaskCompletionSource<RangedAttackChoice> tcs, RangedAttackChoice choice)
    {
        lock (_lock) { _request = null; _tcs = null; }
        tcs.SetResult(choice);
    }
}
