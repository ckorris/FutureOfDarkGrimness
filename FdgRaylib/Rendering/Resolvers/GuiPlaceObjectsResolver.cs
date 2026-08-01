using System.Numerics;
using FDG;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FdgRaylib.Placement;
using FdgRaylib.Rendering.Previews;
using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

public class GuiPlaceObjectsResolver<T>
    : IStageResolver<PlaceObjectsRequest<T>, CancellableResult<List<PlacedObjectEntry<T>>>>, IGuiResolver, IGuiCanvasOverlay,
      IEnemyExclusionProvider, IPreviewSource, IGhostFieldSource
{
    private readonly ITableState _tableState;
    private readonly FormationModeState _formationMode;
    private readonly object _lock = new();

    // Layout — main thread only
    private float _scale  = 10f;
    private float _tableH = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
    private int   _originX, _originY;

    // Request state
    private PlaceObjectsRequest<T>? _request;
    private TaskCompletionSource<CancellableResult<List<PlacedObjectEntry<T>>>>? _tcs;
    private readonly List<PlacedObjectEntry<T>> _placed = new();

    // Index into _placed of the model currently being re-placed by drag (single mode); null = none.
    private int? _dragIndex;

    // #269 — the models THIS request is placing, by reference. They are excluded from the on-table occupancy
    // scan: a reposition placement (Teleport / Fanatic / reposition-at-activation) starts with every model
    // still standing at its old spot, and PlacedObjectEntry defers the writes until the whole placement is
    // accepted, so an un-excluded scan has the unit blocking itself out of the very ground it is vacating.
    // Each model is still accounted for exactly once - at its NEW position, via the _placed overlap loop.
    // A no-op for deployment, where the models to place sit at the unplaced (0,0) sentinel and are skipped
    // anyway. Assigned under _lock in Resolve, alongside _request.
    private HashSet<object> _selfModels = new(ReferenceEqualityComparer.Instance);
    // Group-mode pending rotation (radians) about the formation centroid; reset on drop and on Resolve.
    private float _groupRotationDeploy;
    // Single-mode pending facing rotation (radians) applied to the model being placed / dragged (#150).
    private float _singleRotationDeploy;

    // #277: group-mode formation options for this request. For a reposition (Teleport / Fanatic /
    // reposition-at-activation) index 0 is the unit's CURRENT shape, unchanged; a fresh deployment has
    // no current shape, so index 0 is the first legal catalog entry (line when it fits the 9" span,
    // else two rows — the old single default). Built lazily on the first group frame; reset on Resolve.
    private FormationCycle? _formationCycle;

    // #280 remote-preview snapshot (main-thread only, rewritten each Draw): live cursor ghost /
    // group phantoms, keyed by index into the request's ModelsToPlace roster. BuildPreviewState
    // trusts it only while _snapshotRequest matches the pending request.
    private readonly Dictionary<int, (Position pos, Float2 facing)> _ghostSnapshot = new();
    private PlaceObjectsRequest<T>? _snapshotRequest;

    // #230: the same frame's intended positions keyed by model, for the tactical overlay's ghost-anchored
    // field (IGhostFieldSource). Kept beside the index-keyed snapshot above rather than derived from it on
    // demand, because the overlay reads this from the canvas pass while _request may already be gone.
    // Mirrors GuiDefineMovementResolver's GhostPositions, which the field was originally built to read.
    private readonly Dictionary<IModel, Position> _ghostFieldPositions = new();
    private IUnit? _ghostFieldUnit;

    /// <summary>
    /// #230 — the placement in progress, as an anchor for the opportunity field. Every model the request
    /// is placing that is currently drawn somewhere: the live cursor ghost / group phantoms first, then
    /// anything already dropped this placement. So the field follows the cursor while the spot is being
    /// aimed and grows as a unit is built model by model, and a picked-up model contributes at the cursor
    /// rather than at the spot it is being lifted out of.
    /// </summary>
    public bool TryGetGhostField(out IUnit unit, out IReadOnlyDictionary<IModel, Position> ghosts)
    {
        unit = _ghostFieldUnit!;
        ghosts = _ghostFieldPositions;
        return _ghostFieldUnit != null && _ghostFieldPositions.Count > 0;
    }

    /// <summary>
    /// #280: the placement being planned, as the shared ghost+path vocabulary. Committed
    /// placements ride the "base" slot as a single waypoint per placed model (click cadence);
    /// the live cursor ghost / group phantoms ride the "ghost" slot. Placements are appended in
    /// roster order, so _placed[i] pairs with ModelsToPlace[i] (the reach-ring code relies on the
    /// same invariant). Unplaced deployment models sit at the (0,0) sentinel; the presenter
    /// suppresses start-anchored lines for them. Model-only: a roster with any non-ModelData
    /// entry shares nothing (index pairing must stay 1:1 with the roster).
    /// </summary>
    public PreviewState? BuildPreviewState()
    {
        PlaceObjectsRequest<T>? request;
        lock (_lock) { request = _request; }
        if (request == null || request.ModelsToPlace.Count == 0) return null;

        bool ghostSnapshotValid = ReferenceEquals(_snapshotRequest, request);
        var baseModels = new List<GhostPathBaseModel>(request.ModelsToPlace.Count);
        var ghosts = new List<GhostPathGhost>();
        int contentHash = 17;

        for (int i = 0; i < request.ModelsToPlace.Count; i++)
        {
            if (request.ModelsToPlace[i].GetValue() is not ModelData m) return null;

            bool isPlaced = i < _placed.Count;
            var points = new List<GhostPathPoint>(isPlaced ? 1 : 0);
            Float2 facing = m.Facing;
            if (isPlaced)
            {
                PlacedObjectEntry<T> entry = _placed[i];
                points.Add(new GhostPathPoint(PreviewQuantize.Inches(entry.Position.x),
                    PreviewQuantize.Inches(entry.Position.z)));
                facing = entry.Facing ?? facing;
            }
            baseModels.Add(new GhostPathBaseModel(m.ID.ID, points,
                PreviewQuantize.Inches(facing.X), PreviewQuantize.Inches(facing.Y),
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

            if (ghostSnapshotValid && _ghostSnapshot.TryGetValue(i, out var ghost))
            {
                Position committed = isPlaced ? _placed[i].Position : m.Position;
                if (Position.GetDistance2D(committed, ghost.pos) > PreviewQuantize.GhostEpsilonInches)
                {
                    ghosts.Add(new GhostPathGhost(i, PreviewQuantize.Inches(ghost.pos.x),
                        PreviewQuantize.Inches(ghost.pos.z), PreviewQuantize.Inches(ghost.facing.X),
                        PreviewQuantize.Inches(ghost.facing.Y), GhostPathBands.Neutral));
                }
            }
        }

        return new PreviewState(request.TargetPlayerID, new List<PreviewSlotPayload>
        {
            new PreviewSlotPayload(GhostPathSlots.Base, new GhostPathBase(contentHash, baseModels)),
            new PreviewSlotPayload(GhostPathSlots.Ghost, new GhostPathGhosts(contentHash, ghosts)),
        });
    }

    // #214 reach rings. Green like the movement resolver's Advance ring — this IS a reach preview, so it
    // should read as the same kind of thing; dimmer for the models whose turn to be placed hasn't come.
    private static readonly uint ActiveReachRingCol  = ImGui.ColorConvertFloat4ToU32(new Vector4(0.25f, 0.95f, 0.25f, 0.75f));
    private static readonly uint PendingReachRingCol = ImGui.ColorConvertFloat4ToU32(new Vector4(0.25f, 0.95f, 0.25f, 0.30f));

    // Rotates a facing (unit normal) by `radians`, matching the position rotation matrix
    // (rx = dx·cos − dz·sin, rz = dx·sin + dz·cos). The deploy rotation applies relative to the zone's
    // default facing (toward the table centre — see PlacementUtilities.DefaultDeployFacing).
    private static Float2 RotateFloat2(Float2 f, float radians)
    {
        float cos = MathF.Cos(radians), sin = MathF.Sin(radians);
        return new Float2(f.X * cos - f.Y * sin, f.X * sin + f.Y * cos);
    }

    // The rotation (radians) that would take the default forward (0,1) to this facing.
    private static float FacingToRadians(Float2 f) => MathF.Atan2(-f.X, f.Y);

    private string? _errorMessage;
    private double  _errorExpiry;

    public GuiPlaceObjectsResolver(ITableState tableState, FormationModeState formationMode)
    {
        _tableState = tableState;
        _formationMode = formationMode;
    }

    public void UpdateLayout(float scale, int originX, int originY, float tableH)
    {
        _scale = scale; _originX = originX; _originY = originY; _tableH = tableH;
    }

    public bool HasPendingRequest { get { lock (_lock) return _request != null; } }

    public Task<CancellableResult<List<PlacedObjectEntry<T>>>> Resolve(PlaceObjectsRequest<T> request)
    {
        var tcs = new TaskCompletionSource<CancellableResult<List<PlacedObjectEntry<T>>>>();
        lock (_lock)
        {
            _tcs = tcs;
            _placed.Clear();
            _dragIndex = null;
            _groupRotationDeploy = 0f;
            _singleRotationDeploy = 0f;
            _formationCycle = null;
            _errorMessage = null;
            _selfModels = new HashSet<object>(ReferenceEqualityComparer.Instance);
            foreach (DataBinding<T> binding in request.ModelsToPlace)
            {
                if (binding.GetValue() is ModelData model) _selfModels.Add(model);
            }
            _request = request;
        }
        return tcs.Task;
    }

    public void Draw(int screenW, int screenH)
    {
        PlaceObjectsRequest<T>? request;
        TaskCompletionSource<CancellableResult<List<PlacedObjectEntry<T>>>>? tcs;
        lock (_lock) { request = _request; tcs = _tcs; }
        if (request == null || tcs == null) return;

        // #280: rebuilt below from this frame's ghost/phantom positions.
        _ghostSnapshot.Clear();
        _snapshotRequest = request;

        // Edge case: zero models to place — finish immediately
        if (request.ModelsToPlace.Count == 0)
        {
            Complete(tcs, new List<PlacedObjectEntry<T>>());
            return;
        }

        var io   = ImGui.GetIO();
        var dl   = ImGui.GetBackgroundDrawList();
        var zone = request.DeploymentZone;

        float minEnemyDist = request.MinDistanceFromEnemiesInches;
        var enemies = minEnemyDist > 0f ? GetEnemyPositions(request.TargetPlayerID) : _noEnemies;

        bool group = _formationMode.IsGroup;
        bool overTable = IsOverTable(io.MousePos.X, io.MousePos.Y);
        bool wantInput = !io.WantCaptureMouse && !io.WantCaptureKeyboard;

        DrawZone(dl, zone);
        DrawReachRings(dl, request);
        DrawPlacedSoFar(dl, _dragIndex ?? -1);

        // G toggles Group/Single for the rest of the game (shared with movement).
        if (wantInput && ImGui.IsKeyPressed(ImGuiKey.G))
        {
            _formationMode.Toggle();
            _dragIndex = null;
        }

        // #247: V is handled globally by the tactical overlay, not here — the same key must mean the same
        // thing while moving, placing and idle. The panel checkbox below drives the same flag.

        // The group follow-formation ghost only shows before the first drop. Once anything is on the
        // table, both modes switch to per-model editing (drag a placed model, or place a missing one),
        // so clicks no longer re-drop the whole unit. Use Restart to re-form from scratch.
        if (group && _placed.Count == 0)
            DrawGroupDeploy(dl, io, request, zone, enemies, minEnemyDist, overTable, wantInput);
        else
            DrawSingleDeploy(dl, io, request, zone, enemies, minEnemyDist, overTable);

        IUnit? owner = FindOwningUnit(request);

        // #230: republish this frame's intended positions for the tactical overlay's field. After the mode
        // handlers, which is what fills _ghostSnapshot.
        UpdateGhostField(request, owner);

        if (_errorMessage != null && ImGui.GetTime() > _errorExpiry) _errorMessage = null;

        DrawInfoPanel(screenW, request, tcs, owner);
    }

    /// <summary>
    /// #230 — rebuilds the by-model position map the tactical overlay anchors its field on, from this
    /// frame's ghosts and committed placements. Cleared when the placement isn't a unit's models
    /// (objectives, terrain) or nothing is on screen yet, so the field goes away with the ghosts.
    /// <para>#247: this reports what there IS to anchor on, not what should be drawn — the V toggle and the
    /// hover-beats-ghosts rule are the controller's call (<c>FieldAnchorPlan</c>), so the decision lives in
    /// one place instead of being half-made here.</para>
    /// </summary>
    private void UpdateGhostField(PlaceObjectsRequest<T> request, IUnit? owner)
    {
        _ghostFieldPositions.Clear();
        _ghostFieldUnit = owner;
        if (_ghostFieldUnit == null) return;

        for (int i = 0; i < request.ModelsToPlace.Count; i++)
        {
            if (request.ModelsToPlace[i].GetValue() is not ModelData model) continue;
            if (TryIntendedPosition(i, out Position at)) _ghostFieldPositions[model] = at;
        }
    }

    /// <summary>
    /// Where model <paramref name="index"/> is headed, as drawn on screen this frame: the live ghost wins
    /// over a committed placement (a picked-up model is drawn at the cursor, not where it was dropped), then
    /// anything already placed. False for a model that isn't on screen at all — still to place with the mouse
    /// off the table, or picked up and being dragged off-table.
    /// </summary>
    private bool TryIntendedPosition(int index, out Position position)
    {
        if (_ghostSnapshot.TryGetValue(index, out var ghost)) { position = ghost.pos; return true; }
        if (_dragIndex == index) { position = default; return false; }
        if (index < _placed.Count) { position = _placed[index].Position; return true; }
        position = default;
        return false;
    }

    /// <summary>
    /// Single mode: click an empty spot to place the next model (original behaviour); click an already
    /// placed model to pick it up and re-drop it elsewhere (drag-edit, used to fine-tune after a group
    /// drop). Completion is via the Done button — no auto-finish on the last model.
    /// </summary>
    private void DrawSingleDeploy(ImDrawListPtr dl, ImGuiIOPtr io, PlaceObjectsRequest<T> request,
        IBoundedZone zone, List<Position> enemies, float minEnemyDist, bool overTable)
    {
        var (mouseInX, mouseInZ) = PixelToInches(io.MousePos.X, io.MousePos.Y);
        bool clicked = overTable && !io.WantCaptureMouse && ImGui.IsMouseClicked(ImGuiMouseButton.Left);

        // Rotation input rotates the facing of the one model being placed / dragged (#150); the pending
        // rotation persists across placements (place several facing the same way) and resets on Resolve.
        // Shared GroupInput semantics (#277); the formation delta has no meaning in single mode.
        var (singleRotationDelta, _) = GroupInput.Read(!io.WantCaptureMouse && !io.WantCaptureKeyboard);
        _singleRotationDeploy += singleRotationDelta;
        Float2 facing = RotateFloat2(
            PlacementUtilities.DefaultDeployFacing(zone.Bounds, _tableH), _singleRotationDeploy);

        // Re-placing an existing model.
        if (_dragIndex.HasValue)
        {
            int k = _dragIndex.Value;
            var binding = _placed[k].Binding;
            float r = GetBaseRadius(binding.GetValue());
            var cand = new Position(mouseInX, mouseInZ);
            bool valid = IsPlacementValid(cand, r, GetBaseShape(binding.GetValue()), facing, zone, enemies,
                minEnemyDist, k, StartPositionOf(binding.GetValue()), request.MaxDistanceFromStartInches, out string? why);
            if (overTable)
            {
                DrawGhost(dl, GetBaseShape(binding.GetValue()), io.MousePos, _scale, valid, facing);
                _ghostSnapshot[k] = (cand, facing); // #280
            }
            if (clicked)
            {
                if (valid) { _placed[k] = new PlacedObjectEntry<T>(binding, cand, facing); _dragIndex = null; _errorMessage = null; }
                else { _errorMessage = why; _errorExpiry = ImGui.GetTime() + 2.5; }
            }
            return;
        }

        // Not dragging: a click on a placed model picks it up (and syncs the rotation to its facing,
        // relative to the zone's default facing).
        int hitIdx = HitTestPlaced(mouseInX, mouseInZ);
        if (clicked && hitIdx >= 0)
        {
            _dragIndex = hitIdx;
            _singleRotationDeploy = _placed[hitIdx].Facing is Float2 pf
                ? FacingToRadians(pf) - FacingToRadians(PlacementUtilities.DefaultDeployFacing(zone.Bounds, _tableH))
                : 0f;
            _errorMessage = null;
            return;
        }

        // All placed and nothing picked up: wait for Done (or a pick-up).
        if (_placed.Count >= request.ModelsToPlace.Count) return;

        // Place the next model.
        var currentBinding = request.ModelsToPlace[_placed.Count];
        float curR = GetBaseRadius(currentBinding.GetValue());
        var candidate = new Position(mouseInX, mouseInZ);
        bool ok = IsPlacementValid(candidate, curR, GetBaseShape(currentBinding.GetValue()), facing, zone, enemies,
            minEnemyDist, -1, StartPositionOf(currentBinding.GetValue()), request.MaxDistanceFromStartInches, out string? reason);
        if (overTable)
        {
            DrawGhost(dl, GetBaseShape(currentBinding.GetValue()), io.MousePos, _scale, ok, facing);
            _ghostSnapshot[_placed.Count] = (candidate, facing); // #280
        }
        if (clicked)
        {
            if (ok) { _placed.Add(new PlacedObjectEntry<T>(currentBinding, candidate, facing)); _errorMessage = null; }
            else { _errorMessage = reason; _errorExpiry = ImGui.GetTime() + 2.5; }
        }
    }

    /// <summary>
    /// Group mode: the whole unit is laid out as a forward line (wrapping to two balanced rows when it
    /// would break the max-pairwise cohesion span), rotatable with the wheel / R, its centroid following
    /// the cursor. A left-click drops every model at once (replacing any prior placement); red ghosts mean
    /// at least one model is in an illegal spot and the click is a no-op.
    /// </summary>
    private void DrawGroupDeploy(ImDrawListPtr dl, ImGuiIOPtr io, PlaceObjectsRequest<T> request,
        IBoundedZone zone, List<Position> enemies, float minEnemyDist, bool overTable, bool wantInput)
    {
        var models = request.ModelsToPlace;
        int n = models.Count;

        // Rotation input: wheel both ways; R clockwise, Shift+R counter-clockwise. Ctrl+Wheel cycles
        // the formation (#277).
        var (rotationDelta, formationDelta) = GroupInput.Read(wantInput);
        _groupRotationDeploy += rotationDelta;

        // Per-axis half-extents at the un-rotated deploy facing so the formation layout packs a wide rectangle
        // tight on both axes (#150); the circumscribing radius is still used for overlap/zone checks.
        var radii = new float[n];
        var halfXs = new float[n];
        var halfZs = new float[n];
        Float2 baseFacing = PlacementUtilities.DefaultDeployFacing(zone.Bounds, _tableH);
        for (int i = 0; i < n; i++)
        {
            radii[i] = GetBaseRadius(models[i].GetValue());
            (halfXs[i], halfZs[i]) = HalfExtents(models[i].GetValue(), baseFacing);
        }

        bool reposition = request.MaxDistanceFromStartInches > 0f;
        _formationCycle ??= FormationCycle.Build(halfXs, halfZs, radii, includeCurrentShape: reposition);
        if (formationDelta != 0) _formationCycle.Cycle(formationDelta);

        // Forward = toward table centre: longer/front row sits on that side.
        float forwardSign = zone.Bounds.CenterZ < _tableH * 0.5f ? 1f : -1f;

        var currentPositions = new Position[n];
        for (int i = 0; i < n; i++) currentPositions[i] = StartPositionOf(models[i].GetValue());

        // Offsets from the formation centroid + each model's facing BEFORE the user rotation: the
        // current-shape option (#277 reposition index 0) keeps the standing layout and each model's own
        // facing; generated shapes lay rows out per FormationLibrary, all facing the zone default.
        (float dx, float dz)[] offsets;
        var baseFacings = new Float2[n];
        if (_formationCycle.IsCurrentShape)
        {
            Position c0 = GroupFormationUtilities.Centroid(currentPositions);
            offsets = new (float dx, float dz)[n];
            for (int i = 0; i < n; i++)
            {
                offsets[i] = (currentPositions[i].x - c0.x, currentPositions[i].z - c0.z);
                baseFacings[i] = models[i].GetValue() is ModelData md ? md.Facing : baseFacing;
            }
        }
        else
        {
            offsets = FormationLibrary.PlanFormationOffsets(currentPositions, halfXs, halfZs,
                _formationCycle.Selected.RowCounts, gap: 0.1f);
            // FormationLibrary lays the front (longer) row toward +z; mirror when the zone's forward
            // direction is -z so it still leads toward the table centre (the old default's behavior).
            // A reposition skips this — slots were assigned nearest-to-current, and mirroring after
            // the fact would undo that.
            if (!reposition && forwardSign < 0f)
                for (int i = 0; i < n; i++) offsets[i] = (offsets[i].dx, -offsets[i].dz);
            for (int i = 0; i < n; i++) baseFacings[i] = baseFacing;
        }

        // Centroid follows the cursor (over table); otherwise preview at the zone's forward-centre.
        Position centroid;
        if (overTable && !io.WantCaptureMouse)
        {
            var (mx, mz) = PixelToInches(io.MousePos.X, io.MousePos.Y);
            centroid = new Position(mx, mz);
        }
        else
        {
            ZoneBounds b = zone.Bounds;
            float cz = forwardSign > 0f ? b.Top - 3f : b.Bottom + 3f;
            centroid = new Position(b.CenterX, cz);
        }

        float cos = MathF.Cos(_groupRotationDeploy), sin = MathF.Sin(_groupRotationDeploy);
        var facings = new Float2[n];
        var positions = new Position[n];
        bool allValid = true;
        for (int i = 0; i < n; i++)
        {
            float dx = offsets[i].dx, dz = offsets[i].dz;
            float rx = dx * cos - dz * sin, rz = dx * sin + dz * cos;
            positions[i] = new Position(centroid.x + rx, centroid.z + rz);
            facings[i] = RotateFloat2(baseFacings[i], _groupRotationDeploy);
            bool valid = IsGroupSlotValid(positions[i], radii[i], GetBaseShape(models[i].GetValue()), facings[i],
                zone, enemies, minEnemyDist, StartPositionOf(models[i].GetValue()), request.MaxDistanceFromStartInches);
            DrawGhost(dl, GetBaseShape(models[i].GetValue()), ToPixelVec(positions[i]), _scale, valid, facings[i]);
            _ghostSnapshot[i] = (positions[i], facings[i]); // #280
            if (!valid) allValid = false;
        }

        if (overTable && !io.WantCaptureMouse && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            // #029: an edge-constrained placement (Aircraft redeploy) must have at least one base touching a
            // table edge — the formation's leading row, in practice.
            bool touchOk = !request.MustTouchTableEdge;
            for (int i = 0; i < n && !touchOk; i++)
                touchOk = PlacementUtilities.TouchesZoneEdge(positions[i], GetBaseRadius(models[i].GetValue()), zone.Bounds);

            if (allValid && touchOk)
            {
                _placed.Clear();
                for (int i = 0; i < n; i++) _placed.Add(new PlacedObjectEntry<T>(models[i], positions[i], facings[i]));
                _groupRotationDeploy = 0f;
                _errorMessage = null;
            }
            else
            {
                _errorMessage = allValid
                    ? "Must come on touching a table edge - move the formation to an edge."
                    : "Some models are in an invalid spot - rotate or reposition the formation.";
                _errorExpiry = ImGui.GetTime() + 2.5;
            }
        }
    }

    private Vector2 ToPixelVec(Position p)
    {
        var (px, py) = InchesToPixel(p.x, p.z);
        return new Vector2(px, py);
    }

    /// <summary>Index of the placed model whose base contains the given table point, or -1.</summary>
    private int HitTestPlaced(float x, float z)
    {
        for (int i = 0; i < _placed.Count; i++)
        {
            float dx = x - _placed[i].Position.x, dz = z - _placed[i].Position.z;
            if (GetBaseShape(_placed[i].Binding.GetValue()).ContainsLocalPoint(dx, dz)) return i;
        }
        return -1;
    }

    /// <summary>Full single-placement validity for a candidate, ignoring placed model <paramref name="excludeIndex"/>.
    /// Overlap, cohesion and terrain use the true oriented footprint (#150), not a bounding circle — so tight,
    /// legal packings of elongated rectangles aren't falsely rejected. <paramref name="r"/> (circumscribing)
    /// still bounds the zone/table containment (rotation-safe).</summary>
    private bool IsPlacementValid(Position cand, float r, IBaseShape shape, Float2 facing, IBoundedZone zone,
        List<Position> enemies, float minEnemyDist, int excludeIndex, Position startPos, float maxFromStart,
        out string? reason)
    {
        // #197 reposition-at-activation: each model has its own radius around where it already stands.
        if (!PlacementUtilities.IsWithinStartRadius(cand, startPos, maxFromStart))
        { reason = $"Too far - this model may move at most {maxFromStart:F1}\" from where it stands."; return false; }

        if (!PlacementUtilities.IsBaseWithinZone(cand, shape, facing, zone))
        { reason = "Outside deployment zone."; return false; }

        string? overlap = CheckOverlap(cand, shape, facing, excludeIndex);
        if (overlap != null) { reason = $"Bases overlap ({overlap})."; return false; }

        // #269: a reposition placement checks cohesion once over the FINISHED formation (the Done gate), not
        // per click. Gating each click on "within 1in of an already-placed model" forces the unit to be
        // rebuilt in list order and confines every model after the first to a thin band around model 1,
        // however much of its own reach ring is free — which is what made Teleport feel broken. Deployment
        // keeps the incremental check: there the unit is built from nothing and the running feedback is the
        // only cohesion signal a player gets.
        if (maxFromStart <= 0f && !IsInCohesion(cand, shape, facing, excludeIndex))
        { reason = $"Outside cohesion - must be within {GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES}\" base-to-base of a placed model."; return false; }

        // #197 P22: the shared authority folds the flat minimum, Repel keep-out discs and Beacon
        // waivers together; TooCloseToEnemy is kept only to word the reason for the common case.
        PlaceObjectsRequest<T>? req = ConstraintRequest;
        if (req != null && PlacementDistanceRules.ViolatesEnemyDistance(req, cand, enemies))
        {
            reason = minEnemyDist > 0f && TooCloseToEnemy(cand, enemies, minEnemyDist)
                ? $"Too close to an enemy - must be over {minEnemyDist:F0}\" from enemy units."
                : "Inside an enemy's Repel Ambushers keep-away - set up further from that unit " +
                  "(or within a friendly Ambush Beacon's range).";
            return false;
        }

        if (PlacementUtilities.OverlapsImpassibleTerrain(cand, shape, facing, _tableState.Terrain.Objects))
        { reason = "On impassible terrain - the model's base would overlap a building or blocker."; return false; }

        reason = null;
        return true;
    }

    /// <summary>Validity for a model in a dropped group: zone containment, no overlap with on-table
    /// occupants or terrain, and enemy spacing. Intra-formation overlap/cohesion are guaranteed by the
    /// layout, so they aren't re-checked here.</summary>
    private bool IsGroupSlotValid(Position cand, float r, IBaseShape shape, Float2 facing, IBoundedZone zone,
        List<Position> enemies, float minEnemyDist, Position startPos, float maxFromStart)
    {
        if (!PlacementUtilities.IsWithinStartRadius(cand, startPos, maxFromStart)) return false;
        if (!PlacementUtilities.IsBaseWithinZone(cand, shape, facing, zone)) return false;
        foreach (var (pos, oShape, oFacing) in GetTableOccupants())
            if (BaseShapeGeometry.AreColliding(shape, cand, facing, oShape, pos, oFacing)) return false;
        PlaceObjectsRequest<T>? req = ConstraintRequest;
        if (req != null && PlacementDistanceRules.ViolatesEnemyDistance(req, cand, enemies)) return false;
        if (PlacementUtilities.OverlapsImpassibleTerrain(cand, shape, facing, _tableState.Terrain.Objects)) return false;
        return true;
    }

    private void DrawZone(ImDrawListPtr dl, IZone zone)
    {
        uint fill    = ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 0.60f, 1.00f, 0.12f));
        uint outline = ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 0.60f, 1.00f, 0.80f));
        ZoneRenderer.DrawFilled(zone, dl, _scale, _originX, _originY, _tableH, fill, outline);
    }

    /// <summary>
    /// #214: the reach circle for a reposition-style placement (Teleport, Fanatic, reposition-at-
    /// activation). Those requests are bounded per MODEL by <c>MaxDistanceFromStartInches</c> around each
    /// model's own start, not by the deployment zone - they pass the whole table as the zone - so
    /// <see cref="DrawZone"/> alone showed no constraint at all and the radius was invisible until a click
    /// was rejected. Disembark looked right only because it happens to express its 6" as a
    /// <c>CircularZone</c>, which the zone renderer already draws.
    /// <para>
    /// One ring per model still to place, since each model is bounded by its own start — see
    /// <see cref="ReachRingPlan"/>, which owns the selection rules.
    /// </para>
    /// </summary>
    private void DrawReachRings(ImDrawListPtr dl, PlaceObjectsRequest<T> request)
    {
        float reach = request.MaxDistanceFromStartInches;
        if (reach <= 0f) return;

        bool groupDrop = _formationMode.IsGroup && _placed.Count == 0 && !_dragIndex.HasValue;

        foreach (ReachRingPlan.Ring ring in
                 ReachRingPlan.Build(request.ModelsToPlace.Count, _placed.Count, _dragIndex, groupDrop))
        {
            // A dragged model is indexed into _placed; everything else into the request's list. Both hold
            // the same binding at the same index (placements are appended in order), so either works for
            // the dragged one — read it from _placed to stay honest about where the index came from.
            DataBinding<T> binding = ring.ModelIndex < _placed.Count
                ? _placed[ring.ModelIndex].Binding
                : request.ModelsToPlace[ring.ModelIndex];

            Position start = StartPositionOf(binding.GetValue());
            var (px, py) = InchesToPixel(start.x, start.z);
            dl.AddCircle(new Vector2(px, py), reach * _scale,
                ring.IsActive ? ActiveReachRingCol : PendingReachRingCol, 64, ring.IsActive ? 2f : 1.5f);
        }
    }

    private void DrawPlacedSoFar(ImDrawListPtr dl, int skipIndex)
    {
        uint fill    = ImGui.ColorConvertFloat4ToU32(new Vector4(0.40f, 0.95f, 1.00f, 0.90f));
        uint outline = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 1f));
        for (int i = 0; i < _placed.Count; i++)
        {
            if (i == skipIndex) continue; // hidden while being dragged
            var entry = _placed[i];
            var (px, py) = InchesToPixel(entry.Position.x, entry.Position.z);
            var shape = GetBaseShape(entry.Binding.GetValue());
            Float2 facing = entry.Facing ?? new Float2(0f, 1f);
            ModelBaseRenderer.DrawFilledImGui(dl, shape, new Vector2(px, py), _scale, fill, outline, 1f, facing);
            ModelBaseRenderer.DrawHeadingImGui(dl, shape, new Vector2(px, py), _scale, facing, outline);
        }
    }

    private static void DrawGhost(ImDrawListPtr dl, IBaseShape shape, Vector2 center, float scale, bool valid, Float2 facing)
    {
        uint fill = valid
            ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 1.00f, 0.20f, 0.50f))
            : ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.20f, 0.20f, 0.50f));
        uint outline = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.80f));
        ModelBaseRenderer.DrawFilledImGui(dl, shape, center, scale, fill, outline, 1f, facing);
        ModelBaseRenderer.DrawHeadingImGui(dl, shape, center, scale, facing, outline);
    }

    private void DrawInfoPanel(int screenW, PlaceObjectsRequest<T> request,
        TaskCompletionSource<CancellableResult<List<PlacedObjectEntry<T>>>> tcs, IUnit? deploying)
    {
        int total = request.ModelsToPlace.Count;
        bool group = _formationMode.IsGroup;
        bool dropping = group && _placed.Count == 0; // showing the whole-unit ghost
        float panelW = ResolverPanelLayout.W;   // dock into the right-column resolver panel
        ImGui.SetNextWindowPos(new Vector2(ResolverPanelLayout.X, ResolverPanelLayout.Y), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(ResolverPanelLayout.W, ResolverPanelLayout.H), ImGuiCond.Always);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.10f, 0.10f, 0.15f, 0.92f));
        ImGui.Begin("##PlacePanel",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBringToFrontOnFocus);
        ImGui.PopStyleColor();

        ImGui.TextUnformatted($"Deploy: {_placed.Count} / {total} models placed");
        ImGui.SameLine();
        ImGui.TextDisabled($"  zone X {request.DeploymentZone.Bounds.Left:F0}-{request.DeploymentZone.Bounds.Right:F0}\"");

        if (ImGui.Button(group ? "Mode: Group (G)" : "Mode: Single (G)"))
        {
            _formationMode.Toggle();
            _dragIndex = null;
        }
        if (dropping && _formationCycle != null)
        {
            ImGui.SameLine();
            ImGui.TextUnformatted($"Formation: {_formationCycle.Label} ({_formationCycle.Index + 1}/{_formationCycle.Count})");
        }

        // #230: the hotkey alone would be undiscoverable, so the toggle is also a checkbox here. Only
        // offered where there is a unit to show reach for (not for objective / terrain placement).
        if (deploying != null)
        {
            ImGui.Checkbox("Weapon reach (V)", ref ViewSettings.ShowReachOverlay);
            if (ImGui.IsItemHovered())
                ImGui.SetTooltip("Show what this spot could hit from here: weapon-range bands, shaded for\n" +
                                 "line of sight and cover from the pending positions.\n" +
                                 "Hovering any unit shows that unit's reach instead, while you hover it.");
        }

        // Everything below the stat box is composed BEFORE it is drawn, so its height can be measured and
        // the stat box given exactly the space that is left (#288). Order of appearance is unchanged.
        string statusText = _errorMessage ??
            (dropping              ? "Position the unit in the blue zone. Wheel / R rotate, Ctrl+Wheel changes formation. Click drops the whole unit." :
             _dragIndex.HasValue   ? "Click to drop the picked-up model." :
             _placed.Count < total ? "Click empty space to place the next model, or click a placed model to move it." :
                                     "Click any placed model to pick it up and move it.");
        Vector4 statusColor = _errorMessage != null
            ? new Vector4(1f, 0.4f, 0.4f, 1f)
            : new Vector4(0.6f, 0.6f, 0.6f, 1f);

        // #269: for a reposition the cohesion verdict is about the finished formation, so it is reported
        // here (and gates Done) rather than rejecting individual clicks. Only meaningful once every model
        // is down — until then FinalCohesion reports nothing.
        string? cohesionIssue = PlacementCohesion.Describe(FinalCohesion(request));

        // #029: edge-constrained placement (Aircraft redeploy) — surface the touch requirement live.
        bool touchingEdge = request.MustTouchTableEdge && PlacedTouchesEdge(request);
        string? edgeText = !request.MustTouchTableEdge ? null
            : touchingEdge ? "Touching a table edge." : "Must come on touching a table edge.";

        // The unit's stats (weapons with special rules + unit special rules, WITH their descriptions —
        // the same detail the model hover tooltip carries). Most needed for an Ambush arrival, where the
        // unit is coming on from reserve and isn't on the table to hover at all. Shown for any model
        // placement; skipped for non-model objects (objectives).
        //
        // #288: the box fills whatever is left above the footer instead of a fixed 118px — a rule-heavy
        // unit was reading three lines at a time through a keyhole. Measuring the footer first is what
        // keeps Done/Back on screen no matter how much the unit has to say.
        if (deploying != null)
        {
            ImGui.Spacing();
            float statsH = PlacementPanelLayout.StatsHeight(ImGui.GetContentRegionAvail().Y,
                MeasureFooterHeight(request, statusText, cohesionIssue, edgeText),
                ImGui.GetTextLineHeight());
            ImGui.BeginChild("##DeployStats", new Vector2(0, statsH), ImGuiChildFlags.Borders);
            // Wrap descriptions at the child's own width: the shared renderer's tooltip default (300px)
            // is wider than this docked column and would run rule text off the edge.
            UnitStatBlockRenderer.Draw(deploying, includeRuleDescriptions: true,
                descriptionWrapWidth: MathF.Max(120f, ImGui.GetContentRegionAvail().X - ImGui.GetStyle().IndentSpacing));
            ImGui.EndChild();
        }

        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Text, statusColor);
        ImGui.TextWrapped(statusText);
        ImGui.PopStyleColor();

        if (cohesionIssue != null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.95f, 0.75f, 0.3f, 1f));
            ImGui.TextWrapped(cohesionIssue);
            ImGui.PopStyleColor();
        }

        if (edgeText != null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, touchingEdge
                ? new Vector4(0.5f, 0.9f, 0.5f, 1f)
                : new Vector4(0.95f, 0.75f, 0.3f, 1f));
            ImGui.TextWrapped(edgeText);
            ImGui.PopStyleColor();
        }

        ImGui.Spacing();
        float fullW = panelW - ImGui.GetStyle().WindowPadding.X * 2;
        float halfW = (fullW - ImGui.GetStyle().ItemSpacing.X) / 2f;
        // #298: button heights come from the font via PlacementPanelLayout, which is also what
        // MeasureFooterHeight costs them at - the two must not drift.
        float lineH     = ImGui.GetTextLineHeight();
        float doneH     = PlacementPanelLayout.DoneButtonHeight(lineH);
        float backH     = PlacementPanelLayout.BackButtonHeight(lineH);
        float secondH   = PlacementPanelLayout.SecondaryRowHeight(lineH);
        float restartH  = PlacementPanelLayout.RestartButtonHeight(lineH);

        // Primary: Done -- larger, accented, commits on click or the Confirm key (gated on all models placed).
        bool canDone = _placed.Count == total && !_dragIndex.HasValue
            && (!request.MustTouchTableEdge || PlacedTouchesEdge(request))
            && cohesionIssue == null;
        if (ResolverButtons.Primary("Done", new Vector2(fullW, doneH), enabled: canDone))
        {
            Complete(tcs, new List<PlacedObjectEntry<T>>(_placed));
            ImGui.End();
            return;
        }

        // Back: abandon the whole placement. Only offered where the player still has somewhere to return
        // to (Disembark, Teleport/reposition, and #308 deployment) - Scout, Ambush arrival and spillout
        // are mandatory. What backing out MEANS differs per caller, so the request words it (#308: the
        // hard-coded "stays aboard its transport" became a lie the moment deployment allowed cancelling).
        if (request.AllowCancel)
        {
            // #248: Backspace = back here too (no waypoint-undo exists yet in placement - #161 C).
            if (ResolverButtons.Deemphasized("Back (Backspace)", new Vector2(fullW, backH))
                || ResolverHotkeys.IsBackPressed())
            {
                CompleteCancelled(tcs);
                ImGui.End();
                return;
            }
            if (ImGui.IsItemHovered() && !string.IsNullOrEmpty(request.CancelHint))
                ImGui.SetTooltip(request.CancelHint);
        }

        // Secondary row: Undo + Auto-place.
        ImGui.BeginDisabled(_placed.Count == 0);
        if (ImGui.Button("Undo", new Vector2(halfW, secondH)))
        {
            _placed.RemoveAt(_placed.Count - 1);
            _dragIndex = null;
            _errorMessage = null;
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(_placed.Count >= total);
        if (ImGui.Button("Auto-place", new Vector2(halfW, secondH)))
        {
            if (AutoPlaceRemaining(request)) _errorMessage = null;
            else
            {
                _errorMessage = "Could not auto-place all remaining models - zone too crowded.";
                _errorExpiry  = ImGui.GetTime() + 3.0;
            }
        }
        ImGui.EndDisabled();

        // Destructive: Restart -- set apart below a separator, red-tinted.
        ImGui.Separator();
        ImGui.BeginDisabled(_placed.Count == 0);
        if (ResolverButtons.Destructive("Restart", new Vector2(fullW, restartH)))
        {
            _placed.Clear();
            _dragIndex = null;
            _errorMessage = null;
        }
        ImGui.EndDisabled();

        ImGui.End();
    }

    /// <summary>
    /// #288 — measures the panel's live text/style values and asks <see cref="PlacementPanelLayout"/> how
    /// much room the footer needs. Must be called with the panel window current, before the stat box is
    /// drawn (the ImGui measuring lives here; the arithmetic is over there, where it is unit-tested).
    /// </summary>
    private static float MeasureFooterHeight(PlaceObjectsRequest<T> request,
        string statusText, string? cohesionIssue, string? edgeText)
    {
        float wrapW = ImGui.GetContentRegionAvail().X;
        return PlacementPanelLayout.FooterHeight(
            ImGui.GetStyle().ItemSpacing.Y,
            ImGui.GetTextLineHeight(),
            WrappedTextHeight(statusText, wrapW),
            cohesionIssue != null ? WrappedTextHeight(cohesionIssue, wrapW) : null,
            edgeText != null ? WrappedTextHeight(edgeText, wrapW) : null,
            request.AllowCancel);
    }

    private static float WrappedTextHeight(string text, float wrapWidth) =>
        ImGui.CalcTextSize(text, false, MathF.Max(1f, wrapWidth)).Y;

    /// <summary>
    /// #269 — cohesion of the FINISHED reposition. Reports "nothing wrong" when the check doesn't apply:
    /// for a deployment (which gates per click instead) or while models are still to be placed, where a
    /// half-built formation would raise a warning about a state the player is mid-way through fixing.
    /// </summary>
    private PlacementCohesion.Report FinalCohesion(PlaceObjectsRequest<T> request)
    {
        if (request.MaxDistanceFromStartInches <= 0f || _placed.Count != request.ModelsToPlace.Count)
            return new PlacementCohesion.Report(0, 0, 0f);

        var before = new List<PlacementCohesion.Footprint>(_placed.Count);
        var after  = new List<PlacementCohesion.Footprint>(_placed.Count);
        foreach (PlacedObjectEntry<T> entry in _placed)
        {
            T value = entry.Binding.GetValue();
            IBaseShape shape = GetBaseShape(value);
            Float2 startFacing = value is ModelData m ? m.Facing : new Float2(0f, 1f);
            before.Add(new PlacementCohesion.Footprint(StartPositionOf(value), shape, startFacing));
            after.Add(new PlacementCohesion.Footprint(entry.Position, shape, entry.Facing ?? startFacing));
        }

        return PlacementCohesion.Evaluate(before, after);
    }

    // The unit the placed models belong to, or null when placing non-model objects (objectives) or when it
    // can't be found. Matches on the first model's reference against every unit's model list.
    private IUnit? FindOwningUnit(PlaceObjectsRequest<T> request)
    {
        if (request.ModelsToPlace.Count == 0) return null;
        if (request.ModelsToPlace[0].GetValue() is not ModelData first) return null;
        foreach (var unit in _tableState.Units.Objects)
            foreach (var m in unit.Models)
                if (ReferenceEquals(m, first)) return unit;
        return null;
    }

    /// <summary>Tries to place all remaining models into free spots; returns false if any can't fit.</summary>
    private bool AutoPlaceRemaining(PlaceObjectsRequest<T> request)
    {
        var zone = request.DeploymentZone;
        Float2 deployFacing = PlacementUtilities.DefaultDeployFacing(zone.Bounds, _tableH);

        // Per-axis step so a wide rectangle packs tight rows (not one long row), + a column cap so the unit
        // stays a compact block inside the 9" all-pairs rule (#150).
        float stepX = 2f * 0.1f, stepZ = 2f * 0.1f;
        foreach (var b0 in request.ModelsToPlace)
        {
            var (hx, hz) = HalfExtents(b0.GetValue(), deployFacing);
            stepX = MathF.Max(stepX, 2f * hx + 0.1f);
            stepZ = MathF.Max(stepZ, 2f * hz + 0.1f);
        }
        int total = request.ModelsToPlace.Count;
        int targetCols = Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt(total)));
        float startCz = zone.Bounds.CenterZ;

        float minEnemyDist = request.MinDistanceFromEnemiesInches;
        var enemies = minEnemyDist > 0f ? GetEnemyPositions(request.TargetPlayerID) : _noEnemies;

        for (int i = _placed.Count; i < request.ModelsToPlace.Count; i++)
        {
            var binding = request.ModelsToPlace[i];

            // #197 reposition: the block-packing search aims at a deployment lane and knows nothing about this
            // model's own radius. Standing still is always inside it and always legal.
            if (request.MaxDistanceFromStartInches > 0f)
            {
                _placed.Add(new PlacedObjectEntry<T>(binding, StartPositionOf(binding.GetValue())));
                continue;
            }

            float r = GetBaseRadius(binding.GetValue());
            IBaseShape shape = GetBaseShape(binding.GetValue());
            if (!TryFindAutoPosition(r, shape, deployFacing, stepX, stepZ, targetCols, zone, startCz, enemies, minEnemyDist, out Position pos))
                return false;
            _placed.Add(new PlacedObjectEntry<T>(binding, pos, deployFacing));
        }
        return true;
    }

    private bool TryFindAutoPosition(float r, IBaseShape shape, Float2 facing, float stepX, float stepZ, int targetCols,
        IBoundedZone zone, float startCz, List<Position> enemies, float minEnemyDist, out Position result)
    {
        // Fill a compact ~targetCols-wide block, anchored at the first placed model, scanning Z rows outward.
        ZoneBounds b = zone.Bounds;
        float xStart = _placed.Count > 0 ? _placed[0].Position.x : b.Left + r;
        float rowXEnd = MathF.Min(b.Right - r, xStart + (targetCols - 1) * stepX + 0.001f);
        int maxRows = (int)((b.Top - b.Bottom) / stepZ) + 1;
        for (int rowOffset = 0; rowOffset <= maxRows; rowOffset++)
        {
            int signCount = rowOffset == 0 ? 1 : 2;
            for (int s = 0; s < signCount; s++)
            {
                float z = startCz + (s == 0 ? rowOffset : -rowOffset) * stepZ;
                if (z < b.Bottom + r || z > b.Top - r) continue;

                for (float x = xStart; x <= rowXEnd; x += stepX)
                {
                    var c = new Position(x, z);
                    if (!PlacementUtilities.IsBaseWithinZone(c, shape, facing, zone)) continue;
                    if (CheckOverlap(c, shape, facing) != null) continue;
                    if (PlacementUtilities.OverlapsImpassibleTerrain(c, shape, facing, _tableState.Terrain.Objects)) continue;
                    PlaceObjectsRequest<T>? req = ConstraintRequest;
                    if (req != null && PlacementDistanceRules.ViolatesEnemyDistance(req, c, enemies)) continue;
                    if (!IsInCohesion(c, shape, facing, -1)) continue;
                    result = c; return true;
                }
            }
        }
        result = default;
        return false;
    }

    private static readonly List<Position> _noEnemies = new();
    private static readonly List<PlacementDisc> _noDiscs = new();

    // #197 P22: the pending request, read where the legality checks and the exclusion provider run
    // (main thread; the engine thread is blocked awaiting this resolution, so the snapshot is stable).
    private PlaceObjectsRequest<T>? ConstraintRequest
    {
        get { lock (_lock) return _request; }
    }

    // IEnemyExclusionProvider: surfaces the no-go geometry while an Ambush-style placement is pending, so
    // the renderer can draw the blob. Keep-out = the flat over-9" rule as a disc per live enemy model plus
    // the request's Repel Ambushers discs (#197 P22, each at its own radius); waivers = Ambush Beacon
    // regions the renderer ERASES from the blob (and outlines), since inside one every enemy-distance
    // restriction is void. The engine thread is blocked awaiting this resolution during placement, so
    // reading table state here is safe.
    public bool TryGetEnemyExclusion(out IReadOnlyList<PlacementDisc> keepOut,
        out IReadOnlyList<PlacementDisc> waivers)
    {
        PlaceObjectsRequest<T>? request = ConstraintRequest;

        bool constrained = request != null
            && (request.MinDistanceFromEnemiesInches > 0f || request.EnemyKeepOutDiscs.Count > 0);
        if (!constrained)
        {
            keepOut = _noDiscs;
            waivers = _noDiscs;
            return false;
        }

        var discs = new List<PlacementDisc>(request!.EnemyKeepOutDiscs);
        if (request.MinDistanceFromEnemiesInches > 0f)
        {
            foreach (Position enemy in GetEnemyPositions(request.TargetPlayerID))
                discs.Add(new PlacementDisc(enemy, request.MinDistanceFromEnemiesInches));
        }

        keepOut = discs;
        waivers = request.EnemyDistanceWaiverDiscs;
        return true;
    }

    private List<Position> GetEnemyPositions(PlayerID self)
    {
        var positions = new List<Position>();
        foreach (var unit in _tableState.Units.Objects)
        {
            if (!TeamAwareness.IsEnemyUnit(_tableState, self, unit)) continue;
            foreach (var model in unit.Models)
            {
                var pos = model.Position;
                if (!model.GetIsAlive()) continue;
                if (pos.x == 0f && pos.z == 0f) continue;
                positions.Add(pos);
            }
        }
        return positions;
    }

    // Where the model stands now. Safe to read at validation time: PlacedObjectEntry defers every write until
    // the whole placement is accepted, so no model has moved yet.
    private static Position StartPositionOf(T value) =>
        value is ModelData model ? model.Position : new Position();

    private static bool TooCloseToEnemy(Position p, List<Position> enemies, float minDist)
    {
        foreach (var e in enemies)
            if (Dist(p, e) < minDist) return true;
        return false;
    }

    /// <summary>Returns null if free; otherwise a brief description of the conflict. Ignores the placed
    /// model at <paramref name="excludeIndex"/> (the one being re-placed by drag). True-shape overlap (#150):
    /// two bases overlap iff their real oriented footprints intersect, not their circumscribing circles.</summary>
    private string? CheckOverlap(Position newPos, IBaseShape newShape, Float2 newFacing, int excludeIndex = -1)
    {
        for (int i = 0; i < _placed.Count; i++)
        {
            if (i == excludeIndex) continue;
            var entry = _placed[i];
            IBaseShape es = GetBaseShape(entry.Binding.GetValue());
            Float2 ef = entry.Facing ?? new Float2(0f, 1f);
            if (BaseShapeGeometry.AreColliding(newShape, newPos, newFacing, es, entry.Position, ef))
                return $"gap {BaseShapeGeometry.SurfaceGap2D(newShape, newPos, newFacing, es, entry.Position, ef):F2}\"";
        }
        foreach (var (pos, shape, facing) in GetTableOccupants())
            if (BaseShapeGeometry.AreColliding(newShape, newPos, newFacing, shape, pos, facing))
                return $"gap {BaseShapeGeometry.SurfaceGap2D(newShape, newPos, newFacing, shape, pos, facing):F2}\"";
        return null;
    }

    private IEnumerable<(Position pos, IBaseShape shape, Float2 facing)> GetTableOccupants()
    {
        foreach (var model in _tableState.Models.Objects)
        {
            // Casualties are removed from play - a dead model's base must not keep blocking placement from
            // wherever it happened to die. Dead models aren't drawn, so without this the spot where a unit
            // was wiped out becomes an invisible no-place region (seen as an inexplicable red Ambush drop).
            if (!model.GetIsAlive()) continue;
            var pos = model.Position;
            // Default-constructed Position is (0,0,0); models there haven't been placed yet.
            if (pos.x == 0f && pos.z == 0f) continue;
            // #269: a model this request is repositioning doesn't block its own placement — it is about to
            // leave, and it is re-checked at its new position through the _placed loop.
            if (_selfModels.Contains(model)) continue;
            yield return (pos, model.BaseShape, model.Facing);
        }
    }

    // #029: at least one placed model's base touches an edge of the placement zone (the whole table for the
    // Aircraft redeploy). Unit-level — the back rows of a formation naturally sit off the edge.
    private bool PlacedTouchesEdge(PlaceObjectsRequest<T> request)
    {
        foreach (var entry in _placed)
            if (PlacementUtilities.TouchesZoneEdge(entry.Position,
                    GetBaseRadius(entry.Binding.GetValue()), request.DeploymentZone.Bounds))
                return true;
        return false;
    }

    private void Complete(TaskCompletionSource<CancellableResult<List<PlacedObjectEntry<T>>>> tcs, List<PlacedObjectEntry<T>> entries) =>
        CompleteWith(tcs, new Selected<List<PlacedObjectEntry<T>>>(entries));

    /// <summary> Abandon the placement (Disembark only). Nothing has been repositioned yet. </summary>
    private void CompleteCancelled(TaskCompletionSource<CancellableResult<List<PlacedObjectEntry<T>>>> tcs) =>
        CompleteWith(tcs, new Cancelled<List<PlacedObjectEntry<T>>>());

    private void CompleteWith(TaskCompletionSource<CancellableResult<List<PlacedObjectEntry<T>>>> tcs,
        CancellableResult<List<PlacedObjectEntry<T>>> result)
    {
        lock (_lock) { _request = null; _tcs = null; _placed.Clear(); }
        // #230: drop the field anchor with the request so the overlay stops drawing this placement's reach
        // (and we stop holding the unit) the instant it is committed.
        _ghostFieldPositions.Clear();
        _ghostFieldUnit = null;
        tcs.SetResult(result);
    }

    private (float x, float z) PixelToInches(float px, float py) =>
        ((px - _originX) / _scale, _tableH - (py - _originY) / _scale);

    private (float px, float py) InchesToPixel(float x, float z) =>
        (_originX + x * _scale, _originY + (_tableH - z) * _scale);

    private bool IsOverTable(float px, float py) =>
        px >= _originX && py >= _originY &&
        px <= _originX + GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES * _scale &&
        py <= _originY + _tableH * _scale;

    private static float Dist(Position a, Position b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return MathF.Sqrt(dx * dx + dz * dz);
    }

    // The radius used for placement spacing / overlap / zone containment (#150): the CIRCUMSCRIBING circle, so a
    // rotatable rectangle is kept non-overlapping and fully inside the zone at ANY facing (its true footprint
    // fits inside this circle). Conservative — a touch of wasted space — but never lets bases overlap or a
    // corner poke out of the zone. (NOT IModel.BaseRadiusInches, which #149 made the smaller inscribed circle.)
    private static float GetBaseRadius(T value) => value is ModelData m ? m.BaseShape.CircumscribedRadiusInches : 0.75f;

    // The base shape for rendering / hit-testing (#149). Non-model T (e.g. objectives) → a default circle.
    private static IBaseShape GetBaseShape(T value) => value is ModelData m ? m.BaseShape : new CircleBase(0.75f);

    // Per-axis footprint half-extents at the deploy facing, for the auto-placement grid step (#150).
    private static (float hx, float hz) HalfExtents(T value, Float2 facing) =>
        value is ModelData m ? BaseShapeGeometry.FootprintHalfExtents(m.BaseShape, facing) : (0.75f, 0.75f);

    /// <summary>True if the candidate is within nearest-neighbour cohesion of at least one other placed
    /// model, ignoring <paramref name="excludeIndex"/>. Vacuously true when there are no other models. Uses the
    /// true oriented footprint gap (#150) so the readout matches the server's shape-aware coherency check.</summary>
    private bool IsInCohesion(Position candidate, IBaseShape shape, Float2 facing, int excludeIndex)
    {
        bool anyOther = false;
        for (int i = 0; i < _placed.Count; i++)
        {
            if (i == excludeIndex) continue;
            anyOther = true;
            var entry = _placed[i];
            IBaseShape es = GetBaseShape(entry.Binding.GetValue());
            Float2 ef = entry.Facing ?? new Float2(0f, 1f);
            float b2b = BaseShapeGeometry.SurfaceGap2D(shape, candidate, facing, es, entry.Position, ef);
            if (b2b <= GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES)
                return true;
        }
        return !anyOther;
    }
}
