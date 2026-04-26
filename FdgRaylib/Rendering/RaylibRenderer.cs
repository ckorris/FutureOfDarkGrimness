using System.Collections.Concurrent;
using System.Numerics;
using FDG;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;

namespace FdgRaylib.Rendering;

public class RaylibRenderer
{
    private const float Scale = 10f;  // pixels per inch
    private const int Margin = 50;
    private const int TableWIn = (int)GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES;
    private const int TableHIn = (int)GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
    private const int LogPanelWidth = 350;

    private static readonly Color TableColor  = new(40, 100, 40, 255);
    private static readonly Color TableBorder = new(20, 60, 20, 255);
    private static readonly Color Background  = new(30, 30, 30, 255);

    private readonly ITableState _tableState;
    private readonly Func<PlayerID, Color> _colorForPlayer;
    private readonly GameLog? _log;

    private readonly ConcurrentDictionary<IModel, Color> _placedModels = new();

    // Auto-scroll state for the log panel.
    private bool _autoScroll = true;
    private int _lastLogCount = 0;

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
        int tableW = (int)(TableWIn * Scale) + Margin * 2;
        int tableH = (int)(TableHIn * Scale) + Margin * 2;
        int winW = tableW + (_log != null ? LogPanelWidth : 0);
        int winH = tableH;

        Raylib.InitWindow(winW, winH, "Future of Dark Grimness");
        Raylib.SetTargetFPS(30);
        rlImGui.Setup(true);

        while (!Raylib.WindowShouldClose())
        {
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Background);

            DrawTable();
            DrawModels();

            rlImGui.Begin();
            if (_log != null) DrawLogPanel(tableW, winH);
            rlImGui.End();

            Raylib.EndDrawing();
        }

        rlImGui.Shutdown();
        Raylib.CloseWindow();
    }

    private static void DrawTable()
    {
        int tw = (int)(TableWIn * Scale);
        int th = (int)(TableHIn * Scale);
        Raylib.DrawRectangle(Margin, Margin, tw, th, TableColor);
        Raylib.DrawRectangleLines(Margin, Margin, tw, th, TableBorder);
    }

    private void DrawModels()
    {
        foreach (var (model, color) in _placedModels)
        {
            if (!model.GetIsAlive()) continue;

            var pos = model.Position;
            int cx = Margin + (int)(pos.x * Scale);
            int cy = Margin + (int)((TableHIn - pos.z) * Scale);
            float radius = model.BaseRadiusInches * Scale;

            Raylib.DrawCircle(cx, cy, radius, color);
            Raylib.DrawCircleLines(cx, cy, radius, Color.Black);
        }
    }

    private void DrawLogPanel(int x, int height)
    {
        ImGui.SetNextWindowPos(new Vector2(x, 0), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(LogPanelWidth, height), ImGuiCond.Always);
        ImGui.Begin("Game Log",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse);

        var messages = _log!.Snapshot();
        bool hasNew = messages.Count > _lastLogCount;
        _lastLogCount = messages.Count;

        ImGui.BeginChild("scrolling", Vector2.Zero, ImGuiChildFlags.None,
            ImGuiWindowFlags.HorizontalScrollbar);

        foreach (var msg in messages)
            ImGui.TextWrapped(msg);

        // Auto-scroll to bottom when new messages arrive, unless user scrolled up.
        if (hasNew && _autoScroll)
            ImGui.SetScrollHereY(1.0f);

        // If user scrolled away from bottom, disable auto-scroll; re-enable at bottom.
        _autoScroll = ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 4f;

        ImGui.EndChild();
        ImGui.End();
    }
}
