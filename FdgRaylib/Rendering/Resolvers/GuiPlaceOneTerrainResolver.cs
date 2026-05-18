using System.Numerics;
using FDG;
using FDG.SaveLoad;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// GUI resolver for <see cref="PlaceOneTerrainRequest"/>.
///
/// Three modes for the active placer:
///   TemplateSelection — right-side panel lists templates. No ghost on the canvas.
///   AwaitingClick     — a chosen template's ghost follows the cursor. Outline green
///                       when the placement is legal (in-bounds, no overlap); red
///                       otherwise. Click commits to AwaitingConfirm.
///   AwaitingConfirm   — frozen ghost; Confirm/Cancel panel appears.
///                       Enter = Confirm; Esc = Cancel back to template selection.
/// </summary>
public class GuiPlaceOneTerrainResolver
    : IStageResolver<PlaceOneTerrainRequest, TerrainPlacementResult>, IGuiResolver, IGuiCanvasOverlay
{
    private readonly ITableState _tableState;
    private readonly object _lock = new();

    private float _scale = 10f;
    private float _tableH = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
    private int _originX, _originY;

    private PlaceOneTerrainRequest? _request;
    private TaskCompletionSource<TerrainPlacementResult>? _tcs;
    private int? _selectedTemplate;
    private Float2? _pendingCenter;

    public GuiPlaceOneTerrainResolver(ITableState tableState) => _tableState = tableState;

    public void UpdateLayout(float scale, int originX, int originY, float tableH)
    {
        _scale = scale; _originX = originX; _originY = originY; _tableH = tableH;
    }

    public bool HasPendingRequest { get { lock (_lock) return _request != null; } }

    public Task<TerrainPlacementResult> Resolve(PlaceOneTerrainRequest request)
    {
        var tcs = new TaskCompletionSource<TerrainPlacementResult>();
        lock (_lock)
        {
            _tcs = tcs;
            _request = request;
            _selectedTemplate = null;
            _pendingCenter = null;
        }
        return tcs.Task;
    }

    public void Draw(int screenW, int screenH)
    {
        PlaceOneTerrainRequest? request;
        TaskCompletionSource<TerrainPlacementResult>? tcs;
        int? selected;
        Float2? pending;
        lock (_lock) { request = _request; tcs = _tcs; selected = _selectedTemplate; pending = _pendingCenter; }
        if (request == null || tcs == null) return;

        var io = ImGui.GetIO();
        var dl = ImGui.GetBackgroundDrawList();

        if (selected.HasValue)
        {
            TerrainPieceEntry template = request.Pool[selected.Value];

            if (pending.HasValue)
            {
                // AwaitingConfirm: frozen ghost.
                DrawGhost(dl, template, pending.Value, valid: true, frozen: true);

                if (ImGui.IsKeyPressed(ImGuiKey.Escape))
                {
                    lock (_lock) { _pendingCenter = null; }
                    pending = null;
                }
            }
            else
            {
                // AwaitingClick: live ghost following cursor.
                bool overTable = IsOverTable(io.MousePos.X, io.MousePos.Y);
                var (mx, mz) = PixelToInches(io.MousePos.X, io.MousePos.Y);
                Float2 center = new Float2(mx, mz);

                IZone candidateShape = TerrainTemplateUtilities.TranslateToCenter(template.Shape, center);
                bool valid = TerrainPlacementValidator.Check(
                    candidateShape, request.TableWidthInches, request.TableHeightInches,
                    _tableState.Terrain.Objects) == TerrainPlacementValidity.Valid;

                if (overTable)
                    DrawGhost(dl, template, center, valid, frozen: false);

                if (overTable && !io.WantCaptureMouse && valid &&
                    ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    lock (_lock) _pendingCenter = center;
                    pending = center;
                }

                // Right-click or Esc returns to template selection.
                if (ImGui.IsKeyPressed(ImGuiKey.Escape) ||
                    (!io.WantCaptureMouse && ImGui.IsMouseClicked(ImGuiMouseButton.Right)))
                {
                    lock (_lock) _selectedTemplate = null;
                    selected = null;
                }
            }
        }

        DrawInfoPanel(screenW, screenH, request, tcs, selected, pending);
    }

    private void DrawGhost(ImDrawListPtr dl, TerrainPieceEntry template, Float2 center, bool valid, bool frozen)
    {
        IZone placed = TerrainTemplateUtilities.TranslateToCenter(template.Shape, center);

        (Vector4 fillBase, Vector4 outlineBase) = TerrainTypeColors(template.TerrainType);
        float fillAlpha = frozen ? 0.45f : 0.30f;
        Vector4 fillColor = new Vector4(fillBase.X, fillBase.Y, fillBase.Z, fillAlpha);

        Vector4 validityOutline = valid
            ? new Vector4(0.30f, 1.00f, 0.30f, 0.95f)
            : new Vector4(1.00f, 0.30f, 0.30f, 0.95f);

        uint fillU32 = ImGui.ColorConvertFloat4ToU32(fillColor);
        uint outlineU32 = ImGui.ColorConvertFloat4ToU32(validityOutline);

        ZoneRenderer.DrawFilled(placed, dl, _scale, _originX, _originY, _tableH, fillU32, outlineU32, outlineThickness: 2.5f);
    }

    private static (Vector4 fill, Vector4 outline) TerrainTypeColors(ETerrainType type)
    {
        // Pick a representative tint by flag priority: dangerous (red) > impassable (dark grey)
        // > blocking (medium grey) > cover (green) > difficult (yellow) > default (light grey).
        if (type.HasFlag(ETerrainType.Dangerous))
            return (new Vector4(0.95f, 0.40f, 0.20f, 1f), new Vector4(0.95f, 0.40f, 0.20f, 1f));
        if (type.HasFlag(ETerrainType.Impassible))
            return (new Vector4(0.25f, 0.25f, 0.30f, 1f), new Vector4(0.40f, 0.40f, 0.50f, 1f));
        if (type.HasFlag(ETerrainType.Blocking))
            return (new Vector4(0.45f, 0.45f, 0.50f, 1f), new Vector4(0.55f, 0.55f, 0.60f, 1f));
        if (type.HasFlag(ETerrainType.Cover))
            return (new Vector4(0.30f, 0.65f, 0.35f, 1f), new Vector4(0.40f, 0.85f, 0.45f, 1f));
        if (type.HasFlag(ETerrainType.Difficult))
            return (new Vector4(0.80f, 0.70f, 0.30f, 1f), new Vector4(0.95f, 0.85f, 0.40f, 1f));
        return (new Vector4(0.65f, 0.65f, 0.65f, 1f), new Vector4(0.80f, 0.80f, 0.80f, 1f));
    }

    private void DrawInfoPanel(int screenW, int screenH, PlaceOneTerrainRequest request,
        TaskCompletionSource<TerrainPlacementResult> tcs, int? selected, Float2? pending)
    {
        const float PanelWidth = 280f;
        ImGui.SetNextWindowPos(new Vector2(screenW - PanelWidth - 12f, 80f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowSize(new Vector2(PanelWidth, 0f), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowBgAlpha(0.92f);

        ImGui.Begin("Place Terrain",
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings);

        int remaining = request.TotalPieces - request.PiecesPlaced;
        ImGui.TextUnformatted($"Pieces remaining: {remaining}");
        ImGui.TextUnformatted($"(piece {request.PiecesPlaced + 1} of {request.TotalPieces})");
        ImGui.Separator();

        if (pending.HasValue && selected.HasValue)
        {
            ImGui.TextWrapped($"Place {DescribeTemplate(request.Pool[selected.Value])} here?");
            ImGui.TextDisabled($"Center: ({pending.Value.X:F1}\", {pending.Value.Y:F1}\")");
            ImGui.Spacing();

            float btnW = 120f;
            bool confirmPressed = ImGui.Button("Confirm", new Vector2(btnW, 28f))
                || ImGui.IsKeyPressed(ImGuiKey.Enter)
                || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter);
            ImGui.SameLine();
            bool cancelPressed = ImGui.Button("Cancel", new Vector2(btnW, 28f));

            if (confirmPressed)
                Complete(tcs, new TerrainPlacementResult(selected.Value, pending.Value));
            else if (cancelPressed)
                lock (_lock) { _pendingCenter = null; _selectedTemplate = null; }
        }
        else if (selected.HasValue)
        {
            ImGui.TextWrapped($"Placing: {DescribeTemplate(request.Pool[selected.Value])}");
            ImGui.TextDisabled("Hover to preview. Left-click to place. Right-click or Esc to switch template.");
        }
        else
        {
            ImGui.TextUnformatted("Pick a piece:");
            for (int i = 0; i < request.Pool.Count; i++)
            {
                var entry = request.Pool[i];
                string label = $"{DescribeTemplate(entry)}##t{i}";
                if (ImGui.Button(label, new Vector2(PanelWidth - 20f, 0f)))
                {
                    lock (_lock) _selectedTemplate = i;
                }
            }
        }

        ImGui.End();
    }

    private static string DescribeTemplate(TerrainPieceEntry entry)
    {
        string typeText = entry.TerrainType == ETerrainType.None ? "None" : entry.TerrainType.ToString();
        return entry.Shape switch
        {
            RectangularZone r => $"{typeText} ({r.Right - r.Left:F1}\"x{r.Top - r.Bottom:F1}\")",
            CircularZone c => $"{typeText} (r={c.Radius:F1}\")",
            _ => typeText,
        };
    }

    private void Complete(TaskCompletionSource<TerrainPlacementResult> tcs, TerrainPlacementResult result)
    {
        lock (_lock) { _request = null; _tcs = null; _selectedTemplate = null; _pendingCenter = null; }
        tcs.SetResult(result);
    }

    private (float x, float z) PixelToInches(float px, float py) =>
        ((px - _originX) / _scale, _tableH - (py - _originY) / _scale);

    private bool IsOverTable(float px, float py) =>
        px >= _originX && py >= _originY &&
        px <= _originX + GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES * _scale &&
        py <= _originY + _tableH * _scale;
}
