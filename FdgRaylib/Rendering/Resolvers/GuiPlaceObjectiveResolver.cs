using System.Numerics;
using FDG;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// GUI resolver for <see cref="PlaceObjectiveRequest"/>.
///
/// Two modes:
///   AwaitingClick   — a ghost marker follows the cursor. Green outline when the
///                     position is legal; red otherwise. Click commits to AwaitingConfirm.
///   AwaitingConfirm — ghost is frozen at the click position (slightly brighter to
///                     read as pending). Info panel shows Confirm/Cancel buttons.
///                     Enter = Confirm; Esc / right-click = Cancel returns to AwaitingClick.
///
/// Info panel default position is top-left (16, 16) to avoid the Outstanding Tasks
/// window that lives top-center, and it's draggable (FirstUseEver) so the user can
/// move it once and the choice sticks.
/// </summary>
public class GuiPlaceObjectiveResolver
    : IStageResolver<PlaceObjectiveRequest, Position>, IGuiResolver, IGuiCanvasOverlay
{
    private readonly ITableState _tableState;
    private readonly object _lock = new();

    // Layout — main thread only
    private float _scale  = 10f;
    private float _tableH = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
    private int   _originX, _originY;

    // Request state
    private PlaceObjectiveRequest? _request;
    private TaskCompletionSource<Position>? _tcs;
    private Position? _pendingCandidate;

    private const float ObjectiveBaseRadiusInches    = 0.5f;
    private const float ObjectiveSeizureRadiusInches = 3f;

    public GuiPlaceObjectiveResolver(ITableState tableState) => _tableState = tableState;

    public void UpdateLayout(float scale, int originX, int originY, float tableH)
    {
        _scale = scale; _originX = originX; _originY = originY; _tableH = tableH;
    }

    public bool HasPendingRequest { get { lock (_lock) return _request != null; } }

    public Task<Position> Resolve(PlaceObjectiveRequest request)
    {
        var tcs = new TaskCompletionSource<Position>();
        lock (_lock)
        {
            _tcs = tcs;
            _request = request;
            _pendingCandidate = null;
        }
        return tcs.Task;
    }

    public void Draw(int screenW, int screenH)
    {
        PlaceObjectiveRequest? request;
        TaskCompletionSource<Position>? tcs;
        Position? pending;
        lock (_lock) { request = _request; tcs = _tcs; pending = _pendingCandidate; }
        if (request == null || tcs == null) return;

        var io = ImGui.GetIO();
        var dl = ImGui.GetBackgroundDrawList();

        DrawLegalBand(dl, request.LegalBand);
        DrawExistingMarkers(dl);

        int markerNumber = request.MarkerIndex;

        if (pending.HasValue)
        {
            // Confirmation mode — frozen ghost, brighter, plus the Confirm/Cancel panel.
            DrawGhost(dl, pending.Value, markerNumber, valid: true, frozen: true);

            // Esc / right-click cancels.
            if (EscapeGate.TryConsume() ||
                (!io.WantCaptureMouse && ImGui.IsMouseClicked(ImGuiMouseButton.Right)))
            {
                lock (_lock) _pendingCandidate = null;
                pending = null;
            }
        }
        else
        {
            // Live ghost mode — follows cursor; click commits to confirmation.
            bool overTable = IsOverTable(io.MousePos.X, io.MousePos.Y);
            var (mx, mz) = PixelToInches(io.MousePos.X, io.MousePos.Y);
            var candidate = new Position(mx, mz);

            var validity = Validate(candidate, request);
            bool valid = validity == ObjectivePlacementValidity.Valid;

            if (overTable)
                DrawGhost(dl, candidate, markerNumber, valid, frozen: false);

            if (overTable && !io.WantCaptureMouse && valid &&
                ImGui.IsMouseClicked(ImGuiMouseButton.Left))
            {
                lock (_lock) _pendingCandidate = candidate;
                pending = candidate;
            }
        }

        DrawInfoPanel(screenW, request, tcs, pending);
    }

    // ---- drawing ----

    private void DrawLegalBand(ImDrawListPtr dl, RectangularZone band)
    {
        uint fill    = ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 0.85f, 0.30f, 0.10f));
        uint outline = ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 0.85f, 0.30f, 0.70f));
        ZoneRenderer.DrawFilled(band, dl, _scale, _originX, _originY, _tableH, fill, outline);
    }

    private void DrawExistingMarkers(ImDrawListPtr dl)
    {
        var objectives = _tableState.Objectives.Objects.ToList();
        for (int i = 0; i < objectives.Count; i++)
        {
            var obj = objectives[i];
            var (px, py) = InchesToPixel(obj.Position.x, obj.Position.z);
            var center = new Vector2(px, py);

            // 9" exclusion zone (no-go for new markers).
            float exclusionPx = _request!.MinSeparationInches * _scale;
            uint exclusionFill    = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.20f, 0.20f, 0.06f));
            uint exclusionOutline = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.20f, 0.20f, 0.35f));
            dl.AddCircleFilled(center, exclusionPx, exclusionFill);
            dl.AddCircle(center, exclusionPx, exclusionOutline, 48, 1f);

            DrawMarkerVisual(dl, center, i + 1, neutral: true, brightness: 1.0f);
        }
    }

    private void DrawGhost(ImDrawListPtr dl, Position pos, int number, bool valid, bool frozen)
    {
        var (px, py) = InchesToPixel(pos.x, pos.z);
        var center = new Vector2(px, py);

        // Outline encodes validity (only meaningful in live mode; frozen is always "valid"
        // because we don't enter frozen mode from an invalid click).
        uint outline = valid
            ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.30f, 1.00f, 0.30f, 0.95f))
            : ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.30f, 0.30f, 0.95f));

        // 3" seizure ring — measured from the center, base disc is separate.
        float seizurePx = ObjectiveSeizureRadiusInches * _scale;
        uint ringFill = ImGui.ColorConvertFloat4ToU32(
            new Vector4(0.70f, 0.70f, 0.70f, frozen ? 0.30f : 0.20f));
        dl.AddCircleFilled(center, seizurePx, ringFill);
        dl.AddCircle(center, seizurePx, outline, 48, 2f);

        DrawMarkerVisual(dl, center, number, neutral: true, brightness: frozen ? 1.0f : 0.85f);
    }

    private void DrawMarkerVisual(ImDrawListPtr dl, Vector2 center, int number, bool neutral, float brightness)
    {
        float radiusPx = ObjectiveBaseRadiusInches * _scale;
        // Neutral grey for unowned markers — matches RaylibRenderer.ObjectiveNeutralColor (160,160,160).
        float g = 160f / 255f * brightness;
        uint fill    = ImGui.ColorConvertFloat4ToU32(new Vector4(g, g, g, 1f));
        uint outline = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 1f));
        dl.AddCircleFilled(center, radiusPx, fill);
        dl.AddCircle(center, radiusPx, outline, 32, 1f);

        string label = number.ToString();
        float fontSize = MathF.Max(10f, radiusPx * 1.5f);
        var textSize = ImGui.CalcTextSize(label);
        // ImGui.CalcTextSize uses the current font's size; scale by fontSize/font.FontSize for our intended size.
        // Simpler: just place at default font size centered.
        dl.AddText(new Vector2(center.X - textSize.X / 2f, center.Y - textSize.Y / 2f),
            ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 1f)), label);
    }

    // ---- info panel ----

    private void DrawInfoPanel(int screenW, PlaceObjectiveRequest request,
        TaskCompletionSource<Position> tcs, Position? pending)
    {
        // Docked into the right-column resolver panel.
        ResolverPanelLayout.BeginDocked("Place Objective##placeobjectivepanel");

        ImGui.TextUnformatted($"Objective {request.MarkerIndex} of {request.TotalMarkers}");
        ImGui.Spacing();

        if (pending.HasValue)
        {
            ImGui.TextWrapped($"Place here? (X {pending.Value.x:F1}\", Z {pending.Value.z:F1}\")");
            ImGui.Spacing();

            // Primary: Confirm (accent + Enter). Cancel is de-emphasized.
            bool confirmPressed = ResolverButtons.Primary("Confirm", new Vector2(190f, 30f));
            ImGui.SameLine();
            bool cancelPressed = ResolverButtons.Deemphasized("Cancel", new Vector2(120f, 30f));

            if (confirmPressed)
            {
                Complete(tcs, pending.Value);
            }
            else if (cancelPressed)
            {
                lock (_lock) _pendingCandidate = null;
            }
        }
        else
        {
            ImGui.TextWrapped("Click in the green band, >9\" from other markers, outside impassable terrain.");
            ImGui.Spacing();
            ImGui.TextDisabled("Hover the table to preview placement.");
        }

        ImGui.End();
    }

    // ---- validation ----

    private ObjectivePlacementValidity Validate(Position candidate, PlaceObjectiveRequest request)
    {
        var existing = _tableState.Objectives.Objects;
        var impassable = _tableState.Terrain.Objects
            .Where(t => t.TerrainType.HasFlag(ETerrainType.Impassible));
        return ObjectivePlacementValidator.Check(
            candidate, request.LegalBand, request.MinSeparationInches, existing, impassable);
    }

    // ---- helpers ----

    private void Complete(TaskCompletionSource<Position> tcs, Position result)
    {
        lock (_lock) { _request = null; _tcs = null; _pendingCandidate = null; }
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
}
