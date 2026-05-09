using System.Numerics;
using FDG;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using ImGuiNET;
using static FDG.StageResolution.Requests.ChooseRangedAttackRequest;

namespace FdgRaylib.Rendering.Resolvers;

public class GuiChooseRangedAttackResolver
    : IStageResolver<ChooseRangedAttackRequest, RangedAttackChoice>,
      IGuiResolver, IGuiCanvasOverlay, ICanvasInteractionHandler
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
    private IUnit?  _hoveredUnit;   // set by GetHoverLabel (canvas hit-test pass)
    private IModel? _hoveredModel;
    private (int weaponIdx, int targetIdx) _hoveredOption = (-1, -1); // set by button IsItemHovered

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

    // ICanvasInteractionHandler -----------------------------------------------

    public string? GetHoverLabel(IUnit unit, IModel model)
    {
        _hoveredUnit  = unit;
        _hoveredModel = model;

        ChooseRangedAttackRequest? request;
        lock (_lock) request = _request;
        if (request == null) return null;

        // Only annotate the opposing player's units
        if (unit.PlayerID != request.TargetPlayerID) return null;

        return IsValidTarget(request, unit)
            ? "✓ Valid target"
            : "✗ " + GetInvalidReason(request, unit);
    }

    public void HandleClick(IUnit unit, IModel model) { }

    // IGuiResolver + IGuiCanvasOverlay ----------------------------------------

    public void Draw(int screenW, int screenH)
    {
        ChooseRangedAttackRequest? request;
        TaskCompletionSource<RangedAttackChoice>? tcs;
        lock (_lock) { request = _request; tcs = _tcs; }
        if (request == null || tcs == null) return;

        var io = ImGui.GetIO();
        var dl = ImGui.GetBackgroundDrawList();

        var (mouseInX, mouseInZ) = PixelToInches(io.MousePos.X, io.MousePos.Y);
        bool overTable = IsOverTable(io.MousePos.X, io.MousePos.Y);

        // ---- Highlight enemy units (canvas-hover rings) ----
        uint validNormal  = ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 1.00f, 0.20f, 0.55f));
        uint validHovered = ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 1.00f, 0.20f, 1.00f));
        uint validFill    = ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 1.00f, 0.20f, 0.15f));
        uint invalidNorm  = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.25f, 0.25f, 0.40f));
        uint invalidHover = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.25f, 0.25f, 0.80f));

        foreach (var unit in _tableState.Units.Objects)
        {
            if (unit.PlayerID != request.TargetPlayerID) continue;
            if (!unit.GetIsAlive()) continue;

            bool isValid   = IsValidTarget(request, unit);
            bool isHovered = unit == _hoveredUnit;
            uint ring      = isValid
                ? (isHovered ? validHovered : validNormal)
                : (isHovered ? invalidHover : invalidNorm);
            float thickness = isHovered ? 2.5f : 1.5f;

            foreach (var model in unit.Models)
            {
                if (!model.GetIsAlive()) continue;
                var pos = model.Position;
                if (pos.x == 0f && pos.z == 0f) continue;

                var (px, py) = InchesToPixel(pos.x, pos.z);
                float r = model.BaseRadiusInches * _scale;
                if (isValid && isHovered)
                    dl.AddCircleFilled(new Vector2(px, py), r, validFill);
                dl.AddCircle(new Vector2(px, py), r, ring, 32, thickness);
            }
        }

        // ---- Shoot lines: attacker → nearest defender, for each model that can shoot ----
        if (_hoveredUnit != null && IsValidTarget(request, _hoveredUnit))
        {
            var canShoot = new HashSet<DataBinding<ModelData>>(ReferenceEqualityComparer.Instance);
            foreach (var wo in request.WeaponOptions)
            {
                var ts = wo.WeaponTargetStats.FirstOrDefault(t => t.TargetUnit.GetValue().Name == _hoveredUnit.Name);
                if (ts != null)
                    foreach (var b in ts.modelsThatCanShoot) canShoot.Add(b);
            }

            var defModels = _hoveredUnit.Models
                .Where(m => m.GetIsAlive() && (m.Position.x != 0f || m.Position.z != 0f))
                .ToList();

            if (defModels.Count > 0)
            {
                uint shootLine = ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 1.00f, 0.20f, 0.65f));
                foreach (var binding in canShoot)
                {
                    var md = binding.GetValue();
                    if (md.Position.x == 0f && md.Position.z == 0f) continue;
                    var nearest = defModels.MinBy(d =>
                    {
                        float ddx = d.Position.x - md.Position.x;
                        float ddz = d.Position.z - md.Position.z;
                        return ddx * ddx + ddz * ddz;
                    })!;
                    var (ax, ay) = InchesToPixel(md.Position.x, md.Position.z);
                    var (bx, by) = InchesToPixel(nearest.Position.x, nearest.Position.z);
                    dl.AddLine(new Vector2(ax, ay), new Vector2(bx, by), shootLine, 1.5f);
                }
            }
        }

        // ---- Distance line from nearest attacking model to cursor ----
        if (overTable)
        {
            var attackModels = request.AttackingUnit.GetValue().ModelBindings
                .Select(mb => mb.GetValue())
                .Where(m => m.Position.x != 0f || m.Position.z != 0f)
                .ToList();

            if (attackModels.Count > 0)
            {
                var nearest = attackModels.MinBy(m =>
                {
                    float dx = m.Position.x - mouseInX;
                    float dz = m.Position.z - mouseInZ;
                    return dx * dx + dz * dz;
                })!;

                var (sx, sy) = InchesToPixel(nearest.Position.x, nearest.Position.z);
                uint lineColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 0.55f, 0.65f));
                dl.AddLine(new Vector2(sx, sy), io.MousePos, lineColor, 1.2f);

                float centerDist = MathF.Sqrt(
                    (mouseInX - nearest.Position.x) * (mouseInX - nearest.Position.x) +
                    (mouseInZ - nearest.Position.z) * (mouseInZ - nearest.Position.z));
                float displayDist = MathF.Max(0f, centerDist - nearest.BaseRadiusInches);

                string distText  = $"{displayDist:F1}\"";
                var    textPos   = new Vector2(io.MousePos.X + 14f, io.MousePos.Y - 14f);
                uint   textColor = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 0.55f, 0.90f));
                uint   shadow    = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.70f));
                dl.AddText(textPos + new Vector2(1, 1), shadow, distText);
                dl.AddText(textPos, textColor, distText);
            }
        }

        // ---- Button-hover: per-weapon shoot lines ----
        var options = BuildOptions(request);
        DrawHoverLines(request, options);

        // ---- Dialog window (moveable) ----
        float pad  = 16f;
        float rowH = 36f;
        float dw   = MathF.Min(screenW * 0.55f, 680f);
        float dh   = MathF.Min(80f + options.Count * rowH + rowH + pad * 3, screenH * 0.82f);

        ImGui.SetNextWindowPos(new Vector2((screenW - dw) * 0.5f, (screenH - dh) * 0.5f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(dw, dh), ImGuiCond.Always);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.15f, 0.15f, 0.20f, 0.97f));
        ImGui.Begin("Choose Target##RangedDialog",
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleColor();

        ImGui.PushTextWrapPos(dw - pad);
        ImGui.TextUnformatted($"Shoot: {request.AttackingUnit.GetValue().Name}");
        ImGui.Spacing();
        ImGui.TextUnformatted("Choose a weapon and target.");
        ImGui.PopTextWrapPos();
        ImGui.Spacing();

        float listH = ImGui.GetContentRegionAvail().Y;
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
        ImGui.End();

        // Clear hover state — GetHoverLabel sets it each frame, so stale values are harmless
        // only if the cursor moves off a unit before Draw runs next frame.
        _hoveredUnit  = null;
        _hoveredModel = null;
    }

    // Helpers -----------------------------------------------------------------

    private static bool IsValidTarget(ChooseRangedAttackRequest request, IUnit unit) =>
        request.WeaponOptions.Any(wo =>
            wo.WeaponTargetStats.Any(ts => ts.TargetUnit.GetValue().Name == unit.Name));

    private static string GetInvalidReason(ChooseRangedAttackRequest request, IUnit enemy)
    {
        float maxRange = request.WeaponOptions
            .Select(wo => wo.Weapon.RangeInches)
            .DefaultIfEmpty(0f)
            .Max();

        var attackers = request.AttackingUnit.GetValue().ModelBindings
            .Select(mb => mb.GetValue())
            .Where(m => m.Position.x != 0f || m.Position.z != 0f)
            .ToList();

        var defenders = enemy.Models
            .Where(m => m.GetIsAlive() && (m.Position.x != 0f || m.Position.z != 0f))
            .ToList();

        if (attackers.Count == 0 || defenders.Count == 0)
            return "Not a valid target";

        float minDist = float.MaxValue;
        foreach (var a in attackers)
            foreach (var d in defenders)
            {
                float dx   = a.Position.x - d.Position.x;
                float dz   = a.Position.z - d.Position.z;
                float dist = MathF.Sqrt(dx * dx + dz * dz) - a.BaseRadiusInches - d.BaseRadiusInches;
                if (dist < minDist) minDist = dist;
            }

        return minDist > maxRange
            ? $"Out of range ({minDist:F1}\" away — max {maxRange:F0}\")"
            : "No line of sight";
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
        _hoveredUnit   = null;
        _hoveredModel  = null;
        _hoveredOption = (-1, -1);
        tcs.SetResult(choice);
    }

    private (float x, float z) PixelToInches(float px, float py) =>
        ((px - _originX) / _scale, _tableH - (py - _originY) / _scale);

    private (float px, float py) InchesToPixel(float x, float z) =>
        (_originX + x * _scale, _originY + (_tableH - z) * _scale);

    private bool IsOverTable(float px, float py) =>
        px >= _originX && py >= _originY &&
        px <= _originX + GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES * _scale &&
        py <= _originY + _tableH * _scale;
}
