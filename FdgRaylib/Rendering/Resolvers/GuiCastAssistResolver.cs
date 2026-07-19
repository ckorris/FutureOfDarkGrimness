using System.Numerics;
using FDG;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// #103 — GUI resolver for the cast-assist prompt. On the table canvas it highlights the assisting Caster
/// and draws a line to the casting Caster — <b>blue</b> for a friendly boost (+1 per token), <b>orange</b>
/// for an enemy disruption (-1 per token) — and labels the assister's spell-token count. A compact picker
/// (0..available) at the top of the screen returns how many tokens to spend. The dialog sits up top so the
/// highlighted units and the line stay visible below it.
/// </summary>
public class GuiCastAssistResolver : IStageResolver<CastAssistRequest, int>, IGuiResolver, IGuiCanvasOverlay
{
    private static readonly Vector4 FriendlyRgba = new(0.30f, 0.60f, 1.00f, 1.00f); // blue
    private static readonly Vector4 EnemyRgba    = new(1.00f, 0.60f, 0.15f, 1.00f); // orange

    private readonly object _lock = new();
    private CastAssistRequest? _request;
    private TaskCompletionSource<int>? _tcs;

    private float _scale = 10f;
    private float _tableH = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
    private int _originX, _originY;

    public void UpdateLayout(float scale, int originX, int originY, float tableH)
    {
        _scale = scale;
        _originX = originX;
        _originY = originY;
        _tableH = tableH;
    }

    public bool HasPendingRequest { get { lock (_lock) return _request != null; } }

    public Task<int> Resolve(CastAssistRequest request)
    {
        var tcs = new TaskCompletionSource<int>();
        lock (_lock) { _tcs = tcs; _request = request; }
        return tcs.Task;
    }

    public void Draw(int screenW, int screenH)
    {
        CastAssistRequest? request;
        TaskCompletionSource<int>? tcs;
        lock (_lock) { request = _request; tcs = _tcs; }
        if (request == null || tcs == null) return;

        DrawCanvas(request);
        DrawDialog(request, tcs, screenW, screenH);
    }

