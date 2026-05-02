using System.Collections.Concurrent;
using System.Numerics;
using FDG;
using FdgRaylib.Rendering.Resolvers;
using ImGuiNET;
using Raylib_cs;
using rlImGui_cs;

namespace FdgRaylib.Rendering;

public class RaylibRenderer
{
    // Populated once during Run() after fonts are loaded; null until then.
    public static ImGuiNET.ImFontPtr BodyFont;
    public static ImGuiNET.ImFontPtr LargeFont;

    private const float TableWIn      = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES;
    private const float TableHIn      = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
    private const int   LogPanelWidth = 350;
    private const int   MinMargin     = 20;

    private static readonly Color TableColor  = new(40, 100, 40, 255);
    private static readonly Color TableBorder = new(20, 60, 20, 255);
    private static readonly Color Background  = new(30, 30, 30, 255);

    public MainMenuScreen    MainMenu     { get; } = new();
    public ArmyBuilderScreen ArmyBuilder  { get; } = new();
    public HostModal         HostModal    { get; } = new();
    public ClientModal       ClientModal  { get; } = new();
    public LobbyScreen       LobbyScreen  { get; } = new();

    private IAppScreen _currentScreen;

    private ITableState? _tableState;
    private Func<PlayerID, Color>? _colorForPlayer;
    private GameLog? _log;
    private GuiResolverOverlay? _resolverOverlay;
    private GuiOutstandingTaskDisplay? _taskDisplay;
    private bool _inGame = false;
    private bool _closeRequested = false;

    public RaylibRenderer()
    {
        _currentScreen = MainMenu;
    }

    public void NavigateTo(IAppScreen screen) => _currentScreen = screen;

    private readonly ConcurrentDictionary<IModel, Color> _placedModels = new();
    private bool _autoScroll = true;
    private int  _lastLogCount = 0;

    private record Layout(float Scale, int OriginX, int OriginY, int LogX, int ScreenH);

    public void TransitionToGame(ITableState tableState, Func<PlayerID, Color> colorForPlayer,
        GameLog? log, GuiResolverOverlay? resolverOverlay = null,
        GuiOutstandingTaskDisplay? taskDisplay = null)
    {
        _tableState      = tableState;
        _colorForPlayer  = colorForPlayer;
        _log             = log;
        _resolverOverlay = resolverOverlay;
        _taskDisplay     = taskDisplay;

        tableState.Models.OnObjectCreated += SubscribeToModel;
        foreach (var model in tableState.Models.Objects)
            SubscribeToModel(model);

        tableState.Terrain.OnObjectCreated += AddTerrain;
        tableState.Terrain.OnObjectRemoved += RemoveTerrain;
        foreach (var terrain in tableState.Terrain.Objects)
            AddTerrain(terrain);

        _inGame = true;
    }

    private readonly List<ITerrain> _terrain = new();
    private readonly object _terrainLock = new();

    private void AddTerrain(ITerrain terrain)
    {
        lock (_terrainLock) _terrain.Add(terrain);
    }

    private void RemoveTerrain(ITerrain terrain)
    {
        lock (_terrainLock) _terrain.Remove(terrain);
    }

    public void RequestClose() => _closeRequested = true;

    private void SubscribeToModel(IModel model)
    {
        model.OnPositionChanged += (_, _) => OnModelPlaced(model);
    }

    private void OnModelPlaced(IModel model)
    {
        var unit = _tableState!.Units.Objects.FirstOrDefault(u => u.Models.Contains(model));
        if (unit != null)
            _placedModels[model] = _colorForPlayer!(unit.PlayerID);
    }

    public void Run()
    {
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow);
        Raylib.InitWindow(1280, 720, "Future of Dark Grimness");
        Raylib.SetTargetFPS(30);

        int monitor   = Raylib.GetCurrentMonitor();
        int monitorW  = Raylib.GetMonitorWidth(monitor);
        int monitorH  = Raylib.GetMonitorHeight(monitor);
        int initW     = Math.Min(1280 * 2, monitorW);
        int initH     = Math.Min(720  * 2, monitorH);
        Raylib.SetWindowSize(initW, initH);

        rlImGui.Setup(true);

        // Replace the default 13px bitmap font with DejaVuSans TTF.
        // Must clear the atlas first — Setup already added the pixel font at index 0;
        // adding without clearing would leave it as the default and push ours to index 1.
        string fontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "DejaVuSans.ttf");
        if (File.Exists(fontPath))
        {
            var fonts = ImGui.GetIO().Fonts;
            fonts.Clear();
            BodyFont  = fonts.AddFontFromFileTTF(fontPath, 18f);
            LargeFont = fonts.AddFontFromFileTTF(fontPath, 32f);
            rlImGui.ReloadFonts();
        }

        while (!Raylib.WindowShouldClose() && !_closeRequested)
        {
            int screenW = Raylib.GetScreenWidth();
            int screenH = Raylib.GetScreenHeight();

            Raylib.BeginDrawing();
            Raylib.ClearBackground(Background);

            if (_inGame)
            {
                var layout = ComputeLayout(screenW, screenH);
                DrawTable(layout);
                DrawTerrain(layout);
                DrawModels(layout);

                rlImGui.Begin();
                if (_log != null) DrawLogPanel(layout);
                _taskDisplay?.Draw(screenW, screenH);
                _resolverOverlay?.UpdateLayout(layout.Scale, layout.OriginX, layout.OriginY, TableHIn);
                _resolverOverlay?.Draw(screenW, screenH);
                rlImGui.End();
            }
            else
            {
                rlImGui.Begin();
                _currentScreen.Draw(screenW, screenH);
                rlImGui.End();
            }

            Raylib.EndDrawing();
        }

        rlImGui.Shutdown();
        Raylib.CloseWindow();
    }

    private Layout ComputeLayout(int screenW, int screenH)
    {
        int logW       = _log != null ? LogPanelWidth : 0;
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

    private void DrawTerrain(Layout l)
    {
        ITerrain[] snapshot;
        lock (_terrainLock) snapshot = _terrain.ToArray();

        foreach (var terrain in snapshot)
        {
            (Color fill, Color outline) = TerrainColors.For(terrain.TerrainType);
            ZoneRenderer.DrawFilled(terrain.Shape, l.Scale, l.OriginX, l.OriginY, TableHIn, fill, outline);
        }
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
