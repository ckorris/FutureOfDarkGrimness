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

    // Layout — main-thread only
    private float _scale  = 10f;
    private float _tableH = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
    private int   _originX, _originY;

    // Request state
    private DefineMovementPathRequest? _request;
    private TaskCompletionSource<List<ModelMoveEntry>>? _tcs;

    // Pathing state — main-thread only after Resolve assigns it
    private PathTemplate? _pathTemplate;
    private IModel? _selectedModel;
    private bool _stayInAdvance; // toggle — off by default, reset each Resolve
    private bool _showTargeting = true; // toggle — on by default, persists across Resolve calls (covers both ranged + melee)

    // Move bands: Advance (green, can shoot after), Rush (yellow, can't shoot), Charge (orange,
    // only legal if at least one model ends in melee). The Charge band only exists for units
    // whose Charge distance is greater than their Rush distance — when equal, there are only
    // two bands and the visual matches the pre-decoupling behavior.
    private enum MoveBand { Advance, Rush, Charge }

    private static readonly uint AdvanceColor    = ImGui.ColorConvertFloat4ToU32(new Vector4(0.25f, 0.95f, 0.25f, 0.95f));
    private static readonly uint RushColor       = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.92f, 0.20f, 0.95f));
    private static readonly uint ChargeColor     = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.55f, 0.10f, 0.95f));
    private static readonly uint AdvanceRingCol  = ImGui.ColorConvertFloat4ToU32(new Vector4(0.25f, 0.95f, 0.25f, 0.55f));
    private static readonly uint RushRingCol     = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.92f, 0.20f, 0.55f));
    private static readonly uint ChargeRingCol   = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.55f, 0.10f, 0.55f));
    private static readonly uint AdvanceFill     = ImGui.ColorConvertFloat4ToU32(new Vector4(0.25f, 0.95f, 0.25f, 0.40f));
    private static readonly uint RushFill        = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.92f, 0.20f, 0.40f));
    private static readonly uint ChargeFill      = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.55f, 0.10f, 0.40f));
    private static readonly uint SelectionOutline = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.95f));
    private static readonly uint ModelOutline    = ImGui.ColorConvertFloat4ToU32(new Vector4(0.7f, 0.7f, 0.7f, 0.7f));
    private static readonly uint GhostOutline    = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.85f));
    private static readonly uint FinalGhostCol   = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.25f));
    private static readonly uint CohesionLineCol = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.55f, 0.55f, 0.90f));
    private static readonly uint OverlapFill     = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.25f, 0.25f, 0.55f));
    private static readonly uint ChargeTextCol   = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.92f, 0.30f, 1f));
    private static readonly uint ChargeLineCol   = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.85f, 0.20f, 0.95f));

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
        var template = new PathTemplate(request.UnitDataBinding, request.MaxAdvanceDistance, request.MaxDistanceInches);
        var first = request.UnitDataBinding.GetValue().ModelBindings
            .Select(mb => mb.GetValue() as IModel)
            .FirstOrDefault(m => m != null && m.GetIsAlive());

        lock (_lock)
        {
            _tcs           = tcs;
            _request       = request;
            _pathTemplate  = template;
            _selectedModel = first;
            _stayInAdvance = false;
        }
        return tcs.Task;
    }

    public void Draw(int screenW, int screenH)
    {
        DefineMovementPathRequest? request;
        TaskCompletionSource<List<ModelMoveEntry>>? tcs;
        PathTemplate? pt;
        lock (_lock) { request = _request; tcs = _tcs; pt = _pathTemplate; }
        if (request == null || tcs == null || pt == null) return;

        var io       = ImGui.GetIO();
        var dl       = ImGui.GetBackgroundDrawList();
        var terrain  = _tableState.Terrain.Objects.ToList();
        var paths    = pt.CurrentPaths;

        float maxAdvance = request.MaxAdvanceDistance;
        float maxRush    = request.MaxRushDistance;
        float maxCharge  = request.MaxDistanceInches;
        bool  hasChargeBand = maxCharge > maxRush + 0.0001f;

        // 1) Draw each model's start circle + committed path lines + final ghost circle
        foreach (var kvp in paths)
        {
            var model = kvp.Key;
            var pathPoints = kvp.Value;
            var start = model.Position;
            var (sx, sy) = InchesToPixel(start.x, start.z);
            float r = model.BaseRadiusInches * _scale;

            // Start circle (real model position)
            uint outline = ReferenceEquals(model, _selectedModel) ? SelectionOutline : ModelOutline;
            float thick  = ReferenceEquals(model, _selectedModel) ? 2.5f : 1.5f;
            dl.AddCircle(new Vector2(sx, sy), r, outline, 32, thick);

            // Path lines
            if (pathPoints.Count > 0)
            {
                Position prev = start;
                float cum = 0f;
                for (int i = 0; i < pathPoints.Count; i++)
                {
                    var cur = pathPoints[i];
                    uint col = LineColorFor(ClassifyBand(cum, maxAdvance, maxRush, hasChargeBand));
                    var (px, py) = InchesToPixel(prev.x, prev.z);
                    var (cx, cy) = InchesToPixel(cur.x, cur.z);
                    dl.AddLine(new Vector2(px, py), new Vector2(cx, cy), col, 2f);
                    cum += Position.GetDistance3D(prev, cur);
                    prev = cur;
                }

                // Final position ghost circle
                var last = pathPoints[^1];
                var (lx, ly) = InchesToPixel(last.x, last.z);
                dl.AddCircleFilled(new Vector2(lx, ly), r, FinalGhostCol);
                dl.AddCircle(new Vector2(lx, ly), r, outline, 32, thick);
            }
        }

        // 2) Range rings around selected model's last waypoint
        if (_selectedModel != null)
            DrawRangeRings(dl, pt, maxAdvance, maxRush, maxCharge, hasChargeBand);

        // 3) Ghost following mouse for selected model (clamped)
        bool overTable = IsOverTable(io.MousePos.X, io.MousePos.Y);
        bool wantInput = !io.WantCaptureMouse && !io.WantCaptureKeyboard;
        Position? ghostPos = null;
        bool ghostIsRush = false;
        bool ghostOverlaps = false;
        float ghostExtraDist = 0f;

        bool advanceOnly = _stayInAdvance || ImGui.IsKeyDown(ImGuiKey.LeftShift) || ImGui.IsKeyDown(ImGuiKey.RightShift);

        if (_selectedModel != null && overTable && !io.WantCaptureMouse)
        {
            var anchor = pt.GetModelLastPathPosition(_selectedModel);
            float totalSoFar = pt.GetTotalDistanceMoved(_selectedModel);
            float cap        = advanceOnly ? maxAdvance : maxCharge;
            float remaining  = cap - totalSoFar;
            var (mx, mz) = PixelToInches(io.MousePos.X, io.MousePos.Y);
            float dx = mx - anchor.x;
            float dz = mz - anchor.z;
            float dist = MathF.Sqrt(dx * dx + dz * dz);

            float allowed;
            if (remaining <= 0.001f)
            {
                allowed = 0f;
            }
            else if (dist <= remaining)
            {
                allowed = dist;
            }
            else
            {
                allowed = MathF.Max(0f, remaining - 0.001f); // small margin against float drift
            }

            float nx, nz;
            if (dist < 0.0001f) { nx = anchor.x; nz = anchor.z; }
            else                { nx = anchor.x + dx / dist * allowed; nz = anchor.z + dz / dist * allowed; }
            ghostPos = new Position(nx, nz);

            float cumWithGhost = totalSoFar + allowed;
            MoveBand ghostBand = ClassifyBand(cumWithGhost, maxAdvance, maxRush, hasChargeBand);
            ghostIsRush    = ghostBand != MoveBand.Advance;
            ghostExtraDist = allowed;
            ghostOverlaps  = WouldOverlapAnyModel(ghostPos.Value, _selectedModel, request, paths);

            // Preview line from anchor to ghost
            var (ax, ay) = InchesToPixel(anchor.x, anchor.z);
            var (gx, gy) = InchesToPixel(nx, nz);
            dl.AddLine(new Vector2(ax, ay), new Vector2(gx, gy), LineColorFor(ghostBand), 2f);

            // Ghost base circle
            float r = _selectedModel.BaseRadiusInches * _scale;
            uint fill = ghostOverlaps ? OverlapFill : FillColorFor(ghostBand);
            dl.AddCircleFilled(new Vector2(gx, gy), r, fill);
            dl.AddCircle(new Vector2(gx, gy), r, GhostOutline, 32, 1.5f);

            // Cohesion warnings: indicators reflect would-be positions if user committed here
            var finalsWithGhost = BuildFinalPositions(paths, _selectedModel, ghostPos);
            DrawCohesionIndicators(dl, finalsWithGhost, _selectedModel);
        }

        // 3b) Targeting overlay (toggle, on by default) — combines ranged + melee
        if (_showTargeting)
            DrawTargeting(dl, screenW, request, pt, paths, ghostPos, ghostExtraDist);

        // 4) Mouse / keyboard input
        if (overTable && !io.WantCaptureMouse)
        {
            // Left-click selects a model whose start circle is hit
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
                    float rr = model.BaseRadiusInches * model.BaseRadiusInches;
                    if (d2 <= rr && d2 < bestDist) { hit = model; bestDist = d2; }
                }
                if (hit != null) _selectedModel = hit;
            }

            // Right-click adds a waypoint at clamped ghost position (blocked if it would overlap another model)
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Right) && _selectedModel != null && ghostPos.HasValue && !ghostOverlaps)
            {
                float totalSoFar = pt.GetTotalDistanceMoved(_selectedModel);
                float cap = advanceOnly ? maxAdvance : maxCharge;
                if (cap - totalSoFar > 0.001f)
                    pt.AddStep(_selectedModel, ghostPos.Value);
            }
        }

        // Backspace removes last waypoint of selected model
        if (wantInput && _selectedModel != null && ImGui.IsKeyPressed(ImGuiKey.Backspace))
        {
            if (paths.TryGetValue(_selectedModel, out var list) && list.Count > 0)
                pt.RemoveLastStep(_selectedModel);
        }

        // Spacebar cycles to next model in the unit's list
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

    private void DrawInfoPanel(int screenW, DefineMovementPathRequest request, PathTemplate pt,
        TaskCompletionSource<List<ModelMoveEntry>> tcs, List<ITerrain> terrain)
    {
        float panelW = MathF.Min(screenW * 0.5f, 560f);
        ImGui.SetNextWindowPos(new Vector2((screenW - panelW) * 0.5f, 16f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSizeConstraints(new Vector2(panelW, 0f), new Vector2(panelW, float.MaxValue));
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.10f, 0.10f, 0.15f, 0.92f));
        ImGui.Begin("##MovementPanel",
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysAutoResize);
        ImGui.PopStyleColor();

        string unitName = request.UnitDataBinding.GetValue().Name;
        ImGui.TextUnformatted($"Move: {unitName}");
        ImGui.SameLine();
        ImGui.TextDisabled("  " + FormatDistanceSummary(request));

        ImGui.Spacing();
        if (_selectedModel != null)
        {
            float dist = pt.GetTotalDistanceMoved(_selectedModel);
            bool inRush = dist + 0.0001f >= request.MaxAdvanceDistance;
            var color = inRush ? new Vector4(1.00f, 0.55f, 0.10f, 1f) : new Vector4(0.25f, 0.95f, 0.25f, 1f);
            ImGui.PushStyleColor(ImGuiCol.Text, color);
            ImGui.TextUnformatted($"Selected model: {dist:F2}\" / {FormatInches(request.MaxDistanceInches)}\"  ({(inRush ? "RUSH - cannot shoot" : "advance - may shoot")})");
            ImGui.PopStyleColor();
        }
        else
        {
            ImGui.TextDisabled("No model selected. Left-click a model on the table.");
        }

        ImGui.TextDisabled("L-click: select   R-click: waypoint");
        ImGui.TextDisabled("Space: next model   Backspace: undo");

        ImGui.Checkbox("Stay within Advance (hold Shift to force)", ref _stayInAdvance);
        ImGui.Checkbox("Show targeting", ref _showTargeting);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Show ranged weapons in range AND with line of sight to each enemy unit (green), how many of your models can charge it (yellow), fire lines from the selected model, and a charge line when the ghost is within melee range. Fire lines turn yellow and dashed when the shot passes through cover (+1 to the target's defense roll). When no enemy model is visible to a weapon, a red blocked-stub with an X marks where the shot would hit a wall. Ranged info hides if the unit has moved too far to shoot.");

        ImGui.Spacing();
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        float pad     = ImGui.GetStyle().WindowPadding.X * 2;
        float btnW    = (panelW - pad - spacing * 3) / 4f;

        var results = pt.GetResultsAsList();
        var enemyPositions = GetEnemyPositionsForRequest(request);
        bool engineValid = MovementUtilities.ValidatePaths(results,
            request.MaxRushDistance, request.MaxDistanceInches,
            enemyPositions, terrain, out var engineErrors);
        var finals = BuildFinalPositions(pt.CurrentPaths, null, null);
        var cohesion = CheckCohesion(finals);

        var issues = new List<string>();
        if (!engineValid)
        {
            // Skip the engine's coherency errors — its check is broken; we report our own below.
            foreach (var e in engineErrors)
            {
                if (e.ErrorReasonType == EErrorReasonType.TooFarFromAnyUnitModel ||
                    e.ErrorReasonType == EErrorReasonType.TooFarFromAllUnitModels) continue;
                issues.Add(MovementUtilities.ErrorReasonToString(e.ErrorReasonType));
            }
        }
        foreach (var t in cohesion.TooFarFromAny)
            issues.Add($"Cohesion: model is {t.dist:F2}\" from nearest other (max {FormatInches(GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES)}\")");
        if (cohesion.FarthestPair.HasValue)
            issues.Add($"Cohesion: two models would be {cohesion.FarthestPair.Value.dist:F2}\" apart (max {FormatInches(GameWideConstants.MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES)}\")");

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
        bool skipPressed = ImGui.Button("Skip all", new Vector2(btnW, 28f));
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Don't move the unit. Every model stays in place.");
        if (skipPressed)
        {
            pt.ClearAllSteps();
            Complete(tcs, pt.GetResultsAsList());
            ImGui.End();
            return;
        }

        ImGui.SameLine();
        bool autoPressed = ImGui.Button("Auto-advance", new Vector2(btnW, 28f));
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Move the whole unit toward the nearest enemy, stopping ~1\" short. Stays within Advance distance so the unit can still shoot.");
        if (autoPressed)
        {
            Complete(tcs, AutoAdvance(request));
            ImGui.End();
            return;
        }

        ImGui.End();
    }

    /// <summary>
    /// Draws up to three remaining-range rings around the selected model's current path
    /// endpoint. The Rush ring is suppressed when Rush equals Charge — keeping the visual
    /// identical to before the Rush/Charge split.
    /// </summary>
    private void DrawRangeRings(ImDrawListPtr dl, PathTemplate pt,
        float maxAdvance, float maxRush, float maxCharge, bool hasChargeBand)
    {
        if (_selectedModel == null) return;

        float totalSoFar = pt.GetTotalDistanceMoved(_selectedModel);
        var anchor = pt.GetModelLastPathPosition(_selectedModel);
        var (ax, ay) = InchesToPixel(anchor.x, anchor.z);
        var center = new Vector2(ax, ay);

        DrawRingIfRemaining(dl, center, maxAdvance - totalSoFar,            AdvanceRingCol);
        if (hasChargeBand)
            DrawRingIfRemaining(dl, center, maxRush - totalSoFar,           RushRingCol);
        DrawRingIfRemaining(dl, center, maxCharge - totalSoFar,             ChargeRingCol);
    }

    private void DrawRingIfRemaining(ImDrawListPtr dl, Vector2 center, float remainingInches, uint color)
    {
        if (remainingInches > 0.01f)
            dl.AddCircle(center, remainingInches * _scale, color, 64, 1.5f);
    }

    /// <summary>
    /// Classifies a cumulative move distance into a band. When Charge equals Rush, the
    /// Rush band is suppressed and post-Advance distance maps directly to Charge — keeping
    /// the visual identical to before the Rush/Charge split.
    /// </summary>
    private static MoveBand ClassifyBand(float cumDistance, float maxAdvance, float maxRush, bool hasChargeBand)
    {
        const float Epsilon = 0.0001f;
        if (cumDistance + Epsilon < maxAdvance) return MoveBand.Advance;
        if (hasChargeBand && cumDistance + Epsilon < maxRush) return MoveBand.Rush;
        return MoveBand.Charge;
    }

    private static uint LineColorFor(MoveBand band) => band switch
    {
        MoveBand.Advance => AdvanceColor,
        MoveBand.Rush    => RushColor,
        _                => ChargeColor,
    };

    private static uint FillColorFor(MoveBand band) => band switch
    {
        MoveBand.Advance => AdvanceFill,
        MoveBand.Rush    => RushFill,
        _                => ChargeFill,
    };

    private static string FormatDistanceSummary(DefineMovementPathRequest request)
    {
        string s = $"advance up to {FormatInches(request.MaxAdvanceDistance)}\"   rush up to {FormatInches(request.MaxRushDistance)}\"";
        if (request.MaxDistanceInches > request.MaxRushDistance + 0.0001f)
            s += $"   charge up to {FormatInches(request.MaxDistanceInches)}\"";
        return s;
    }

    private List<Position> GetEnemyPositionsForRequest(DefineMovementPathRequest request)
    {
        var positions = new List<Position>();
        foreach (var u in _tableState.Units.Objects)
        {
            if (u.PlayerID == request.TargetPlayerID) continue;
            foreach (var m in u.Models)
                if (m.GetIsAlive()) positions.Add(m.Position);
        }
        return positions;
    }

    private bool WouldOverlapAnyModel(Position ghostPos, IModel ghostModel,
        DefineMovementPathRequest request,
        IReadOnlyDictionary<IModel, IReadOnlyList<Position>> paths)
    {
        var ownUnit = request.UnitDataBinding.GetValue();
        float gr = ghostModel.BaseRadiusInches;
        foreach (var unit in _tableState.Units.Objects)
        {
            bool isOwnUnit = unit == ownUnit;
            foreach (var m in unit.Models)
            {
                if (!m.GetIsAlive()) continue;
                if (ReferenceEquals(m, ghostModel)) continue;

                // Same-unit models follow their planned path's end; others stay where they are.
                Position p = m.Position;
                if (isOwnUnit && paths.TryGetValue(m, out var path) && path.Count > 0)
                    p = path[^1];

                float dx = ghostPos.x - p.x;
                float dz = ghostPos.z - p.z;
                float horiz = MathF.Sqrt(dx * dx + dz * dz);
                if (horiz + 0.001f < gr + m.BaseRadiusInches) return true;
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

    private readonly struct CohesionViolations
    {
        public readonly List<(IModel model, IModel nearest, Position pos, Position nearestPos, float dist)> TooFarFromAny;
        public readonly (IModel a, IModel b, Position pa, Position pb, float dist)? FarthestPair;

        public CohesionViolations(
            List<(IModel, IModel, Position, Position, float)> tooFar,
            (IModel, IModel, Position, Position, float)? farthest)
        { TooFarFromAny = tooFar; FarthestPair = farthest; }

        public bool Any => TooFarFromAny.Count > 0 || FarthestPair.HasValue;
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
                float d = DistanceUtilities.GetBaseToBaseDistanceInches_3D(
                    finals[i].pos, finals[j].pos,
                    finals[i].model.BaseRadiusInches, finals[j].model.BaseRadiusInches);
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

        // Rule A: line from ghost edge to its nearest neighbor edge, only if ghost itself is the violator
        if (ghostModel != null)
        {
            var ghostEntry = v.TooFarFromAny.FirstOrDefault(t => ReferenceEquals(t.model, ghostModel));
            if (ghostEntry.model != null)
                DrawDimensionLine(dl, ghostEntry.pos, ghostEntry.model.BaseRadiusInches,
                                      ghostEntry.nearestPos, ghostEntry.nearest.BaseRadiusInches);
        }

        // Rule B: bounding circle around the two farthest models
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
        // Edge-of-base endpoints in inches
        float aEdgeX = a.x + nx * ra,  aEdgeZ = a.z + nz * ra;
        float bEdgeX = b.x - nx * rb,  bEdgeZ = b.z - nz * rb;

        var (ax, ay) = InchesToPixel(aEdgeX, aEdgeZ);
        var (bx, by) = InchesToPixel(bEdgeX, bEdgeZ);
        AddDottedLine(dl, new Vector2(ax, ay), new Vector2(bx, by), CohesionLineCol, 1f);

        // Perpendicular serif ticks at each endpoint
        // Pixel-space perpendicular (y axis is flipped vs z but symmetric so perpendicular formula holds)
        float lpx = bx - ax, lpy = by - ay;
        float lplen = MathF.Sqrt(lpx * lpx + lpy * lpy);
        if (lplen < 0.0001f) return;
        float px = -lpy / lplen, py = lpx / lplen;
        const float tick = 4f;
        dl.AddLine(new Vector2(ax - px * tick, ay - py * tick), new Vector2(ax + px * tick, ay + py * tick), CohesionLineCol, 1f);
        dl.AddLine(new Vector2(bx - px * tick, by - py * tick), new Vector2(bx + px * tick, by + py * tick), CohesionLineCol, 1f);
    }

    private void DrawTargeting(ImDrawListPtr dl, int screenW,
        DefineMovementPathRequest request,
        PathTemplate pt,
        IReadOnlyDictionary<IModel, IReadOnlyList<Position>> paths,
        Position? ghostPos, float ghostExtraDist)
    {
        // Shooting aggregate is driven by committed paths so it doesn't flicker on rush-boundary hover.
        // Per-line shooting from the selected model + the charge overlay are ghost-aware.
        bool canShootCommitted = true;
        foreach (var m in paths.Keys)
            if (pt.GetTotalDistanceMoved(m) > request.MaxAdvanceDistance + 0.0001f) { canShootCommitted = false; break; }

        bool canShootWithGhost = canShootCommitted;
        if (canShootWithGhost && _selectedModel != null && ghostPos.HasValue)
        {
            float selTotal = pt.GetTotalDistanceMoved(_selectedModel) + ghostExtraDist;
            if (selTotal > request.MaxAdvanceDistance + 0.0001f) canShootWithGhost = false;
        }

        // Committed positions per own model (shooting aggregate uses these as-is).
        var committed = new Dictionary<IModel, Position>(paths.Count);
        foreach (var kvp in paths)
            committed[kvp.Key] = kvp.Value.Count > 0 ? kvp.Value[^1] : kvp.Key.Position;

        // Ghost-aware positions per own model (charge aggregate uses these — selected model uses live ghost).
        var ghostAware = new Dictionary<IModel, Position>(committed);
        if (_selectedModel != null && ghostPos.HasValue)
            ghostAware[_selectedModel] = ghostPos.Value;

        var ourPlayerID = request.TargetPlayerID;
        uint enemyTextCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.60f, 1.00f, 0.60f, 1f));
        uint lineCol      = ImGui.ColorConvertFloat4ToU32(new Vector4(0.30f, 1.00f, 0.30f, 0.85f));
        uint midTextCol   = ImGui.ColorConvertFloat4ToU32(new Vector4(0.60f, 1.00f, 0.60f, 1f));
        uint blockedLineCol = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.35f, 0.35f, 0.85f));
        uint coverLineCol   = ImGui.ColorConvertFloat4ToU32(new Vector4(0.85f, 0.80f, 0.30f, 0.85f));
        uint coverTextCol   = ImGui.ColorConvertFloat4ToU32(new Vector4(0.95f, 0.85f, 0.35f, 1f));
        float lineH = ImGui.GetTextLineHeight();
        const float meleeRange = GameWideConstants.MELEE_RANGE_INCHES_HORIZONTAL;

        // Per-frame blocker snapshots: each enemy unit gets its own terrain+model-blocker list
        // because BuildModelBlockers excludes the defender unit (so it won't self-block).
        // Cost: one BuildModelBlockers call per enemy unit per frame. Cheap; the resulting
        // lists are walked many times during LoS checks below.
        IUnit ourUnit = request.UnitDataBinding.GetValue();
        var terrainSnapshot = _tableState.Terrain.Objects.ToList();
        var blockersByEnemyUnit = new Dictionary<IUnit, List<ITerrain>>(ReferenceEqualityComparer.Instance);
        foreach (IUnit enemyUnit in _tableState.Units.Objects)
        {
            if (enemyUnit.PlayerID == ourPlayerID) continue;
            var modelBlockers = LineOfSightUtilities.BuildModelBlockers(_tableState, ourUnit, enemyUnit);
            var combined = new List<ITerrain>(terrainSnapshot.Count + modelBlockers.Count);
            combined.AddRange(terrainSnapshot);
            combined.AddRange(modelBlockers);
            blockersByEnemyUnit[enemyUnit] = combined;
        }

        // LoS cache keyed by (attacker model, defender model) — values are committed-position
        // results only. The selected model's ghost-position LoS is computed fresh on use
        // because the ghost moves with the mouse and would invalidate cached values.
        var losCache = new Dictionary<(IModel, IModel), bool>();
        bool LoSCommitted(IModel attackerModel, Position attackerPos, IModel defenderModel, IUnit defenderUnit)
        {
            var key = (attackerModel, defenderModel);
            if (losCache.TryGetValue(key, out bool v)) return v;
            v = LineOfSightUtilities.HasLineOfSight(attackerPos, defenderModel.Position, blockersByEnemyUnit[defenderUnit]);
            losCache[key] = v;
            return v;
        }

        // 1) Per-enemy-unit aggregate text: shooting weapon counts (green) + charger count (yellow), combined.
        foreach (IUnit enemyUnit in _tableState.Units.Objects)
        {
            if (enemyUnit.PlayerID == ourPlayerID) continue;
            var aliveEnemies = enemyUnit.Models.Where(em => em.GetIsAlive()).ToList();
            if (aliveEnemies.Count == 0) continue;

            // Shooting weapon counts (committed-only, only when our unit can still shoot).
            var weaponCounts = new Dictionary<string, int>();
            if (canShootCommitted)
            {
                foreach (var kvp in committed)
                {
                    IModel ourModel = kvp.Key;
                    Position from = kvp.Value;
                    foreach (var w in ourModel.Weapons)
                    {
                        if (!w.IsRanged()) continue;
                        bool inRange = false;
                        foreach (var em in aliveEnemies)
                        {
                            float b2b = DistanceUtilities.GetBaseToBaseDistanceInches_2D(
                                from, em.Position, ourModel.BaseRadiusInches, em.BaseRadiusInches);
                            if (b2b > w.RangeInches) continue;
                            if (!LoSCommitted(ourModel, from, em, enemyUnit)) continue;
                            inRange = true; break;
                        }
                        if (inRange)
                        {
                            weaponCounts.TryGetValue(w.Name, out int c);
                            weaponCounts[w.Name] = c + 1;
                        }
                    }
                }
            }

            // Charger count (ghost-aware — selected model uses live ghost position).
            int chargers = 0;
            foreach (var kvp in ghostAware)
            {
                IModel ourModel = kvp.Key;
                Position from = kvp.Value;
                foreach (var em in aliveEnemies)
                {
                    float b2b = DistanceUtilities.GetBaseToBaseDistanceInches_2D(
                        from, em.Position, ourModel.BaseRadiusInches, em.BaseRadiusInches);
                    if (b2b <= meleeRange) { chargers++; break; }
                }
            }

            if (weaponCounts.Count == 0 && chargers == 0) continue;

            // Build combined display lines (charge line first, then shooting weapons).
            var lines = new List<(string text, uint color)>();
            if (chargers > 0)
                lines.Add(($"{chargers} can charge", ChargeTextCol));
            foreach (var kv in weaponCounts.OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key))
                lines.Add(($"{kv.Value}x {kv.Key}", enemyTextCol));

            float blockH = lines.Count * lineH;
            float blockW = 0f;
            var sizes = new Vector2[lines.Count];
            for (int i = 0; i < lines.Count; i++) { sizes[i] = ImGui.CalcTextSize(lines[i].text); if (sizes[i].X > blockW) blockW = sizes[i].X; }

            float ecz = aliveEnemies.Average(em => em.Position.z);
            var (_, cpy) = InchesToPixel(0, ecz);

            // Unit's horizontal screen-pixel extent (base edges included).
            float minPx = float.MaxValue, maxPx = float.MinValue;
            foreach (var em in aliveEnemies)
            {
                var (epx, _) = InchesToPixel(em.Position.x, em.Position.z);
                float r = em.BaseRadiusInches * _scale;
                if (epx - r < minPx) minPx = epx - r;
                if (epx + r > maxPx) maxPx = epx + r;
            }

            const float margin = 12f;
            float xLeftAnchor  = maxPx + margin;
            float xRightAnchor = minPx - margin - blockW;
            float xAnchor = xLeftAnchor + blockW <= screenW - 4f ? xLeftAnchor : xRightAnchor;
            float yTop = cpy - blockH * 0.5f;
            for (int i = 0; i < lines.Count; i++)
                dl.AddText(new Vector2(xAnchor, yTop + i * lineH), lines[i].color, lines[i].text);
        }

        // Charge line: short edge-to-edge line from the selected model's ghost to its nearest chargeable enemy model.
        if (_selectedModel != null && ghostPos.HasValue)
        {
            IModel? nearestChargeTarget = null;
            float nearestB2B = float.MaxValue;
            foreach (IUnit enemyUnit in _tableState.Units.Objects)
            {
                if (enemyUnit.PlayerID == ourPlayerID) continue;
                foreach (var em in enemyUnit.Models)
                {
                    if (!em.GetIsAlive()) continue;
                    float b2b = DistanceUtilities.GetBaseToBaseDistanceInches_2D(
                        ghostPos.Value, em.Position, _selectedModel.BaseRadiusInches, em.BaseRadiusInches);
                    if (b2b > meleeRange) continue;
                    if (b2b < nearestB2B) { nearestB2B = b2b; nearestChargeTarget = em; }
                }
            }
            if (nearestChargeTarget != null)
            {
                float ax = ghostPos.Value.x, az = ghostPos.Value.z;
                float bx = nearestChargeTarget.Position.x, bz = nearestChargeTarget.Position.z;
                float dx = bx - ax, dz = bz - az;
                float centerDist = MathF.Sqrt(dx * dx + dz * dz);
                if (centerDist > 0.0001f)
                {
                    float nx = dx / centerDist, nz = dz / centerDist;
                    float aEdgeX = ax + nx * _selectedModel.BaseRadiusInches;
                    float aEdgeZ = az + nz * _selectedModel.BaseRadiusInches;
                    float bEdgeX = bx - nx * nearestChargeTarget.BaseRadiusInches;
                    float bEdgeZ = bz - nz * nearestChargeTarget.BaseRadiusInches;
                    var (px0, py0) = InchesToPixel(aEdgeX, aEdgeZ);
                    var (px1, py1) = InchesToPixel(bEdgeX, bEdgeZ);
                    dl.AddLine(new Vector2(px0, py0), new Vector2(px1, py1), ChargeLineCol, 2f);
                }
            }
        }

        // 2) Per-selected-model fire lines + per-line weapon labels (ghost-aware).
        if (!canShootWithGhost) return;
        if (_selectedModel == null) return;
        var selRanged = _selectedModel.Weapons.Where(w => w.IsRanged()).ToList();
        if (selRanged.Count == 0) return;
        Position selPos = ghostPos ?? committed[_selectedModel];

        // For each weapon, prefer the nearest in-range enemy model with line of sight (clear shot).
        // If no in-range model has LoS, fall back to the nearest in-range model overall as a
        // "blocked" target — we'll render that with a red stub + X at the block point so the
        // player can see why the shot doesn't land.
        // Group resulting (target enemy model) -> list of weapons hitting it, partitioned by
        // clear vs blocked so the renderer can style them differently.
        var byTarget = new Dictionary<IModel, List<IWeapon>>();
        var coverTargets = new HashSet<IModel>(ReferenceEqualityComparer.Instance);
        var blockedByTarget = new Dictionary<IModel, (List<IWeapon> weapons, IUnit enemyUnit)>();
        foreach (IUnit enemyUnit in _tableState.Units.Objects)
        {
            if (enemyUnit.PlayerID == ourPlayerID) continue;
            var aliveEnemies = enemyUnit.Models.Where(em => em.GetIsAlive()).ToList();
            if (aliveEnemies.Count == 0) continue;

            var unitBlockers = blockersByEnemyUnit[enemyUnit];
            foreach (var w in selRanged)
            {
                IModel? nearestClear   = null; float nearestClearB2B   = float.MaxValue;
                IModel? nearestInRange = null; float nearestInRangeB2B = float.MaxValue;
                ESightLineEffect nearestClearEffect = ESightLineEffect.Clear;
                foreach (var em in aliveEnemies)
                {
                    float b2b = DistanceUtilities.GetBaseToBaseDistanceInches_2D(
                        selPos, em.Position, _selectedModel.BaseRadiusInches, em.BaseRadiusInches);
                    if (b2b > w.RangeInches) continue;
                    if (b2b < nearestInRangeB2B) { nearestInRangeB2B = b2b; nearestInRange = em; }
                    var effect = LineOfSightUtilities.EvaluateSightLine(selPos, em.Position, unitBlockers);
                    if (effect != ESightLineEffect.Blocking)
                    {
                        if (b2b < nearestClearB2B) { nearestClearB2B = b2b; nearestClear = em; nearestClearEffect = effect; }
                    }
                }
                if (nearestClear != null)
                {
                    if (!byTarget.TryGetValue(nearestClear, out var list)) byTarget[nearestClear] = list = new List<IWeapon>();
                    list.Add(w);
                    if (nearestClearEffect == ESightLineEffect.Cover) coverTargets.Add(nearestClear);
                }
                else if (nearestInRange != null)
                {
                    if (!blockedByTarget.TryGetValue(nearestInRange, out var entry))
                        blockedByTarget[nearestInRange] = entry = (new List<IWeapon>(), enemyUnit);
                    entry.weapons.Add(w);
                }
            }
        }

        const float stagger = 6f;
        foreach (var kvp in byTarget)
        {
            var target = kvp.Key;
            var weapons = kvp.Value.OrderBy(w => w.Name).ToList();
            int n = weapons.Count;
            bool inCover = coverTargets.Contains(target);
            uint thisLineCol = inCover ? coverLineCol : lineCol;
            uint thisTextCol = inCover ? coverTextCol : midTextCol;

            var (ax, ay) = InchesToPixel(selPos.x, selPos.z);
            var (bx, by) = InchesToPixel(target.Position.x, target.Position.z);
            float dx = bx - ax, dy = by - ay;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 0.001f) continue;
            float perpX = -dy / len, perpY = dx / len;

            for (int i = 0; i < n; i++)
            {
                float offset = (i - (n - 1) * 0.5f) * stagger;
                var sa = new Vector2(ax + perpX * offset, ay + perpY * offset);
                var sb = new Vector2(bx + perpX * offset, by + perpY * offset);
                if (inCover) AddDottedLine(dl, sa, sb, thisLineCol, 1.5f);
                else         dl.AddLine(sa, sb, thisLineCol, 1.5f);
            }

            // Weapon name labels stacked beside the line midpoint (screen-right by default,
            // flipped to screen-left if right side would clip the window edge). When the shot
            // passes through cover terrain, append "(cover)" to each weapon name so the player
            // sees the +1 defense modifier inline.
            float mx = (ax + bx) * 0.5f, my = (ay + by) * 0.5f;
            float blockH = n * lineH;
            float blockW = 0f;
            string[] labels = new string[n];
            var sizes = new Vector2[n];
            for (int i = 0; i < n; i++)
            {
                labels[i] = inCover ? $"{weapons[i].Name} (cover)" : weapons[i].Name;
                sizes[i] = ImGui.CalcTextSize(labels[i]);
                if (sizes[i].X > blockW) blockW = sizes[i].X;
            }
            const float margin = 8f;
            float xLeftAnchor  = mx + margin;
            float xRightAnchor = mx - margin - blockW;
            float xAnchor = xLeftAnchor + blockW <= screenW - 4f ? xLeftAnchor : xRightAnchor;
            float yTop = my - blockH * 0.5f;
            for (int i = 0; i < n; i++)
                dl.AddText(new Vector2(xAnchor, yTop + i * lineH), thisTextCol, labels[i]);
        }

        // Blocked targets: stub from the selected model toward the would-be-nearest target,
        // stopping at the first blocker's entry point, with an X drawn at the block point.
        foreach (var kvp in blockedByTarget)
        {
            var target = kvp.Key;
            var weapons = kvp.Value.weapons.OrderBy(w => w.Name).ToList();
            int n = weapons.Count;

            var hit = LineOfSightUtilities.GetFirstBlockingHit(selPos, target.Position,
                blockersByEnemyUnit[kvp.Value.enemyUnit]);
            if (!hit.HasValue) continue; // sanity: we only land here when LoS failed, so a blocker exists

            var (ax, ay) = InchesToPixel(selPos.x, selPos.z);
            var (bx, by) = InchesToPixel(hit.Value.hit.x, hit.Value.hit.z);
            float dx = bx - ax, dy = by - ay;
            float len = MathF.Sqrt(dx * dx + dy * dy);
            if (len < 0.001f) continue;
            float perpX = -dy / len, perpY = dx / len;

            for (int i = 0; i < n; i++)
            {
                float offset = (i - (n - 1) * 0.5f) * stagger;
                var sa = new Vector2(ax + perpX * offset, ay + perpY * offset);
                var sb = new Vector2(bx + perpX * offset, by + perpY * offset);
                dl.AddLine(sa, sb, blockedLineCol, 1.5f);
            }

            // X marker at the block point
            const float xSize = 6f;
            dl.AddLine(new Vector2(bx - xSize, by - xSize), new Vector2(bx + xSize, by + xSize), blockedLineCol, 2f);
            dl.AddLine(new Vector2(bx - xSize, by + xSize), new Vector2(bx + xSize, by - xSize), blockedLineCol, 2f);

            // Weapon name labels stacked at the midpoint of the stub.
            float mx = (ax + bx) * 0.5f, my = (ay + by) * 0.5f;
            float blockH = n * lineH;
            float blockW = 0f;
            var sizes = new Vector2[n];
            for (int i = 0; i < n; i++) { sizes[i] = ImGui.CalcTextSize(weapons[i].Name); if (sizes[i].X > blockW) blockW = sizes[i].X; }
            const float margin = 8f;
            float xLeftAnchor  = mx + margin;
            float xRightAnchor = mx - margin - blockW;
            float xAnchor = xLeftAnchor + blockW <= screenW - 4f ? xLeftAnchor : xRightAnchor;
            float yTop = my - blockH * 0.5f;
            for (int i = 0; i < n; i++)
                dl.AddText(new Vector2(xAnchor, yTop + i * lineH), blockedLineCol, weapons[i].Name);
        }
    }

    private void DrawBoundingCircle(ImDrawListPtr dl, Position a, float ra, Position b, float rb)
    {
        float dx = b.x - a.x;
        float dz = b.z - a.z;
        float centerDist = MathF.Sqrt(dx * dx + dz * dz);
        if (centerDist < 0.0001f) return;
        float nx = dx / centerDist, nz = dz / centerDist;
        // Far edges of each base, along the line between centers
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
        // Aim for ~6px dashes around the circumference.
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

    private static List<ModelMoveEntry> StayInPlace(DefineMovementPathRequest request) =>
        request.UnitDataBinding.GetValue().ModelBindings
            .Select(mb => new ModelMoveEntry(mb, new List<Position>()))
            .ToList();

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

    private static string FormatInches(float value)
    {
        float frac = value - MathF.Floor(value);
        if (frac < 0.05f || frac > 0.95f) return MathF.Round(value).ToString("0");
        return value.ToString("0.0");
    }

    private bool IsOverTable(float px, float py) =>
        px >= _originX && py >= _originY &&
        px <= _originX + GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES * _scale &&
        py <= _originY + _tableH * _scale;
}
