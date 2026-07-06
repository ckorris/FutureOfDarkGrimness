using System.Collections.Concurrent;
using System.Numerics;
using FDG;
using FDG.Players;
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
    // A large atlas for the big menu text. The menu scales DOWN from this (crisp) instead of stretching
    // the 18px body font up ~5x (which looked aliased at fullscreen). MenuFontPx is the baked size, 0 if
    // fonts failed to load -- callers fall back to the old body-font scaling in that case.
    public static ImGuiNET.ImFontPtr MenuFont;
    public static float MenuFontPx;

    private const float TableWIn      = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES;
    private const float TableHIn      = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
    private const int   MinMargin     = 20;
    private const float TableZoom     = 1.15f;  // fill more of the available space than a strict fit
    // Anchor for the resolution-derived UI scale (see ComputeUiScale): the multiplier that was tuned
    // by hand on a 4K (2160p) desktop. Smaller displays scale down from here, larger ones cap here.
    private const float ReferenceUiScale  = 1.4f;
    private const float ReferenceHeightPx = 2160f;

    private static readonly Color TableColor  = new(40, 100, 40, 255);
    private static readonly Color TableBorder = new(20, 60, 20, 255);
    private static readonly Color Background  = new(30, 30, 30, 255);

    // Table grid: minor lines every 6", major every 12" (matches the game's inch measurements — a
    // major square is one charge move across). Lines are etched darker than the felt for an engraved
    // look rather than painted on top. A soft edge vignette adds depth. Everything here is confined to
    // the table rectangle by construction, so it never bleeds onto terrain/objectives/models drawn after.
    private const float GridMinorInches = 6f;
    private const float GridMajorInches = 12f;
    private static readonly Color GridMinorColor = new(33, 85, 33, 80);
    private static readonly Color GridMajorColor = new(24, 66, 24, 150);
    private const int   FeltVignetteAlpha = 55;

    // Toggled from the table toolbar (TableTooltipOverlay) alongside the label toggle. Read by
    // DrawTableGrid's call site so the grid/felt can be turned off without touching anything else.
    public static bool ShowGrid = true;

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

    // Bottom console (#105): a collapsible, full-width dock. Log and Chat are independent TOGGLES (not
    // exclusive tabs) -- with both on, their lines are merged into one column in arrival order.
    private bool _consoleCollapsed = false;
    private bool _showChat = true;   // Chat source shown (button on the left)
    private bool _showLog  = true;   // Log source shown
    private EChatMessageType _chatChannel = EChatMessageType.Global;
    private bool _chatUnread    = false;  // new chat arrived while Chat is toggled off / console collapsed
    private int  _lastChatCount = 0;      // for the unread check
    private GuiResolverOverlay? _resolverOverlay;
    private GuiOutstandingTaskDisplay? _taskDisplay;
    private PresentationPlayer? _presentationPlayer;
    private AudioManager? _audio;
    private readonly TableTooltipOverlay _tooltipOverlay = new();
    private readonly TableHitTester      _hitTester      = new();
    private readonly MeasurementOverlay  _measurementOverlay = new();
    private readonly TacticalOverlay.TacticalOverlayController _tacticalOverlay = new();
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

    private record Layout(float Scale, int OriginX, int OriginY, int AreaW, int ScreenH);

    // Bottom-console height: a thin bar (tabs only) when collapsed, ~26% of the window when open.
    private int ConsoleHeight(int screenH) =>
        _log == null ? 0
        : _consoleCollapsed ? Math.Max(34, (int)(screenH * 0.038f))
                            : Math.Max(170, (int)(screenH * 0.26f));

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
        _measurementOverlay.Attach(tableState);
        _tacticalOverlay.Attach(tableState, msg => _log?.Add(msg, new TextColor(255, 180, 90, 255)));
        _tacticalOverlay.AttachMovementResolver(resolverOverlay?.MovementResolver);
        _tooltipOverlay.AttachTacticalOverlay(_tacticalOverlay);

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

        _measurementOverlay.Reset();
        _tacticalOverlay.Detach();
        _placedModels.Clear();
        lock (_terrainLock)    _terrain.Clear();
        lock (_objectivesLock) _objectives.Clear();

        _tableState            = null;
        _colorForPlayer        = null;
        _log                   = null;
        _playerMessageUI       = null;
        _chatInput             = "";
        _consoleCollapsed      = false;
        _showChat              = true;
        _showLog               = true;
        _chatChannel           = EChatMessageType.Global;
        _chatUnread            = false;
        _lastChatCount         = 0;
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
        // Apply the app-wide "Dark Grimness" theme BEFORE scaling, so its rounding/border sizes scale
        // with the display too (colors are unaffected by ScaleAllSizes).
        ImGuiTheme.Apply();
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
            // Baked near the largest on-screen menu text (title ~7% of display height) so the menu never
            // upscales the atlas. Clamped so it stays a sane texture size on tiny and 4K displays alike.
            MenuFontPx = Math.Clamp(monitorH * 0.075f, 48f, 220f);
            MenuFont   = fonts.AddFontFromFileTTF(fontPath, MenuFontPx);
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
                // Push the layout to the tactical overlay once per frame so both its canvas-pass draws
                // (below) and its ImGui-pass instruments read the same world<->screen mapping.
                _tacticalOverlay.UpdateLayout(layout.Scale, layout.OriginX, layout.OriginY, TableHIn);
                DrawTable(layout);
                if (ShowGrid)
                    DrawTableGrid(layout);   // etched grid + felt vignette, under terrain/objectives/models
                _tacticalOverlay.DrawField();    // opportunity field: under terrain (spec draw order)
                DrawTerrain(layout);
                _tacticalOverlay.DrawContours(); // threat + secondary contours: above terrain, under objectives
                DrawObjectives(layout);
                DrawAmbushExclusion(layout, screenW, screenH);
                DrawActiveUnitSpotlight(layout);
                DrawModels(layout);
                DrawDeathBursts(layout);

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
                    DiceOverlay.Draw(diceBeat, diceProgress, layout.AreaW, screenH);
                }

                if (_presentationPlayer != null &&
                    _presentationPlayer.TryGetActiveRollOff(out var rollOffBeat, out var rollOffProgress))
                {
                    DiceOverlay.DrawRollOff(rollOffBeat, rollOffProgress, layout.AreaW, screenH);
                }

                if (_presentationPlayer != null &&
                    _presentationPlayer.TryGetActiveBanner(out var bannerBeat, out var bannerProgress))
                {
                    BannerOverlay.Draw(bannerBeat, bannerProgress, layout.AreaW, screenH);
                }

                DrawStatusHud(layout);

                rlImGui.Begin();
                // Runs before the hit tester / resolvers so its Alt-measure WantCaptureMouse override
                // lands before they read that flag (see MeasurementOverlay).
                _measurementOverlay.UpdateLayout(layout.Scale, layout.OriginX, layout.OriginY, TableHIn);
                _measurementOverlay.Draw(screenW, screenH);
                _hitTester.Update(_tableState!, layout.Scale, layout.OriginX, layout.OriginY, TableHIn);
                // Overlay input (F toggle, hover timing, pins) runs after the hit tester so hover state
                // is fresh; heavy rebuilds happen in the next frame's DrawField.
                _tacticalOverlay.UpdateInput(Raylib.GetFrameTime(), _hitTester);
                DrawBottomConsole(layout);
                // Outstanding Tasks window hidden per user request; re-enable by restoring this draw call.
                // _taskDisplay?.Draw(screenW, screenH);
                _tooltipOverlay.UpdateLayout(layout.Scale, layout.OriginX, layout.OriginY, TableHIn);
                _tooltipOverlay.Draw(screenW, screenH, _hitTester, _resolverOverlay?.ActiveInteractionHandler);
                // Instruments sit on the background draw list, above tokens and under ImGui windows --
                // same layer as the existing ghosts/fire lines they annotate.
                _tacticalOverlay.DrawInstruments(screenW, screenH);
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
        // Full-width table; the console reserves height at the bottom instead of a right-side strip.
        int consoleH   = ConsoleHeight(screenH);
        int tableAreaH = screenH - consoleH;

        float scaleX = (screenW     - MinMargin * 2f) / TableWIn;
        float scaleY = (tableAreaH  - MinMargin * 2f) / TableHIn;
        // Nudge the auto-fit up so the board fills more of the (otherwise slack) space. The board is
        // usually height-bound, so this trades the vertical margin for a bigger table, centered.
        float scale  = Math.Max(1f, Math.Min(scaleX, scaleY)) * TableZoom;

        int tablePixW = (int)(TableWIn * scale);
        int tablePixH = (int)(TableHIn * scale);
        int originX   = (screenW     - tablePixW) / 2;
        int originY   = (tableAreaH  - tablePixH) / 2;

        return new Layout(scale, originX, originY, screenW, screenH);
    }

    private static void DrawTable(Layout l)
    {
        int tw = (int)(TableWIn * l.Scale);
        int th = (int)(TableHIn * l.Scale);
        Raylib.DrawRectangle(l.OriginX, l.OriginY, tw, th, TableColor);
        Raylib.DrawRectangleLines(l.OriginX, l.OriginY, tw, th, TableBorder);
    }

    // Etched inch grid + a soft felt vignette, drawn only within the table rect (so it stays under
    // terrain/objectives/models, which draw afterward). Interior lines only — the border is the edge.
    private static void DrawTableGrid(Layout l)
    {
        int tw = (int)(TableWIn * l.Scale);
        int th = (int)(TableHIn * l.Scale);
        int x0 = l.OriginX, y0 = l.OriginY;
        int x1 = x0 + tw,   y1 = y0 + th;

        for (float xi = GridMinorInches; xi < TableWIn; xi += GridMinorInches)
        {
            int px = x0 + (int)(xi * l.Scale);
            Raylib.DrawLine(px, y0, px, y1, IsMajorGridLine(xi) ? GridMajorColor : GridMinorColor);
        }
        for (float zi = GridMinorInches; zi < TableHIn; zi += GridMinorInches)
        {
            int py = y0 + (int)(zi * l.Scale);
            Raylib.DrawLine(x0, py, x1, py, IsMajorGridLine(zi) ? GridMajorColor : GridMinorColor);
        }

        // Edge vignette: a dark band fading inward on each side (corners overlap for a little extra
        // emphasis). Clipped to the table rect.
        int band = Math.Max(6, (int)(Math.Min(tw, th) * 0.08f));
        var edge  = new Color((byte)0, (byte)0, (byte)0, (byte)FeltVignetteAlpha);
        var clear = new Color((byte)0, (byte)0, (byte)0, (byte)0);
        Raylib.DrawRectangleGradientV(x0, y0, tw, band, edge, clear);            // top
        Raylib.DrawRectangleGradientV(x0, y1 - band, tw, band, clear, edge);     // bottom
        Raylib.DrawRectangleGradientH(x0, y0, band, th, edge, clear);            // left
        Raylib.DrawRectangleGradientH(x1 - band, y0, band, th, clear, edge);     // right
    }

    private static bool IsMajorGridLine(float inches)
    {
        float q = inches / GridMajorInches;
        return Math.Abs(q - MathF.Round(q)) < 0.01f;
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

    // #6 -- a soft pulsing halo under each model of the unit currently taking its activation, so whose
    // turn it is reads at a glance. The activating unit comes from ITableState.Progress (live, replicated
    // state), and the ring rides the presentation animation position so it stays under a gliding model.
    private static readonly (byte r, byte g, byte b) SpotlightRGB = (255, 205, 110); // warm highlight
    private void DrawActiveUnitSpotlight(Layout l)
    {
        if (_tableState == null) return;
        IUnit? active = _tableState.Progress.ActivatingUnit;
        if (active == null) return;

        float pulse = 0.5f + 0.5f * MathF.Sin((float)Raylib.GetTime() * 3.2f); // 0..1
        var (r, g, b) = SpotlightRGB;
        var fill  = new Color(r, g, b, (byte)(28 + 22 * pulse));
        var ring  = new Color(r, g, b, (byte)(130 + 90 * pulse));
        var halo  = new Color(r, g, b, (byte)(40 + 30 * pulse));

        foreach (IModel model in active.Models)
        {
            // Same position source as DrawModels so the halo tracks gliding/hurt/dying state.
            ModelDrawState draw = _presentationPlayer?.GetModelDrawState(model)
                ?? (model.GetIsAlive()
                    ? new ModelDrawState(true, model.Position, 1f, null)
                    : ModelDrawState.Hidden);
            if (!draw.Visible) continue;

            int cx = l.OriginX + (int)(draw.Position.x * l.Scale);
            int cy = l.OriginY + (int)((TableHIn - draw.Position.z) * l.Scale);
            float baseR = (model.BaseRadiusInches + 0.18f) * l.Scale; // just outside the base

            Raylib.DrawCircle(cx, cy, baseR, fill);
            Raylib.DrawCircleLines(cx, cy, baseR, ring);
            Raylib.DrawCircleLines(cx, cy, baseR + 3f + 6f * pulse, halo); // expanding pulse ring
        }
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

    // Top-center status strip: current round (from ITableState.Progress) + a live objective scoreboard
    // (one player-colored pip + controlled count per player). Both read as live state each frame -- the
    // round comes from the replicated GameProgressData, the counts from the objectives' owners.
    private void DrawStatusHud(Layout l)
    {
        if (_tableState == null || _colorForPlayer == null) return;

        // The aggregate progress read model does the work (round, per-player objective counts); the
        // renderer just maps each player to its table color. RoundCount is null before the main phase.
        IGameProgress progress = _tableState.Progress;

        var scores = new List<(Color color, int count)>();
        foreach (PlayerObjectiveScore s in progress.Scores)
            scores.Add((_colorForPlayer(s.PlayerID), s.ObjectiveCount));

        StatusHudOverlay.Draw(l.AreaW, progress.RoundCount, progress.TotalRounds, scores);
    }

    // A short-lived dust puff where a model died: an expanding, fading gray cloud plus a few debris
    // specks flung outward, riding the death animation's progress. Drawn over the (red, fading) model.
    private static readonly (byte r, byte g, byte b) DustRGB = (150, 140, 128);
    private void DrawDeathBursts(Layout l)
    {
        if (_presentationPlayer == null) return;
        foreach (var (pos, progress) in _presentationPlayer.GetActiveDeathBursts())
        {
            int cx = l.OriginX + (int)(pos.x * l.Scale);
            int cy = l.OriginY + (int)((TableHIn - pos.z) * l.Scale);
            var (r, g, b) = DustRGB;

            // Central cloud: grows from the base size outward, fades as it expands.
            float radius = (0.18f + 0.55f * progress) * l.Scale;
            byte cloudA  = (byte)Math.Clamp((1f - progress) * 130f, 0f, 255f);
            Raylib.DrawCircleV(new Vector2(cx, cy), radius, new Color(r, g, b, cloudA));

            // Debris specks flung outward on fixed spokes.
            byte specA = (byte)Math.Clamp((1f - progress) * 200f, 0f, 255f);
            var spec = new Color(r, g, b, specA);
            float reach = (0.15f + 0.85f * progress) * l.Scale;
            for (int k = 0; k < 6; k++)
            {
                float ang = k * (MathF.PI * 2f / 6f) + 0.3f;
                float px = cx + MathF.Cos(ang) * reach;
                float py = cy + MathF.Sin(ang) * reach;
                Raylib.DrawCircleV(new Vector2(px, py), MathF.Max(1.2f, 0.05f * l.Scale), spec);
            }
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

    // Bottom console (#105): a full-width, collapsible dock. Log and Chat are independent TOGGLES (Chat on
    // the left); with both on, their lines merge into one column in arrival order (by the shared
    // LogEntry.Sequence). The engine GameLog is the Log source; the sender-coloured chat store is the Chat
    // source, which also shows the Global/Team channel toggle + input. Chat flags unread when a message
    // arrives while Chat is toggled off or the console is collapsed.
    private void DrawBottomConsole(Layout l)
    {
        if (_log == null) return;
        int h = ConsoleHeight(l.ScreenH);

        // Unread bookkeeping (every frame, regardless of what's shown).
        int chatCount = _playerMessageUI?.ChatLog.Count ?? 0;
        if (chatCount > _lastChatCount && (_consoleCollapsed || !_showChat))
            _chatUnread = true;
        _lastChatCount = chatCount;

        ImGui.SetNextWindowPos(new Vector2(0, l.ScreenH - h), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(l.AreaW, h), ImGuiCond.Always);
        ImGui.Begin("##console",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBringToFrontOnFocus);

        // Source toggles: Chat (left), then Log.
        DrawConsoleToggle((_chatUnread ? "Chat *" : "Chat") + "##chattoggle", ref _showChat, isChat: true);
        ImGui.SameLine();
        DrawConsoleToggle("Log##logtoggle", ref _showLog, isChat: false);

        // Collapse / expand button, pinned right.
        const float collapseW = 32f;
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetWindowWidth() - collapseW - 10f);
        if (ImGui.Button((_consoleCollapsed ? "+" : "-") + "##consolecollapse", new Vector2(collapseW, 0f)))
            _consoleCollapsed = !_consoleCollapsed;

        if (!_consoleCollapsed)
        {
            ImGui.Separator();
            DrawConsoleContent();
        }

        ImGui.End();
    }

    // A source toggle, highlighted when on. Turning Chat on clears its unread flag.
    private void DrawConsoleToggle(string labelWithId, ref bool on, bool isChat)
    {
        // Capture the state BEFORE the button: clicking flips `on`, and the pop must match the push
        // regardless of that flip (else the style stack is left unbalanced -> ImGui asserts/crashes).
        bool wasOn = on;
        if (wasOn) ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]);
        if (ImGui.Button(labelWithId))
        {
            on = !on;
            if (isChat && on) _chatUnread = false;
        }
        if (wasOn) ImGui.PopStyleColor();
    }

    // Merged scrollback of the enabled sources, plus the chat input row when Chat is on.
    private void DrawConsoleContent()
    {
        if (_showChat) _chatUnread = false; // chat is visible

        float inputH = _showChat ? ImGui.GetFrameHeightWithSpacing() : 0f;
        ImGui.BeginChild("##consolescroll", new Vector2(0, -inputH), ImGuiChildFlags.None,
            ImGuiWindowFlags.HorizontalScrollbar);

        List<LogEntry>? logMsgs  = _showLog ? _log!.Snapshot() : null;
        List<LogEntry>? chatMsgs = (_showChat && _playerMessageUI != null) ? _playerMessageUI.ChatLog.Snapshot() : null;
        int ln = logMsgs?.Count ?? 0, cn = chatMsgs?.Count ?? 0;

        if (ln == 0 && cn == 0)
        {
            ImGui.TextDisabled(!_showLog && !_showChat ? "Log and Chat hidden -- toggle one on above."
                                                       : "No messages yet.");
        }
        else
        {
            // Merge two arrival-ordered lists by Sequence (two-pointer -- both already sorted).
            int li = 0, ci = 0;
            while (li < ln || ci < cn)
            {
                bool takeLog = ci >= cn || (li < ln && logMsgs![li].Sequence <= chatMsgs![ci].Sequence);
                RenderConsoleLine(takeLog ? logMsgs![li++] : chatMsgs![ci++]);
            }
        }

        int total = ln + cn;
        bool hasNew = total > _lastLogCount;
        _lastLogCount = total;
        if (hasNew && _autoScroll) ImGui.SetScrollHereY(1.0f);
        _autoScroll = ImGui.GetScrollY() >= ImGui.GetScrollMaxY() - 4f;

        ImGui.EndChild();

        if (_showChat) DrawChatInputRow();
    }

    private static void RenderConsoleLine(LogEntry entry)
    {
        var c = entry.Color;
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(c.R / 255f, c.G / 255f, c.B / 255f, c.A / 255f));
        ImGui.TextWrapped(entry.Message);
        ImGui.PopStyleColor();
    }

    private void DrawChatInputRow()
    {
        bool team = _chatChannel == EChatMessageType.Team;
        if (team) ImGui.PushStyleColor(ImGuiCol.Button, ImGui.GetStyle().Colors[(int)ImGuiCol.ButtonActive]);
        if (ImGui.Button((team ? "Team" : "Global") + "##chatchannel", new Vector2(74f, 0f)))
            _chatChannel = team ? EChatMessageType.Global : EChatMessageType.Team;
        if (team) ImGui.PopStyleColor();
        ImGui.SameLine();

        ImGui.SetNextItemWidth(-1f);
        if (_playerMessageUI != null &&
            ImGui.InputTextWithHint("##chatinput", "Chat... (Enter to send)", ref _chatInput, 512,
                ImGuiInputTextFlags.EnterReturnsTrue))
        {
            _playerMessageUI.Submit(_chatInput, _chatChannel);
            _chatInput = "";
        }
    }
}
