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
    private readonly FormationModeState _formationMode;
    private readonly object _lock = new();

    // Group-mode pending rotation (radians), applied about the formation centroid. Reset to 0 each
    // time a group step is committed (the rotation is baked into the new positions) and each Resolve.
    private float _groupRotation;
    // Total group facing rotation (radians) across the whole move (#150). Unlike _groupRotation this is NOT
    // reset per committed step — the position rotation accumulates via the committed waypoints, so this tracks
    // the matching total heading. Reset only each Resolve.
    private float _groupFacingAngle;

    // The live pending position of every model in group mode (where the phantom currently sits), so the
    // targeting overlay can draw ghost-aware fire lines for the whole unit. Refreshed each frame the
    // group ghost is drawn; null otherwise.
    private Dictionary<IModel, Position>? _groupGhostPositions;

    // Per-model manual rotation offset (radians) (#150): a model the player rotated by hand in single mode
    // faces its direction of travel rotated by this offset (not a fixed lock). Cleared each Resolve.
    private readonly Dictionary<IModel, float> _manualOffsets = new();

    // Rotates an existing facing (unit normal) by `radians`, same matrix as the rigid position rotation.
    private static Float2 RotateFloat2(Float2 f, float radians)
    {
        float cos = MathF.Cos(radians), sin = MathF.Sin(radians);
        return new Float2(f.X * cos - f.Y * sin, f.X * sin + f.Y * cos);
    }

    // Facing from a segment's direction of travel (#150); falls back for a zero-length segment.
    private static Float2 TravelFacing(Position from, Position to, Float2 fallback)
    {
        float dx = to.x - from.x, dz = to.z - from.z;
        float len = MathF.Sqrt(dx * dx + dz * dz);
        return len > 1e-4f ? new Float2(dx / len, dz / len) : fallback;
    }

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

    // #155: per-frame difficult-terrain warning state, set where the ghost/phantoms clamp and read by the
    // info panel. Crossing = a model moves through difficult terrain (total move held to 6"); stopped = a
    // model couldn't afford to enter, so it was held at the terrain edge. Reset at the top of each Draw.
    private bool _frameDifficultCrossing;
    private bool _frameDifficultStopped;

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
    private static readonly uint DangerBadgeFill    = ImGui.ColorConvertFloat4ToU32(new Vector4(0.85f, 0.15f, 0.15f, 0.90f));
    private static readonly uint DangerBadgeOutline = ImGui.ColorConvertFloat4ToU32(new Vector4(0.30f, 0.02f, 0.02f, 1f));
    private static readonly uint DangerBadgeTextCol = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 1f));
    // #155: a path that crosses Dangerous terrain draws solid red (overriding its band colour); one that
    // crosses Difficult terrain draws dotted gray. Dangerous wins when a path crosses both.
    private static readonly uint DangerPathCol    = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.20f, 0.20f, 0.95f));
    private static readonly uint DifficultPathCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.62f, 0.62f, 0.62f, 0.95f));

    public GuiDefineMovementResolver(ITableState tableState, FormationModeState formationMode)
    {
        _tableState = tableState;
        _formationMode = formationMode;
    }

    public void UpdateLayout(float scale, int originX, int originY, float tableH)
    {
        _scale   = scale;
        _originX = originX;
        _originY = originY;
        _tableH  = tableH;
    }

    public bool HasPendingRequest { get { lock (_lock) return _request != null; } }

    /// <summary>
    /// The move job currently being resolved, or null when idle. Read by the tactical overlay on the
    /// main thread (pull model) to auto-show threat frontiers, pick the reference player/radius, and
    /// scope pins to the job. Lock-guarded like <see cref="HasPendingRequest"/> since Resolve runs on
    /// the engine thread.
    /// </summary>
    public DefineMovementPathRequest? ActiveRequest { get { lock (_lock) return _request; } }

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
            _groupRotation = 0f;
            _groupFacingAngle = 0f;
            _groupGhostPositions = null;
            _manualOffsets.Clear();
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

        // #155: per-frame terrain-consequence state. Difficult is ENFORCED (the ghost clamps so the
        // preview can never propose a move the validator's 6" cap would reject); Dangerous is ADVISORY
        // (badge + panel line - the roll is a gamble the player may take deliberately). Flying
        // (IgnoresImpassibleTerrain = the AllTerrain scope) waives Dangerous rolls entirely;
        // Strider/Flying (IgnoresDifficultTerrain) waive the difficult cap.
        bool difficultActive = !request.IgnoresDifficultTerrain
            && terrain.Any(t => t.TerrainType.HasFlag(ETerrainType.Difficult));
        bool dangerousActive = !request.IgnoresImpassibleTerrain
            && terrain.Any(t => t.TerrainType.HasFlag(ETerrainType.Dangerous));
        _frameDifficultCrossing = false;
        _frameDifficultStopped = false;
        Dictionary<IModel, DataBinding<ModelData>> bindings = BuildBindingMap(request);
        var committedCrossedDifficult = new HashSet<IModel>();
        var committedCrossedDangerous = new HashSet<IModel>();
        foreach (var kvp in paths)
        {
            if (kvp.Value.Count == 0 || !bindings.TryGetValue(kvp.Key, out var binding)) continue;
            var entry = new ModelMoveEntry(binding, kvp.Value.ToList());
            if (difficultActive && MovementUtilities.DoesPathCrossDifficultTerrain(entry, terrain))
                committedCrossedDifficult.Add(kvp.Key);
            if (dangerousActive && MovementUtilities.DoesPathCrossDangerousTerrain(entry, terrain))
                committedCrossedDangerous.Add(kvp.Key);
        }

        float maxAdvance = request.MaxAdvanceDistance;
        float maxRush    = request.MaxRushDistance;
        float maxCharge  = request.MaxDistanceInches;
        // #093: single mode operates on the selected model, so the ghost/clamp/range-rings/bands use ITS own
        // budget — a joined hero with Fast shows bigger rings and can be placed further than its unitmates.
        // Group mode keeps the unit scalars here and applies each model's own budget in DrawGroupGhostAndInput.
        if (!_formationMode.IsGroup && _selectedModel != null)
            (maxAdvance, maxRush, maxCharge) = request.BudgetFor(_selectedModel.ID);
        bool  hasChargeBand = maxCharge > maxRush + 0.0001f;

        // 1) Draw each model's start circle + committed path lines + final ghost circle
        foreach (var kvp in paths)
        {
            var model = kvp.Key;
            var pathPoints = kvp.Value;
            var start = model.Position;
            var (sx, sy) = InchesToPixel(start.x, start.z);

            // Start base outline (real model position) — drawn as the model's true shape (#149)
            uint outline = ReferenceEquals(model, _selectedModel) ? SelectionOutline : ModelOutline;
            float thick  = ReferenceEquals(model, _selectedModel) ? 2.5f : 1.5f;
            ModelBaseRenderer.DrawOutlineImGui(dl, model.BaseShape, new Vector2(sx, sy), _scale, outline, thick);

            // Path lines. #155: a model whose committed path crosses Dangerous terrain draws its whole path
            // solid red; crossing Difficult draws dotted gray; otherwise the per-segment band colour.
            if (pathPoints.Count > 0)
            {
                bool danger = committedCrossedDangerous.Contains(model);
                bool diff   = !danger && committedCrossedDifficult.Contains(model);
                Position prev = start;
                float cum = 0f;
                for (int i = 0; i < pathPoints.Count; i++)
                {
                    var cur = pathPoints[i];
                    var (px, py) = InchesToPixel(prev.x, prev.z);
                    var (cx, cy) = InchesToPixel(cur.x, cur.z);
                    var a = new Vector2(px, py); var b = new Vector2(cx, cy);
                    if      (danger) dl.AddLine(a, b, DangerPathCol, 2.5f);
                    else if (diff)   AddDottedLine(dl, a, b, DifficultPathCol, 2.5f);
                    else             dl.AddLine(a, b, LineColorFor(ClassifyBand(cum, maxAdvance, maxRush, hasChargeBand)), 2f);
                    cum += Position.GetDistance3D(prev, cur);
                    prev = cur;
                }

                // Final position ghost (true shape) + heading (#150): a locked hand/group facing, else the
                // direction of travel into the last committed waypoint.
                var last = pathPoints[^1];
                var (lx, ly) = InchesToPixel(last.x, last.z);
                Position beforeLast = pathPoints.Count >= 2 ? pathPoints[pathPoints.Count - 2] : start;
                float finalOffset = _formationMode.IsGroup ? _groupFacingAngle : _manualOffsets.GetValueOrDefault(model);
                Float2 finalFacing = RotateFloat2(TravelFacing(beforeLast, last, model.Facing), finalOffset);
                ModelBaseRenderer.DrawFilledImGui(dl, model.BaseShape, new Vector2(lx, ly), _scale, FinalGhostCol, outline, thick, finalFacing);
                ModelBaseRenderer.DrawHeadingImGui(dl, model.BaseShape, new Vector2(lx, ly), _scale, finalFacing, outline);
            }
        }

        // Shared pointer/keyboard state and the current placement mode.
        bool overTable = IsOverTable(io.MousePos.X, io.MousePos.Y);
        bool wantInput = !io.WantCaptureMouse && !io.WantCaptureKeyboard;
        bool advanceOnly = _stayInAdvance || ImGui.IsKeyDown(ImGuiKey.LeftShift) || ImGui.IsKeyDown(ImGuiKey.RightShift);
        bool group = _formationMode.IsGroup;

        Position? ghostPos = null;
        bool ghostOverlaps = false;
        float ghostExtraDist = 0f;

        // 2) Range rings around selected model's last waypoint (single mode only)
        if (!group && _selectedModel != null)
        {
            // #155: once the committed path has crossed difficult terrain, the 6" total cap governs
            // every band - shrink the rings so they show the model's true remaining reach.
            float ringAdvance = maxAdvance, ringRush = maxRush, ringCharge = maxCharge;
            if (difficultActive && committedCrossedDifficult.Contains(_selectedModel))
            {
                float cap = GameWideConstants.DIFFICULT_TERRAIN_MOVE_CAP_INCHES;
                ringAdvance = MathF.Min(ringAdvance, cap);
                ringRush    = MathF.Min(ringRush, cap);
                ringCharge  = MathF.Min(ringCharge, cap);
            }
            DrawRangeRings(dl, pt, ringAdvance, ringRush, ringCharge, ringCharge > ringRush + 0.0001f);
        }

        // 3) Ghost following mouse for selected model (clamped)
        if (!group && _selectedModel != null && overTable && !io.WantCaptureMouse)
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

            // Ghost heading (#150): the travel direction toward the mouse (stable regardless of clamp
            // distance), rotated by any hand offset. Drives the ghost footprint AND both terrain/enemy clamps
            // so the clamps measure the same oriented base the ghost draws.
            Float2 ghostFacing = RotateFloat2(TravelFacing(anchor, new Position(mx, mz), _selectedModel.Facing),
                _manualOffsets.GetValueOrDefault(_selectedModel));

            // #155: difficult terrain enforces its own cap on top of the band cap. Crossing difficult
            // caps this model's TOTAL move at 6"; when entry is no longer affordable the ghost stops
            // just short of the terrain edge - the preview can never propose an invalid move.
            if (difficultActive && allowed > 0.0001f && dist > 0.0001f)
            {
                var segStart = new Float2(anchor.x, anchor.z);
                var segEnd   = new Float2(anchor.x + dx / dist * allowed, anchor.z + dz / dist * allowed);
                var clamp = MovementUtilities.ClampTravelForDifficultTerrainDetailed(segStart, segEnd, totalSoFar,
                    committedCrossedDifficult.Contains(_selectedModel), _selectedModel.BaseShape,
                    ghostFacing, terrain, request.IgnoresDifficultTerrain);
                allowed = clamp.AllowedInches;
                if (clamp.Kind == MovementUtilities.EDifficultClampKind.CappedCrossing) _frameDifficultCrossing = true;
                else if (clamp.Kind == MovementUtilities.EDifficultClampKind.StoppedShortOfEdge) _frameDifficultStopped = true;
            }

            // #155: other units' bases block the slide - stop the ghost just short of first contact so the
            // preview can't draw a move that passes through another model. Fly-over units (Strafing) skip it.
            if (allowed > 0.0001f && dist > 0.0001f && !request.CanMoveThroughEnemies)
                allowed = EnemyClampTravel(anchor, dx / dist, dz / dist, allowed, _selectedModel, ghostFacing, request);

            float nx, nz;
            if (dist < 0.0001f) { nx = anchor.x; nz = anchor.z; }
            else                { nx = anchor.x + dx / dist * allowed; nz = anchor.z + dz / dist * allowed; }
            ghostPos = new Position(nx, nz);

            float cumWithGhost = totalSoFar + allowed;
            MoveBand ghostBand = ClassifyBand(cumWithGhost, maxAdvance, maxRush, hasChargeBand);
            ghostExtraDist = allowed;
            ghostOverlaps  = WouldOverlapAnyModel(ghostPos.Value, ghostFacing, _selectedModel, request, paths);

            // Preview line from anchor to ghost. #155: red solid if the model's path (committed + this ghost)
            // crosses Dangerous terrain, dotted gray if it crosses Difficult, else the band colour.
            bool ghDanger = committedCrossedDangerous.Contains(_selectedModel);
            bool ghDiff   = committedCrossedDifficult.Contains(_selectedModel);
            if ((dangerousActive || difficultActive) && bindings.TryGetValue(_selectedModel, out var selBinding))
            {
                var eff = new List<Position>(paths.TryGetValue(_selectedModel, out var sp) ? sp : (IReadOnlyList<Position>)System.Array.Empty<Position>()) { ghostPos.Value };
                var effEntry = new ModelMoveEntry(selBinding, eff);
                if (dangerousActive) ghDanger |= MovementUtilities.DoesPathCrossDangerousTerrain(effEntry, terrain);
                if (difficultActive) ghDiff   |= MovementUtilities.DoesPathCrossDifficultTerrain(effEntry, terrain);
            }
            var (ax, ay) = InchesToPixel(anchor.x, anchor.z);
            var (gx, gy) = InchesToPixel(nx, nz);
            if      (ghDanger) dl.AddLine(new Vector2(ax, ay), new Vector2(gx, gy), DangerPathCol, 2.5f);
            else if (ghDiff)   AddDottedLine(dl, new Vector2(ax, ay), new Vector2(gx, gy), DifficultPathCol, 2.5f);
            else               dl.AddLine(new Vector2(ax, ay), new Vector2(gx, gy), LineColorFor(ghostBand), 2f);

            // Ghost base (true shape) + heading.
            uint fill = ghostOverlaps ? OverlapFill : FillColorFor(ghostBand);
            ModelBaseRenderer.DrawFilledImGui(dl, _selectedModel.BaseShape, new Vector2(gx, gy), _scale, fill, GhostOutline, 1.5f, ghostFacing);
            ModelBaseRenderer.DrawHeadingImGui(dl, _selectedModel.BaseShape, new Vector2(gx, gy), _scale, ghostFacing, GhostOutline);

            // Cohesion warnings: indicators reflect would-be positions if user committed here
            var finalsWithGhost = BuildFinalPositions(paths, _selectedModel, ghostPos);
            DrawCohesionIndicators(dl, finalsWithGhost, _selectedModel);
        }

        // Group mode: rigidly rotate + translate the whole unit; one left-click commits a group step.
        if (group)
            DrawGroupGhostAndInput(dl, io, request, pt, paths, overTable, wantInput,
                maxAdvance, maxRush, maxCharge, hasChargeBand, advanceOnly,
                terrain, difficultActive, dangerousActive,
                committedCrossedDifficult, committedCrossedDangerous, bindings);

        // #155: dangerous-terrain badge pass, ghost-aware (the selected model's live ghost in single mode /
        // every live phantom in group mode counts as a pending final segment). Draws a warning badge beside
        // each model whose path would cross Dangerous terrain and counts them for the panel line. (The
        // difficult-terrain warnings are set at the clamp sites via _frameDifficult* since only the clamp
        // knows whether a model was capped-while-crossing or stopped at the edge.)
        int dangerousCrossers = 0;
        if (dangerousActive)
        {
            foreach (var kvp in paths)
            {
                IModel m = kvp.Key;
                if (!bindings.TryGetValue(m, out var binding)) continue;

                Position pathEnd = kvp.Value.Count > 0 ? kvp.Value[^1] : m.Position;
                Position? pending = null;
                if (group && _groupGhostPositions != null && _groupGhostPositions.TryGetValue(m, out var phantom))
                    pending = phantom;
                else if (!group && ReferenceEquals(m, _selectedModel) && ghostPos.HasValue)
                    pending = ghostPos.Value;

                var positions = new List<Position>(kvp.Value);
                if (pending.HasValue && Position.GetDistance2D(pathEnd, pending.Value) > 0.0001f)
                    positions.Add(pending.Value);
                if (positions.Count == 0) continue; // not moving - no roll

                var entry = new ModelMoveEntry(binding, positions);
                if (MovementUtilities.DoesPathCrossDangerousTerrain(entry, terrain))
                {
                    dangerousCrossers++;
                    DrawDangerBadge(dl, positions[^1], m.BaseShape.CircumscribedRadiusInches);
                }
            }
        }

        // 3b) Targeting overlay (toggle, on by default) — combines ranged + melee
        if (_showTargeting)
            DrawTargeting(dl, screenW, screenH, request, pt, paths, ghostPos, ghostExtraDist, group);

        // 4) Mouse / keyboard input (single mode — group mode handles its own clicks above)
        if (!group && overTable && !io.WantCaptureMouse)
        {
            // Left-click: if it lands on a model's start circle, select that model; otherwise place a
            // waypoint for the selected model at the clamped ghost position (blocked if it would overlap
            // another model). Left-click places in BOTH single and group mode (consistency).
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
                    float cap = advanceOnly ? maxAdvance : maxCharge;
                    if (cap - totalSoFar > 0.001f)
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

        // Backspace removes last waypoint of selected model (single mode)
        if (!group && wantInput && _selectedModel != null && ImGui.IsKeyPressed(ImGuiKey.Backspace))
        {
            if (paths.TryGetValue(_selectedModel, out var list) && list.Count > 0)
                pt.RemoveLastStep(_selectedModel);
        }

        // R / Shift+R rotates the selected model's facing — an offset from its direction of travel (#150).
        if (!group && wantInput && _selectedModel != null && ImGui.IsKeyPressed(ImGuiKey.R))
        {
            bool shift = ImGui.IsKeyDown(ImGuiKey.LeftShift) || ImGui.IsKeyDown(ImGuiKey.RightShift);
            _manualOffsets[_selectedModel] = _manualOffsets.GetValueOrDefault(_selectedModel)
                + (shift ? GroupRotationStep : -GroupRotationStep);
        }

        // Spacebar cycles to next model in the unit's list (single mode)
        if (!group && wantInput && ImGui.IsKeyPressed(ImGuiKey.Space))
        {
            var keys = paths.Keys.ToList();
            if (keys.Count > 0)
            {
                int idx = _selectedModel == null ? -1 : keys.IndexOf(_selectedModel);
                _selectedModel = keys[(idx + 1) % keys.Count];
            }
        }

        // G toggles Group/Single for the rest of the game (shared with deployment).
        if (wantInput && ImGui.IsKeyPressed(ImGuiKey.G))
            _formationMode.Toggle();

        bool selectedCapped = difficultActive && _selectedModel != null
            && committedCrossedDifficult.Contains(_selectedModel);
        DrawInfoPanel(screenW, request, pt, tcs, terrain, dangerousCrossers,
            _frameDifficultCrossing, _frameDifficultStopped, selectedCapped);
    }

    private static readonly float GroupRotationStep = MathF.PI / 12f; // 15° per wheel notch / key press
    private const float GroupMoveSafetyMargin = 0.005f; // keep the farthest model just under its cap

    /// <summary>
    /// Group mode: previews the whole unit rigidly rotated (wheel / R) and translated so its centroid
    /// follows the cursor, with the translation scaled back so no model exceeds its remaining move
    /// (true per-model travel). A left-click appends one waypoint per model when every phantom sits in
    /// a legal spot; otherwise the phantoms turn red and the click is a no-op.
    /// </summary>
    private void DrawGroupGhostAndInput(ImDrawListPtr dl, ImGuiIOPtr io,
        DefineMovementPathRequest request, PathTemplate pt,
        IReadOnlyDictionary<IModel, IReadOnlyList<Position>> paths,
        bool overTable, bool wantInput,
        float maxAdvance, float maxRush, float maxCharge, bool hasChargeBand, bool advanceOnly,
        List<ITerrain> terrain, bool difficultActive, bool dangerousActive,
        HashSet<IModel> committedCrossedDifficult, HashSet<IModel> committedCrossedDangerous,
        Dictionary<IModel, DataBinding<ModelData>> bindings)
    {
        var models = paths.Keys.ToList();
        if (models.Count == 0) return;

        // Rotation input: wheel both ways; R clockwise, Shift+R counter-clockwise.
        if (wantInput)
        {
            if (io.MouseWheel != 0f)
            {
                float d = io.MouseWheel > 0f ? GroupRotationStep : -GroupRotationStep;
                _groupRotation += d; _groupFacingAngle += d;
            }
            if (ImGui.IsKeyPressed(ImGuiKey.R))
            {
                bool shift = ImGui.IsKeyDown(ImGuiKey.LeftShift) || ImGui.IsKeyDown(ImGuiKey.RightShift);
                float d = shift ? GroupRotationStep : -GroupRotationStep;
                _groupRotation += d; _groupFacingAngle += d;
            }
        }

        var lastPositions = new List<Position>(models.Count);
        var budgets = new List<float>(models.Count);
        foreach (var m in models)
        {
            lastPositions.Add(pt.GetModelLastPathPosition(m));
            // #093: each model's own cap (a joined hero's Fast/Slow), so the group step is bottlenecked by
            // whichever model has the least remaining budget. Land just shy of the cap so a model that
            // travels its full allowance still classifies as Advance (not Rush) and clears the engine's
            // shoot-after-advance gate — matches single mode.
            var (mAdvance, _, mCharge) = request.BudgetFor(m.ID);
            float cap = advanceOnly ? mAdvance : mCharge;
            // #155: a model whose committed path already crossed difficult terrain has its whole move
            // capped at 6" total - fold that into its group budget (the clamp margin keeps the plan's
            // budget-limited steps under what ClampTravelForDifficultTerrain will allow below).
            if (difficultActive && committedCrossedDifficult.Contains(m))
                cap = MathF.Min(cap, GameWideConstants.DIFFICULT_TERRAIN_MOVE_CAP_INCHES
                    - MovementUtilities.DIFFICULT_TERRAIN_CLAMP_MARGIN_INCHES);
            budgets.Add(cap - pt.GetTotalDistanceMoved(m) - GroupMoveSafetyMargin);
        }

        // #094: if the unit is out of coherency (e.g. a model died mid-unit), re-form the group ghost into a
        // legal shape by contracting toward the centroid the least amount needed, so one click moves them
        // into coherency. The rigid transform then rides on this repaired base, but the per-model budget is
        // still measured from each model's REAL start (lastPositions), so the cohesion correction plus the
        // move can never push any model past its cap. Coherent units skip this and keep the pure rigid path.
        var startPairs = new List<(IModel model, Position pos)>(models.Count);
        for (int i = 0; i < models.Count; i++) startPairs.Add((models[i], lastPositions[i]));
        IReadOnlyList<Position> basePositions = lastPositions;
        if (CheckCohesion(startPairs).Any)
        {
            // Circumscribing radii: the layout/repair packs by a conservative bounding circle so rectangular
            // bases don't overlap (the authoritative move is re-validated shape-aware by the server). #150.
            var radii = models.Select(m => m.BaseShape.CircumscribedRadiusInches).ToList();
            basePositions = GroupFormationUtilities.RepairCoherencyByContraction(
                lastPositions, radii,
                GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES,
                GameWideConstants.MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES);
        }

        Position pivot = GroupFormationUtilities.Centroid(basePositions);
        float cos = MathF.Cos(_groupRotation), sin = MathF.Sin(_groupRotation);

        // Desired translation moves the centroid under the cursor (none when the mouse is off-table).
        float desiredTx = 0f, desiredTz = 0f;
        if (overTable && !io.WantCaptureMouse)
        {
            var (mx, mz) = PixelToInches(io.MousePos.X, io.MousePos.Y);
            desiredTx = mx - pivot.x;
            desiredTz = mz - pivot.z;
        }

        var plan = GroupFormationUtilities.PlanGroupMove(basePositions, lastPositions, budgets, pivot, cos, sin, desiredTx, desiredTz);
        IReadOnlyList<Position> newPositions = plan.NewPositions;

        // #155: the budget solve above can't see terrain or other units. If any phantom's step would violate
        // the difficult-terrain cap (entering caps that model's total move at 6"; an unaffordable entry stops
        // short of the edge) or slide through another unit's base, pull the applied translation back further -
        // bisected with those clamps as the feasibility test, so the group preview never proposes a step the
        // validator would reject. If even the rotation alone is infeasible, the step is invalid outright (same
        // treatment as an over-budget rotation).
        bool enemyClampActive = !request.CanMoveThroughEnemies;
        bool clampActive = difficultActive || enemyClampActive;
        bool terrainBlocked = false;
        if (clampActive)
        {
            float appliedTx = desiredTx * plan.TranslationScale;
            float appliedTz = desiredTz * plan.TranslationScale;

            Position[] PositionsAt(float s)
            {
                var arr = new Position[models.Count];
                for (int i = 0; i < models.Count; i++)
                    arr[i] = GroupFormationUtilities.RigidTransform(
                        basePositions[i], pivot, cos, sin, appliedTx * s, appliedTz * s);
                return arr;
            }

            Float2 StepFacing(Position from, Position to, IModel m) =>
                RotateFloat2(TravelFacing(from, to, m.Facing), _groupFacingAngle);

            bool Feasible(IReadOnlyList<Position> candidate)
            {
                for (int i = 0; i < models.Count; i++)
                {
                    Position from = lastPositions[i];
                    Position to = candidate[i];
                    float stepDist = Position.GetDistance2D(from, to);
                    if (stepDist <= 0.0001f) continue;
                    Float2 facing = StepFacing(from, to, models[i]);
                    if (difficultActive)
                    {
                        float allowedDist = MovementUtilities.ClampTravelForDifficultTerrain(
                            new Float2(from.x, from.z), new Float2(to.x, to.z),
                            pt.GetTotalDistanceMoved(models[i]), committedCrossedDifficult.Contains(models[i]),
                            models[i].BaseShape, facing, terrain, ignoresDifficultTerrain: false);
                        if (stepDist > allowedDist + 0.001f) return false;
                    }
                    if (enemyClampActive)
                    {
                        float dirX = (to.x - from.x) / stepDist, dirZ = (to.z - from.z) / stepDist;
                        float enemyAllowed = EnemyClampTravel(from, dirX, dirZ, stepDist, models[i], facing, request);
                        if (stepDist > enemyAllowed + 0.001f) return false;
                    }
                }
                return true;
            }

            if (!Feasible(newPositions))
            {
                if (!Feasible(PositionsAt(0f)))
                    terrainBlocked = true;
                else
                    newPositions = PositionsAt(
                        GroupFormationUtilities.LargestFeasibleScale(s => Feasible(PositionsAt(s))));
            }

            // Difficult-terrain warning state: read each phantom's clamp Kind on its DESIRED (budget-feasible,
            // pre-terrain-clamp) segment so the panel can say "moving through" vs "stopped at the edge".
            if (difficultActive)
            {
                var desired = PositionsAt(1f);
                for (int i = 0; i < models.Count; i++)
                {
                    Position from = lastPositions[i];
                    if (Position.GetDistance2D(from, desired[i]) <= 0.0001f) continue;
                    var clamp = MovementUtilities.ClampTravelForDifficultTerrainDetailed(
                        new Float2(from.x, from.z), new Float2(desired[i].x, desired[i].z),
                        pt.GetTotalDistanceMoved(models[i]), committedCrossedDifficult.Contains(models[i]),
                        models[i].BaseShape, StepFacing(from, desired[i], models[i]), terrain, ignoresDifficultTerrain: false);
                    if (clamp.Kind == MovementUtilities.EDifficultClampKind.CappedCrossing) _frameDifficultCrossing = true;
                    else if (clamp.Kind == MovementUtilities.EDifficultClampKind.StoppedShortOfEdge) _frameDifficultStopped = true;
                }
            }
        }

        // Publish the live phantom positions so the targeting overlay can draw ghost-aware fire lines
        // for the whole unit (updates as the mouse moves, before any step is committed).
        var ghostMap = new Dictionary<IModel, Position>(models.Count);
        for (int i = 0; i < models.Count; i++) ghostMap[models[i]] = newPositions[i];
        _groupGhostPositions = ghostMap;

        IUnit ownUnit = request.UnitDataBinding.GetValue();
        bool allValid = plan.WithinBudget && !terrainBlocked;
        var blocked = new bool[models.Count];
        // Each phantom's live facing (its travel direction rotated by the accumulated group offset), computed
        // once so the overlap check measures the same oriented footprint the ghost draws (#150).
        var groupFacings = new Float2[models.Count];
        bool anyMovement = false;
        for (int i = 0; i < models.Count; i++)
        {
            groupFacings[i] = RotateFloat2(
                TravelFacing(lastPositions[i], newPositions[i], models[i].Facing), _groupFacingAngle);
            blocked[i] = GroupPositionBlocked(newPositions[i], models[i].BaseShape, groupFacings[i], ownUnit);
            if (blocked[i]) allValid = false;
            if (Position.GetDistance2D(lastPositions[i], newPositions[i]) > 0.001f) anyMovement = true;
        }

        // Pending ghost: line from each model's last spot to its new spot + a base circle, band-coloured
        // (red when that phantom is blocked or the rotation alone busts the move budget). #155: a phantom
        // whose path crosses Dangerous terrain draws solid red, one crossing Difficult draws dotted gray -
        // but a blocked/over-budget phantom keeps its overlap-red so the "can't place here" signal wins.
        for (int i = 0; i < models.Count; i++)
        {
            float stepDist = Position.GetDistance2D(lastPositions[i], newPositions[i]);
            float cum = pt.GetTotalDistanceMoved(models[i]) + stepDist;
            MoveBand band = ClassifyBand(cum, maxAdvance, maxRush, hasChargeBand);
            bool bad = blocked[i] || !plan.WithinBudget || terrainBlocked;
            uint fill = bad ? OverlapFill : FillColorFor(band);

            bool danger = committedCrossedDangerous.Contains(models[i]);
            bool diff   = committedCrossedDifficult.Contains(models[i]);
            if (!bad && (dangerousActive || difficultActive) && bindings.TryGetValue(models[i], out var b))
            {
                var eff = new List<Position>(paths[models[i]]) { newPositions[i] };
                var e = new ModelMoveEntry(b, eff);
                if (dangerousActive) danger |= MovementUtilities.DoesPathCrossDangerousTerrain(e, terrain);
                if (difficultActive) diff   |= MovementUtilities.DoesPathCrossDifficultTerrain(e, terrain);
            }

            var (sx, sy) = InchesToPixel(lastPositions[i].x, lastPositions[i].z);
            var (nx, ny) = InchesToPixel(newPositions[i].x, newPositions[i].z);
            var pa = new Vector2(sx, sy); var pb = new Vector2(nx, ny);
            if      (bad)          dl.AddLine(pa, pb, OverlapFill, 2f);
            else if (danger)       dl.AddLine(pa, pb, DangerPathCol, 2.5f);
            else if (diff)         AddDottedLine(dl, pa, pb, DifficultPathCol, 2.5f);
            else                   dl.AddLine(pa, pb, LineColorFor(band), 2f);
            // Heading (#150): the step's direction of travel, rotated by the accumulated group offset (computed above).
            Float2 groupFacing = groupFacings[i];
            ModelBaseRenderer.DrawFilledImGui(dl, models[i].BaseShape, new Vector2(nx, ny), _scale, fill, GhostOutline, 1.5f, groupFacing);
            ModelBaseRenderer.DrawHeadingImGui(dl, models[i].BaseShape, new Vector2(nx, ny), _scale, groupFacing, GhostOutline);
        }

        // Commit on left-click when every phantom is legal and something actually moves.
        if (overTable && !io.WantCaptureMouse && ImGui.IsMouseClicked(ImGuiMouseButton.Left)
            && allValid && anyMovement)
        {
            for (int i = 0; i < models.Count; i++)
                pt.AddStep(models[i], newPositions[i]);
            _groupRotation = 0f;
        }

        // Right-click clears the last group waypoint (one per model), if any — mirrors single mode + Backspace
        // so right-click clears the last path point in ALL modes.
        if (overTable && !io.WantCaptureMouse && ImGui.IsMouseClicked(ImGuiMouseButton.Right))
        {
            foreach (var m in models)
                if (paths.TryGetValue(m, out var list) && list.Count > 0)
                    pt.RemoveLastStep(m);
        }
    }

    /// <summary>True if <paramref name="shape"/> at <paramref name="p"/> facing <paramref name="facing"/>
    /// would overlap a live model of another unit or sit on impassible terrain — measured by the true
    /// oriented footprints (#150), not a bounding circle. Same-unit models are ignored (the group moves
    /// rigidly, so internal spacing is preserved).</summary>
    private bool GroupPositionBlocked(Position p, IBaseShape shape, Float2 facing, IUnit ownUnit)
    {
        foreach (var unit in _tableState.Units.Objects)
        {
            if (ReferenceEquals(unit, ownUnit)) continue;
            foreach (var m in unit.Models)
            {
                if (!m.GetIsAlive()) continue;
                var mp = m.Position;
                if (mp.x == 0f && mp.z == 0f) continue;
                if (BaseShapeGeometry.AreColliding(shape, p, facing, m.BaseShape, mp, m.Facing)) return true;
            }
        }
        return PlacementUtilities.OverlapsImpassibleTerrain(p, shape, facing, _tableState.Terrain.Objects);
    }

    private void DrawInfoPanel(int screenW, DefineMovementPathRequest request, PathTemplate pt,
        TaskCompletionSource<List<ModelMoveEntry>> tcs, List<ITerrain> terrain,
        int dangerousCrossers, bool difficultCrossing, bool difficultStopped, bool selectedCapped)
    {
        float panelW = MathF.Min(screenW * 0.6f, 680f);
        ImGui.SetNextWindowPos(new Vector2(screenW - panelW - 12f, 16f), ImGuiCond.FirstUseEver); // right-aligned (#105)
        ImGui.SetNextWindowSizeConstraints(new Vector2(panelW, 0f), new Vector2(panelW, float.MaxValue));
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.10f, 0.10f, 0.15f, 0.92f));
        ImGui.Begin("##MovementPanel",
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.AlwaysAutoResize);
        ImGui.PopStyleColor();

        string unitName = request.UnitDataBinding.GetValue().Name;
        ImGui.TextUnformatted($"Move: {unitName}");
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1f));
        ImGui.TextWrapped(FormatDistanceSummary(request));
        ImGui.PopStyleColor();

        // #155: live terrain-consequence warnings (ghost-aware, computed in Draw).
        if (dangerousCrossers > 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1.00f, 0.40f, 0.35f, 1f));
            ImGui.TextWrapped(dangerousCrossers == 1
                ? "Dangerous terrain: 1 model's path crosses - it rolls a d6 on commit, a 1 deals 1 wound"
                : $"Dangerous terrain: {dangerousCrossers} models' paths cross - each rolls a d6 on commit, a 1 deals 1 wound");
            ImGui.PopStyleColor();
        }
        if (difficultCrossing)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.75f, 0.75f, 0.78f, 1f));
            ImGui.TextWrapped("Difficult terrain: moving through it caps this move at "
                + $"{FormatInches(GameWideConstants.DIFFICULT_TERRAIN_MOVE_CAP_INCHES)}\" total");
            ImGui.PopStyleColor();
        }
        if (difficultStopped)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.75f, 0.75f, 0.78f, 1f));
            ImGui.TextWrapped("Difficult terrain: already moved too far to enter - stopped at the terrain edge");
            ImGui.PopStyleColor();
        }

        bool group = _formationMode.IsGroup;

        ImGui.Spacing();
        if (group)
        {
            float deg = _groupRotation * 180f / MathF.PI;
            deg -= 360f * MathF.Floor((deg + 180f) / 360f); // normalize to (-180, 180]
            ImGui.TextUnformatted($"Group move - whole unit   rotation {deg:0} deg");
            ImGui.TextDisabled("Wheel / R / Shift+R: rotate 15 deg   L-click: place step");
        }
        else if (_selectedModel != null)
        {
            float dist = pt.GetTotalDistanceMoved(_selectedModel);
            bool inRush = dist + 0.0001f >= request.MaxAdvanceDistance;
            // #155: once this model's committed path crossed difficult terrain, 6" total is its real max.
            float maxShown = selectedCapped
                ? MathF.Min(request.MaxDistanceInches, GameWideConstants.DIFFICULT_TERRAIN_MOVE_CAP_INCHES)
                : request.MaxDistanceInches;
            string capTag = selectedCapped ? " (difficult terrain cap)" : string.Empty;
            var color = inRush ? new Vector4(1.00f, 0.55f, 0.10f, 1f) : new Vector4(0.25f, 0.95f, 0.25f, 1f);
            ImGui.PushStyleColor(ImGuiCol.Text, color);
            ImGui.TextUnformatted($"Selected model: {dist:F2}\" / {FormatInches(maxShown)}\"{capTag}  ({(inRush ? "RUSH - cannot shoot" : "advance - may shoot")})");
            ImGui.PopStyleColor();
            ImGui.TextDisabled("L-click: select   R-click: waypoint   Space: next   Backspace: undo");
        }
        else
        {
            ImGui.TextDisabled("No model selected. Left-click a model on the table.");
            ImGui.TextDisabled("L-click: select   R-click: waypoint   Space: next   Backspace: undo");
        }

        if (ImGui.Button(group ? "Mode: Group (G)" : "Mode: Single (G)"))
            _formationMode.Toggle();
        ImGui.SameLine();
        ImGui.TextDisabled(group ? "whole unit moves together" : "one model at a time");

        ImGui.Checkbox("Stay within Advance (hold Shift to force)", ref _stayInAdvance);
        ImGui.Checkbox("Show targeting", ref _showTargeting);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Show ranged weapons in range AND with line of sight to each enemy unit (green), how many of your models can charge it (yellow), fire lines from the selected model, and a charge line when the ghost is within melee range. Fire lines turn yellow and dashed when the shot passes through cover (+1 to the target's defense roll). When no enemy model is visible to a weapon, a red blocked-stub with an X marks where the shot would hit a wall. Ranged info hides if the unit has moved too far to shoot.");

        ImGui.Spacing();
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        float pad     = ImGui.GetStyle().WindowPadding.X * 2;
        float btnW    = (panelW - pad - spacing) / 2f; // two buttons per row

        // Facing overrides (#150): a group rotation faces the whole unit that way; single-mode hand-rotations
        // lock per model. Otherwise each waypoint follows its direction of travel (see PathTemplate).
        Dictionary<IModel, float>? facingOffsets = null;
        if (group)
        {
            if (_groupFacingAngle != 0f)
            {
                facingOffsets = new Dictionary<IModel, float>();
                foreach (IModel m in pt.CurrentPaths.Keys) facingOffsets[m] = _groupFacingAngle;
            }
        }
        else if (_manualOffsets.Count > 0)
        {
            facingOffsets = _manualOffsets;
        }
        var results = pt.GetResultsAsList(facingOffsets, travelDirectionFacing: true);
        var enemyFootprints = GetEnemyFootprintsForRequest(request);
        // #093: validate each model against its OWN budget so Done gates exactly as the authoritative stage.
        bool engineValid = MovementUtilities.ValidatePaths(results,
            entry => { var (_, rush, maxDist) = request.BudgetFor(entry.Model.GetValue().ID);
                       return new ModelMoveBudget(rush, maxDist); },
            enemyFootprints, request.CanMoveThroughEnemies, request.IgnoresDifficultTerrain, request.IgnoresImpassibleTerrain, terrain, out var engineErrors);
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
        bool clearPressed = ImGui.Button(group ? "Clear all" : "Clear selected", new Vector2(btnW, 28f));
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip(group
                ? "Remove all waypoints from every model in the unit."
                : "Remove all waypoints from the currently selected model.");
        if (clearPressed)
        {
            if (group) pt.ClearAllSteps();
            else if (_selectedModel != null) pt.ClearModelSteps(_selectedModel);
        }

        // Row 2
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

    // #155: the terrain checks take ModelMoveEntry (binding + waypoints), but the path template keys by
    // IModel - map back to each model's binding via the unit.
    private static Dictionary<IModel, DataBinding<ModelData>> BuildBindingMap(DefineMovementPathRequest request)
    {
        var map = new Dictionary<IModel, DataBinding<ModelData>>();
        foreach (var mb in request.UnitDataBinding.GetValue().ModelBindings)
            if (mb.GetValue() is IModel m)
                map[m] = mb;
        return map;
    }

    /// <summary>Warning badge (red triangle + '!') beside a model whose path crosses Dangerous terrain
    /// (#155) - offset up-right of the path's end position so it doesn't cover the ghost base.</summary>
    private void DrawDangerBadge(ImDrawListPtr dl, Position pathEnd, float baseRadiusInches)
    {
        var (px, py) = InchesToPixel(pathEnd.x, pathEnd.z);
        float off = baseRadiusInches * _scale * 0.7071f;
        var center = new Vector2(px + off + 12f, py - off - 12f);
        const float s = 14f; // #155: twice the original 7px
        var a = new Vector2(center.X, center.Y - s);
        var b = new Vector2(center.X - s, center.Y + s * 0.9f);
        var c = new Vector2(center.X + s, center.Y + s * 0.9f);
        dl.AddTriangleFilled(a, b, c, DangerBadgeFill);
        dl.AddTriangle(a, b, c, DangerBadgeOutline, 2f);
        // Larger '!' scaled to the badge (default font drawn at ~1.7x).
        ImFontPtr font = ImGui.GetFont();
        float fontSize = ImGui.GetFontSize() * 1.7f;
        var t = font.CalcTextSizeA(fontSize, float.MaxValue, 0f, "!");
        dl.AddText(font, fontSize, new Vector2(center.X - t.X * 0.5f, center.Y - t.Y * 0.5f + 3f), DangerBadgeTextCol, "!");
    }

    // #155: keeps the enemy-clamped ghost a hair short of exact base contact so the separate overlap test
    // (which fires on a negative surface gap) stays clear at the clamped endpoint.
    private const float EnemyClampMargin = 0.02f;

    /// <summary>Shrinks <paramref name="allowed"/> so the moving base, sliding from <paramref name="anchor"/>
    /// along (dirX,dirZ), stops just short of first contact with any other unit's live model (#155) - the
    /// distance-limit analogue of the difficult-terrain clamp, for "can't move through a unit". Own-unit
    /// models are ignored (their spacing is handled by cohesion/overlap); reaching contact still leaves a
    /// charger inside melee range, so this doesn't block charges.</summary>
    private float EnemyClampTravel(Position anchor, float dirX, float dirZ, float allowed,
        IModel movingModel, Float2 facing, DefineMovementPathRequest request)
    {
        var start = new Float2(anchor.x, anchor.z);
        var end   = new Float2(anchor.x + dirX * allowed, anchor.z + dirZ * allowed);
        IUnit ownUnit = request.UnitDataBinding.GetValue();
        float limited = allowed;

        foreach (var unit in _tableState.Units.Objects)
        {
            if (ReferenceEquals(unit, ownUnit)) continue;
            foreach (var m in unit.Models)
            {
                if (!m.GetIsAlive()) continue;
                var mp = m.Position;
                if (mp.x == 0f && mp.z == 0f) continue; // unplaced (reserve) model
                float t = BaseShapeGeometry.MaxTravelBeforeBaseCollision(
                    movingModel.BaseShape, start, end, facing, m.BaseShape, mp, m.Facing);
                if (t < limited) limited = t;
            }
        }
        if (limited < allowed) limited = MathF.Max(0f, limited - EnemyClampMargin);
        return limited;
    }

    private List<EnemyModelFootprint> GetEnemyFootprintsForRequest(DefineMovementPathRequest request)
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

    private bool WouldOverlapAnyModel(Position ghostPos, Float2 ghostFacing, IModel ghostModel,
        DefineMovementPathRequest request,
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

                // Same-unit models follow their planned path's end; others stay where they are.
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
                // Shape- and facing-aware (#150) so the cohesion readout matches the server's measurement; a
                // model's current facing stands in for its projected end facing (this drives a display/repair
                // hint the server re-validates).
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

    private void DrawTargeting(ImDrawListPtr dl, int screenW, int screenH,
        DefineMovementPathRequest request,
        PathTemplate pt,
        IReadOnlyDictionary<IModel, IReadOnlyList<Position>> paths,
        Position? ghostPos, float ghostExtraDist, bool group)
    {
        // Shooting aggregate is driven by committed paths so it doesn't flicker on rush-boundary hover.
        // Per-line shooting from the selected model + the charge overlay are ghost-aware.
        bool canShootCommitted = true;
        foreach (var m in paths.Keys)
            if (pt.GetTotalDistanceMoved(m) > request.MaxAdvanceDistance + 0.0001f) { canShootCommitted = false; break; }

        bool groupGhost = group && _groupGhostPositions != null;

        // Committed positions per own model (single-mode shooting aggregate uses these as-is).
        var committed = new Dictionary<IModel, Position>(paths.Count);
        foreach (var kvp in paths)
            committed[kvp.Key] = kvp.Value.Count > 0 ? kvp.Value[^1] : kvp.Key.Position;

        // Ghost-aware positions per own model. Single mode moves only the selected model to its live
        // ghost; group mode moves the whole unit to its pending phantom positions.
        var ghostAware = new Dictionary<IModel, Position>(committed);
        if (groupGhost)
        {
            foreach (var kvp in _groupGhostPositions!)
                if (ghostAware.ContainsKey(kvp.Key)) ghostAware[kvp.Key] = kvp.Value;
        }
        else if (_selectedModel != null && ghostPos.HasValue)
            ghostAware[_selectedModel] = ghostPos.Value;

        // Can the unit still shoot after this (ghost-aware) move? Every model must stay within Advance.
        bool canShootWithGhost = canShootCommitted;
        if (canShootWithGhost)
            foreach (var m in paths.Keys)
            {
                float total = pt.GetTotalDistanceMoved(m)
                    + Position.GetDistance2D(pt.GetModelLastPathPosition(m), ghostAware[m]);
                if (total > request.MaxAdvanceDistance + 0.0001f) { canShootWithGhost = false; break; }
            }

        // In group mode the aggregate counts track the live phantom; in single mode they stay on the
        // committed positions so they don't flicker as the one selected model crosses the rush boundary.
        var aggShootPos = groupGhost ? ghostAware : committed;
        bool aggCanShoot = groupGhost ? canShootWithGhost : canShootCommitted;

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
        // Skip enemy units not yet on the table: an Ambush unit sits in reserve at the origin (0,0,0) until
        // it arrives, so without this the overlay would draw fire lines to the table corner (GetIsOnBattlefield),
        // and the per-model checks below drop any stray unplaced model for good measure.
        foreach (IUnit enemyUnit in _tableState.Units.Objects)
        {
            if (enemyUnit.PlayerID == ourPlayerID || !enemyUnit.GetIsOnBattlefield()) continue;
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

        // #042 Indirect/Takedown/Blast: per-weapon sight overrides from the request, so the post-move
        // targeting overlay reflects weapons that ignore line of sight (a target behind terrain is still
        // shootable) or cover (no +1, no dashed "cover" line). Keyed by weapon name (unit-scoped today).
        var sightByWeapon = new Dictionary<string, (bool ignoresLoS, bool ignoresCover, string? losRule, string? coverRule)>();
        foreach (var profile in request.WeaponSightProfiles)
            sightByWeapon[profile.Weapon.Name] = (profile.IgnoresTerrain, profile.IgnoresCover,
                profile.LineOfSightIgnoreRule, profile.CoverIgnoreRule);
        bool WeaponIgnoresLoS(IWeapon w) => sightByWeapon.TryGetValue(w.Name, out var s) && s.ignoresLoS;
        bool WeaponIgnoresCover(IWeapon w) => sightByWeapon.TryGetValue(w.Name, out var s) && s.ignoresCover;
        // #052: the alias-aware attribution suffix for a weapon, e.g. " (Indirect ignores line of sight)".
        string WeaponSightSuffix(IWeapon w) => sightByWeapon.TryGetValue(w.Name, out var s)
            ? SightRuleLabel.Parenthetical(s.coverRule, s.losRule) : string.Empty;

        // #102: per-(weapon, enemy-unit) effective shooting range from the request, for when a range-modifier
        // rule changes it (Increased Shooting Range widens the mover's reach; Ranged Shrouding narrows it for
        // that enemy). Falls back to the weapon's base range — keeps this post-move preview in step with the
        // authoritative ChooseRangedAttackStage range check.
        var rangeOverrideByWeaponAndEnemy = new Dictionary<(string, UnitID), float>();
        foreach (var ov in request.WeaponRangeOverrides)
            rangeOverrideByWeaponAndEnemy[(ov.WeaponName, ov.EnemyUnitId)] = ov.EffectiveRangeInches;
        float EffectiveWeaponRange(IWeapon w, IUnit enemyUnit) =>
            rangeOverrideByWeaponAndEnemy.TryGetValue((w.Name, enemyUnit.ID), out float r) ? r : w.RangeInches;

        // 1) Per-enemy-unit aggregate text: shooting weapon counts (green) + charger count (yellow), combined.
        foreach (IUnit enemyUnit in _tableState.Units.Objects)
        {
            if (enemyUnit.PlayerID == ourPlayerID || !enemyUnit.GetIsOnBattlefield()) continue;
            var aliveEnemies = enemyUnit.Models
                .Where(em => em.GetIsAlive() && (em.Position.x != 0f || em.Position.z != 0f)).ToList();
            if (aliveEnemies.Count == 0) continue;

            // Shooting weapon counts (group: live phantom; single: committed), only when our unit can shoot.
            var weaponCounts = new Dictionary<string, int>();
            if (aggCanShoot)
            {
                foreach (var kvp in aggShootPos)
                {
                    IModel ourModel = kvp.Key;
                    Position from = kvp.Value;
                    foreach (var w in ourModel.Weapons)
                    {
                        if (!w.IsRanged()) continue;
                        bool ignoresLoS = WeaponIgnoresLoS(w);
                        bool inRange = false;
                        foreach (var em in aliveEnemies)
                        {
                            float b2b = DistanceUtilities.GetBaseToBaseDistanceInches_2D(
                                from, em.Position, ourModel.BaseShape, ourModel.Facing, em.BaseShape, em.Facing);
                            if (b2b > EffectiveWeaponRange(w, enemyUnit)) continue;
                            // Indirect/Takedown ignore line of sight, so only range gates them.
                            if (!ignoresLoS && !LoSCommitted(ourModel, from, em, enemyUnit)) continue;
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
                        from, em.Position, ourModel.BaseShape, ourModel.Facing, em.BaseShape, em.Facing);
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
                if (enemyUnit.PlayerID == ourPlayerID || !enemyUnit.GetIsOnBattlefield()) continue;
                foreach (var em in enemyUnit.Models)
                {
                    if (!em.GetIsAlive()) continue;
                    float b2b = DistanceUtilities.GetBaseToBaseDistanceInches_2D(
                        ghostPos.Value, em.Position, _selectedModel.BaseShape, _selectedModel.Facing, em.BaseShape, em.Facing);
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

        // 2) Fire lines from the shooting model(s). Single mode draws just the selected model
        // (ghost-aware, with weapon-name labels and blocked-shot stubs). Group mode draws a clear-shot
        // line from EVERY model in the unit (committed positions) but suppresses per-line labels and
        // blocked stubs — the per-enemy aggregate above already lists "Nx Weapon", so labels here would
        // just be noise.
        if (!canShootWithGhost) return;

        var attackers = new List<(IModel model, Position pos)>();
        if (group)
        {
            // Whole unit, live phantom positions — fire lines track the mouse before committing.
            foreach (var kvp in ghostAware) attackers.Add((kvp.Key, kvp.Value));
        }
        else
        {
            if (_selectedModel == null) return;
            attackers.Add((_selectedModel, ghostPos ?? committed[_selectedModel]));
        }
        bool drawLabels = !group;
        const float stagger = 6f;

        foreach (var (attacker, selPos) in attackers)
        {
            var selRanged = attacker.Weapons.Where(w => w.IsRanged()).ToList();
            if (selRanged.Count == 0) continue;

            // For each weapon, prefer the nearest in-range enemy model with line of sight (clear shot).
            // If no in-range model has LoS, fall back to the nearest in-range model overall as a
            // "blocked" target — rendered (single mode only) with a red stub + X at the block point.
            var byTarget = new Dictionary<IModel, List<IWeapon>>();
            var coverTargets = new HashSet<IModel>(ReferenceEqualityComparer.Instance);
            var blockedByTarget = new Dictionary<IModel, (List<IWeapon> weapons, IUnit enemyUnit)>();
            foreach (IUnit enemyUnit in _tableState.Units.Objects)
            {
                if (enemyUnit.PlayerID == ourPlayerID || !enemyUnit.GetIsOnBattlefield()) continue;
                var aliveEnemies = enemyUnit.Models
                .Where(em => em.GetIsAlive() && (em.Position.x != 0f || em.Position.z != 0f)).ToList();
                if (aliveEnemies.Count == 0) continue;

                var unitBlockers = blockersByEnemyUnit[enemyUnit];
                foreach (var w in selRanged)
                {
                    bool ignoresLoS = WeaponIgnoresLoS(w);
                    bool ignoresCover = WeaponIgnoresCover(w);
                    IModel? nearestClear   = null; float nearestClearB2B   = float.MaxValue;
                    IModel? nearestInRange = null; float nearestInRangeB2B = float.MaxValue;
                    ESightLineEffect nearestClearEffect = ESightLineEffect.Clear;
                    foreach (var em in aliveEnemies)
                    {
                        float b2b = DistanceUtilities.GetBaseToBaseDistanceInches_2D(
                            selPos, em.Position, attacker.BaseShape, attacker.Facing, em.BaseShape, em.Facing);
                        if (b2b > EffectiveWeaponRange(w, enemyUnit)) continue;
                        if (b2b < nearestInRangeB2B) { nearestInRangeB2B = b2b; nearestInRange = em; }
                        var effect = LineOfSightUtilities.EvaluateSightLine(selPos, em.Position, unitBlockers);
                        // #042 Indirect/Takedown ignore LoS (a blocked line still hits); Blast/Indirect/Takedown
                        // ignore cover (no dashed cover line). Normalise the effect so the shot draws as clear.
                        if (ignoresLoS && effect == ESightLineEffect.Blocking) effect = ESightLineEffect.Clear;
                        if (ignoresCover && effect == ESightLineEffect.Cover) effect = ESightLineEffect.Clear;
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

                if (!drawLabels) continue;

                // Weapon name labels stacked beside the line midpoint (screen-right by default,
                // flipped to screen-left if right side would clip the window edge). When the shot
                // passes through cover terrain, append "(cover)" so the +1 defense modifier shows inline.
                float mx = (ax + bx) * 0.5f, my = (ay + by) * 0.5f;
                float blockH = n * lineH;
                float blockW = 0f;
                string[] labels = new string[n];
                var sizes = new Vector2[n];
                for (int i = 0; i < n; i++)
                {
                    string coverTag = inCover ? " (cover)" : string.Empty;
                    labels[i] = $"{weapons[i].Name}{coverTag}{WeaponSightSuffix(weapons[i])}";
                    sizes[i] = ImGui.CalcTextSize(labels[i]);
                    if (sizes[i].X > blockW) blockW = sizes[i].X;
                }
                const float margin = 8f;
                float xLeftAnchor  = mx + margin;
                float xRightAnchor = mx - margin - blockW;
                float xAnchor = xLeftAnchor + blockW <= screenW - 4f ? xLeftAnchor : xRightAnchor;
                xAnchor = Math.Clamp(xAnchor, 4f, MathF.Max(4f, screenW - blockW - 4f));
                float yTop = my - blockH * 0.5f;
                yTop = Math.Clamp(yTop, 4f, MathF.Max(4f, screenH - blockH - 4f));
                for (int i = 0; i < n; i++)
                    dl.AddText(new Vector2(xAnchor, yTop + i * lineH), thisTextCol, labels[i]);
            }

            // Blocked-shot stubs are single-mode only (too noisy when every model draws them).
            if (!drawLabels) continue;
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

        // #155: shrink the shared step so no model's straight path violates the difficult-terrain cap
        // (the whole unit moves by one delta, so the most-constrained model governs). Without this an
        // auto-advance across difficult terrain could submit a move the authoritative stage rejects.
        if (!request.IgnoresDifficultTerrain && step > 0.001f)
        {
            var terrain = _tableState.Terrain.Objects.ToList();
            float ux = dx / dist, uz = dz / dist;
            foreach (var mb in models)
            {
                if (mb.GetValue() is not IModel m || !m.GetIsAlive()) continue;
                var from = new Float2(m.Position.x, m.Position.z);
                var to   = new Float2(m.Position.x + ux * step, m.Position.z + uz * step);
                step = MathF.Min(step, MovementUtilities.ClampTravelForDifficultTerrain(
                    from, to, traveledBeforeSegmentInches: 0f, pathAlreadyCrossedDifficultTerrain: false,
                    m.BaseShape, m.Facing, terrain, request.IgnoresDifficultTerrain));
            }
            if (step <= 0.001f) return StayInPlace(request);
        }

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
