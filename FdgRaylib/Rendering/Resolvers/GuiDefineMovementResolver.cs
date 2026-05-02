using System.Numerics;
using FDG;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

public class GuiDefineMovementResolver
    : IStageResolver<DefineMovementPathRequest, List<ModelMoveEntry>>, IGuiResolver, IGuiCanvasOverlay
{
    private readonly ITableState _tableState;
    private readonly object _lock = new();

    // Layout — main-thread only, no lock needed
    private float _scale   = 10f;
    private float _tableH  = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
    private int   _originX, _originY;

    // Request state
    private DefineMovementPathRequest? _request;
    private TaskCompletionSource<List<ModelMoveEntry>>? _tcs;

    // Error feedback — main-thread only
    private string? _errorMessage;
    private double  _errorExpiry;

    public GuiDefineMovementResolver(ITableState tableState) => _tableState = tableState;

    public void UpdateLayout(float scale, int originX, int originY, float tableH)
    {
        _scale   = scale;
        _originX = originX;
        _originY = originY;
        _tableH  = tableH;
    }

    public bool HasPendingRequest { get { lock (_lock) return _request != null; } }

    public Task<List<ModelMoveEntry>> Resolve(DefineMovementPathRequest request)
    {
        var tcs = new TaskCompletionSource<List<ModelMoveEntry>>();
        lock (_lock) { _tcs = tcs; _request = request; _errorMessage = null; }
        return tcs.Task;
    }

    public void Draw(int screenW, int screenH)
    {
        DefineMovementPathRequest? request;
        TaskCompletionSource<List<ModelMoveEntry>>? tcs;
        lock (_lock) { request = _request; tcs = _tcs; }
        if (request == null || tcs == null) return;

        var terrain = _tableState.Terrain.Objects.ToList();

        var io     = ImGui.GetIO();
        var dl     = ImGui.GetBackgroundDrawList();
        var models = request.UnitDataBinding.GetValue().ModelBindings;

        // Unit centre
        float cx = models.Average(mb => mb.GetValue().Position.x);
        float cz = models.Average(mb => mb.GetValue().Position.z);

        // Mouse → table inches
        var (mouseInX, mouseInZ) = PixelToInches(io.MousePos.X, io.MousePos.Y);
        float ddx = mouseInX - cx;
        float ddz = mouseInZ - cz;
        bool overTable = IsOverTable(io.MousePos.X, io.MousePos.Y);

        // Range rings on each model's current position
        uint advColor  = ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 0.90f, 0.20f, 0.50f));
        uint rushColor = ImGui.ColorConvertFloat4ToU32(new Vector4(0.90f, 0.85f, 0.20f, 0.40f));
        uint capColor  = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.55f, 0.10f, 0.65f));
        foreach (var mb in models)
        {
            var m = mb.GetValue();
            var (px, py) = InchesToPixel(m.Position.x, m.Position.z);
            dl.AddCircle(new Vector2(px, py), request.MaxAdvanceDistance * _scale, advColor,  64, 1.5f);
            dl.AddCircle(new Vector2(px, py), request.MaxChargeDistance  * _scale, rushColor, 64, 1.5f);
        }

        // Ghost formation at hover
        List<ModelMoveEntry>? tentative = null;
        bool tentativeValid = false;
        bool tentativeCrossesDifficult = false;
        if (overTable)
        {
            tentative = BuildEntries(request, ddx, ddz);
            tentativeValid = MovementUtilities.ValidatePaths(tentative, request.MaxChargeDistance, terrain, out _);
            tentativeCrossesDifficult = tentative.Any(m => CrossesDifficultTerrain(m, terrain));

            // Draw orange difficult-terrain cap rings when relevant
            if (tentativeCrossesDifficult)
            {
                foreach (var mb in models)
                {
                    var m = mb.GetValue();
                    var (px, py) = InchesToPixel(m.Position.x, m.Position.z);
                    dl.AddCircle(new Vector2(px, py), GameWideConstants.DIFFICULT_TERRAIN_MOVE_CAP_INCHES * _scale, capColor, 64, 2f);
                }
            }

            uint ghostFill    = tentativeValid
                ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 1.00f, 0.20f, 0.45f))
                : ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.20f, 0.20f, 0.45f));
            uint ghostOutline = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.80f));

            foreach (var mb in models)
            {
                var m  = mb.GetValue();
                float nx = m.Position.x + ddx;
                float nz = m.Position.z + ddz;
                var (gx, gy) = InchesToPixel(nx, nz);
                float r = m.BaseRadiusInches * _scale;
                dl.AddCircleFilled(new Vector2(gx, gy), r, ghostFill);
                dl.AddCircle(new Vector2(gx, gy), r, ghostOutline, 32, 1f);
            }
        }

        // Click to confirm destination
        if (overTable && !io.WantCaptureMouse && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            tentative ??= BuildEntries(request, ddx, ddz);
            if (MovementUtilities.ValidatePaths(tentative, request.MaxChargeDistance, terrain, out var errors))
            {
                Complete(tcs, tentative);
                return;
            }
            _errorMessage = string.Join(" | ", errors.Select(e => MovementUtilities.ErrorReasonToString(e.ErrorReasonType)));
            _errorExpiry  = ImGui.GetTime() + 2.5;
        }

        if (_errorMessage != null && ImGui.GetTime() > _errorExpiry)
            _errorMessage = null;

        DrawInfoPanel(screenW, request, request.UnitDataBinding.GetValue().Name, tcs, tentativeCrossesDifficult);
    }

    private void DrawInfoPanel(int screenW, DefineMovementPathRequest request, string unitName,
        TaskCompletionSource<List<ModelMoveEntry>> tcs, bool crossesDifficult)
    {
        float panelW = MathF.Min(screenW * 0.42f, 500f);
        float panelH = crossesDifficult ? 130f : 110f;
        ImGui.SetNextWindowPos(new Vector2((screenW - panelW) * 0.5f, 16f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(panelW, panelH), ImGuiCond.Always);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.10f, 0.10f, 0.15f, 0.92f));
        ImGui.Begin("##MovementPanel",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleColor();

        ImGui.TextUnformatted($"Move: {unitName}");
        ImGui.SameLine();
        ImGui.TextDisabled($"  advance ≤ {request.MaxAdvanceDistance:F1}\"  |  rush ≤ {request.MaxChargeDistance:F1}\"");

        ImGui.Spacing();
        if (_errorMessage != null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.4f, 0.4f, 1f));
            ImGui.TextWrapped(_errorMessage);
            ImGui.PopStyleColor();
        }
        else
        {
            ImGui.TextDisabled("Click the table to move.  Green ring = advance,  yellow = rush.");
        }

        if (crossesDifficult)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.00f, 0.55f, 0.10f, 1f));
            ImGui.TextUnformatted($"Difficult terrain in path — max {GameWideConstants.DIFFICULT_TERRAIN_MOVE_CAP_INCHES}\"");
            ImGui.PopStyleColor();
        }

        ImGui.Spacing();
        float btnW = (panelW - ImGui.GetStyle().ItemSpacing.X - ImGui.GetStyle().WindowPadding.X * 2) * 0.5f;
        if (ImGui.Button("Skip (stay in place)", new Vector2(btnW, 28f)))
            Complete(tcs, StayInPlace(request));
        ImGui.SameLine();
        if (ImGui.Button("Auto-advance", new Vector2(btnW, 28f)))
            Complete(tcs, AutoAdvance(request));

        ImGui.End();
    }

    private List<ModelMoveEntry> BuildEntries(DefineMovementPathRequest request, float ddx, float ddz) =>
        request.UnitDataBinding.GetValue().ModelBindings.Select(mb =>
        {
            var m = mb.GetValue();
            return new ModelMoveEntry(mb, new List<Position> { new Position(m.Position.x + ddx, m.Position.z + ddz) });
        }).ToList();

    private static List<ModelMoveEntry> StayInPlace(DefineMovementPathRequest request) =>
        request.UnitDataBinding.GetValue().ModelBindings
            .Select(mb => new ModelMoveEntry(mb, new List<Position> { mb.GetValue().Position }))
            .ToList();

    private List<ModelMoveEntry> AutoAdvance(DefineMovementPathRequest request)
    {
        var models = request.UnitDataBinding.GetValue().ModelBindings;

        var enemyPositions = new List<Position>();
        foreach (var u in _tableState.Units.Objects)
        {
            if (u.PlayerID == request.TargetPlayerID) continue;
            foreach (var m in u.Models)
                if (m.GetIsAlive()) enemyPositions.Add(m.Position);
        }

        if (enemyPositions.Count == 0) return StayInPlace(request);

        float cx = models.Average(mb => mb.GetValue().Position.x);
        float cz = models.Average(mb => mb.GetValue().Position.z);

        var nearest = enemyPositions
            .OrderBy(p => (p.x - cx) * (p.x - cx) + (p.z - cz) * (p.z - cz))
            .First();

        float dx   = nearest.x - cx;
        float dz   = nearest.z - cz;
        float dist = MathF.Sqrt(dx * dx + dz * dz);
        if (dist < 0.01f) return StayInPlace(request);

        float step = Math.Min(request.MaxAdvanceDistance - 0.001f, Math.Max(0f, dist - 1f));
        float ndx  = dx / dist * step;
        float ndz  = dz / dist * step;

        return models.Select(mb =>
        {
            var m = mb.GetValue();
            return new ModelMoveEntry(mb, new List<Position> { new Position(m.Position.x + ndx, m.Position.z + ndz) });
        }).ToList();
    }

    private void Complete(TaskCompletionSource<List<ModelMoveEntry>> tcs, List<ModelMoveEntry> entries)
    {
        lock (_lock) { _request = null; _tcs = null; }
        tcs.SetResult(entries);
    }

    private (float x, float z) PixelToInches(float px, float py) =>
        ((px - _originX) / _scale, _tableH - (py - _originY) / _scale);

    private (float px, float py) InchesToPixel(float x, float z) =>
        (_originX + x * _scale, _originY + (_tableH - z) * _scale);

    private bool IsOverTable(float px, float py) =>
        px >= _originX && py >= _originY &&
        px <= _originX + GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES * _scale &&
        py <= _originY + _tableH * _scale;

    private static bool CrossesDifficultTerrain(ModelMoveEntry move, List<ITerrain> terrain)
    {
        var difficult = terrain.Where(t => t.TerrainType.HasFlag(ETerrainType.Difficult)).ToList();
        if (difficult.Count == 0 || move.Positions.Count == 0) return false;

        var startPos = move.Model.GetValue().PositionBinding.GetValue();
        var seg = new Float2(startPos.x, startPos.z);
        for (int i = 0; i < move.Positions.Count; i++)
        {
            var end = new Float2(move.Positions[i].x, move.Positions[i].z);
            if (difficult.Any(p => p.DoesPathIntersectZone(seg, end))) return true;
            seg = end;
        }
        return false;
    }
}
