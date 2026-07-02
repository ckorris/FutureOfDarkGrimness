using System.Numerics;
using FDG;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

public class GuiPlaceObjectsResolver<T>
    : IStageResolver<PlaceObjectsRequest<T>, List<PlacedObjectEntry<T>>>, IGuiResolver, IGuiCanvasOverlay,
      IEnemyExclusionProvider
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
    private TaskCompletionSource<List<PlacedObjectEntry<T>>>? _tcs;
    private readonly List<PlacedObjectEntry<T>> _placed = new();

    // Index into _placed of the model currently being re-placed by drag (single mode); null = none.
    private int? _dragIndex;
    // Group-mode pending rotation (radians) about the formation centroid; reset on drop and on Resolve.
    private float _groupRotationDeploy;
    // Single-mode pending facing rotation (radians) applied to the model being placed / dragged (#150).
    private float _singleRotationDeploy;

    private static readonly float GroupRotationStep = MathF.PI / 12f; // 15° per wheel notch / key press

    // A yaw facing (unit normal) = the default forward (+Z) rotated by `radians`, matching the position
    // rotation matrix (rx = dx·cos − dz·sin, rz = dx·sin + dz·cos) applied to (0,1).
    private static Float2 RotateFacing(float radians) => new Float2(-MathF.Sin(radians), MathF.Cos(radians));

    // The inverse: the rotation (radians) that RotateFacing would need to produce this facing.
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

    public Task<List<PlacedObjectEntry<T>>> Resolve(PlaceObjectsRequest<T> request)
    {
        var tcs = new TaskCompletionSource<List<PlacedObjectEntry<T>>>();
        lock (_lock)
        {
            _tcs = tcs;
            _placed.Clear();
            _dragIndex = null;
            _groupRotationDeploy = 0f;
            _singleRotationDeploy = 0f;
            _errorMessage = null;
            _request = request;
        }
        return tcs.Task;
    }

    public void Draw(int screenW, int screenH)
    {
        PlaceObjectsRequest<T>? request;
        TaskCompletionSource<List<PlacedObjectEntry<T>>>? tcs;
        lock (_lock) { request = _request; tcs = _tcs; }
        if (request == null || tcs == null) return;

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
        DrawPlacedSoFar(dl, _dragIndex ?? -1);

        // G toggles Group/Single for the rest of the game (shared with movement).
        if (wantInput && ImGui.IsKeyPressed(ImGuiKey.G))
        {
            _formationMode.Toggle();
            _dragIndex = null;
        }

        // The group follow-formation ghost only shows before the first drop. Once anything is on the
        // table, both modes switch to per-model editing (drag a placed model, or place a missing one),
        // so clicks no longer re-drop the whole unit. Use Restart to re-form from scratch.
        if (group && _placed.Count == 0)
            DrawGroupDeploy(dl, io, request, zone, enemies, minEnemyDist, overTable, wantInput);
        else
            DrawSingleDeploy(dl, io, request, zone, enemies, minEnemyDist, overTable);

        if (_errorMessage != null && ImGui.GetTime() > _errorExpiry) _errorMessage = null;

        DrawInfoPanel(screenW, request, tcs);
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
        if (!io.WantCaptureMouse && !io.WantCaptureKeyboard)
        {
            if (io.MouseWheel != 0f) _singleRotationDeploy += io.MouseWheel > 0f ? GroupRotationStep : -GroupRotationStep;
            if (ImGui.IsKeyPressed(ImGuiKey.R))
            {
                bool shift = ImGui.IsKeyDown(ImGuiKey.LeftShift) || ImGui.IsKeyDown(ImGuiKey.RightShift);
                _singleRotationDeploy += shift ? GroupRotationStep : -GroupRotationStep;
            }
        }
        Float2 facing = RotateFacing(_singleRotationDeploy);

        // Re-placing an existing model.
        if (_dragIndex.HasValue)
        {
            int k = _dragIndex.Value;
            var binding = _placed[k].Binding;
            float r = GetBaseRadius(binding.GetValue());
            var cand = new Position(mouseInX, mouseInZ);
            bool valid = IsPlacementValid(cand, r, zone, enemies, minEnemyDist, k, out string? why);
            if (overTable) DrawGhost(dl, GetBaseShape(binding.GetValue()), io.MousePos, _scale, valid, facing);
            if (clicked)
            {
                if (valid) { _placed[k] = new PlacedObjectEntry<T>(binding, cand, facing); _dragIndex = null; _errorMessage = null; }
                else { _errorMessage = why; _errorExpiry = ImGui.GetTime() + 2.5; }
            }
            return;
        }

        // Not dragging: a click on a placed model picks it up (and syncs the rotation to its facing).
        int hitIdx = HitTestPlaced(mouseInX, mouseInZ);
        if (clicked && hitIdx >= 0)
        {
            _dragIndex = hitIdx;
            _singleRotationDeploy = _placed[hitIdx].Facing is Float2 pf ? FacingToRadians(pf) : 0f;
            _errorMessage = null;
            return;
        }

        // All placed and nothing picked up: wait for Done (or a pick-up).
        if (_placed.Count >= request.ModelsToPlace.Count) return;

        // Place the next model.
        var currentBinding = request.ModelsToPlace[_placed.Count];
        float curR = GetBaseRadius(currentBinding.GetValue());
        var candidate = new Position(mouseInX, mouseInZ);
        bool ok = IsPlacementValid(candidate, curR, zone, enemies, minEnemyDist, -1, out string? reason);
        if (overTable) DrawGhost(dl, GetBaseShape(currentBinding.GetValue()), io.MousePos, _scale, ok, facing);
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

        // Rotation input: wheel both ways; R clockwise, Shift+R counter-clockwise.
        if (wantInput)
        {
            if (io.MouseWheel != 0f)
                _groupRotationDeploy += io.MouseWheel > 0f ? GroupRotationStep : -GroupRotationStep;
            if (ImGui.IsKeyPressed(ImGuiKey.R))
            {
                bool shift = ImGui.IsKeyDown(ImGuiKey.LeftShift) || ImGui.IsKeyDown(ImGuiKey.RightShift);
                _groupRotationDeploy += shift ? GroupRotationStep : -GroupRotationStep;
            }
        }

        var radii = new float[n];
        for (int i = 0; i < n; i++) radii[i] = GetBaseRadius(models[i].GetValue());

        // Forward = toward table centre: longer/front row sits on that side.
        float forwardSign = zone.Bounds.CenterZ < _tableH * 0.5f ? 1f : -1f;
        var offsets = GroupFormationUtilities.ComputeDeploymentOffsets(
            radii, 0.1f, GameWideConstants.MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES, forwardSign);

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
        Float2 groupFacing = RotateFacing(_groupRotationDeploy); // all models face the rotated direction (#150)
        var positions = new Position[n];
        bool allValid = true;
        for (int i = 0; i < n; i++)
        {
            float dx = offsets[i].dx, dz = offsets[i].dz;
            float rx = dx * cos - dz * sin, rz = dx * sin + dz * cos;
            positions[i] = new Position(centroid.x + rx, centroid.z + rz);
            bool valid = IsGroupSlotValid(positions[i], radii[i], zone, enemies, minEnemyDist);
            DrawGhost(dl, GetBaseShape(models[i].GetValue()), ToPixelVec(positions[i]), _scale, valid, groupFacing);
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
                for (int i = 0; i < n; i++) _placed.Add(new PlacedObjectEntry<T>(models[i], positions[i], groupFacing));
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

    /// <summary>Full single-placement validity for a candidate, ignoring placed model <paramref name="excludeIndex"/>.</summary>
    private bool IsPlacementValid(Position cand, float r, IBoundedZone zone, List<Position> enemies,
        float minEnemyDist, int excludeIndex, out string? reason)
    {
        if (!IsBaseWithinZone(cand, r, zone))
        { reason = "Outside deployment zone."; return false; }

        string? overlap = CheckOverlap(cand, r, excludeIndex);
        if (overlap != null) { reason = $"Bases overlap ({overlap})."; return false; }

        if (!IsInCohesion(cand, r, excludeIndex))
        { reason = $"Outside cohesion - must be within {GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES}\" base-to-base of a placed model."; return false; }

        if (minEnemyDist > 0f && TooCloseToEnemy(cand, enemies, minEnemyDist))
        { reason = $"Too close to an enemy - must be over {minEnemyDist:F0}\" from enemy units."; return false; }

        if (OnImpassibleTerrain(cand, r))
        { reason = "On impassible terrain - the model's base would overlap a building or blocker."; return false; }

        reason = null;
        return true;
    }

    /// <summary>Validity for a model in a dropped group: zone containment, no overlap with on-table
    /// occupants or terrain, and enemy spacing. Intra-formation overlap/cohesion are guaranteed by the
    /// layout, so they aren't re-checked here.</summary>
    private bool IsGroupSlotValid(Position cand, float r, IBoundedZone zone, List<Position> enemies, float minEnemyDist)
    {
        if (!IsBaseWithinZone(cand, r, zone)) return false;
        foreach (var (pos, radius) in GetTableOccupants())
            if (Overlaps(cand, r, pos, radius)) return false;
        if (minEnemyDist > 0f && TooCloseToEnemy(cand, enemies, minEnemyDist)) return false;
        if (OnImpassibleTerrain(cand, r)) return false;
        return true;
    }

    // A model's base is within the zone if its centre keeps the base inside the bounding box (inset by the
    // base radius) AND the centre is inside the zone's true shape — so a circular zone constrains placement
    // to the real circle, not its bounding square, while a rectangle keeps its base-fully-inside behaviour.
    private static bool IsBaseWithinZone(Position cand, float r, IBoundedZone zone)
    {
        ZoneBounds b = zone.Bounds;
        return cand.x >= b.Left + r && cand.x <= b.Right - r
            && cand.z >= b.Bottom + r && cand.z <= b.Top - r
            && zone.IsPointWithinZone(cand);
    }

    private void DrawZone(ImDrawListPtr dl, IZone zone)
    {
        uint fill    = ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 0.60f, 1.00f, 0.12f));
        uint outline = ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 0.60f, 1.00f, 0.80f));
        ZoneRenderer.DrawFilled(zone, dl, _scale, _originX, _originY, _tableH, fill, outline);
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
        TaskCompletionSource<List<PlacedObjectEntry<T>>> tcs)
    {
        int total = request.ModelsToPlace.Count;
        bool group = _formationMode.IsGroup;
        bool dropping = group && _placed.Count == 0; // showing the whole-unit ghost
        float panelW = MathF.Min(screenW * 0.5f, 580f);
        float panelH = 212f;
        ImGui.SetNextWindowPos(new Vector2((screenW - panelW) * 0.5f, 16f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(panelW, panelH), ImGuiCond.Always);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.10f, 0.10f, 0.15f, 0.92f));
        ImGui.Begin("##PlacePanel",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleColor();

        ImGui.TextUnformatted($"Deploy: {_placed.Count} / {total} models placed");
        ImGui.SameLine();
        ImGui.TextDisabled($"  zone X {request.DeploymentZone.Bounds.Left:F0}-{request.DeploymentZone.Bounds.Right:F0}\"");

        if (ImGui.Button(group ? "Mode: Group (G)" : "Mode: Single (G)"))
        {
            _formationMode.Toggle();
            _dragIndex = null;
        }

        ImGui.Spacing();
        if (_errorMessage != null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.4f, 0.4f, 1f));
            ImGui.TextWrapped(_errorMessage);
            ImGui.PopStyleColor();
        }
        else
        {
            string hint =
                dropping              ? "Position the unit in the blue zone. Wheel / R rotate. Click drops the whole unit." :
                _dragIndex.HasValue   ? "Click to drop the picked-up model." :
                _placed.Count < total ? "Click empty space to place the next model, or click a placed model to move it." :
                                        "Click any placed model to pick it up and move it.";
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.6f, 0.6f, 0.6f, 1f));
            ImGui.TextWrapped(hint);
            ImGui.PopStyleColor();
        }

        // #029: edge-constrained placement (Aircraft redeploy) — surface the touch requirement live.
        if (request.MustTouchTableEdge)
        {
            bool touching = PlacedTouchesEdge(request);
            ImGui.PushStyleColor(ImGuiCol.Text, touching
                ? new Vector4(0.5f, 0.9f, 0.5f, 1f)
                : new Vector4(0.95f, 0.75f, 0.3f, 1f));
            ImGui.TextWrapped(touching
                ? "Touching a table edge."
                : "Must come on touching a table edge.");
            ImGui.PopStyleColor();
        }

        ImGui.Spacing();
        float btnW = (panelW - ImGui.GetStyle().ItemSpacing.X * 2 - ImGui.GetStyle().WindowPadding.X * 2) / 3f;
        float fullW = panelW - ImGui.GetStyle().WindowPadding.X * 2;

        ImGui.BeginDisabled(_placed.Count == 0);
        if (ImGui.Button("Undo", new Vector2(btnW, 28f)))
        {
            _placed.RemoveAt(_placed.Count - 1);
            _dragIndex = null;
            _errorMessage = null;
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(_placed.Count >= total);
        if (ImGui.Button("Auto-place", new Vector2(btnW, 28f)))
        {
            if (AutoPlaceRemaining(request)) _errorMessage = null;
            else
            {
                _errorMessage = "Could not auto-place all remaining models - zone too crowded.";
                _errorExpiry  = ImGui.GetTime() + 3.0;
            }
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(_placed.Count == 0);
        if (ImGui.Button("Restart", new Vector2(btnW, 28f)))
        {
            _placed.Clear();
            _dragIndex = null;
            _errorMessage = null;
        }
        ImGui.EndDisabled();

        bool canDone = _placed.Count == total && !_dragIndex.HasValue
            && (!request.MustTouchTableEdge || PlacedTouchesEdge(request));
        ImGui.BeginDisabled(!canDone);
        bool donePressed = ImGui.Button("Done", new Vector2(fullW, 28f));
        ImGui.EndDisabled();
        if (canDone && donePressed)
        {
            Complete(tcs, new List<PlacedObjectEntry<T>>(_placed));
            ImGui.End();
            return;
        }

        ImGui.End();
    }

    /// <summary>Tries to place all remaining models into free spots; returns false if any can't fit.</summary>
    private bool AutoPlaceRemaining(PlaceObjectsRequest<T> request)
    {
        var zone = request.DeploymentZone;
        float maxRadius = request.ModelsToPlace
            .Select(b => GetBaseRadius(b.GetValue()))
            .DefaultIfEmpty(0.75f).Max();
        float step = maxRadius * 2 + 0.1f;
        float startCz = zone.Bounds.CenterZ;

        float minEnemyDist = request.MinDistanceFromEnemiesInches;
        var enemies = minEnemyDist > 0f ? GetEnemyPositions(request.TargetPlayerID) : _noEnemies;

        for (int i = _placed.Count; i < request.ModelsToPlace.Count; i++)
        {
            var binding = request.ModelsToPlace[i];
            float r = GetBaseRadius(binding.GetValue());

            if (!TryFindAutoPosition(r, step, zone, startCz, enemies, minEnemyDist, out Position pos))
                return false;
            _placed.Add(new PlacedObjectEntry<T>(binding, pos));
        }
        return true;
    }

    private bool TryFindAutoPosition(float r, float step, IBoundedZone zone, float startCz,
        List<Position> enemies, float minEnemyDist, out Position result)
    {
        // Sweep rows out from zone centre: 0, +step, -step, +2*step, -2*step, ...
        ZoneBounds b = zone.Bounds;
        int maxRows = (int)((b.Top - b.Bottom) / step) + 1;
        for (int rowOffset = 0; rowOffset <= maxRows; rowOffset++)
        {
            int signCount = rowOffset == 0 ? 1 : 2;
            for (int s = 0; s < signCount; s++)
            {
                float z = startCz + (s == 0 ? rowOffset : -rowOffset) * step;
                if (z < b.Bottom + r || z > b.Top - r) continue;

                for (float x = b.Left + r; x <= b.Right - r; x += step * 0.5f)
                {
                    var c = new Position(x, z);
                    if (!zone.IsPointWithinZone(c)) continue; // outside the true shape (e.g. a circle's corners)
                    if (CheckOverlap(c, r) != null) continue;
                    if (OnImpassibleTerrain(c, r)) continue;
                    if (minEnemyDist > 0f && TooCloseToEnemy(c, enemies, minEnemyDist)) continue;
                    if (!IsInCohesion(c, r, -1)) continue;
                    result = c; return true;
                }
            }
        }
        result = default;
        return false;
    }

    private static readonly List<Position> _noEnemies = new();

    // IEnemyExclusionProvider: surfaces the live enemy centres + radius while an Ambush-style placement
    // (MinDistanceFromEnemiesInches > 0) is pending, so the renderer can draw the no-go blob. The engine
    // thread is blocked awaiting this resolution during placement, so reading table state here is safe.
    public bool TryGetEnemyExclusion(out IReadOnlyList<Position> enemyCenters, out float radiusInches)
    {
        PlaceObjectsRequest<T>? request;
        lock (_lock) request = _request;

        if (request == null || request.MinDistanceFromEnemiesInches <= 0f)
        {
            enemyCenters = _noEnemies;
            radiusInches = 0f;
            return false;
        }

        enemyCenters = GetEnemyPositions(request.TargetPlayerID);
        radiusInches = request.MinDistanceFromEnemiesInches;
        return true;
    }

    private List<Position> GetEnemyPositions(PlayerID self)
    {
        var positions = new List<Position>();
        foreach (var unit in _tableState.Units.Objects)
        {
            if (unit.PlayerID == self) continue;
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

    private static bool TooCloseToEnemy(Position p, List<Position> enemies, float minDist)
    {
        foreach (var e in enemies)
            if (Dist(p, e) < minDist) return true;
        return false;
    }

    // True if a model placed here would overlap impassible terrain (a model occupies its base disc).
    private bool OnImpassibleTerrain(Position candidate, float radius) =>
        PlacementUtilities.OverlapsImpassibleTerrain(candidate, radius, _tableState.Terrain.Objects);

    /// <summary>Returns null if free; otherwise a brief description of the conflict. Ignores the placed
    /// model at <paramref name="excludeIndex"/> (the one being re-placed by drag).</summary>
    private string? CheckOverlap(Position newPos, float newRadius, int excludeIndex = -1)
    {
        for (int i = 0; i < _placed.Count; i++)
        {
            if (i == excludeIndex) continue;
            var entry = _placed[i];
            float er = GetBaseRadius(entry.Binding.GetValue());
            if (Overlaps(newPos, newRadius, entry.Position, er))
                return $"need {newRadius + er:F2}\", got {Dist(newPos, entry.Position):F2}\"";
        }
        foreach (var (pos, radius) in GetTableOccupants())
        {
            if (Overlaps(newPos, newRadius, pos, radius))
                return $"need {newRadius + radius:F2}\", got {Dist(newPos, pos):F2}\"";
        }
        return null;
    }

    private IEnumerable<(Position pos, float radius)> GetTableOccupants()
    {
        foreach (var model in _tableState.Models.Objects)
        {
            var pos = model.Position;
            // Default-constructed Position is (0,0,0); models there haven't been placed yet.
            if (pos.x == 0f && pos.z == 0f) continue;
            // Circumscribing radius so a placed rectangle's whole footprint is avoided at any facing (#150).
            yield return (pos, model.BaseShape.CircumscribedRadiusInches);
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

    private void Complete(TaskCompletionSource<List<PlacedObjectEntry<T>>> tcs, List<PlacedObjectEntry<T>> entries)
    {
        lock (_lock) { _request = null; _tcs = null; _placed.Clear(); }
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

    private static bool Overlaps(Position a, float ra, Position b, float rb) => Dist(a, b) < ra + rb;

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

    /// <summary>True if the candidate is within nearest-neighbour cohesion of at least one other placed
    /// model, ignoring <paramref name="excludeIndex"/>. Vacuously true when there are no other models.</summary>
    private bool IsInCohesion(Position candidate, float candidateRadius, int excludeIndex)
    {
        bool anyOther = false;
        for (int i = 0; i < _placed.Count; i++)
        {
            if (i == excludeIndex) continue;
            anyOther = true;
            var entry = _placed[i];
            float er = GetBaseRadius(entry.Binding.GetValue());
            float b2b = Dist(candidate, entry.Position) - candidateRadius - er;
            if (b2b <= GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES)
                return true;
        }
        return !anyOther;
    }
}
