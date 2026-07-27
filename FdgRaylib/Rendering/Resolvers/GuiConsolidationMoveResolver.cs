using System.Numerics;
using FDG;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FdgRaylib.Rendering.Previews;
using ImGuiNET;
using FdgRaylib.Placement;

namespace FdgRaylib.Rendering.Resolvers;

public class GuiConsolidationMoveResolver
    : IStageResolver<ConsolidationMoveRequest, List<ModelMoveEntry>>, IGuiResolver, IGuiCanvasOverlay,
      IPreviewSource
{
    private readonly ITableState _tableState;
    // #215: shared Group/Single toggle (same instance the movement + deployment resolvers use).
    private readonly FormationModeState _formationMode;
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

    // #215 group-mode state (main-thread only): rotation of the CURRENT pending step, and the accumulated
    // facing rotation across the whole move (baked into each committed step's facings). Reset per request.
    private float _groupRotation;
    private float _groupFacingAngle;
    private const float GroupMoveSafetyMargin = 0.005f;

    // #280 remote-preview snapshot (main-thread only, rewritten each Draw): the live pending
    // position per model - the mouse ghost in single mode, every phantom in group mode.
    // BuildPreviewState trusts it only while _snapshotRequest matches the pending request, so a
    // fresh request never streams the previous move's ghosts.
    private readonly Dictionary<IModel, Position> _ghostSnapshot = new();
    private ConsolidationMoveRequest? _snapshotRequest;

    /// <summary>
    /// #280: the consolidation being planned, as the shared ghost+path vocabulary - committed
    /// waypoints per model in the "base" slot, live ghost/phantom positions in the "ghost" slot.
    /// All Neutral band: consolidation has no advance/rush/charge semantics. Called by the preview
    /// publisher on the main thread after Draw, so the ghost snapshot is this frame's.
    /// </summary>
    public PreviewState? BuildPreviewState()
    {
        ConsolidationMoveRequest? request;
        PathTemplate? pt;
        lock (_lock) { request = _request; pt = _pathTemplate; }
        if (request == null || pt == null) return null;

        bool group = _formationMode.IsGroup;
        bool ghostSnapshotValid = ReferenceEquals(_snapshotRequest, request);
        var paths = pt.CurrentPaths; // allocates per access - hold it

        var baseModels = new List<GhostPathBaseModel>(paths.Count);
        var ghosts = new List<GhostPathGhost>();
        int contentHash = 17;
        int index = 0;

        foreach (var kvp in paths)
        {
            IModel m = kvp.Key;
            IReadOnlyList<Position> waypoints = kvp.Value;

            var points = new List<GhostPathPoint>(waypoints.Count);
            foreach (Position p in waypoints)
                points.Add(new GhostPathPoint(PreviewQuantize.Inches(p.x), PreviewQuantize.Inches(p.z)));

            // Consolidation slides without rotating (#250); group mode's wheel rotation is the only
            // facing change, applied to the model's own facing rather than the travel direction.
            // #283: the committed endpoint keeps the rotation its step was committed with; only the
            // live ghost follows the wheel - mirrors the local draw.
            IReadOnlyList<float> stored = pt.GetModelFacingOffsets(m);
            Float2 committedFacing = RotateFloat2(m.Facing, stored.Count > 0 ? stored[^1] : 0f);
            Float2 ghostFacing = group ? RotateFloat2(m.Facing, _groupFacingAngle) : m.Facing;
            float qFacingX = PreviewQuantize.Inches(ghostFacing.X);
            float qFacingZ = PreviewQuantize.Inches(ghostFacing.Y);

            baseModels.Add(new GhostPathBaseModel(m.ID.ID, points,
                PreviewQuantize.Inches(committedFacing.X), PreviewQuantize.Inches(committedFacing.Y),
                GhostPathBands.Neutral));

            unchecked
            {
                contentHash = contentHash * 31 + m.ID.ID.GetHashCode();
                foreach (GhostPathPoint p in points)
                {
                    contentHash = contentHash * 31 + p.X.GetHashCode();
                    contentHash = contentHash * 31 + p.Z.GetHashCode();
                }
            }

            Position committed = pt.GetModelLastPathPosition(m);
            if (ghostSnapshotValid && _ghostSnapshot.TryGetValue(m, out Position ghost)
                && Position.GetDistance2D(committed, ghost) > PreviewQuantize.GhostEpsilonInches)
            {
                ghosts.Add(new GhostPathGhost(index, PreviewQuantize.Inches(ghost.x),
                    PreviewQuantize.Inches(ghost.z), qFacingX, qFacingZ, GhostPathBands.Neutral));
            }
            index++;
        }

        return new PreviewState(request.TargetPlayerID, new List<PreviewSlotPayload>
        {
            new PreviewSlotPayload(GhostPathSlots.Base, new GhostPathBase(contentHash, baseModels)),
            new PreviewSlotPayload(GhostPathSlots.Ghost, new GhostPathGhosts(contentHash, ghosts)),
        });
    }

    // #277: group-mode formation options (index 0 = current shape). Built lazily per request; back to
    // "current" on each commit (the committed step bakes the picked shape into the waypoints).
    private FormationCycle? _formationCycle;

    private static readonly uint MoveColor      = ImGui.ColorConvertFloat4ToU32(new Vector4(0.40f, 0.85f, 1.00f, 0.95f));
    private static readonly uint RangeRingCol   = ImGui.ColorConvertFloat4ToU32(new Vector4(0.40f, 0.85f, 1.00f, 0.55f));
    private static readonly uint SelectionOutline = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.95f));
    private static readonly uint ModelOutline   = ImGui.ColorConvertFloat4ToU32(new Vector4(0.7f, 0.7f, 0.7f, 0.7f));
    // #295: hover on an unselected model in single mode -- a dimmer, filled version of the white selection
    // outline, so it reads as "this is about to BE the selection" (and never as the cyan move preview).
    private static readonly uint HoverOutline   = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.75f));
    private static readonly uint HoverFill      = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.18f));
    private static readonly uint GhostOutline   = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.85f));
    private static readonly uint FinalGhostCol  = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.25f));
    private static readonly uint CohesionLineCol = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.55f, 0.55f, 0.90f));
    private static readonly uint OverlapFill    = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.25f, 0.25f, 0.55f));
    private static readonly uint GhostFill      = ImGui.ColorConvertFloat4ToU32(new Vector4(0.40f, 0.85f, 1.00f, 0.40f));

    public GuiConsolidationMoveResolver(ITableState tableState, FormationModeState formationMode)
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
            _groupRotation = 0f;
            _groupFacingAngle = 0f;
            _formationCycle = null;
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

        // #280: rebuilt below from this frame's ghost/phantom positions.
        _ghostSnapshot.Clear();
        _snapshotRequest = request;

        var io      = ImGui.GetIO();
        var dl      = ImGui.GetBackgroundDrawList();
        var terrain = _tableState.Terrain.Objects.ToList();
        var paths   = pt.CurrentPaths;

        float maxDist = request.MaxDistanceInches;

        bool overTable = IsOverTable(io.MousePos.X, io.MousePos.Y);
        bool wantInput = !io.WantCaptureMouse && !io.WantCaptureKeyboard;

        // #295: single mode switches models by clicking the model you want (Space used to cycle; Space now
        // confirms). One hit test up front feeds both the hover highlight and the click, so what lights up
        // is exactly what a click selects. Group mode has no per-model selection, so nothing is hoverable.
        IModel? hoveredModel = null;
        if (!_formationMode.IsGroup && overTable && !io.WantCaptureMouse)
        {
            var (hx, hz) = PixelToInches(io.MousePos.X, io.MousePos.Y);
            hoveredModel = ModelPicker.HitTest(paths.Keys, hx, hz);
        }

        // 1) Draw each model's start circle + committed path + final ghost
        foreach (var kvp in paths)
        {
            var model = kvp.Key;
            var pathPoints = kvp.Value;
            var start = model.Position;
            var (sx, sy) = InchesToPixel(start.x, start.z);

            bool isSelected = ReferenceEquals(model, _selectedModel);
            bool isHovered  = !isSelected && ReferenceEquals(model, hoveredModel);
            uint outline = isSelected ? SelectionOutline : isHovered ? HoverOutline : ModelOutline;
            float thick  = isSelected || isHovered ? 2.5f : 1.5f;
            // #250: consolidation slides without rotating, so every ghost/outline keeps the model's facing.
            // #295: the hovered model also gets a wash, so "click me to switch" reads at a glance.
            if (isHovered)
                ModelBaseRenderer.DrawFilledImGui(dl, model.BaseShape, new Vector2(sx, sy), _scale, HoverFill,
                    outline, thick, facing: model.Facing);
            else
                ModelBaseRenderer.DrawOutlineImGui(dl, model.BaseShape, new Vector2(sx, sy), _scale, outline, thick,
                    facing: model.Facing);

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
                // #283: the committed marker keeps the rotation its step was committed with (0 outside group
                // mode = the model's own facing) - it no longer snaps back unrotated after a rotated commit,
                // and a later wheel turn only moves the phantoms.
                IReadOnlyList<float> storedOffsets = pt.GetModelFacingOffsets(model);
                Float2 committedFacing = RotateFloat2(model.Facing, storedOffsets.Count > 0 ? storedOffsets[^1] : 0f);
                ModelBaseRenderer.DrawFilledImGui(dl, model.BaseShape, new Vector2(lx, ly), _scale, FinalGhostCol, outline, thick,
                    facing: committedFacing);
            }
        }

        // #215: G toggles Group/Single for the rest of the game (shared with movement/deployment).
        if (wantInput && ImGui.IsKeyPressed(ImGuiKey.G)) _formationMode.Toggle();

        if (_formationMode.IsGroup)
        {
            DrawGroupMode(request, pt, dl, io, terrain, overTable, wantInput);
            DrawInfoPanel(screenW, request, pt, tcs, terrain);
            return;
        }

        // ---- Single mode ----
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

            // #291: stop at the table edge so the model's whole BASE stays on the board. Measured against
            // the true oriented footprint via the shared engine clamp - the old circumscribing-radius
            // clamp held a rectangular vehicle over an inch further out than the rule requires.
            if (allowed > 0.0001f && dist > 0.0001f)
                allowed = MovementUtilities.ClampTravelToTable(anchor, dx / dist, dz / dist, allowed,
                    _selectedModel.BaseShape, _selectedModel.Facing);

            float nx, nz;
            if (dist < 0.0001f) { nx = anchor.x; nz = anchor.z; }
            else                { nx = anchor.x + dx / dist * allowed; nz = anchor.z + dz / dist * allowed; }
            ghostPos = new Position(nx, nz);
            _ghostSnapshot[_selectedModel] = ghostPos.Value; // #280

            // Consolidation slides without rotating, so the ghost keeps the model's facing.
            ghostOverlaps = WouldOverlapAnyModel(ghostPos.Value, _selectedModel.Facing, _selectedModel, request, paths);

            var (apx, apy) = InchesToPixel(anchor.x, anchor.z);
            var (gx, gy)   = InchesToPixel(nx, nz);
            dl.AddLine(new Vector2(apx, apy), new Vector2(gx, gy), MoveColor, 2f);

            ModelBaseRenderer.DrawFilledImGui(dl, _selectedModel.BaseShape, new Vector2(gx, gy), _scale,
                ghostOverlaps ? OverlapFill : GhostFill, GhostOutline, facing: _selectedModel.Facing);

            var finalsWithGhost = BuildFinalPositions(paths, _selectedModel, ghostPos);
            DrawCohesionIndicators(dl, finalsWithGhost, _selectedModel);
        }

        // 4) Input
        if (overTable && !io.WantCaptureMouse)
        {
            // Left-click: select a model whose start footprint is hit (#295 -- the only way to switch models
            // now that Space commits); otherwise place a waypoint for the selected model at the clamped ghost
            // position. Left-click places (consistent with movement).
            if (ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                if (hoveredModel != null)
                {
                    _selectedModel = hoveredModel;
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

        // #295: Space no longer cycles models here -- click the model you want (hover-highlighted above),
        // which frees Space to join Enter as the universal Confirm key.

        DrawInfoPanel(screenW, request, pt, tcs, terrain);
    }

    // #215: whole-unit group move. Drag translates the formation so its centroid follows the cursor; the
    // mouse wheel / R rotates the formation (Shift+R the other way). Reuses the same GroupFormationUtilities
    // (budget solve + out-of-coherency repair) the normal-move group mode uses; the authoritative
    // ValidateConsolidationPaths in DrawInfoPanel still gates Done, so this only previews/commits legal steps.
    private void DrawGroupMode(ConsolidationMoveRequest request, PathTemplate pt, ImDrawListPtr dl,
        ImGuiIOPtr io, List<ITerrain> terrain, bool overTable, bool wantInput)
    {
        var paths  = pt.CurrentPaths;
        var models = paths.Keys.ToList();
        if (models.Count == 0) return;
        IUnit ownUnit = request.UnitDataBinding.GetValue();
        float maxDist = request.MaxDistanceInches;

        // Wheel/R rotate; Ctrl+Wheel cycles the target formation (#277, shared GroupInput semantics).
        var (rotationDelta, formationDelta) = GroupInput.Read(wantInput);
        if (rotationDelta != 0f) { _groupRotation += rotationDelta; _groupFacingAngle += rotationDelta; }
        if (_formationCycle == null)
        {
            var cycleRadii = models.Select(m => m.BaseShape.CircumscribedRadiusInches).ToList();
            _formationCycle = FormationCycle.Build(cycleRadii, cycleRadii, cycleRadii, includeCurrentShape: true);
        }
        if (formationDelta != 0) _formationCycle.Cycle(formationDelta);

        var lastPositions = new List<Position>(models.Count);
        var budgets       = new List<float>(models.Count);
        foreach (var m in models)
        {
            lastPositions.Add(pt.GetModelLastPathPosition(m));
            budgets.Add(maxDist - pt.GetTotalDistanceMoved(m) - GroupMoveSafetyMargin);
        }

        // Re-form an out-of-coherency unit (a mid-unit casualty left survivors >1" apart) toward its centroid,
        // so one drag can pull them back into cohesion; budgets are still measured from each real start.
        var startPairs = new List<(IModel model, Position pos)>(models.Count);
        for (int i = 0; i < models.Count; i++) startPairs.Add((models[i], lastPositions[i]));
        var radii = models.Select(m => m.BaseShape.CircumscribedRadiusInches).ToList();
        IReadOnlyList<Position> basePositions = lastPositions;
        if (_formationCycle is { IsCurrentShape: false })
        {
            // #277: morph toward the picked formation; the consolidation cap still bounds every model
            // (an unreachable shape shows over-budget red phantoms rather than moving anyone illegally).
            var offsets = FormationLibrary.PlanFormationOffsets(
                lastPositions, radii, radii, _formationCycle.Selected.RowCounts, gap: 0.1f);
            Position c0 = GroupFormationUtilities.Centroid(lastPositions);
            var formed = new Position[models.Count];
            for (int i = 0; i < models.Count; i++)
                formed[i] = new Position(c0.x + offsets[i].dx, c0.z + offsets[i].dz);
            basePositions = formed;
        }
        else if (CheckCohesion(startPairs).Any)
        {
            basePositions = GroupFormationUtilities.RepairCoherencyByContraction(
                lastPositions, radii,
                GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES,
                GameWideConstants.MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES);
        }

        Position pivot = GroupFormationUtilities.Centroid(basePositions);
        float cos = MathF.Cos(_groupRotation), sin = MathF.Sin(_groupRotation);

        float desiredTx = 0f, desiredTz = 0f;
        if (overTable && !io.WantCaptureMouse)
        {
            var (mx, mz) = PixelToInches(io.MousePos.X, io.MousePos.Y);
            desiredTx = mx - pivot.x;
            desiredTz = mz - pivot.z;
        }

        var plan = GroupFormationUtilities.PlanGroupMove(basePositions, lastPositions, budgets, pivot, cos, sin, desiredTx, desiredTz);
        var newPositions = plan.NewPositions;
        // #291: hold each model's step short of the table edge, measured against its true oriented
        // footprint. Per-model (rather than scaling the whole step) preserves this resolver's existing
        // behaviour; consolidation steps are 1-3" so the formation barely deforms.
        for (int i = 0; i < models.Count; i++)
        {
            Position from = lastPositions[i];
            Position to = newPositions[i];
            float stepDist = Position.GetDistance2D(from, to);
            if (stepDist <= 0.0001f) continue;
            float allowedStep = MovementUtilities.ClampTravelToTable(from,
                (to.x - from.x) / stepDist, (to.z - from.z) / stepDist, stepDist,
                models[i].BaseShape, models[i].Facing);
            if (allowedStep >= stepDist) continue;
            newPositions[i] = new Position(from.x + (to.x - from.x) / stepDist * allowedStep,
                                           from.z + (to.z - from.z) / stepDist * allowedStep);
        }

        var groupFacings = new Float2[models.Count];
        var blocked      = new bool[models.Count];
        bool anyMovement = false;
        for (int i = 0; i < models.Count; i++)
        {
            groupFacings[i] = RotateFloat2(models[i].Facing, _groupFacingAngle);
            blocked[i] = PhantomOverlapsOtherUnit(newPositions[i], models[i].BaseShape, groupFacings[i], ownUnit);
            if (Position.GetDistance2D(lastPositions[i], newPositions[i]) > 0.001f) anyMovement = true;
            _ghostSnapshot[models[i]] = newPositions[i]; // #280
        }
        bool allValid = plan.WithinBudget && !blocked.Any(b => b);

        for (int i = 0; i < models.Count; i++)
        {
            bool bad = blocked[i] || !plan.WithinBudget;
            var (sx, sy) = InchesToPixel(lastPositions[i].x, lastPositions[i].z);
            var (nx, ny) = InchesToPixel(newPositions[i].x, newPositions[i].z);
            dl.AddLine(new Vector2(sx, sy), new Vector2(nx, ny), bad ? OverlapFill : MoveColor, 2f);
            ModelBaseRenderer.DrawFilledImGui(dl, models[i].BaseShape, new Vector2(nx, ny), _scale,
                bad ? OverlapFill : GhostFill, GhostOutline, 1.5f, groupFacings[i]);
            ModelBaseRenderer.DrawHeadingImGui(dl, models[i].BaseShape, new Vector2(nx, ny), _scale, groupFacings[i], GhostOutline);
        }

        var finals = new List<(IModel model, Position pos)>(models.Count);
        for (int i = 0; i < models.Count; i++) finals.Add((models[i], newPositions[i]));
        DrawCohesionIndicators(dl, finals, null);

        if (overTable && !io.WantCaptureMouse && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && allValid && anyMovement)
        {
            for (int i = 0; i < models.Count; i++) pt.AddStep(models[i], newPositions[i], _groupFacingAngle); // #283
            _groupRotation = 0f; // rotation is folded into _groupFacingAngle (baked into the committed facings)
            _formationCycle?.Reset(); // #277: the committed step's shape is the current shape now
        }

        // Right-click / Backspace undo the last committed group step (one per model).
        if ((wantInput && ImGui.IsKeyPressed(ImGuiKey.Backspace))
            || (overTable && !io.WantCaptureMouse && ImGui.IsMouseClicked(ImGuiMouseButton.Right)))
        {
            foreach (var m in models)
                if (paths.TryGetValue(m, out var l) && l.Count > 0) pt.RemoveLastStep(m);
        }
    }

    private static Float2 RotateFloat2(Float2 f, float radians)
    {
        float cos = MathF.Cos(radians), sin = MathF.Sin(radians);
        return new Float2(f.X * cos - f.Y * sin, f.X * sin + f.Y * cos);
    }

    // True if the shape at p (facing) overlaps a live model of ANOTHER unit. Own-unit models are ignored -
    // the group moves rigidly, so its internal spacing is preserved (and cohesion is checked separately).
    private bool PhantomOverlapsOtherUnit(Position p, IBaseShape shape, Float2 facing, IUnit ownUnit)
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
        return false;
    }

    private void DrawInfoPanel(int screenW, ConsolidationMoveRequest request, PathTemplate pt,
        TaskCompletionSource<List<ModelMoveEntry>> tcs, List<ITerrain> terrain)
    {
        float panelW = ResolverPanelLayout.W;   // dock into the right-column resolver panel
        ImGui.SetNextWindowPos(new Vector2(ResolverPanelLayout.X, ResolverPanelLayout.Y), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(ResolverPanelLayout.W, ResolverPanelLayout.H), ImGuiCond.Always);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.10f, 0.10f, 0.15f, 0.92f));
        ImGui.Begin("##ConsolidatePanel",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBringToFrontOnFocus);
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

        // #215: Group/Single toggle (shared with the movement + deployment resolvers).
        if (ImGui.Button(_formationMode.IsGroup ? "Mode: Group (G)" : "Mode: Single (G)"))
            _formationMode.Toggle();
        if (_formationMode.IsGroup)
        {
            string formation = _formationCycle == null
                ? "current"
                : $"{_formationCycle.Label} ({_formationCycle.Index + 1}/{_formationCycle.Count})";
            ImGui.TextUnformatted($"Formation: {formation}");
            ImGui.TextDisabled("Drag: move unit   Wheel/R: rotate   Ctrl+Wheel: formation\nL-click: commit   R-click/Bksp: undo");
        }
        else
            ImGui.TextDisabled("L-click a model: switch to it   L-click elsewhere: place waypoint\nR-click/Bksp: undo");

        ImGui.Spacing();
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        float pad     = ImGui.GetStyle().WindowPadding.X * 2;
        float fullW   = panelW - pad;

        // #283: the group-mode rotation reaches the executed move as rotate-in-place facings - the model's
        // own facing rotated by the offset each step was committed with (matching the phantoms), never the
        // travel direction. Unrotated paths carry identity facings, so single mode is unchanged.
        var results = pt.GetResultsAsList(EPathFacingDerivation.RotateInPlace);
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
        // Primary: Done -- larger, accented, commits on click or the Confirm key (gated on a valid move).
        bool donePressed = ResolverButtons.Primary("Done", new Vector2(fullW, 34f), enabled: canSubmit);
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            ImGui.SetTooltip(canSubmit
                ? $"Commit this move and continue. {ResolverKeybinds.Confirm.Parenthetical}"
                : string.Join("\n", issues));
        if (donePressed)
        {
            Complete(tcs, results);
            ImGui.End();
            return;
        }

        // Secondary: Stay in place (de-emphasized).
        bool stayPressed = ResolverButtons.Deemphasized("Stay in place", new Vector2(fullW, 28f));
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Don't move the unit. Every model stays where it is.");
        if (stayPressed)
        {
            pt.ClearAllSteps();
            Complete(tcs, pt.GetResultsAsList());
            ImGui.End();
            return;
        }

        // Destructive: Clear -- set apart below a separator, red-tinted.
        ImGui.Separator();
        bool clearPressed = ResolverButtons.Destructive("Clear selected", new Vector2(fullW, 26f));
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Remove all waypoints from the currently selected model.");
        if (clearPressed && _selectedModel != null) pt.ClearModelSteps(_selectedModel);

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
            if (!TeamAwareness.IsEnemyUnit(_tableState, request.TargetPlayerID, u)) continue;
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
            _groupRotation = 0f;
            _groupFacingAngle = 0f;
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

}
