using System.Collections.Concurrent;
using System.Numerics;
using FDG;
using FdgRaylib.Audio;
using FdgRaylib.Rendering.Presentation;
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
    // Anchor for the resolution-derived UI scale (see ComputeUiScale): the multiplier that was tuned
    // by hand on a 4K (2160p) desktop. Smaller displays scale down from here, larger ones cap here.
    private const float ReferenceUiScale  = 1.4f;
    private const float ReferenceHeightPx = 2160f;

    private static readonly Color TableColor  = new(40, 100, 40, 255);
    private static readonly Color TableBorder = new(20, 60, 20, 255);
    private static readonly Color Background  = new(30, 30, 30, 255);

    public MainMenuScreen    MainMenu     { get; } = new();
    public ArmyBuilderScreen ArmyBuilder  { get; } = new();
    public ArmyForgeScreen   ArmyForge    { get; } = new();
    public HostModal         HostModal    { get; } = new();
    public ClientModal       ClientModal  { get; } = new();
    public LobbyScreen       LobbyScreen  { get; } = new();

    private IAppScreen _currentScreen;

    private ITableState? _tableState;
    private Func<PlayerID, Color>? _colorForPlayer;
    private GameLog? _log;
    private GuiPlayerMessageUI? _playerMessageUI;  // in-game chat sink + send hook (#077)
    private string _chatInput = "";
    private GuiResolverOverlay? _resolverOverlay;
    private GuiOutstandingTaskDisplay? _taskDisplay;
    private PresentationPlayer? _presentationPlayer;
    private AudioManager? _audio;
    private readonly TableTooltipOverlay _tooltipOverlay = new();
    private readonly TableHitTester      _hitTester      = new();
    private bool _inGame = false;
    private bool _closeRequested = false;
    private bool _resolverOverlayFaulted = false;
    // Set from the engine thread when the game ends (see ShowGameOver); read on the main thread to draw
    // the game-over overlay. Non-null = game finished, result string to display.
    private volatile string? _gameOverResult = null;

    // Offscreen target for the Ambush enemy-exclusion blob: discs are painted opaque here (so overlaps
    // overwrite instead of stacking alpha), then composited once at a uniform light alpha. Lazily sized
    // to the window and recreated on resize; unloaded on shutdown.
    private RenderTexture2D _exclusionRT;
    private bool _exclusionRTReady;
    private int  _exclusionRTW, _exclusionRTH;
    // Opaque while painting the union; the final on-table alpha comes from the composite tint below.
    private static readonly Color ExclusionFill = new(235, 95, 95, 255);
    private const byte ExclusionCompositeAlpha = 70;

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
        GuiOutstandingTaskDisplay? taskDisplay = null,
        PresentationPlayer? presentationPlayer = null,
        Func<string?>? saveGameToJson = null,
        GuiPlayerMessageUI? playerMessageUI = null)
    {
        _tableState         = tableState;
        _colorForPlayer     = colorForPlayer;
        _log                = log;
        _resolverOverlay    = resolverOverlay;
        _taskDisplay        = taskDisplay;
        _presentationPlayer = presentationPlayer;
        _playerMessageUI    = playerMessageUI;
        _tooltipOverlay.Attach(tableState, colorForPlayer, saveGameToJson);

        // Play a sound cue the moment each beat becomes active, in lockstep with its visual. Audio is
        // GUI-only and may be unavailable (then AudioManager no-ops), so this is best-effort.
        if (_presentationPlayer != null && _audio != null)
            _presentationPlayer.BeatStarted += beat =>
            {
                string? cue = PresentationSoundCues.CueFor(beat);
                if (cue != null) _audio.Play(cue);
            };

        tableState.Models.OnObjectCreated += SubscribeToModel;
        foreach (var model in tableState.Models.Objects)
            SubscribeToModel(model);

        tableState.Terrain.OnObjectCreated += AddTerrain;
        tableState.Terrain.OnObjectRemoved += RemoveTerrain;
        foreach (var terrain in tableState.Terrain.Objects)
            AddTerrain(terrain);

        tableState.Objectives.OnObjectCreated += AddObjective;
        tableState.Objectives.OnObjectRemoved += RemoveObjective;
        foreach (var objective in tableState.Objectives.Objects)
            AddObjective(objective);

        _inGame = true;
    }

    private readonly List<ITerrain>   _terrain      = new();
    private readonly object           _terrainLock  = new();
    private readonly List<IObjective> _objectives   = new();
    private readonly object           _objectivesLock = new();

    private void AddTerrain(ITerrain terrain)
    {
        lock (_terrainLock) _terrain.Add(terrain);
    }

    private void RemoveTerrain(ITerrain terrain)
    {
        lock (_terrainLock) _terrain.Remove(terrain);
    }

    private void AddObjective(IObjective objective)
    {
        lock (_objectivesLock) _objectives.Add(objective);
    }

    private void RemoveObjective(IObjective objective)
    {
        lock (_objectivesLock) _objectives.Remove(objective);
    }

    public void RequestClose() => _closeRequested = true;

    /// <summary>
    /// Records that the game has finished so the next frame can draw the game-over overlay. Called from
    /// the engine thread (via the lobby's <c>OnGameEnded</c>), so it only stores the result — the actual
    /// teardown + navigation happens on the main thread when the player clicks "Return to Main Menu".
    /// </summary>
    public void ShowGameOver(string result) => _gameOverResult = result;

    /// <summary>
    /// Tears down all in-game state so the renderer can return to the screen stack and a later launch
    /// starts clean. Unsubscribes the table-state event handlers wired in <see cref="TransitionToGame"/>
    /// and drops every per-game reference. Runs on the main thread.
    /// </summary>
    private void ExitGame()
    {
        if (_tableState != null)
        {
            _tableState.Models.OnObjectCreated      -= SubscribeToModel;
            _tableState.Terrain.OnObjectCreated     -= AddTerrain;
            _tableState.Terrain.OnObjectRemoved     -= RemoveTerrain;
            _tableState.Objectives.OnObjectCreated  -= AddObjective;
            _tableState.Objectives.OnObjectRemoved  -= RemoveObjective;
        }

        _placedModels.Clear();
        lock (_terrainLock)    _terrain.Clear();
        lock (_objectivesLock) _objectives.Clear();

        _tableState            = null;
        _colorForPlayer        = null;
        _log                   = null;
        _playerMessageUI       = null;
        _chatInput             = "";
        _resolverOverlay       = null;
        _taskDisplay           = null;
        _presentationPlayer    = null;
        _resolverOverlayFaulted = false;
        _lastLogCount          = 0;
        _gameOverResult        = null;
        _inGame                = false;
    }

    private void SubscribeToModel(IModel model)
    {
        model.OnPositionChanged += (_, _) => OnModelPlaced(model);

        // A model restored from a save already has its position set, so no OnPositionChanged will
        // fire to register it for drawing — seed it now. (0,0,0) means unplaced, so skip those.
        if (model.Position.x != 0f || model.Position.z != 0f)
            OnModelPlaced(model);
    }

    private void OnModelPlaced(IModel model)
    {
        // A per-model OnPositionChanged subscription (wired via a lambda we can't unsubscribe) may fire
        // after ExitGame has dropped the table state. Nothing to draw once the game is gone.
        if (_tableState == null || _colorForPlayer == null) return;

        var unit = _tableState.Units.Objects.FirstOrDefault(u => u.Models.Contains(model));
        if (unit != null)
            _placedModels[model] = _colorForPlayer(unit.PlayerID);
    }

    /// <summary>
    /// UI scale derived from the display height so the interface isn't oversized on a 1080p laptop yet
    /// stays exactly as tuned on a 4K desktop. Anchored at <see cref="ReferenceUiScale"/> for a 2160p
    /// display, scaled proportionally below that, and clamped: floored at 1.0 so small screens stay
    /// readable, capped at the reference so &gt;4K doesn't balloon.
    ///
    /// TODO: only verified on a 1080p laptop and a 4K desktop. Test on more monitors (1440p, ultrawide,
    /// and displays with fractional OS scaling) and tune the anchor/floor if the UI feels off. Also note
    /// this is computed once at startup from the monitor — it doesn't re-derive on window resize / monitor
    /// move (fonts are baked at load).
    /// </summary>
    internal static float ComputeUiScale(int monitorHeightPx)
    {
        if (monitorHeightPx <= 0) return ReferenceUiScale; // unknown display — keep the tuned default
        float scaled = monitorHeightPx / ReferenceHeightPx * ReferenceUiScale;
        return Math.Clamp(scaled, 1.0f, ReferenceUiScale);
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

        float uiScale = ComputeUiScale(monitorH);

        rlImGui.Setup(true);
        // Enlarge every widget's padding/spacing/frame sizes (fonts are scaled at load below).
        ImGui.GetStyle().ScaleAllSizes(uiScale);

        // App-wide audio device + presentation cue bank (placeholder until real assets land in
        // Assets/Sounds/). No-ops gracefully if no audio device is available.
        _audio = new AudioManager();
        PresentationSoundCues.LoadInto(_audio);

        // Replace the default 13px bitmap font with DejaVuSans TTF.
        // Must clear the atlas first — Setup already added the pixel font at index 0;
        // adding without clearing would leave it as the default and push ours to index 1.
        string fontPath = Path.Combine(AppContext.BaseDirectory, "Assets", "DejaVuSans.ttf");
        if (File.Exists(fontPath))
        {
            var fonts = ImGui.GetIO().Fonts;
            fonts.Clear();
            BodyFont  = fonts.AddFontFromFileTTF(fontPath, 18f * uiScale);
            LargeFont = fonts.AddFontFromFileTTF(fontPath, 32f * uiScale);
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
                _presentationPlayer?.Update(Raylib.GetFrameTime());

                var layout = ComputeLayout(screenW, screenH);
                DrawTable(layout);
                DrawTerrain(layout);
                DrawObjectives(layout);
                DrawAmbushExclusion(layout, screenW, screenH);
                DrawModels(layout);

                if (_presentationPlayer != null &&
                    _presentationPlayer.TryGetActiveAttack(out var attackBeat, out var attackProgress))
                {
                    AttackOverlay.Draw(attackBeat, attackProgress, layout.Scale, layout.OriginX, layout.OriginY, TableHIn);
                }

                if (_presentationPlayer != null &&
                    _presentationPlayer.TryGetActiveSave(out var saveBeat, out var saveProgress))
                {
                    SaveOverlay.Draw(saveBeat, saveProgress, layout.Scale, layout.OriginX, layout.OriginY, TableHIn);
                }

                if (_presentationPlayer != null &&
                    _presentationPlayer.TryGetActiveDice(out var diceBeat, out var diceProgress))
                {
                    DiceOverlay.Draw(diceBeat, diceProgress, layout.LogX, screenH);
                }

                if (_presentationPlayer != null &&
                    _presentationPlayer.TryGetActiveRollOff(out var rollOffBeat, out var rollOffProgress))
                {
                    DiceOverlay.DrawRollOff(rollOffBeat, rollOffProgress, layout.LogX, screenH);
                }

                if (_presentationPlayer != null &&
                    _presentationPlayer.TryGetActiveBanner(out var bannerBeat, out var bannerProgress))
                {
                    BannerOverlay.Draw(bannerBeat, bannerProgress, layout.LogX, screenH);
                }

                rlImGui.Begin();
                _hitTester.Update(_tableState!, layout.Scale, layout.OriginX, layout.OriginY, TableHIn);
                if (_log != null) DrawLogPanel(layout);
                if (_playerMessageUI != null) DrawChatInput(layout);
                // Outstanding Tasks window hidden per user request; re-enable by restoring this draw call.
                // _taskDisplay?.Draw(screenW, screenH);
                _tooltipOverlay.UpdateLayout(layout.Scale, layout.OriginX, layout.OriginY, TableHIn);
                _tooltipOverlay.Draw(screenW, screenH, _hitTester, _resolverOverlay?.ActiveInteractionHandler);
                _resolverOverlay?.UpdateLayout(layout.Scale, layout.OriginX, layout.OriginY, TableHIn);
                // Hold interactive prompts until the animation queue drains, so the player always
                // sees movement / shots land before being asked to react.
                bool animating = _presentationPlayer?.IsAnimating ?? false;
                if (!_resolverOverlayFaulted && !animating)
                {
                    try
                    {
                        _resolverOverlay?.Draw(screenW, screenH);
                    }
                    catch (Exception ex)
                    {
                        _resolverOverlayFaulted = true;
                        var errColor = new TextColor(255, 120, 120, 255);
                        _log?.Add($"[RESOLVER ERROR] {ex.GetType().Name}: {ex.Message}", errColor);
                        _log?.Add(ex.StackTrace ?? "(no stack trace)", errColor);
                    }
                }
                DrawGameOverOverlay(screenW, screenH);
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
        if (_exclusionRTReady) Raylib.UnloadRenderTexture(_exclusionRT);
        _audio?.Dispose();
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

    private static readonly Color ObjectiveNeutralColor = new(160, 160, 160, 255);
    private const float ObjectiveMarkerRadiusInches = 0.5f;
    private const float ObjectiveSeizureRadiusInches = 3f;

    private void DrawObjectives(Layout l)
    {
        IObjective[] snapshot;
        lock (_objectivesLock) snapshot = _objectives.ToArray();

        for (int i = 0; i < snapshot.Length; i++)
        {
            var obj = snapshot[i];
            int cx = l.OriginX + (int)(obj.Position.x * l.Scale);
            int cy = l.OriginY + (int)((TableHIn - obj.Position.z) * l.Scale);

            Color baseColor = obj.OwnerID.HasValue
                ? _colorForPlayer!(obj.OwnerID.Value)
                : ObjectiveNeutralColor;

            // Translucent 3" seizure zone.
            float seizurePx = ObjectiveSeizureRadiusInches * l.Scale;
            Raylib.DrawCircle(cx, cy, seizurePx, new Color(baseColor.R, baseColor.G, baseColor.B, (byte)45));
            Raylib.DrawCircleLines(cx, cy, seizurePx, new Color(baseColor.R, baseColor.G, baseColor.B, (byte)180));

            // Solid inner marker.
            float markerPx = ObjectiveMarkerRadiusInches * l.Scale;
            Raylib.DrawCircle(cx, cy, markerPx, baseColor);
            Raylib.DrawCircleLines(cx, cy, markerPx, Color.Black);

            // Index number centered inside the marker.
            string label = (i + 1).ToString();
            int fontSize = Math.Max(8, (int)(markerPx * 1.5f));
            int textW    = Raylib.MeasureText(label, fontSize);
            Raylib.DrawText(label, cx - textW / 2, cy - fontSize / 2, fontSize, Color.White);
        }
    }

    // Draws the Ambush no-go region: a single blended blob covering everywhere within the exclusion
    // radius of an enemy model. Each enemy disc is painted OPAQUE into an offscreen texture so overlaps
    // overwrite rather than stack, then the whole union is composited once at a uniform light alpha —
    // five clustered models read as one clean blob instead of a mess of darker overlapping rings.
    private void DrawAmbushExclusion(Layout l, int screenW, int screenH)
    {
        IEnemyExclusionProvider? provider = _resolverOverlay?.ActiveEnemyExclusion;
        if (provider == null) return;
        if (!provider.TryGetEnemyExclusion(out IReadOnlyList<Position> centers, out float radiusInches)
            || centers.Count == 0)
        {
            return;
        }

        EnsureExclusionTexture(screenW, screenH);

        float radiusPx = radiusInches * l.Scale;

        Raylib.BeginTextureMode(_exclusionRT);
        Raylib.ClearBackground(new Color(0, 0, 0, 0));
        foreach (Position c in centers)
        {
            float px = l.OriginX + c.x * l.Scale;
            float py = l.OriginY + (TableHIn - c.z) * l.Scale;
            Raylib.DrawCircleV(new Vector2(px, py), radiusPx, ExclusionFill);
        }
        Raylib.EndTextureMode();

        // Composite the opaque union once. Render-texture contents are y-flipped, hence the negative
        // source height. The tint must be WHITE (it multiplies the texture's colour, so a coloured tint
        // would shift the hue) — only its alpha scales, making the whole blob uniformly translucent.
        var src = new Rectangle(0, 0, _exclusionRT.Texture.Width, -_exclusionRT.Texture.Height);
        Raylib.DrawTextureRec(_exclusionRT.Texture, src, Vector2.Zero,
            new Color((byte)255, (byte)255, (byte)255, ExclusionCompositeAlpha));
    }

    private void EnsureExclusionTexture(int w, int h)
    {
        if (_exclusionRTReady && _exclusionRTW == w && _exclusionRTH == h) return;
        if (_exclusionRTReady) Raylib.UnloadRenderTexture(_exclusionRT);
        _exclusionRT = Raylib.LoadRenderTexture(w, h);
        _exclusionRTW = w;
        _exclusionRTH = h;
        _exclusionRTReady = true;
    }

    private void DrawModels(Layout l)
    {
        foreach (var (model, color) in _placedModels)
        {
            // The presentation player decides position/visibility/effects: gliding mid-move,
            // tinted while dying (red, fading) or hurt (orange), hidden once dead, else authoritative.
            ModelDrawState draw = _presentationPlayer?.GetModelDrawState(model)
                ?? (model.GetIsAlive()
                    ? new ModelDrawState(true, model.Position, 1f, null)
                    : ModelDrawState.Hidden);

            if (!draw.Visible) continue;

            int cx = l.OriginX + (int)(draw.Position.x * l.Scale);
            int cy = l.OriginY + (int)((TableHIn - draw.Position.z) * l.Scale);

            Color baseColor = draw.Tint is { } tint ? new Color(tint.R, tint.G, tint.B, (byte)255) : color;
            byte a = (byte)Math.Clamp(draw.Alpha * 255f, 0f, 255f);
            Color fill    = new(baseColor.R, baseColor.G, baseColor.B, a);
            Color outline = new((byte)0, (byte)0, (byte)0, a);

            ModelBaseRenderer.DrawFilledRaylib(model.BaseShape, cx, cy, l.Scale, fill, outline, model.Facing);
            ModelBaseRenderer.DrawHeadingRaylib(model.BaseShape, cx, cy, l.Scale, model.Facing,
                new Color((byte)255, (byte)255, (byte)255, a));
        }
    }

    // Centered "Game Over" card shown once the game ends. The board stays visible behind it; the player
    // must click through to leave (no auto-return), at which point we tear down and return to the menu.
    private void DrawGameOverOverlay(int screenW, int screenH)
    {
        string? result = _gameOverResult;
        if (result == null) return;

        var size = new Vector2(Math.Min(460f, screenW * 0.8f), 200f);
        ImGui.SetNextWindowPos(new Vector2((screenW - size.X) / 2f, (screenH - size.Y) / 2f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
        ImGui.Begin("Game Over##overlay",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoSavedSettings);

        ImGui.PushFont(LargeFont);
        ImGui.TextUnformatted("Game Over");
        ImGui.PopFont();

        ImGui.Spacing();
        ImGui.TextWrapped(result);
        ImGui.Spacing();
        ImGui.Spacing();

        float btnH = 44f;
        // Pin the button to the bottom of the card.
        ImGui.SetCursorPosY(size.Y - btnH - ImGui.GetStyle().WindowPadding.Y);
        if (ImGui.Button("Return to Main Menu", new Vector2(ImGui.GetContentRegionAvail().X, btnH)))
        {
            ExitGame();
            NavigateTo(MainMenu);
        }

        ImGui.End();
    }

    // A thin chat bar across the bottom of the main game area (left of the log panel). Submitting a line
    // routes it through GuiPlayerMessageUI → the engine relay, which echoes it back into the side log
    // (where received chat from other players also appears). Not auto-focused, so game hotkeys keep
    // working until the player clicks into it. (#077 in-game chat)
    private void DrawChatInput(Layout l)
    {
        const float height = 34f;
        // Lifted half its own height off the very bottom so it doesn't sit under the OS task bar (#105).
        ImGui.SetNextWindowPos(new Vector2(0, l.ScreenH - height * 1.5f), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(l.LogX, height), ImGuiCond.Always);
        ImGui.Begin("Chat",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar);

        ImGui.SetNextItemWidth(-1f);
        if (ImGui.InputTextWithHint("##gamechat", "Chat… (Enter to send)", ref _chatInput, 512,
                ImGuiInputTextFlags.EnterReturnsTrue))
        {
            _playerMessageUI!.Submit(_chatInput);
            _chatInput = "";
        }

        ImGui.End();
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

        foreach (var entry in messages)
        {
            var c = entry.Color;
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f));
            ImGui.TextWrapped(entry.Message);
            ImGui.PopStyleColor();
        }

        if (hasNew && _autoScroll)
            ImGui.SetScrollHereY(1.0f);

        _autoScroll = ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 4f;

        ImGui.EndChild();
        ImGui.End();
    }
}
