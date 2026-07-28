using System.Numerics;
using FDG;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FdgRaylib.Rendering.Previews;
using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// #029/#150 — the Aircraft forced move, made visual: the whole unit slides along the fixed heading, and the
/// mouse picks a point on the segment 30–36" ahead (projected + clamped — it feels like normal movement, just
/// constrained to that line; there is no rotation). Ghost bases + heading triangles preview the landing spot.
/// A left-click commits; if the spot would carry any base across the table bounds the ghosts turn red and the
/// click opens a confirm — Yes flies off the table (the activation ends, no shooting), No keeps placing.
/// </summary>
public class GuiAircraftAdvanceResolver
    : IStageResolver<AircraftAdvanceRequest, AircraftAdvanceResult>, IGuiResolver, IGuiCanvasOverlay,
      IPreviewSource
{
    private readonly object _lock = new();

    // Layout — main thread only
    private float _scale  = 10f;
    private float _tableH = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
    private int   _originX, _originY;

    // Request state
    private AircraftAdvanceRequest? _request;
    private TaskCompletionSource<AircraftAdvanceResult>? _tcs;

    // The currently previewed distance along the heading; frozen while the fly-off confirm is open.
    private float _currentDistance;
    private bool _confirmOpen;

    /// <summary>
    /// #280: the forced move being previewed, as the shared ghost+path vocabulary - the unit's
    /// living models as the "base" roster (no waypoints; the anchor line the presenter draws from
    /// each live position IS the approach), ghosts at the previewed landing spot along the fixed
    /// heading. No stale-snapshot guard needed: _currentDistance is reset in Resolve under the same
    /// lock that publishes the request, so it always belongs to the request read with it.
    /// </summary>
    public PreviewState? BuildPreviewState()
    {
        AircraftAdvanceRequest? request;
        float distance;
        lock (_lock) { request = _request; distance = _currentDistance; }
        if (request == null) return null;

        var living = request.UnitDataBinding.GetValue().Models.Where(m => m.GetIsAlive()).ToList();
        if (living.Count == 0) return null;

        Float2 h = request.HeadingNormal;
        float qFacingX = PreviewQuantize.Inches(h.X);
        float qFacingZ = PreviewQuantize.Inches(h.Y);

        var baseModels = new List<GhostPathBaseModel>(living.Count);
        var ghosts = new List<GhostPathGhost>(living.Count);
        int contentHash = 17;
        for (int i = 0; i < living.Count; i++)
        {
            IModel m = living[i];
            baseModels.Add(new GhostPathBaseModel(m.ID.ID, Array.Empty<GhostPathPoint>(),
                qFacingX, qFacingZ, GhostPathBands.Advance));
            unchecked { contentHash = contentHash * 31 + m.ID.ID.GetHashCode(); }

            ghosts.Add(new GhostPathGhost(i,
                PreviewQuantize.Inches(m.Position.x + h.X * distance),
                PreviewQuantize.Inches(m.Position.z + h.Y * distance),
                qFacingX, qFacingZ, GhostPathBands.Advance));
        }

        return new PreviewState(request.TargetPlayerID, new List<PreviewSlotPayload>
        {
            new PreviewSlotPayload(GhostPathSlots.Base, new GhostPathBase(contentHash, baseModels)),
            new PreviewSlotPayload(GhostPathSlots.Ghost, new GhostPathGhosts(contentHash, ghosts)),
        });
    }

    private static readonly uint SegmentCol   = ImGui.ColorConvertFloat4ToU32(new Vector4(0.30f, 0.85f, 1.00f, 0.90f));
    private static readonly uint ApproachCol  = ImGui.ColorConvertFloat4ToU32(new Vector4(0.30f, 0.85f, 1.00f, 0.35f));
    private static readonly uint OkFill       = ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 1.00f, 0.20f, 0.50f));
    private static readonly uint OffTableFill = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.25f, 0.25f, 0.55f));
    private static readonly uint GhostOutline = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.85f));

    public void UpdateLayout(float scale, int originX, int originY, float tableH)
    {
        _scale = scale; _originX = originX; _originY = originY; _tableH = tableH;
    }

    public bool HasPendingRequest { get { lock (_lock) return _request != null; } }

    public Task<AircraftAdvanceResult> Resolve(AircraftAdvanceRequest request)
    {
        var tcs = new TaskCompletionSource<AircraftAdvanceResult>();
        lock (_lock)
        {
            _tcs = tcs;
            _currentDistance = request.MinDistanceInches;
            _confirmOpen = false;
            _request = request;
        }
        return tcs.Task;
    }

    public void Draw(int screenW, int screenH)
    {
        AircraftAdvanceRequest? request;
        TaskCompletionSource<AircraftAdvanceResult>? tcs;
        lock (_lock) { request = _request; tcs = _tcs; }
        if (request == null || tcs == null) return;

        var io = ImGui.GetIO();
        var dl = ImGui.GetBackgroundDrawList();

        IUnit unit = request.UnitDataBinding.GetValue();
        var living = unit.Models.Where(m => m.GetIsAlive()).ToList();
        if (living.Count == 0)
        {
            Complete(tcs, new AircraftAdvanceResult(request.MinDistanceInches, false));
            return;
        }

        Float2 h = request.HeadingNormal;
        float cx = living.Average(m => m.Position.x);
        float cz = living.Average(m => m.Position.z);

        // Mouse → distance along the heading (projected onto the line through the centroid, clamped to the band).
        bool overTable = IsOverTable(io.MousePos.X, io.MousePos.Y);
        if (!_confirmOpen && overTable && !io.WantCaptureMouse)
        {
            var (mx, mz) = PixelToInches(io.MousePos.X, io.MousePos.Y);
            float t = (mx - cx) * h.X + (mz - cz) * h.Y;
            _currentDistance = Math.Clamp(t, request.MinDistanceInches, request.MaxDistanceInches);
        }

        // The reachable segment ahead (min→max), plus a faint approach line from the unit to it.
        Vector2 segA = ToPixel(cx + h.X * request.MinDistanceInches, cz + h.Y * request.MinDistanceInches);
        Vector2 segB = ToPixel(cx + h.X * request.MaxDistanceInches, cz + h.Y * request.MaxDistanceInches);
        dl.AddLine(ToPixel(cx, cz), segA, ApproachCol, 2f);
        dl.AddLine(segA, segB, SegmentCol, 4f);
        DrawTick(dl, segA, h);
        DrawTick(dl, segB, h);

        // Ghost bases at the previewed spot — red when any base would cross the table bounds (fly-off).
        var paths = ForcedAircraftMove.BuildPaths(request.UnitDataBinding, h, _currentDistance);
        bool leaves = ForcedAircraftMove.WouldLeaveTable(paths);
        float dx = h.X * _currentDistance, dz = h.Y * _currentDistance;
        foreach (IModel m in living)
        {
            Vector2 at = ToPixel(m.Position.x + dx, m.Position.z + dz);
            ModelBaseRenderer.DrawFilledImGui(dl, m.BaseShape, at, _scale, leaves ? OffTableFill : OkFill, GhostOutline, 1.5f, h);
            ModelBaseRenderer.DrawHeadingImGui(dl, m.BaseShape, at, _scale, h, GhostOutline);
        }

        // Click commits — straight to the result when on-table, via the fly-off confirm otherwise.
        if (!_confirmOpen && overTable && !io.WantCaptureMouse && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            if (!leaves)
            {
                Complete(tcs, new AircraftAdvanceResult(_currentDistance, false));
                return;
            }
            _confirmOpen = true;
            ImGui.OpenPopup("Fly off the table?");
        }

        if (_confirmOpen)
        {
            // Keep the modal request alive across frames (OpenPopup is consumed by the first BeginPopupModal).
            if (!ImGui.IsPopupOpen("Fly off the table?")) ImGui.OpenPopup("Fly off the table?");
            var center = ImGui.GetMainViewport().GetCenter();
            ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
            if (ImGui.BeginPopupModal("Fly off the table?", ImGuiWindowFlags.AlwaysAutoResize))
            {
                ImGui.TextWrapped($"{unit.Name} would cross the table edge and leave play - its activation ends " +
                    "(no shooting), and it flies back on from a table edge at the start of the next round.");
                ImGui.Spacing();
                float confirmH = ResolverPanelLayout.OptionRowHeight();   // #298
                if (ImGui.Button("Yes, fly off", new Vector2(140f, confirmH)))
                {
                    ImGui.CloseCurrentPopup();
                    ImGui.EndPopup();
                    Complete(tcs, new AircraftAdvanceResult(_currentDistance, true));
                    return;
                }
                ImGui.SameLine();
                if (ImGui.Button("No, keep moving", new Vector2(140f, confirmH)))
                {
                    ImGui.CloseCurrentPopup();
                    _confirmOpen = false;
                }
                ImGui.EndPopup();
            }
        }

        DrawInfoPanel(screenW, request, leaves);
    }

    private void DrawInfoPanel(int screenW, AircraftAdvanceRequest request, bool leaves)
    {
        ResolverPanelLayout.BeginDocked("Aircraft Move##aircraftpanel");

        ImGui.TextUnformatted($"{request.UnitDataBinding.GetValue().Name} flies straight ahead.");
        ImGui.TextUnformatted($"Distance: {_currentDistance:0.0}\"  ({request.MinDistanceInches:0.#}-{request.MaxDistanceInches:0.#}\")");

        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Text, leaves ? new Vector4(1f, 0.5f, 0.4f, 1f) : new Vector4(0.6f, 0.6f, 0.6f, 1f));
        ImGui.TextWrapped(leaves
            ? "This spot crosses the table edge - clicking will ask to confirm flying off."
            : "Move the mouse along the segment ahead; click to land there. The heading can't be changed.");
        ImGui.PopStyleColor();

        ImGui.End();
    }

    // A short tick across the segment at its endpoints, so the 30" and 36" limits read at a glance.
    private void DrawTick(ImDrawListPtr dl, Vector2 at, Float2 heading)
    {
        // Perpendicular in screen space (table Z maps to screen −y).
        Vector2 dir = new(heading.X, -heading.Y);
        Vector2 perp = new(-dir.Y, dir.X);
        float half = 0.5f * _scale;
        dl.AddLine(at - perp * half, at + perp * half, SegmentCol, 3f);
    }

    private void Complete(TaskCompletionSource<AircraftAdvanceResult> tcs, AircraftAdvanceResult result)
    {
        lock (_lock) { _request = null; _tcs = null; _confirmOpen = false; }
        tcs.SetResult(result);
    }

    private Vector2 ToPixel(float x, float z) =>
        new(_originX + x * _scale, _originY + (_tableH - z) * _scale);

    private (float x, float z) PixelToInches(float px, float py) =>
        ((px - _originX) / _scale, _tableH - (py - _originY) / _scale);

    private bool IsOverTable(float px, float py) =>
        px >= _originX && py >= _originY &&
        px <= _originX + GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES * _scale &&
        py <= _originY + _tableH * _scale;
}
