using System.Collections.Concurrent;
using System.Numerics;
using FDG;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;

namespace FdgRaylib.Rendering;

public class RaylibRenderer
{
    private const float TableWIn = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES;
    private const float TableHIn = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
    private const int   LogPanelWidth  = 350;   // fixed pixel width for the log panel
    private const int   MinMargin      = 20;     // minimum pixels between table and window edge
    private const float DefaultScale   = 10f;    // initial px/inch at launch

    private static readonly Color TableColor  = new(40, 100, 40, 255);
    private static readonly Color TableBorder = new(20, 60, 20, 255);
    private static readonly Color Background  = new(30, 30, 30, 255);

    private readonly ITableState _tableState;
    private readonly Func<PlayerID, Color> _colorForPlayer;
    private readonly GameLog? _log;

    private readonly ConcurrentDictionary<IModel, Color> _placedModels = new();

    private bool _autoScroll = true;
    private int  _lastLogCount = 0;

    // Per-frame layout, recomputed whenever the window size changes.
    private record Layout(float Scale, int OriginX, int OriginY, int LogX, int ScreenH);

    public RaylibRenderer(ITableState tableState, Func<PlayerID, Color> colorForPlayer,
        GameLog? log = null)
    {
        _tableState = tableState;
        _colorForPlayer = colorForPlayer;
        _log = log;

        tableState.Models.OnObjectCreated += SubscribeToModel;
        foreach (var model in tableState.Models.Objects)
            SubscribeToModel(model);
    }

    private void SubscribeToModel(IModel model)
    {
        model.OnPositionChanged += (_, _) => OnModelPlaced(model);
    }

    private void OnModelPlaced(IModel model)
    {
        var unit = _tableState.Units.Objects.FirstOrDefault(u => u.Models.Contains(model));
        if (unit != null)
            _placedModels[model] = _colorForPlayer(unit.PlayerID);
    }

    public void Run()
    {
        int logW   = _log != null ? LogPanelWidth : 0;
        int initW  = (int)(TableWIn * DefaultScale) + MinMargin * 2 + logW;
        int initH  = (int)(TableHIn * DefaultScale) + MinMargin * 2;

        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);
        Raylib.InitWindow(initW, initH, "Future of Dark Grimness");
        Raylib.SetTargetFPS(30);
        rlImGui.Setup(true);

        while (!Raylib.WindowShouldClose())
        {
            var layout = ComputeLayout(Raylib.GetScreenWidth(), Raylib.GetScreenHeight());

            Raylib.BeginDrawing();
            Raylib.ClearBackground(Background);

            DrawTable(layout);
            DrawModels(layout);

            rlImGui.Begin();
            if (_log != null) DrawLogPanel(layout);
            rlImGui.End();

            Raylib.EndDrawing();
        }

        rlImGui.Shutdown();
        Raylib.CloseWindow();
    }

    // Fit the table (maintaining aspect ratio) into the area left of the log panel,
    // then centre it with equal margins on all sides.
    private Layout ComputeLayout(int screenW, int screenH)
    {
        int logW      = _log != null ? LogPanelWidth : 0;
        int tableAreaW = screenW - logW;

        float scaleX = (tableAreaW - MinMargin * 2f) / TableWIn;
        float scaleY = (screenH   - MinMargin * 2f) / TableHIn;
        float scale  = Math.Max(1f, Math.Min(scaleX, scaleY));

        int tablePixW = (int)(TableWIn * scale);
        int tablePixH = (int)(TableHIn * scale);
        int originX   = (tableAreaW - tablePixW) / 2;
        int originY   = (screenH    - tablePixH) / 2;

        return new Layout(scale, originX, originY, tableAreaW, screenH);
    }

    private static void DrawTable(Layout l)
    {
        int tw = (int)(TableWIn * l.Scale);
        int th = (int)(TableHIn * l.Scale);
        Raylib.DrawRectangle(l.OriginX, l.OriginY, tw, th, TableColor);
        Raylib.DrawRectangleLines(l.OriginX, l.OriginY, tw, th, TableBorder);
    }

    private void DrawModels(Layout l)
    {
        foreach (var (model, color) in _placedModels)
        {
            if (!model.GetIsAlive()) continue;

            var pos = model.Position;
            int cx = l.OriginX + (int)(pos.x * l.Scale);
            int cy = l.OriginY + (int)((TableHIn - pos.z) * l.Scale);
            float radius = model.BaseRadiusInches * l.Scale;

            Raylib.DrawCircle(cx, cy, radius, color);
            Raylib.DrawCircleLines(cx, cy, radius, Color.Black);
        }
    }

    private void DrawLogPanel(Layout l)
    {
        ImGui.SetNextWindowPos(new Vector2(l.LogX, 0), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(LogPanelWidth, l.ScreenH), ImGuiCond.Always);
        ImGui.Begin("Game Log",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse);

        var messages = _log!.Snapshot();
        bool hasNew = messages.Count > _lastLogCount;
        _lastLogCount = messages.Count;

        ImGui.BeginChild("scrolling", Vector2.Zero, ImGuiChildFlags.None,
            ImGuiWindowFlags.HorizontalScrollbar);

        foreach (var msg in messages)
            ImGui.TextWrapped(msg);

        if (hasNew && _autoScroll)
            ImGui.SetScrollHereY(1.0f);

        _autoScroll = ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 4f;

        ImGui.EndChild();
        ImGui.End();
    }
}
