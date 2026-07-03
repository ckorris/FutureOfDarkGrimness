using System.Numerics;
using System.Text;
using FDG;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

public class GuiAssignWoundsResolver
    : IStageResolver<AssignWoundsRequest, AssignWoundsResults>, IGuiResolver,
      IGuiCanvasOverlay, ICanvasInteractionHandler
{
    private readonly object _lock = new();
    private AssignWoundsRequest? _request;
    private AssignWoundsResults? _results;
    private TaskCompletionSource<AssignWoundsResults>? _tcs;

    // Layout — main-thread only (UpdateLayout + Draw both run on the main thread).
    private float _scale = 10f;
    private float _tableH;
    private int   _originX, _originY;

    // The model whose wound-button is hovered this frame; highlighted on the table canvas.
    // Main-thread only: reset at the top of Draw, set while iterating the buttons.
    private IModel? _hoveredModel;

    private static readonly uint HighlightCol      = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.95f, 0.30f, 0.95f));
    private static readonly uint HighlightHaloCol  = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.95f, 0.30f, 0.45f));
    private static readonly uint WeaponTextCol     = ImGui.ColorConvertFloat4ToU32(new Vector4(0.72f, 0.78f, 0.85f, 1f));
    private static readonly uint InvalidFillCol    = ImGui.ColorConvertFloat4ToU32(new Vector4(0.10f, 0.10f, 0.12f, 0.62f));
    private static readonly uint InvalidOutlineCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.55f, 0.55f, 0.60f, 0.80f));

    public bool HasPendingRequest { get { lock (_lock) return _request != null; } }

    public void UpdateLayout(float scale, int originX, int originY, float tableH)
    {
        _scale   = scale;
        _originX = originX;
        _originY = originY;
        _tableH  = tableH;
    }

    public Task<AssignWoundsResults> Resolve(AssignWoundsRequest request)
    {
        var tcs     = new TaskCompletionSource<AssignWoundsResults>();
        var results = new AssignWoundsResults(request.UnitReceivingWounds, request.TotalWoundsToAssign);
        lock (_lock) { _tcs = tcs; _request = request; _results = results; }
        return tcs.Task;
    }

    public void Draw(int screenW, int screenH)
    {
        AssignWoundsRequest? request;
        AssignWoundsResults? results;
        TaskCompletionSource<AssignWoundsResults>? tcs;
        lock (_lock) { request = _request; results = _results; tcs = _tcs; }
        if (request == null || results == null || tcs == null) return;

        _hoveredModel = null;

        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("##WoundsBackdrop",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoInputs |
            ImGuiWindowFlags.NoBackground);

        var models  = results.PendingWounds;
        float pad   = 16f;
        float hdrH  = 72f;
        float footH = 44f;

        // Per-model button heights: one main line (model + wounds) plus a small line per weapon group.
        float mainSize   = ImGui.GetFontSize();
        float smallSize  = MathF.Round(mainSize * 0.72f);
        float mainLineH  = ImGui.GetTextLineHeight();
        float smallLineH = smallSize + 2f;
        const float btnPadY  = 6f;
        float spacingY   = ImGui.GetStyle().ItemSpacing.Y;

        var weaponLines = new List<string>[models.Count];
        var btnHeights  = new float[models.Count];
        float listContentH = 0f;
        for (int i = 0; i < models.Count; i++)
        {
            weaponLines[i] = WeaponLines(models[i].Model.GetValue());
            btnHeights[i]  = btnPadY * 2 + mainLineH + weaponLines[i].Count * smallLineH;
            listContentH  += btnHeights[i] + spacingY;
        }

        float dw = MathF.Min(screenW * 0.5f, 560f);
        float dh = MathF.Min(hdrH + pad + listContentH + pad + footH + pad, screenH * 0.82f);
        float dx = (screenW - dw) * 0.5f;
        float dy = (screenH - dh) * 0.5f;

        ImGui.SetCursorPos(new Vector2(dx, dy));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.15f, 0.15f, 0.20f, 0.97f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f);
        ImGui.BeginChild("##WoundsDialog", new Vector2(dw, dh), ImGuiChildFlags.Borders,
            ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();

        // Header
        string unitName = request.UnitReceivingWounds.GetValue().Name;
        ImGui.SetCursorPos(new Vector2(pad, pad));
        ImGui.PushTextWrapPos(dw - pad);
        ImGui.TextUnformatted($"Assign Wounds: {unitName}");
        ImGui.Spacing();
        ImGui.TextUnformatted($"{results.TotalAssignedWounds:F0} / {results.TotalWoundsToAssign:F0} wounds assigned");
        ImGui.PopTextWrapPos();

        // Model buttons (scrollable if many models)
        float listY = pad + hdrH;
        float listH = dh - listY - pad - footH - pad;
        float btnW  = dw - pad * 2;
        ImGui.SetCursorPos(new Vector2(pad, listY));
        ImGui.BeginChild("##WoundsList", new Vector2(btnW, listH), ImGuiChildFlags.None);

        var dl   = ImGui.GetWindowDrawList();
        var font = ImGui.GetFont();
        for (int i = 0; i < models.Count; i++)
        {
            var pw         = models[i];
            var modelData  = pw.Model.GetValue();
            float total    = modelData.TotalWounds;
            float dealt    = modelData.WoundsDealt;
            float remaining = total - dealt - pw.Wounds;
            // #006: assignability is capacity AND ordering — a joined hero is grayed until the rest of the
            // unit is dead. Same predicate the engine's TryAddWounds uses, so the button never lies.
            bool canTake   = results.CanAssignWoundTo(pw);

            float avail = ImGui.GetContentRegionAvail().X;
            Vector2 origin = ImGui.GetCursorScreenPos();

            ImGui.BeginDisabled(!canTake);
            bool clicked = ImGui.Button($"##model{i}", new Vector2(avail, btnHeights[i]));
            // AllowWhenDisabled so dead/full models still highlight on the map for orientation.
            bool hovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);
            ImGui.EndDisabled();

            // Overlay text: model + wounds on the main line, weapons in smaller text beneath.
            // Pre-assigned wounds (Tough ordering, locked) are surfaced with an "(N assigned)" note.
            uint mainCol = ImGui.GetColorU32(canTake ? ImGuiCol.Text : ImGuiCol.TextDisabled);
            uint wCol    = canTake ? WeaponTextCol : ImGui.GetColorU32(ImGuiCol.TextDisabled);
            string assignedNote = pw.Wounds > 0f ? $"   ({pw.Wounds:F0} assigned)" : "";
            // Show remaining/total only for multi-wound models; single-wound models need no counter.
            string woundText = total > 1f ? $"   -   ({remaining:F0}/{total:F0})" : "";
            float tx = origin.X + 8f;
            float ty = origin.Y + btnPadY;
            dl.AddText(new Vector2(tx, ty), mainCol, $"Model {i + 1}{woundText}{assignedNote}");
            float wy = ty + mainLineH;
            foreach (string line in weaponLines[i])
            {
                dl.AddText(font, smallSize, new Vector2(tx + 12f, wy), wCol, line);
                wy += smallLineH;
            }

            if (hovered) _hoveredModel = modelData;

            if (clicked)
            {
                results.TryAddWounds(pw.Model);
                if (results.IsFinishedAssigning)
                    Complete(tcs, results);
            }
        }

        ImGui.EndChild();

        // Auto-assign button
        float btnY = dh - pad - footH;
        ImGui.SetCursorPos(new Vector2(pad, btnY));
        if (ImGui.Button("Auto-assign All", new Vector2(btnW, footH - 4f)))
        {
            results.AutoFill();
            Complete(tcs, results);
        }

        ImGui.EndChild();
        ImGui.End();

        DrawInvalidTargets(results);
        DrawMapHighlight();
    }

    /// <summary>Dims every model on the table that can't be assigned a wound right now — those already
    /// killed in this pending assignment (incl. locked Tough pre-assignments) AND a joined hero while the
    /// rest of the unit still has room (#006, wounds-last) — so the player can see at a glance which figures
    /// are valid targets, mirroring the disabled dialog buttons.</summary>
    private void DrawInvalidTargets(AssignWoundsResults results)
    {
        var bg = ImGui.GetBackgroundDrawList();
        foreach (PendingWounds pw in results.PendingWounds)
        {
            if (results.CanAssignWoundTo(pw)) continue; // still a valid target

            var md = pw.Model.GetValue();
            var p = md.Position;
            if (p.x == 0f && p.z == 0f) continue;

            var (px, py) = InchesToPixel(p.x, p.z);
            float r = md.BaseRadiusInches * _scale;
            bg.AddCircleFilled(new Vector2(px, py), r, InvalidFillCol);
            bg.AddCircle(new Vector2(px, py), r, InvalidOutlineCol, 32, 1.5f);
        }
    }

    /// <summary>Rings the hovered button's model on the table canvas so the player can connect the
    /// button to the figure on the board. Drawn on the background draw list (above the Raylib canvas,
    /// below ImGui windows).</summary>
    private void DrawMapHighlight()
    {
        IModel? model = _hoveredModel;
        if (model == null) return;

        var p = model.Position;
        if (p.x == 0f && p.z == 0f) return; // never positioned — don't ring the origin

        var (px, py) = InchesToPixel(p.x, p.z);
        float r = model.BaseRadiusInches * _scale;
        var bg = ImGui.GetBackgroundDrawList();
        bg.AddCircle(new Vector2(px, py), r + 3f, HighlightCol, 32, 3f);
        bg.AddCircle(new Vector2(px, py), r + 7f, HighlightHaloCol, 32, 2f);
    }

    // ICanvasInteractionHandler — lets the player read each model's weapons by hovering it on the
    // table, and assign a wound by clicking it (mirrors the dialog buttons).

    public string? GetHoverLabel(IUnit unit, IModel model)
    {
        AssignWoundsRequest? request;
        AssignWoundsResults? results;
        lock (_lock) { request = _request; results = _results; }
        if (request == null || results == null) return null;

        PendingWounds? pw = results.PendingWounds
            .FirstOrDefault(e => ReferenceEquals(e.Model.GetValue(), model));
        if (pw == null) return null; // not a model of the unit receiving wounds

        var modelData = pw.Model.GetValue();
        float remaining = modelData.TotalWounds - modelData.WoundsDealt - pw.Wounds;

        var sb = new StringBuilder();
        // Show remaining/total only for multi-wound models; single-wound models need no counter.
        if (modelData.TotalWounds > 1f)
            sb.AppendLine($"This model - ({remaining:F0}/{modelData.TotalWounds:F0})");
        if (pw.Wounds > 0f)
            sb.AppendLine($"({pw.Wounds:F0} already assigned)");
        sb.AppendLine("Weapons:");
        foreach (string line in WeaponLines(modelData))
            sb.AppendLine($"  {line}");
        if (results.CanAssignWoundTo(pw))
            sb.Append("Click to assign wounds (fills this model)");
        else if (remaining > 0f)
            sb.Append("(a hero is assigned wounds last - not yet a valid target)");
        else
            sb.Append("(no longer a valid target)");
        return sb.ToString();
    }

    public void HandleClick(IUnit unit, IModel model)
    {
        AssignWoundsRequest? request;
        AssignWoundsResults? results;
        TaskCompletionSource<AssignWoundsResults>? tcs;
        lock (_lock) { request = _request; results = _results; tcs = _tcs; }
        if (request == null || results == null || tcs == null) return;

        PendingWounds? pw = results.PendingWounds
            .FirstOrDefault(e => ReferenceEquals(e.Model.GetValue(), model));
        if (pw == null) return;

        if (results.TryAddWounds(pw.Model) && results.IsFinishedAssigning)
            Complete(tcs, results);
    }

    /// <summary>Distinct weapon groups for a model, e.g. "2x Rifle (24\")", "Heavy Rifle (30\")".
    /// Shared with <see cref="GuiModelSelectionResolver"/> (model-pick stats).</summary>
    internal static List<string> WeaponLines(IModel model)
    {
        var lines = new List<string>();
        foreach (var grp in model.Weapons.GroupBy(w => w.Name))
        {
            var w = grp.First();
            int count = grp.Count();
            string range  = w.RangeInches > 0f ? $"{w.RangeInches:0.#}\"" : "melee";
            string prefix = count > 1 ? $"{count}x " : "";
            lines.Add($"{prefix}{w.Name} ({range})");
        }
        if (lines.Count == 0) lines.Add("(no weapons)");
        return lines;
    }

    private (float px, float py) InchesToPixel(float x, float z) =>
        (_originX + x * _scale, _originY + (_tableH - z) * _scale);

    private void Complete(TaskCompletionSource<AssignWoundsResults> tcs, AssignWoundsResults results)
    {
        lock (_lock) { _request = null; _results = null; _tcs = null; }
        tcs.SetResult(results);
    }

}