    // ── Canvas: highlight assister + line to caster + token-count label ─────────
    private void DrawCanvas(CastAssistRequest request)
    {
        UnitData assister = request.AssistingUnit.GetValue();
        UnitData caster   = request.CastingUnit.GetValue();
        uint color = ImGui.ColorConvertFloat4ToU32(request.IsFriendly ? FriendlyRgba : EnemyRgba);
        var dl = ImGui.GetBackgroundDrawList();

        foreach (IModel m in assister.Models)
        {
            if (!m.GetIsAlive()) continue;
            var p = m.Position;
            if (p.x == 0f && p.z == 0f) continue;
            var (px, py) = InchesToPixel(p.x, p.z);
            dl.AddCircle(new Vector2(px, py), m.BaseRadiusInches * _scale + 3f, color, 32, 3f);
        }

        if (TryCentroidPixel(assister, out Vector2 aPix) && TryCentroidPixel(caster, out Vector2 cPix))
        {
            dl.AddLine(aPix, cPix, color, 2.5f);

            string label = $"{assister.Name}: {request.AvailableTokens} token" +
                           $"{(request.AvailableTokens == 1 ? "" : "s")}";
            var size = ImGui.CalcTextSize(label);
            var pos = new Vector2(aPix.X - size.X * 0.5f, aPix.Y - size.Y - 14f);
            uint bg = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.6f));
            dl.AddRectFilled(pos - new Vector2(4, 2), pos + size + new Vector2(4, 2), bg, 3f);
            dl.AddText(pos, color, label);
        }
    }

    // ── Dialog: 0..available token picker ───────────────────────────────────────
    private void DrawDialog(CastAssistRequest request, TaskCompletionSource<int> tcs, int screenW, int screenH)
    {
        UnitData assister = request.AssistingUnit.GetValue();
        UnitData caster   = request.CastingUnit.GetValue();
        string verb = request.IsFriendly ? "help" : "disrupt";
        string sign = request.IsFriendly ? "+" : "-";

        int buttonCount = request.AvailableTokens + 1; // "Don't spend" + one per token
        const float rowH = 30f, gap = 4f, pad = 14f;

        float dw = ResolverPanelLayout.W;   // dock into the right-column resolver panel
        float dh = ResolverPanelLayout.H;
        float dx = ResolverPanelLayout.X;
        float dy = ResolverPanelLayout.Y;

        // Header sized to its content, not a constant: the question line wraps (long unit/spell names,
        // narrow panels, any UI scale), and a fixed height let the first button overlap a second line.
        // Measured at the same wrap width the draw uses (text starts at x=pad, wrap pos dw-pad).
        string title    = $"{assister.Name}  ({request.AvailableTokens} spell token" +
                          $"{(request.AvailableTokens == 1 ? "" : "s")})";
        string question = $"Spend tokens to {verb} {caster.Name}'s cast of {request.SpellName}? " +
                          $"({sign}1 each)";
        float titleH    = ImGui.CalcTextSize(title).Y;
        float questionH = ImGui.CalcTextSize(question, false, dw - pad * 2f).Y;
        float headerH   = titleH + 6f + questionH + 12f;

        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("##CastAssistBackdrop",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoInputs |
            ImGuiWindowFlags.NoBackground);

        ImGui.SetCursorPos(new Vector2(dx, dy));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.15f, 0.15f, 0.20f, 0.97f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f);
        ImGui.BeginChild("##CastAssistDialog", new Vector2(dw, dh), ImGuiChildFlags.Borders,
            ImGuiWindowFlags.None);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();

        Vector4 accent = request.IsFriendly ? FriendlyRgba : EnemyRgba;
        ImGui.SetCursorPos(new Vector2(pad, pad));
        ImGui.PushStyleColor(ImGuiCol.Text, accent);
        ImGui.TextUnformatted(title);
        ImGui.PopStyleColor();
        ImGui.SetCursorPos(new Vector2(pad, pad + titleH + 6f));
        ImGui.PushTextWrapPos(dw - pad);
        ImGui.TextUnformatted(question);
        ImGui.PopTextWrapPos();

        // #248: the digit IS the quantity here — 0 = don't spend, N = spend N (keys past the
        // affordable count ignored). Applied once after drawing (complete-once discipline).
        int digit = ResolverHotkeys.PressedDigit();
        int picked = digit == 0 ? 0
            : digit > 0 && digit <= request.AvailableTokens ? digit
            : -1;

        float btnW = dw - pad * 2;
        float y = pad + headerH;

        ImGui.SetCursorPos(new Vector2(pad, y));
        if (ImGui.Button("[0] Don't spend##assist0", new Vector2(btnW, rowH)) && picked < 0)
            picked = 0;
        y += rowH + gap;

        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(accent.X * 0.5f, accent.Y * 0.5f, accent.Z * 0.5f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, accent);
        for (int i = 1; i <= request.AvailableTokens; i++)
        {
            ImGui.SetCursorPos(new Vector2(pad, y));
            string numTag = i <= 9 ? $"[{i}] " : "";
            if (ImGui.Button($"{numTag}Spend {i}  ({sign}{i})##assist{i}", new Vector2(btnW, rowH)) && picked < 0)
                picked = i;
            y += rowH + gap;
        }
        ImGui.PopStyleColor(2);

        ImGui.EndChild();
        ImGui.End();

        if (picked >= 0)
            Complete(tcs, picked);
    }

    private bool TryCentroidPixel(UnitData unit, out Vector2 pixel)
    {
        float sx = 0f, sz = 0f;
        int n = 0;
        foreach (IModel m in unit.Models)
        {
            if (!m.GetIsAlive()) continue;
            var p = m.Position;
            if (p.x == 0f && p.z == 0f) continue;
            sx += p.x; sz += p.z; n++;
        }
        if (n == 0) { pixel = default; return false; }
        var (px, py) = InchesToPixel(sx / n, sz / n);
        pixel = new Vector2(px, py);
        return true;
    }

    private (float px, float py) InchesToPixel(float x, float z)
    {
        float px = _originX + x * _scale;
        float py = _originY + (_tableH - z) * _scale;
        return (px, py);
    }

    private void Complete(TaskCompletionSource<int> tcs, int count)
    {
        lock (_lock) { _request = null; _tcs = null; }
        tcs.SetResult(count);
    }
}
