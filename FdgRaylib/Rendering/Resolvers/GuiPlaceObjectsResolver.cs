using System.Numerics;
using FDG;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

public class GuiPlaceObjectsResolver<T>
    : IStageResolver<PlaceObjectsRequest<T>, List<PlacedObjectEntry<T>>>, IGuiResolver, IGuiCanvasOverlay
{
    private readonly ITableState _tableState;
    private readonly object _lock = new();

    // Layout — main thread only
    private float _scale  = 10f;
    private float _tableH = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
    private int   _originX, _originY;

    // Request state
    private PlaceObjectsRequest<T>? _request;
    private TaskCompletionSource<List<PlacedObjectEntry<T>>>? _tcs;
    private readonly List<PlacedObjectEntry<T>> _placed = new();

    private string? _errorMessage;
    private double  _errorExpiry;

    public GuiPlaceObjectsResolver(ITableState tableState) => _tableState = tableState;

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
        var zone = request.DeploymentZone.GetValue();

        DrawZone(dl, zone);
        DrawPlacedSoFar(dl);

        var currentBinding = request.ModelsToPlace[_placed.Count];
        float currentRadius = GetBaseRadius(currentBinding.GetValue());

        var (mouseInX, mouseInZ) = PixelToInches(io.MousePos.X, io.MousePos.Y);
        bool overTable = IsOverTable(io.MousePos.X, io.MousePos.Y);

        var candidate = new Position(mouseInX, mouseInZ);
        bool inZone   = mouseInX >= zone.Left  + currentRadius && mouseInX <= zone.Right - currentRadius &&
                        mouseInZ >= zone.Bottom + currentRadius && mouseInZ <= zone.Top   - currentRadius;
        string? overlap = inZone ? CheckOverlap(candidate, currentRadius) : null;
        bool inCohesion = _placed.Count == 0 || IsInCohesionWithPlaced(candidate, currentRadius);
        bool valid = inZone && overlap == null && inCohesion;

        if (overTable) DrawGhost(dl, io.MousePos, currentRadius * _scale, valid);

        if (overTable && !io.WantCaptureMouse && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            if (!inZone)
            {
                _errorMessage = "Outside deployment zone.";
                _errorExpiry  = ImGui.GetTime() + 2.0;
            }
            else if (overlap != null)
            {
                _errorMessage = $"Bases overlap ({overlap}).";
                _errorExpiry  = ImGui.GetTime() + 2.0;
            }
            else if (!inCohesion)
            {
                _errorMessage = $"Outside cohesion - must be within {GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES}\" base-to-base of a placed model.";
                _errorExpiry  = ImGui.GetTime() + 2.5;
            }
            else
            {
                _placed.Add(new PlacedObjectEntry<T>(currentBinding, candidate));
                _errorMessage = null;
                if (_placed.Count >= request.ModelsToPlace.Count)
                {
                    Complete(tcs, new List<PlacedObjectEntry<T>>(_placed));
                    return;
                }
            }
        }

        if (_errorMessage != null && ImGui.GetTime() > _errorExpiry) _errorMessage = null;

        DrawInfoPanel(screenW, request, tcs);
    }

    private void DrawZone(ImDrawListPtr dl, IZone zone)
    {
        uint fill    = ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 0.60f, 1.00f, 0.12f));
        uint outline = ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 0.60f, 1.00f, 0.80f));
        ZoneRenderer.DrawFilled(zone, dl, _scale, _originX, _originY, _tableH, fill, outline);
    }

    private void DrawPlacedSoFar(ImDrawListPtr dl)
    {
        uint fill    = ImGui.ColorConvertFloat4ToU32(new Vector4(0.40f, 0.95f, 1.00f, 0.90f));
        uint outline = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 1f));
        foreach (var entry in _placed)
        {
            var (px, py) = InchesToPixel(entry.Position.x, entry.Position.z);
            float r = GetBaseRadius(entry.Binding.GetValue()) * _scale;
            dl.AddCircleFilled(new Vector2(px, py), r, fill);
            dl.AddCircle(new Vector2(px, py), r, outline, 32, 1f);
        }
    }

    private static void DrawGhost(ImDrawListPtr dl, Vector2 center, float radiusPx, bool valid)
    {
        uint fill = valid
            ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 1.00f, 0.20f, 0.50f))
            : ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.20f, 0.20f, 0.50f));
        uint outline = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 0.80f));
        dl.AddCircleFilled(center, radiusPx, fill);
        dl.AddCircle(center, radiusPx, outline, 32, 1f);
    }

    private void DrawInfoPanel(int screenW, PlaceObjectsRequest<T> request,
        TaskCompletionSource<List<PlacedObjectEntry<T>>> tcs)
    {
        int total = request.ModelsToPlace.Count;
        float panelW = MathF.Min(screenW * 0.42f, 500f);
        float panelH = 120f;
        ImGui.SetNextWindowPos(new Vector2((screenW - panelW) * 0.5f, 16f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(panelW, panelH), ImGuiCond.Always);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.10f, 0.10f, 0.15f, 0.92f));
        ImGui.Begin("##PlacePanel",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleColor();

        ImGui.TextUnformatted($"Deploy: {_placed.Count} / {total} models placed");
        ImGui.SameLine();
        ImGui.TextDisabled($"  zone X {request.DeploymentZone.GetValue().Left:F0}-{request.DeploymentZone.GetValue().Right:F0}\"");

        ImGui.Spacing();
        if (_errorMessage != null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.4f, 0.4f, 1f));
            ImGui.TextWrapped(_errorMessage);
            ImGui.PopStyleColor();
        }
        else
        {
            ImGui.TextDisabled("Click inside the blue zone to place each model.");
        }

        ImGui.Spacing();
        float btnW = (panelW - ImGui.GetStyle().ItemSpacing.X * 2 - ImGui.GetStyle().WindowPadding.X * 2) / 3f;

        ImGui.BeginDisabled(_placed.Count == 0);
        if (ImGui.Button("Undo", new Vector2(btnW, 28f)))
        {
            _placed.RemoveAt(_placed.Count - 1);
            _errorMessage = null;
        }
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (ImGui.Button("Auto-place rest", new Vector2(btnW, 28f)))
        {
            if (AutoPlaceRemaining(request))
                Complete(tcs, new List<PlacedObjectEntry<T>>(_placed));
            else
            {
                _errorMessage = "Could not auto-place all remaining models - zone too crowded.";
                _errorExpiry  = ImGui.GetTime() + 3.0;
            }
        }

        ImGui.SameLine();
        ImGui.BeginDisabled(_placed.Count == 0);
        if (ImGui.Button("Restart", new Vector2(btnW, 28f)))
        {
            _placed.Clear();
            _errorMessage = null;
        }
        ImGui.EndDisabled();

        ImGui.End();
    }

    /// <summary>Tries to place all remaining models into free spots; returns false if any can't fit.</summary>
    private bool AutoPlaceRemaining(PlaceObjectsRequest<T> request)
    {
        var zone = request.DeploymentZone.GetValue();
        float maxRadius = request.ModelsToPlace
            .Select(b => GetBaseRadius(b.GetValue()))
            .DefaultIfEmpty(0.75f).Max();
        float step = maxRadius * 2 + 0.1f;
        float startCz = (zone.Bottom + zone.Top) / 2f;

        for (int i = _placed.Count; i < request.ModelsToPlace.Count; i++)
        {
            var binding = request.ModelsToPlace[i];
            float r = GetBaseRadius(binding.GetValue());

            if (!TryFindAutoPosition(r, step, zone, startCz, out Position pos))
                return false;
            _placed.Add(new PlacedObjectEntry<T>(binding, pos));
        }
        return true;
    }

    private bool TryFindAutoPosition(float r, float step, RectangularZone zone, float startCz, out Position result)
    {
        // Sweep rows out from zone centre: 0, +step, -step, +2*step, -2*step, ...
        int maxRows = (int)((zone.Top - zone.Bottom) / step) + 1;
        for (int rowOffset = 0; rowOffset <= maxRows; rowOffset++)
        {
            int signCount = rowOffset == 0 ? 1 : 2;
            for (int s = 0; s < signCount; s++)
            {
                float z = startCz + (s == 0 ? rowOffset : -rowOffset) * step;
                if (z < zone.Bottom + r || z > zone.Top - r) continue;

                for (float x = zone.Left + r; x <= zone.Right - r; x += step * 0.5f)
                {
                    var c = new Position(x, z);
                    if (CheckOverlap(c, r) != null) continue;
                    if (!IsInCohesionWithPlaced(c, r)) continue;
                    result = c; return true;
                }
            }
        }
        result = default;
        return false;
    }

    /// <summary>Returns null if free; otherwise a brief description of the conflict.</summary>
    private string? CheckOverlap(Position newPos, float newRadius)
    {
        foreach (var entry in _placed)
        {
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
            yield return (pos, model.BaseRadiusInches);
        }
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

    private static float GetBaseRadius(T value) => value is ModelData m ? m.BaseRadiusInches : 0.75f;

    private bool IsInCohesionWithPlaced(Position candidate, float candidateRadius)
    {
        if (_placed.Count == 0) return true;
        foreach (var entry in _placed)
        {
            float er = GetBaseRadius(entry.Binding.GetValue());
            float b2b = Dist(candidate, entry.Position) - candidateRadius - er;
            if (b2b <= GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES)
                return true;
        }
        return false;
    }
}
