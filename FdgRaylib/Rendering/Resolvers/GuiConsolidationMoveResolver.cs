using System.Numerics;
using FDG;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

public class GuiConsolidationMoveResolver
    : IStageResolver<ConsolidationMoveRequest, List<ModelMoveEntry>>, IGuiResolver, IGuiCanvasOverlay
{
    private readonly ITableState _tableState;
    private readonly object _lock = new();

    // Layout — main-thread only
    private float _scale  = 10f;
    private float _tableH = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
    private int   _originX, _originY;

    // Request state
    private ConsolidationMoveRequest? _request;
    private TaskCompletionSource<List<ModelMoveEntry>>? _tcs;

    // Pathing state — main-thread only after Resolve assigns it
    private PathTemplate? _pathTemplate;
    private IModel? _selectedModel;

    private static readonly uint MoveColor      = ImGui.ColorConvertFloat4ToU32(new Vector4(0.40f, 0.85f, 1.00f, 0.95f));
    private static readonly uint RangeRingCol   = ImGui.ColorConvertFloat4ToU32(new Vector4(0.40f, 0.85f, 1.00f, 0.55f));
    private static readonly uint SelectionOutline = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.95f));
    private static readonly uint ModelOutline   = ImGui.ColorConvertFloat4ToU32(new Vector4(0.7f, 0.7f, 0.7f, 0.7f));
    private static readonly uint GhostOutline   = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.85f));
    private static readonly uint FinalGhostCol  = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.25f));
    private static readonly uint CohesionLineCol = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.55f, 0.55f, 0.90f));
    private static readonly uint OverlapFill    = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.25f, 0.25f, 0.55f));
    private static readonly uint GhostFill      = ImGui.ColorConvertFloat4ToU32(new Vector4(0.40f, 0.85f, 1.00f, 0.40f));

    public GuiConsolidationMoveResolver(ITableState tableState) => _tableState = tableState;

    public void UpdateLayout(float scale, int originX, int originY, float tableH)
    {
        _scale   = scale;
        _originX = originX;
        _originY = originY;
        _tableH  = tableH;
    }

    public bool HasPendingRequest { get { lock (_lock) return _request != null; } }

    public Task<List<ModelMoveEntry>> Resolve(ConsolidationMoveRequest request)
    {
        var tcs = new TaskCompletionSource<List<ModelMoveEntry>>();
        // Consolidation uses a single hard cap; the "advance" threshold is irrelevant here.
        var template = new PathTemplate(request.UnitDataBinding, request.MaxDistanceInches, request.MaxDistanceInches);
        var first = request.UnitDataBinding.GetValue().ModelBindings
            .Select(mb => mb.GetValue() as IModel)
            .FirstOrDefault(m => m != null && m.GetIsAlive());

        lock (_lock)
        {
            _tcs           = tcs;
            _request       = request;
            _pathTemplate  = template;
            _selectedModel = first;
        }
        return tcs.Task;
    }

    public void Draw(int screenW, int screenH)
    {
        ConsolidationMoveRequest? request;
        TaskCompletionSource<List<ModelMoveEntry>>? tcs;
        PathTemplate? pt;
        lock (_lock) { request = _request; tcs = _tcs; pt = _pathTemplate; }
        if (request == null || tcs == null || pt == null) return;

        var io      = ImGui.GetIO();
        var dl      = ImGui.GetBackgroundDrawList();
        var terrain = _tableState.Terrain.Objects.ToList();
        var paths   = pt.CurrentPaths;

        float maxDist = request.MaxDistanceInches;

        // 1) Draw each model's start circle + committed path + final ghost
        foreach (var kvp in paths)
        {
            var model = kvp.Key;
            var pathPoints = kvp.Value;
            var start = model.Position;
            var (sx, sy) = InchesToPixel(start.x, start.z);

            uint outline = ReferenceEquals(model, _selectedModel) ? SelectionOutline : ModelOutline;
            float thick  = ReferenceEquals(model, _selectedModel) ? 2.5f : 1.5f;
            ModelBaseRenderer.DrawOutlineImGui(dl, model.BaseShape, new Vector2(sx, sy), _scale, outline, thick);

            if (pathPoints.Count > 0)
            {
                Position prev = start;
                for (int i = 0; i < pathPoints.Count; i++)
                {
                    var cur = pathPoints[i];
                    var (px, py) = InchesToPixel(prev.x, prev.z);
                    var (cx, cy) = InchesToPixel(cur.x, cur.z);
                    dl.AddLine(new Vector2(px, py), new Vector2(cx, cy), MoveColor, 2f);
                    prev = cur;
                }
                var last = pathPoints[^1];
                var (lx, ly) = InchesToPixel(last.x, last.z);
                ModelBaseRenderer.DrawFilledImGui(dl, model.BaseShape, new Vector2(lx, ly), _scale, FinalGhostCol, outline, thick);
            }
        }

        // 2) Range ring around selected model's last waypoint
        if (_selectedModel != null)
        {
            float totalSoFar = pt.GetTotalDistanceMoved(_selectedModel);
            var anchor = pt.GetModelLastPathPosition(_selectedModel);
            var (ax, ay) = InchesToPixel(anchor.x, anchor.z);
            float remaining = maxDist - totalSoFar;
            if (remaining > 0.01f)
                dl.AddCircle(new Vector2(ax, ay), remaining * _scale, RangeRingCol, 64, 1.5f);
        }

        // 3) Ghost following mouse (clamped to cap AND to table bounds)
        bool overTable = IsOverTable(io.MousePos.X, io.MousePos.Y);
        bool wantInput = !io.WantCaptureMouse && !io.WantCaptureKeyboard;
        Position? ghostPos = null;
        bool ghostOverlaps = false;

        if (_selectedModel != null && overTable && !io.WantCaptureMouse)
        {
            var anchor = pt.GetModelLastPathPosition(_selectedModel);
            float totalSoFar = pt.GetTotalDistanceMoved(_selectedModel);
            float remaining  = maxDist - totalSoFar;
            var (mx, mz) = PixelToInches(io.MousePos.X, io.MousePos.Y);
            float dx = mx - anchor.x;
            float dz = mz - anchor.z;
            float dist = MathF.Sqrt(dx * dx + dz * dz);

            float allowed = remaining <= 0.001f ? 0f
                          : dist <= remaining ? dist
                          : MathF.Max(0f, remaining - 0.001f);

            float nx, nz;
            if (dist < 0.0001f) { nx = anchor.x; nz = anchor.z; }
            else                { nx = anchor.x + dx / dist * allowed; nz = anchor.z + dz / dist * allowed; }

            // Clamp so the model's base stays inside the table (circumscribing radius: rotation-safe).
            (nx, nz) = ClampToTable(nx, nz, _selectedModel.BaseShape.CircumscribedRadiusInches);
            ghostPos = new Position(nx, nz);

            // Consolidation slides without rotating, so the ghost keeps the model's facing.
            ghostOverlaps = WouldOverlapAnyModel(ghostPos.Value, _selectedModel.Facing, _selectedModel, request, paths);

            var (apx, apy) = InchesToPixel(anchor.x, anchor.z);
            var (gx, gy)   = InchesToPixel(nx, nz);
            dl.AddLine(new Vector2(apx, apy), new Vector2(gx, gy), MoveColor, 2f);

            ModelBaseRenderer.DrawFilledImGui(dl, _selectedModel.BaseShape, new Vector2(gx, gy), _scale,
                ghostOverlaps ? OverlapFill : GhostFill, GhostOutline);

            var finalsWithGhost = BuildFinalPositions(paths, _selectedModel, ghostPos);
            DrawCohesionIndicators(dl, finalsWithGhost, _selectedModel);
        }

        // 4) Input
        if (overTable && !io.WantCaptureMouse)
        {
            // Left-click: select a model whose start circle is hit; otherwise place a waypoint for the
            // selected model at the clamped ghost position. Left-click places (consistent with movement).
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                var (mx, mz) = PixelToInches(io.MousePos.X, io.MousePos.Y);
                IModel? hit = null;
                float bestDist = float.MaxValue;
                foreach (var model in paths.Keys)
                {
                    float dx = mx - model.Position.x;
                    float dz = mz - model.Position.z;
                    float d2 = dx * dx + dz * dz;
                    if (model.BaseShape.ContainsLocalPoint(dx, dz) && d2 < bestDist) { hit = model; bestDist = d2; }
                }
                if (hit != null)
                {
                    _selectedModel = hit;
                }
                else if (_selectedModel != null && ghostPos.HasValue && !ghostOverlaps)
                {
                    float totalSoFar = pt.GetTotalDistanceMoved(_selectedModel);
                    if (maxDist - totalSoFar > 0.001f)
                        pt.AddStep(_selectedModel, ghostPos.Value);
                }
            }

            // Right-click clears the selected model's last waypoint, if any (same as Backspace; right-click
            // clears the last path point in ALL modes).
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) && _selectedModel != null
                && paths.TryGetValue(_selectedModel, out var selList) && selList.Count > 0)
            {
                pt.RemoveLastStep(_selectedModel);
            }
        }

        if (wantInput && _selectedModel != null && ImGui.IsKeyPressed(ImGuiKey.Backspace))
        {
            if (paths.TryGetValue(_selectedModel, out var list) && list.Count > 0)
                pt.RemoveLastStep(_selectedModel);
        }

        if (wantInput && ImGui.IsKeyPressed(ImGuiKey.Space))
        {
            var keys = paths.Keys.ToList();
            if (keys.Count > 0)
            {
                int idx = _selectedModel == null ? -1 : keys.IndexOf(_selectedModel);
                _selectedModel = keys[(idx + 1) % keys.Count];
            }
        }

        DrawInfoPanel(screenW, request, pt, tcs, terrain);
    }

    private void DrawInfoPanel(int screenW, ConsolidationMoveRequest request, PathTemplate pt,
        TaskCompletionSource<List<ModelMoveEntry>> tcs, List<ITerrain> terrain)
    {
        float panelW = MathF.Min(screenW * 0.5f, 560f);
        ImGui.SetNextWindowPos(new Vector2(screenW - panelW - 12f, 16f), ImGuiCond.FirstUseEver); // right-aligned (#105)
        ImGui.SetNextWindowSizeConstraints(new Vector2(panelW, 0f), new Vector2(panelW, float.MaxValue));
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.10f, 0.10f, 0.15f, 0.92f));
        ImGui.Begin("##ConsolidatePanel",
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysAutoResize);
        ImGui.PopStyleColor();

        string unitName = request.UnitDataBinding.GetValue().Name;
        string header = request.Reason == EConsolidationReason.Wipeout
            ? $"Consolidate: {unitName} - wiped out, up to {request.MaxDistanceInches:F1}\""
            : $"Disengage: {unitName} - up to {request.MaxDistanceInches:F1}\"";
        ImGui.TextUnformatted(header);

        if (_selectedModel != null)
        {
            float dist = pt.GetTotalDistanceMoved(_selectedModel);
            ImGui.TextUnformatted($"Selected model: {dist:F2}\" / {request.MaxDistanceInches:F1}\"");
        }
        else
        {
            ImGui.TextDisabled("No model selected. Left-click a model on the table.");
        }

        ImGui.TextDisabled("L-click: select   R-click: waypoint");
        ImGui.TextDisabled("Space: next model   Backspace: undo");

        ImGui.Spacing();
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        float pad     = ImGui.GetStyle().WindowPadding.X * 2;
        float btnW    = (panelW - pad - spacing * 2) / 3f;

        var results = pt.GetResultsAsList();
        // #090: enemy-check the consolidation preview so it matches the authoritative ConsolidateStage check.
        var enemyFootprints = GetEnemyFootprintsForRequest(request);
        bool engineValid = MovementUtilities.ValidatePaths(results, request.MaxDistanceInches,
            enemyFootprints, request.CanMoveThroughEnemies, request.IgnoresDifficultTerrain, request.IgnoresImpassibleTerrain, terrain, out var engineErrors);
        var finals = BuildFinalPositions(pt.CurrentPaths, null, null);
        var cohesion = CheckCohesion(finals);

        var issues = new List<string>();
        if (!engineValid)
        {
            foreach (var e in engineErrors)
            {
                if (e.ErrorReasonType == EErrorReasonType.TooFarFromAnyUnitModel ||
                    e.ErrorReasonType == EErrorReasonType.TooFarFromAllUnitModels) continue;
                issues.Add(MovementUtilities.ErrorReasonToString(e.ErrorReasonType));
            }
        }
        foreach (var t in cohesion.TooFarFromAny)
            issues.Add($"Cohesion: model is {t.dist:F2}\" from nearest other (max {GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES:F1}\")");
        if (cohesion.FarthestPair.HasValue)
            issues.Add($"Cohesion: two models would be {cohesion.FarthestPair.Value.dist:F2}\" apart (max {GameWideConstants.MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES:F1}\")");

        bool canSubmit = issues.Count == 0;
        if (!canSubmit) ImGui.BeginDisabled();
        bool donePressed = ImGui.Button("Done", new Vector2(btnW, 28f));
        if (!canSubmit) ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(canSubmit ? "Commit this move and continue." : string.Join("\n", issues));
        if (canSubmit && donePressed)
        {
            Complete(tcs, results);
            ImGui.End();
            return;
        }

        ImGui.SameLine();
        bool clearPressed = ImGui.Button("Clear selected", new Vector2(btnW, 28f));
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Remove all waypoints from the currently selected model.");
        if (clearPressed && _selectedModel != null) pt.ClearModelSteps(_selectedModel);

        ImGui.SameLine();
        bool stayPressed = ImGui.Button("Stay in place", new Vector2(btnW, 28f));
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Don't move the unit. Every model stays where it is.");
        if (stayPressed)
        {
            pt.ClearAllSteps();
            Complete(tcs, pt.GetResultsAsList());
            ImGui.End();
            return;
        }

        ImGui.End();
    }

    private bool WouldOverlapAnyModel(Position ghostPos, Float2 ghostFacing, IModel ghostModel,
        ConsolidationMoveRequest request,
        IReadOnlyDictionary<IModel, IReadOnlyList<Position>> paths)
    {
        var ownUnit = request.UnitDataBinding.GetValue();
        foreach (var unit in _tableState.Units.Objects)
        {
            bool isOwnUnit = unit == ownUnit;
            foreach (var m in unit.Models)
            {
                if (!m.GetIsAlive()) continue;
                if (ReferenceEquals(m, ghostModel)) continue;

                Position p = m.Position;
                if (isOwnUnit && paths.TryGetValue(m, out var path) && path.Count > 0)
                    p = path[^1];

                // True oriented footprints (#150), not a bounding circle.
                if (BaseShapeGeometry.AreColliding(ghostModel.BaseShape, ghostPos, ghostFacing, m.BaseShape, p, m.Facing))
                    return true;
            }
        }
        return false;
    }

    private static List<(IModel model, Position pos)> BuildFinalPositions(
        IReadOnlyDictionary<IModel, IReadOnlyList<Position>> paths,
        IModel? ghostModel, Position? ghostPos)
    {
        var list = new List<(IModel, Position)>(paths.Count);
        foreach (var kvp in paths)
        {
            var m = kvp.Key;
            Position p;
            if (ReferenceEquals(m, ghostModel) && ghostPos.HasValue) p = ghostPos.Value;
            else if (kvp.Value.Count > 0) p = kvp.Value[^1];
            else p = m.Position;
            list.Add((m, p));
        }
        return list;
    }

    // Living enemy model footprints, tagged per-unit. Mirrors MovementUtilities.GetEnemyModelFootprints
    // but reads from ITableState (the resolver's view). #090.
    private List<EnemyModelFootprint> GetEnemyFootprintsForRequest(ConsolidationMoveRequest request)
    {
        var footprints = new List<EnemyModelFootprint>();
        int unitKey = 0;
        foreach (var u in _tableState.Units.Objects)
        {
            if (u.PlayerID == request.TargetPlayerID) continue;
            bool uncontactable = FDG.Rules.Dispatch.AircraftRules.IsAircraft(u); // #029
            bool anyLiving = false;
            foreach (var m in u.Models)
                if (m.GetIsAlive())
                {
                    footprints.Add(new EnemyModelFootprint(m.Position, m.BaseRadiusInches, unitKey, uncontactable, m.BaseShape, m.Facing));
                    anyLiving = true;
                }
            if (anyLiving) unitKey++;
        }
        return footprints;
    }

    private readonly struct CohesionViolations
    {
        public readonly List<(IModel model, IModel nearest, Position pos, Position nearestPos, float dist)> TooFarFromAny;
        public readonly (IModel a, IModel b, Position pa, Position pb, float dist)? FarthestPair;

        public CohesionViolations(
            List<(IModel, IModel, Position, Position, float)> tooFar,
            (IModel, IModel, Position, Position, float)? farthest)
        { TooFarFromAny = tooFar; FarthestPair = farthest; }
    }

    private static CohesionViolations CheckCohesion(List<(IModel model, Position pos)> finals)
    {
        var tooFar = new List<(IModel, IModel, Position, Position, float)>();
        (IModel, IModel, Position, Position, float)? farPair = null;

        if (finals.Count <= 1) return new CohesionViolations(tooFar, null);

        for (int i = 0; i < finals.Count; i++)
        {
            float nearestDist = float.PositiveInfinity;
            int nearestIdx = -1;
            for (int j = 0; j < finals.Count; j++)
            {
                if (i == j) continue;
                // Shape- and facing-aware (#150) so the cohesion readout matches the server's measurement.
                float d = DistanceUtilities.GetBaseToBaseDistanceInches_3D(
                    finals[i].pos, finals[j].pos,
                    finals[i].model.BaseShape, finals[i].model.Facing,
                    finals[j].model.BaseShape, finals[j].model.Facing);
                if (d < nearestDist) { nearestDist = d; nearestIdx = j; }
                if (i < j && (!farPair.HasValue || d > farPair.Value.Item5))
                    farPair = (finals[i].model, finals[j].model, finals[i].pos, finals[j].pos, d);
            }
            if (nearestDist > GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES && nearestIdx >= 0)
                tooFar.Add((finals[i].model, finals[nearestIdx].model, finals[i].pos, finals[nearestIdx].pos, nearestDist));
        }

        if (farPair.HasValue && farPair.Value.Item5 <= GameWideConstants.MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES)
            farPair = null;

        return new CohesionViolations(tooFar, farPair);
    }

    private void DrawCohesionIndicators(ImDrawListPtr dl, List<(IModel model, Position pos)> finals, IModel? ghostModel)
    {
        var v = CheckCohesion(finals);

        if (ghostModel != null)
        {
            var ghostEntry = v.TooFarFromAny.FirstOrDefault(t => ReferenceEquals(t.model, ghostModel));
            if (ghostEntry.model != null)
                DrawDimensionLine(dl, ghostEntry.pos, ghostEntry.model.BaseRadiusInches,
                                      ghostEntry.nearestPos, ghostEntry.nearest.BaseRadiusInches);
        }

        if (v.FarthestPair.HasValue)
        {
            var f = v.FarthestPair.Value;
            DrawBoundingCircle(dl, f.pa, f.a.BaseRadiusInches, f.pb, f.b.BaseRadiusInches);
        }
    }

    private void DrawDimensionLine(ImDrawListPtr dl, Position a, float ra, Position b, float rb)
    {
        float dx = b.x - a.x;
        float dz = b.z - a.z;
        float len = MathF.Sqrt(dx * dx + dz * dz);
        if (len < 0.0001f) return;
        float nx = dx / len, nz = dz / len;
        float aEdgeX = a.x + nx * ra,  aEdgeZ = a.z + nz * ra;
        float bEdgeX = b.x - nx * rb,  bEdgeZ = b.z - nz * rb;

        var (ax, ay) = InchesToPixel(aEdgeX, aEdgeZ);
        var (bx, by) = InchesToPixel(bEdgeX, bEdgeZ);
        AddDottedLine(dl, new Vector2(ax, ay), new Vector2(bx, by), CohesionLineCol, 1f);
    }

    private void DrawBoundingCircle(ImDrawListPtr dl, Position a, float ra, Position b, float rb)
    {
        float dx = b.x - a.x;
        float dz = b.z - a.z;
        float centerDist = MathF.Sqrt(dx * dx + dz * dz);
        if (centerDist < 0.0001f) return;
        float nx = dx / centerDist, nz = dz / centerDist;
        float aFarX = a.x - nx * ra,  aFarZ = a.z - nz * ra;
        float bFarX = b.x + nx * rb,  bFarZ = b.z + nz * rb;
        float midX = (aFarX + bFarX) * 0.5f;
        float midZ = (aFarZ + bFarZ) * 0.5f;
        float radiusInches = (centerDist + ra + rb) * 0.5f;

        var (cx, cy) = InchesToPixel(midX, midZ);
        AddDottedCircle(dl, new Vector2(cx, cy), radiusInches * _scale, CohesionLineCol, 1f);
    }

    private static void AddDottedLine(ImDrawListPtr dl, Vector2 a, Vector2 b, uint color, float thickness,
        float dashLen = 5f, float gapLen = 4f)
    {
        Vector2 d = b - a;
        float len = MathF.Sqrt(d.X * d.X + d.Y * d.Y);
        if (len < 0.01f) return;
        Vector2 dir = d / len;
        float t = 0f;
        while (t < len)
        {
            float t2 = MathF.Min(t + dashLen, len);
            dl.AddLine(a + dir * t, a + dir * t2, color, thickness);
            t = t2 + gapLen;
        }
    }

    private static void AddDottedCircle(ImDrawListPtr dl, Vector2 center, float radius, uint color, float thickness)
    {
        if (radius < 1f) return;
        float circ = MathF.PI * 2f * radius;
        int dashes = Math.Clamp((int)MathF.Round(circ / 10f), 12, 200);
        float step = MathF.PI * 2f / (dashes * 2);
        for (int i = 0; i < dashes; i++)
        {
            float a0 = step * (i * 2);
            float a1 = step * (i * 2 + 1);
            Vector2 p0 = new(center.X + MathF.Cos(a0) * radius, center.Y + MathF.Sin(a0) * radius);
            Vector2 p1 = new(center.X + MathF.Cos(a1) * radius, center.Y + MathF.Sin(a1) * radius);
            dl.AddLine(p0, p1, color, thickness);
        }
    }

    private void Complete(TaskCompletionSource<List<ModelMoveEntry>> tcs, List<ModelMoveEntry> entries)
    {
        lock (_lock)
        {
            _request       = null;
            _tcs           = null;
            _pathTemplate  = null;
            _selectedModel = null;
        }
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

    // Keep the model's full base inside the table.
    private static (float x, float z) ClampToTable(float x, float z, float baseRadius)
    {
        float w = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES;
        float h = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
        return (Math.Clamp(x, baseRadius, w - baseRadius),
                Math.Clamp(z, baseRadius, h - baseRadius));
    }
}
