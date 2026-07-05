using System.Numerics;
using FDG;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

public class GuiChooseDeploymentZoneResolver
    : IStageResolver<ChooseDeploymentZoneRequest, DataBinding<RectangularZone>>, IGuiResolver, IGuiCanvasOverlay
{
    private readonly object _lock = new();
    private ChooseDeploymentZoneRequest? _request;
    private TaskCompletionSource<DataBinding<RectangularZone>>? _tcs;

    // Canvas layout — main thread only
    private float _scale  = 10f;
    private float _tableH = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
    private int   _originX, _originY;

    // 1-frame carry of button-hover so canvas highlight follows it.
    private int _lastButtonHover = -1;

    public bool HasPendingRequest { get { lock (_lock) return _request != null; } }

    public void UpdateLayout(float scale, int originX, int originY, float tableH)
    {
        _scale = scale; _originX = originX; _originY = originY; _tableH = tableH;
    }

    public Task<DataBinding<RectangularZone>> Resolve(ChooseDeploymentZoneRequest request)
    {
        var tcs = new TaskCompletionSource<DataBinding<RectangularZone>>();
        lock (_lock) { _tcs = tcs; _request = request; }
        return tcs.Task;
    }

    public void Draw(int screenW, int screenH)
    {
        ChooseDeploymentZoneRequest? request;
        TaskCompletionSource<DataBinding<RectangularZone>>? tcs;
        lock (_lock) { request = _request; tcs = _tcs; }
        if (request == null || tcs == null) return;

        var io = ImGui.GetIO();
        var dl = ImGui.GetBackgroundDrawList();

        // Reorder zones for display: reading order (top-to-bottom rows, left-to-right within a row).
        // Engine emits AvailableZones in whatever order it builds them; we don't trust that order for labels.
        var availableOrdered = SortReadingOrder(request.AvailableZones);
        var unavailableOrdered = SortReadingOrder(request.UnavailableZones);

        // Geometric hover: which available zone (if any) is under the cursor?
        int canvasHover = -1;
        if (!io.WantCaptureMouse)
        {
            float mx = (io.MousePos.X - _originX) / _scale;
            float mz = _tableH - (io.MousePos.Y - _originY) / _scale;
            for (int i = 0; i < availableOrdered.Count; i++)
            {
                var z = availableOrdered[i].GetValue();
                if (mx >= z.Left && mx <= z.Right && mz >= z.Bottom && mz <= z.Top)
                {
                    canvasHover = i;
                    break;
                }
            }
        }

        int highlight = canvasHover >= 0 ? canvasHover : _lastButtonHover;

        DrawAvailableZones(dl, availableOrdered, highlight);
        DrawUnavailableZones(dl, unavailableOrdered);

        // Direct canvas click → select zone.
        if (canvasHover >= 0 && !io.WantCaptureMouse && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            Complete(tcs, availableOrdered[canvasHover]);
            return;
        }

        _lastButtonHover = DrawDialog(screenW, screenH, availableOrdered, unavailableOrdered, tcs, canvasHover);
    }

    private void DrawAvailableZones(ImDrawListPtr dl, List<DataBinding<RectangularZone>> zones, int highlight)
    {
        for (int i = 0; i < zones.Count; i++)
        {
            var zone = zones[i].GetValue();
            bool hi = i == highlight;
            uint fill    = hi
                ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.35f, 0.85f, 1.00f, 0.28f))
                : ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 0.60f, 1.00f, 0.12f));
            uint outline = hi
                ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.55f, 0.95f, 1.00f, 1.00f))
                : ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 0.60f, 1.00f, 0.80f));
            float thickness = hi ? 4f : 2f;
            ZoneRenderer.DrawFilled(zone, dl, _scale, _originX, _originY, _tableH, fill, outline, thickness);

            string label = $"Zone {i + 1}";
            float fontSize = 32f;
            var font = ImGui.GetFont();
            var size = font.CalcTextSizeA(fontSize, float.MaxValue, 0f, label);
            float cxPx = _originX + (zone.Left + zone.Right) * 0.5f * _scale;
            float cyPx = _originY + (_tableH - (zone.Bottom + zone.Top) * 0.5f) * _scale;
            var textPos = new Vector2(cxPx - size.X * 0.5f, cyPx - size.Y * 0.5f);
            uint bg = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.55f));
            dl.AddRectFilled(textPos - new Vector2(8f, 4f), textPos + size + new Vector2(8f, 4f), bg, 4f);
            dl.AddText(font, fontSize, textPos, 0xFFFFFFFFu, label);
        }
    }

    private void DrawUnavailableZones(ImDrawListPtr dl, List<DataBinding<RectangularZone>> zones)
    {
        uint fill    = ImGui.ColorConvertFloat4ToU32(new Vector4(0.40f, 0.40f, 0.40f, 0.12f));
        uint outline = ImGui.ColorConvertFloat4ToU32(new Vector4(0.45f, 0.45f, 0.45f, 0.80f));
        foreach (var zb in zones)
            ZoneRenderer.DrawFilled(zb.GetValue(), dl, _scale, _originX, _originY, _tableH, fill, outline, 2f);
    }

    /// <summary>Returns the index of the button hovered this frame, or -1.</summary>
    private int DrawDialog(int screenW, int screenH,
        List<DataBinding<RectangularZone>> available,
        List<DataBinding<RectangularZone>> unavailable,
        TaskCompletionSource<DataBinding<RectangularZone>> tcs, int canvasHover)
    {
        int availCount = available.Count;
        int takenCount = unavailable.Count;
        float rowH    = 36f;
        float pad     = 16f;
        float headerH = 56f;
        float dw = MathF.Min(screenW * 0.45f, 560f);
        float dh = MathF.Min(headerH + pad + (availCount + takenCount) * rowH + pad * 2, screenH * 0.80f);
        float dx = screenW - dw - 12f;   // right-aligned in the open right-side space (#105)
        float dy = (screenH - dh) * 0.5f;

        ImGui.SetNextWindowPos(new Vector2(dx, dy), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(dw, dh), ImGuiCond.Always);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.15f, 0.15f, 0.20f, 0.97f));
        ImGui.Begin("##DeployZoneDialog",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleColor();

        ImGui.PushTextWrapPos(dw - pad);
        ImGui.TextUnformatted("Choose Deployment Zone");
        ImGui.Spacing();
        ImGui.TextUnformatted($"Deploy within {GameWideConstants.DEPLOYMENT_DISTANCE_INCHES:F0}\" of your table edge.");
        ImGui.TextDisabled("Click a button or click a zone on the table.");
        ImGui.PopTextWrapPos();

        float btnW = dw - pad * 2;
        int buttonHover = -1;
        for (int i = 0; i < availCount; i++)
        {
            var zb   = available[i];
            var zone = zb.GetValue();
            string label = $"Zone {i + 1}  (X {zone.Left:F0}\"-{zone.Right:F0}\", Z {zone.Bottom:F0}\"-{zone.Top:F0}\")##{i}";

            bool forceHover = i == canvasHover;
            if (forceHover)
            {
                Vector4 hov = ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonHovered];
                ImGui.PushStyleColor(ImGuiCol.Button, hov);
            }
            if (ImGui.Button(label, new Vector2(btnW, rowH - 4f)))
                Complete(tcs, zb);
            if (ImGui.IsItemHovered()) buttonHover = i;
            if (forceHover) ImGui.PopStyleColor();
        }

        if (takenCount > 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.5f, 0.5f, 0.5f, 1f));
            for (int i = 0; i < takenCount; i++)
            {
                var zone = unavailable[i].GetValue();
                string label = $"Zone -- (X {zone.Left:F0}\"-{zone.Right:F0}\", Z {zone.Bottom:F0}\"-{zone.Top:F0}\") [taken]##t{i}";
                ImGui.BeginDisabled(true);
                ImGui.Button(label, new Vector2(btnW, rowH - 4f));
                ImGui.EndDisabled();
            }
            ImGui.PopStyleColor();
        }

        ImGui.End();
        return buttonHover;
    }

    /// <summary>
    /// Reading-order sort: top rows first (higher Z first), then left-to-right within a row (lower X first).
    /// Zones whose centre Z values are within 1" are treated as the same row.
    /// </summary>
    private static List<DataBinding<RectangularZone>> SortReadingOrder(IReadOnlyList<DataBinding<RectangularZone>> zones)
    {
        const float ROW_TOLERANCE_INCHES = 1.0f;
        var list = new List<DataBinding<RectangularZone>>(zones);
        list.Sort((a, b) =>
        {
            var za = a.GetValue();
            var zb = b.GetValue();
            float cza = (za.Top + za.Bottom) * 0.5f;
            float czb = (zb.Top + zb.Bottom) * 0.5f;
            if (MathF.Abs(cza - czb) > ROW_TOLERANCE_INCHES)
                return czb.CompareTo(cza); // higher Z first
            float cxa = (za.Left + za.Right) * 0.5f;
            float cxb = (zb.Left + zb.Right) * 0.5f;
            return cxa.CompareTo(cxb);     // lower X first
        });
        return list;
    }

    private void Complete(TaskCompletionSource<DataBinding<RectangularZone>> tcs, DataBinding<RectangularZone> zone)
    {
        lock (_lock) { _request = null; _tcs = null; _lastButtonHover = -1; }
        tcs.SetResult(zone);
    }
}
