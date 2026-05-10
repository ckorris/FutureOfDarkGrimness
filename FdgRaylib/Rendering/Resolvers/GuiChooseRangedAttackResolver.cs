using System.Numerics;
using FDG;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using ImGuiNET;
using static FDG.StageResolution.Requests.ChooseRangedAttackRequest;

namespace FdgRaylib.Rendering.Resolvers;

public class GuiChooseRangedAttackResolver
    : IStageResolver<ChooseRangedAttackRequest, RangedAttackChoice>, IGuiResolver, IGuiCanvasOverlay,
      ICanvasInteractionHandler
{
    private readonly ITableState _tableState;
    private readonly object _lock = new();
    private ChooseRangedAttackRequest? _request;
    private TaskCompletionSource<RangedAttackChoice>? _tcs;

    // Layout — main-thread only
    private float _scale   = 10f;
    private float _tableH  = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
    private int   _originX, _originY;

    // Selection state — main-thread only
    private ChooseRangedAttackRequest? _lastRequest;
    private int _selectedWeaponIdx  = -1;
    private int _selectedTargetTIdx = -1;

    // Hover state — main-thread only
    // _hoveredOption: set by button hover inside the dialog this frame
    // _canvasHoveredOption: set by GetHoverLabel (called before Draw) this frame
    private (int wIdx, int tIdx) _hoveredOption       = (-1, -1);
    private (int wIdx, int tIdx) _canvasHoveredOption = (-1, -1);

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

    // ── ICanvasInteractionHandler ─────────────────────────────────────────────

    public string? GetHoverLabel(IUnit unit, IModel model)
    {
        ChooseRangedAttackRequest? request;
        lock (_lock) { request = _request; }
        if (request == null || _selectedWeaponIdx < 0) { _canvasHoveredOption = (-1, -1); return null; }

        int tIdx = FindTargetInWeapon(unit, _selectedWeaponIdx, request);
        if (tIdx < 0) { _canvasHoveredOption = (-1, -1); return null; }

        _canvasHoveredOption = (_selectedWeaponIdx, tIdx);
        int canShoot = request.WeaponOptions[_selectedWeaponIdx].WeaponTargetStats[tIdx].modelsThatCanShoot.Count;
        if (canShoot == 0) return "Out of range";
        return $"Click to select  ({canShoot} model{(canShoot != 1 ? "s" : "")} in range)";
    }

    public void HandleClick(IUnit unit, IModel model)
    {
        ChooseRangedAttackRequest? request;
        lock (_lock) { request = _request; }
        if (request == null || _selectedWeaponIdx < 0) return;

        int tIdx = FindTargetInWeapon(unit, _selectedWeaponIdx, request);
        if (tIdx < 0) return;
        if (request.WeaponOptions[_selectedWeaponIdx].WeaponTargetStats[tIdx].modelsThatCanShoot.Count == 0) return;

        _selectedTargetTIdx = tIdx;
    }

    // ── Draw ──────────────────────────────────────────────────────────────────

    public void Draw(int screenW, int screenH)
    {
        ChooseRangedAttackRequest? request;
        TaskCompletionSource<RangedAttackChoice>? tcs;
        lock (_lock) { request = _request; tcs = _tcs; }
        if (request == null || tcs == null) return;

        // Auto-select first weapon on new request
        if (!ReferenceEquals(request, _lastRequest))
        {
            _lastRequest        = request;
            _selectedWeaponIdx  = request.WeaponOptions.Count > 0 ? 0 : -1;
            _selectedTargetTIdx = -1;
        }

        DrawHoverLines(request);

        // Invisible non-interactive backdrop to anchor z-order
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("##RangedBackdrop",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoBackground);
        ImGui.End();

        float dw = MathF.Min(screenW * 0.75f, 920f);
        float dh = MathF.Min(screenH * 0.60f, 440f);
        ImGui.SetNextWindowPos(new Vector2((screenW - dw) * 0.5f, (screenH - dh) * 0.5f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(dw, dh), ImGuiCond.Always);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.13f, 0.13f, 0.18f, 0.97f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f);
        string attackerName = request.AttackingUnit.GetValue().Name;
        ImGui.Begin($"Shoot: {attackerName}##RangedDialog",
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();

        float pad       = 8f;
        float rowH      = 36f;
        float footerH   = rowH + pad * 2;
        float colsH     = ImGui.GetContentRegionAvail().Y - footerH;
        float colW      = (ImGui.GetContentRegionAvail().X - pad * 2) / 3f;

        (int wIdx, int tIdx) newHovered = (-1, -1);

        // ── Column 1: Weapons ─────────────────────────────────────────────────
        ImGui.BeginChild("##WeaponCol", new Vector2(colW, colsH), ImGuiChildFlags.Borders);
        ImGui.TextUnformatted("Weapon");
        ImGui.Separator();
        for (int wi = 0; wi < request.WeaponOptions.Count; wi++)
        {
            var wo      = request.WeaponOptions[wi];
            bool sel    = _selectedWeaponIdx == wi;
            float itemH = ImGui.GetTextLineHeight() * 2.4f;

            if (ImGui.Selectable($"##{wi}", sel, ImGuiSelectableFlags.None, new Vector2(0, itemH)))
            {
                if (_selectedWeaponIdx != wi) _selectedTargetTIdx = -1;
                _selectedWeaponIdx = wi;
            }
            if (ImGui.IsItemHovered())
            {
                // Highlight first valid target for this weapon on hover
                int firstTIdx = FindFirstValidTargetInWeapon(wi, request);
                newHovered = firstTIdx >= 0 ? (wi, firstTIdx) : (-1, -1);
            }

            var rMin = ImGui.GetItemRectMin();
            ImGui.SetCursorScreenPos(rMin + new Vector2(4, 2));
            ImGui.TextUnformatted(wo.Weapon.Name);
            ImGui.SetCursorScreenPos(rMin + new Vector2(4, ImGui.GetTextLineHeight() + 4));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.65f, 0.65f, 0.70f, 1f));
            ImGui.TextUnformatted($"{wo.Weapon.RangeInches}\", A{wo.Weapon.Attacks} AP{wo.Weapon.ArmorPenetration}");
            ImGui.PopStyleColor();
            ImGui.SetCursorPosY(ImGui.GetCursorPosY() + ImGui.GetStyle().ItemSpacing.Y);
        }
        ImGui.EndChild();

        ImGui.SameLine(0, pad);

        // ── Column 2: Targets ─────────────────────────────────────────────────
        ImGui.BeginChild("##TargetCol", new Vector2(colW, colsH), ImGuiChildFlags.Borders);
        ImGui.TextUnformatted("Target");
        ImGui.Separator();
        if (_selectedWeaponIdx >= 0)
        {
            var wo = request.WeaponOptions[_selectedWeaponIdx];
            for (int ti = 0; ti < wo.WeaponTargetStats.Count; ti++)
            {
                var ts       = wo.WeaponTargetStats[ti];
                bool canShoot = ts.modelsThatCanShoot.Count > 0;
                bool sel      = _selectedTargetTIdx == ti;
                string name   = ts.TargetUnit.GetValue().Name;
                float itemH   = ImGui.GetTextLineHeight() * 2.4f;

                if (!canShoot) ImGui.BeginDisabled(true);

                if (ImGui.Selectable($"##{ti}", sel, ImGuiSelectableFlags.None, new Vector2(0, itemH)))
                {
                    if (canShoot) _selectedTargetTIdx = ti;
                }
                if (ImGui.IsItemHovered())
                    newHovered = (_selectedWeaponIdx, ti);

                var rMin = ImGui.GetItemRectMin();
                ImGui.SetCursorScreenPos(rMin + new Vector2(4, 2));
                ImGui.TextUnformatted(name);
                ImGui.SetCursorScreenPos(rMin + new Vector2(4, ImGui.GetTextLineHeight() + 4));

                if (!canShoot)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.70f, 0.35f, 0.35f, 1f));
                    ImGui.TextUnformatted("Out of range");
                    ImGui.PopStyleColor();
                }
                else
                {
                    string sub = $"{ts.modelsThatCanShoot.Count}/{ts.TargetUnit.GetValue().ModelBindings.Count} in range";
                    if (ts.HasCover) sub += ", Cover";
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.40f, 0.85f, 0.40f, 1f));
                    ImGui.TextUnformatted(sub);
                    ImGui.PopStyleColor();
                }

                if (!canShoot) ImGui.EndDisabled();
                ImGui.SetCursorPosY(ImGui.GetCursorPosY() + ImGui.GetStyle().ItemSpacing.Y);
            }
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.55f, 0.55f, 0.55f, 1f));
            ImGui.TextUnformatted("Select a weapon.");
            ImGui.PopStyleColor();
        }
        ImGui.EndChild();

        ImGui.SameLine(0, pad);

        // ── Column 3: Details ─────────────────────────────────────────────────
        ImGui.BeginChild("##DetailCol", new Vector2(colW, colsH), ImGuiChildFlags.Borders);
        ImGui.TextUnformatted("Details");
        ImGui.Separator();
        if (_selectedWeaponIdx >= 0 && _selectedTargetTIdx >= 0)
        {
            var wo = request.WeaponOptions[_selectedWeaponIdx];
            var ts = wo.WeaponTargetStats[_selectedTargetTIdx];
            var tu = ts.TargetUnit.GetValue();

            ImGui.TextUnformatted(wo.Weapon.GetWeaponNameAndStats());
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.TextUnformatted($"Target:  {tu.Name}");
            ImGui.TextUnformatted($"Qua {tu.Quality}+   Def {tu.Defense}+");
            ImGui.Spacing();

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.40f, 0.85f, 0.40f, 1f));
            ImGui.TextUnformatted($"{ts.modelsThatCanShoot.Count} model{(ts.modelsThatCanShoot.Count != 1 ? "s" : "")} in range");
            ImGui.PopStyleColor();

            if (ts.modelsWithWeaponThatCannotShoot.Count > 0)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.80f, 0.45f, 0.45f, 1f));
                ImGui.TextUnformatted($"{ts.modelsWithWeaponThatCannotShoot.Count} out of range");
                ImGui.PopStyleColor();
            }
            if (ts.HasCover)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.85f, 0.75f, 0.30f, 1f));
                ImGui.TextUnformatted("Cover");
                ImGui.PopStyleColor();
            }
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.55f, 0.55f, 0.55f, 1f));
            ImGui.TextUnformatted("Select a weapon\nand target.");
            ImGui.PopStyleColor();
        }
        ImGui.EndChild();

        _hoveredOption = newHovered;

        // ── Footer ────────────────────────────────────────────────────────────
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + pad);
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.25f, 0.25f, 0.30f, 1f));
        if (ImGui.Button("Back##back")) Complete(tcs, null!);
        ImGui.PopStyleColor();

        ImGui.SameLine();

        bool canFire = _selectedWeaponIdx >= 0 && _selectedTargetTIdx >= 0
                    && request.WeaponOptions[_selectedWeaponIdx]
                              .WeaponTargetStats[_selectedTargetTIdx]
                              .modelsThatCanShoot.Count > 0;
        if (!canFire) ImGui.BeginDisabled(true);
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(0.65f, 0.20f, 0.20f, 1f));
        if (ImGui.Button("Fire!##fire"))
        {
            var wo = request.WeaponOptions[_selectedWeaponIdx];
            var ts = wo.WeaponTargetStats[_selectedTargetTIdx];
            Complete(tcs, new RangedAttackChoice(wo.Weapon, ts.TargetUnit));
        }
        ImGui.PopStyleColor();
        if (!canFire) ImGui.EndDisabled();

        ImGui.End();
    }

    // ── Canvas line drawing ───────────────────────────────────────────────────

    private void DrawHoverLines(ChooseRangedAttackRequest request)
    {
        // Priority: button hover > canvas hover > current selection
        (int wIdx, int tIdx) effective =
            _hoveredOption.wIdx >= 0       ? _hoveredOption :
            _canvasHoveredOption.wIdx >= 0 ? _canvasHoveredOption :
            (_selectedWeaponIdx, _selectedTargetTIdx);

        if (effective.wIdx < 0 || effective.wIdx >= request.WeaponOptions.Count) return;
        var wo = request.WeaponOptions[effective.wIdx];
        if (effective.tIdx < 0 || effective.tIdx >= wo.WeaponTargetStats.Count) return;

        var ts          = wo.WeaponTargetStats[effective.tIdx];
        var attackerUnit = request.AttackingUnit.GetValue();
        var targetUnit   = ts.TargetUnit.GetValue();
        var dl           = ImGui.GetBackgroundDrawList();

        uint colorCan    = ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 1.00f, 0.20f, 0.80f));
        uint colorCannot = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.30f, 0.30f, 0.55f));
        uint colorTarget = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.85f, 0.10f, 0.65f));

        foreach (var mb in targetUnit.ModelBindings)
        {
            var m = mb.GetValue();
            var (tx, ty) = InchesToPixel(m.Position.x, m.Position.z);
            dl.AddCircle(new Vector2(tx, ty), m.BaseRadiusInches * _scale + 3f, colorTarget, 32, 2f);
        }

        foreach (var ab in attackerUnit.ModelBindings)
        {
            var attacker = ab.GetValue();
            var (ax, ay) = InchesToPixel(attacker.Position.x, attacker.Position.z);
            uint lineColor = ts.modelsThatCanShoot.Contains(ab) ? colorCan : colorCannot;
            foreach (var db in targetUnit.ModelBindings)
            {
                var defender = db.GetValue();
                var (tx, ty) = InchesToPixel(defender.Position.x, defender.Position.z);
                dl.AddLine(new Vector2(ax, ay), new Vector2(tx, ty), lineColor, 1.5f);
            }
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static int FindTargetInWeapon(IUnit unit, int wIdx, ChooseRangedAttackRequest request)
    {
        var stats = request.WeaponOptions[wIdx].WeaponTargetStats;
        for (int ti = 0; ti < stats.Count; ti++)
            if (stats[ti].TargetUnit.GetValue() == unit) return ti;
        return -1;
    }

    private static int FindFirstValidTargetInWeapon(int wIdx, ChooseRangedAttackRequest request)
    {
        var stats = request.WeaponOptions[wIdx].WeaponTargetStats;
        for (int ti = 0; ti < stats.Count; ti++)
            if (stats[ti].modelsThatCanShoot.Count > 0) return ti;
        return -1;
    }

    private void Complete(TaskCompletionSource<RangedAttackChoice> tcs, RangedAttackChoice choice)
    {
        lock (_lock) { _request = null; _tcs = null; }
        _lastRequest        = null;
        _selectedWeaponIdx  = -1;
        _selectedTargetTIdx = -1;
        _hoveredOption       = (-1, -1);
        _canvasHoveredOption = (-1, -1);
        tcs.SetResult(choice);
    }

    private (float px, float py) InchesToPixel(float x, float z)
    {
        float px = _originX + x * _scale;
        float py = _originY + (_tableH - z) * _scale;
        return (px, py);
    }
}
