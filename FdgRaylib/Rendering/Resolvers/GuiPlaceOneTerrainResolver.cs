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
    private float _rotationDegrees;  // 0..315 in 45° steps; reset on template change.

    private const float RotationStepDegrees = 45f;

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
            _rotationDegrees = 0f;
        }
        return tcs.Task;
    }

    public void Draw(int screenW, int screenH)
    {
        PlaceOneTerrainRequest? request;
        TaskCompletionSource<TerrainPlacementResult>? tcs;
        int? selected;
        Float2? pending;
        float rotation;
        lock (_lock)
        {
            request = _request; tcs = _tcs;
            selected = _selectedTemplate; pending = _pendingCenter;
            rotation = _rotationDegrees;
        }
        if (request == null || tcs == null) return;

        var io = ImGui.GetIO();
        var dl = ImGui.GetBackgroundDrawList();

        if (selected.HasValue)
        {
            // R rotates regardless of whether we're awaiting-click or awaiting-confirm.
            if (ImGui.IsKeyPressed(ImGuiKey.R))
            {
                rotation = (rotation + RotationStepDegrees) % 360f;
                lock (_lock) _rotationDegrees = rotation;
            }

            TerrainPieceEntry template = request.Pool[selected.Value];
            IZone rotatedTemplate = TerrainTemplateUtilities.Rotate(template.Shape, rotation);

            if (pending.HasValue)
            {
                // AwaitingConfirm: frozen ghost, but rotation is still live (R re-rotates around the pending center).
                IZone placedShape = TerrainTemplateUtilities.TranslateToCenter(rotatedTemplate, pending.Value);
                bool stillValid = TerrainPlacementValidator.Check(
                    placedShape, request.TableWidthInches, request.TableHeightInches,
                    _tableState.Terrain.Objects) == TerrainPlacementValidity.Valid;

                DrawGhost(dl, template.TerrainType, placedShape, valid: stillValid, frozen: true);

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

                IZone candidateShape = TerrainTemplateUtilities.TranslateToCenter(rotatedTemplate, center);
                bool valid = TerrainPlacementValidator.Check(
                    candidateShape, request.TableWidthInches, request.TableHeightInches,
                    _tableState.Terrain.Objects) == TerrainPlacementValidity.Valid;

                if (overTable)
                    DrawGhost(dl, template.TerrainType, candidateShape, valid, frozen: false);

                if (overTable && !io.WantCaptureMouse && valid &&
                    ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                {
                    lock (_lock) _pendingCenter = center;
                    pending = center;
                }

                // Right-click or Esc returns to template selection (also resets rotation).
                if (ImGui.IsKeyPressed(ImGuiKey.Escape) ||
                    (!io.WantCaptureMouse && ImGui.IsMouseClicked(ImGuiMouseButton.Right)))
                {
                    lock (_lock) { _selectedTemplate = null; _rotationDegrees = 0f; }
                    selected = null;
                    rotation = 0f;
                }
            }
        }

        DrawInfoPanel(screenW, screenH, request, tcs, selected, pending, rotation);
    }

    private void DrawGhost(ImDrawListPtr dl, ETerrainType terrainType, IZone placed, bool valid, bool frozen)
    {
        (Vector4 fillBase, Vector4 outlineBase) = TerrainTypeColors(terrainType);
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
            return (new Vector4(0.55f, 0.40f, 0.25f, 1f), new Vector4(0.75f, 0.55f, 0.35f, 1f));
        if (type.HasFlag(ETerrainType.Difficult))
            return (new Vector4(0.80f, 0.70f, 0.30f, 1f), new Vector4(0.95f, 0.85f, 0.40f, 1f));
        return (new Vector4(0.65f, 0.65f, 0.65f, 1f), new Vector4(0.80f, 0.80f, 0.80f, 1f));
    }

    private void DrawInfoPanel(int screenW, int screenH, PlaceOneTerrainRequest request,
        TaskCompletionSource<TerrainPlacementResult> tcs, int? selected, Float2? pending, float rotationDegrees)
    {
        float PanelWidth = ResolverPanelLayout.ContentWidth;   // dock into the right-column resolver panel
        ResolverPanelLayout.BeginDocked("Place Terrain##placeterrainpanel");

        int remaining = request.TotalPieces - request.PiecesPlaced;
        ImGui.TextUnformatted($"Pieces remaining: {remaining}");
        ImGui.TextUnformatted($"(piece {request.PiecesPlaced + 1} of {request.TotalPieces})");
        ImGui.Separator();

        if (pending.HasValue && selected.HasValue)
        {
            ImGui.TextWrapped($"Place {DescribeTemplate(request.Pool[selected.Value])} here?");
            ImGui.TextDisabled($"Center: ({pending.Value.X:F1}\", {pending.Value.Y:F1}\")  Rotation: {rotationDegrees:F0}°");
            ImGui.TextDisabled("Press R to rotate 45°.");
            ImGui.Spacing();

            // Primary: Confirm (accent + Enter). Cancel is de-emphasized.
            bool confirmPressed = ResolverButtons.Primary("Confirm", new Vector2(190f, 30f));
            ImGui.SameLine();
            bool cancelPressed = ResolverButtons.Deemphasized("Cancel", new Vector2(120f, 30f));

            if (confirmPressed)
                Complete(tcs, new TerrainPlacementResult(selected.Value, pending.Value, rotationDegrees));
            else if (cancelPressed)
                lock (_lock) { _pendingCenter = null; _selectedTemplate = null; _rotationDegrees = 0f; }
        }
        else if (selected.HasValue)
        {
            ImGui.TextWrapped($"Placing: {DescribeTemplate(request.Pool[selected.Value])}");
            ImGui.TextDisabled($"Rotation: {rotationDegrees:F0}° (R = rotate 45°)");
            ImGui.TextDisabled("Hover to preview. Left-click to place. Right-click or Esc to switch template.");
        }
        else
        {
            ImGui.TextUnformatted("Pick a piece:");
            DrawTemplatePicker(request.Pool, PanelWidth);
        }

        ImGui.End();
    }

    /// <summary>
    /// Per-row template picker with a small to-scale shape preview on the left and the
    /// type/dimensions label on the right. All thumbnails share one pixels-per-inch
    /// scale derived from the largest piece in the pool, so relative sizes read
    /// correctly (a 6x4" building looks bigger than a 5"-radius forest).
    /// </summary>
    private void DrawTemplatePicker(IReadOnlyList<TerrainPieceEntry> pool, float panelWidth)
    {
        const float ThumbW = 56f;
        const float ThumbH = 36f;
        const float RowPad = 4f;
        const float ThumbDrawPad = 3f;
        float rowH = ThumbH + RowPad * 2;
        float rowW = panelWidth - 20f;

        float maxDim = ComputeMaxDimension(pool);
        float ppi = maxDim > 0f
            ? MathF.Min(ThumbW - ThumbDrawPad * 2, ThumbH - ThumbDrawPad * 2) / maxDim
            : 1f;

        var dl = ImGui.GetWindowDrawList();

        for (int i = 0; i < pool.Count; i++)
        {
            TerrainPieceEntry entry = pool[i];
            Vector2 rowOrigin = ImGui.GetCursorScreenPos();

            bool clicked = ImGui.InvisibleButton($"##t{i}", new Vector2(rowW, rowH));
            bool hovered = ImGui.IsItemHovered();
            bool active = ImGui.IsItemActive();

            uint bg = ImGui.GetColorU32(
                active ? ImGuiCol.ButtonActive
                : hovered ? ImGuiCol.ButtonHovered
                : ImGuiCol.Button);
            dl.AddRectFilled(rowOrigin, rowOrigin + new Vector2(rowW, rowH), bg, 4f);

            Vector2 thumbTL = rowOrigin + new Vector2(RowPad, RowPad);
            DrawTemplateThumbnail(dl, entry, thumbTL, new Vector2(ThumbW, ThumbH), ppi);

            Vector2 textPos = rowOrigin + new Vector2(
                RowPad + ThumbW + RowPad * 2,
                rowH * 0.5f - ImGui.GetTextLineHeight() * 0.5f);
            dl.AddText(textPos, ImGui.GetColorU32(ImGuiCol.Text), DescribeTemplate(entry));

            if (clicked)
                lock (_lock) _selectedTemplate = i;
        }
    }

    private static float ComputeMaxDimension(IReadOnlyList<TerrainPieceEntry> pool)
    {
        float max = 0f;
        foreach (var entry in pool)
        {
            (float lx, float hx, float ly, float hy) = entry.Shape.GetAABB();
            max = MathF.Max(max, MathF.Max(hx - lx, hy - ly));
        }
        return max;
    }

    /// <summary>
    /// Draws each primitive in the template at its real relative position, scaled to
    /// the thumbnail by <paramref name="ppi"/> and centered on the template's AABB.
    /// </summary>
    private static void DrawTemplateThumbnail(ImDrawListPtr dl, TerrainPieceEntry entry,
        Vector2 topLeft, Vector2 size, float ppi)
    {
        (Vector4 fillBase, Vector4 outlineBase) = TerrainTypeColors(entry.TerrainType);
        uint fill = ImGui.ColorConvertFloat4ToU32(new Vector4(fillBase.X, fillBase.Y, fillBase.Z, 0.55f));
        uint outline = ImGui.ColorConvertFloat4ToU32(outlineBase);

        Float2 templateCenter = entry.Shape.GetAABBCenter();
        Vector2 thumbCenter = topLeft + size * 0.5f;

        foreach (var prim in entry.Shape.Primitives())
            DrawPrimitiveInThumbnail(dl, prim, templateCenter, thumbCenter, ppi, fill, outline);
    }

    private static void DrawPrimitiveInThumbnail(ImDrawListPtr dl, IZone prim,
        Float2 templateCenter, Vector2 thumbCenter, float ppi, uint fill, uint outline)
    {
        switch (prim)
        {
            case RectangularZone r:
                float relCenterX = (r.Left + r.Right) * 0.5f - templateCenter.X;
                float relCenterY = (r.Bottom + r.Top) * 0.5f - templateCenter.Y;
                float pw = (r.Right - r.Left) * ppi;
                float ph = (r.Top - r.Bottom) * ppi;
                Vector2 rectCenterPx = thumbCenter + new Vector2(relCenterX, relCenterY) * ppi;
                Vector2 tl = rectCenterPx - new Vector2(pw, ph) * 0.5f;
                Vector2 br = rectCenterPx + new Vector2(pw, ph) * 0.5f;
                dl.AddRectFilled(tl, br, fill);
                dl.AddRect(tl, br, outline, 0f, ImDrawFlags.None, 1f);
                break;

            case CircularZone c:
                float ccx = c.Center.X - templateCenter.X;
                float ccy = c.Center.Y - templateCenter.Y;
                float pr = c.Radius * ppi;
                Vector2 cPx = thumbCenter + new Vector2(ccx, ccy) * ppi;
                dl.AddCircleFilled(cPx, pr, fill, 24);
                dl.AddCircle(cPx, pr, outline, 24, 1f);
                break;
        }
    }

    private static string DescribeTemplate(TerrainPieceEntry entry)
    {
        string typeText = entry.TerrainType == ETerrainType.None ? "None" : entry.TerrainType.ToString();
        if (entry.Shape is CircularZone c)
            return $"{typeText} (r={c.Radius:F1}\")";
        (float lx, float hx, float ly, float hy) = entry.Shape.GetAABB();
        return $"{typeText} ({hx - lx:F1}\"x{hy - ly:F1}\")";
    }

    private void Complete(TaskCompletionSource<TerrainPlacementResult> tcs, TerrainPlacementResult result)
    {
        lock (_lock) { _request = null; _tcs = null; _selectedTemplate = null; _pendingCenter = null; _rotationDegrees = 0f; }
        tcs.SetResult(result);
    }

    private (float x, float z) PixelToInches(float px, float py) =>
        ((px - _originX) / _scale, _tableH - (py - _originY) / _scale);

    private bool IsOverTable(float px, float py) =>
        px >= _originX && py >= _originY &&
        px <= _originX + GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES * _scale &&
        py <= _originY + _tableH * _scale;
}
