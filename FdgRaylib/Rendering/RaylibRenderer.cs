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
    // Anchor for the resolution-derived UI scale (see ComputeUiScale): the multiplier that was tuned
    // by hand on a 4K (2160p) desktop. Smaller displays scale down from here, larger ones cap here.
    private const float ReferenceUiScale  = 1.4f;
    private const float ReferenceHeightPx = 2160f;

    private static readonly Color Background  = new(30, 30, 30, 255);

    // The surface, grid tints and edge trim all come from the launched game's #265 table background
    // (green forest felt unless the lobby says otherwise) — see TableBackgrounds.
    private ETableBackground     _background = ETableBackground.Forest;
    private TableBackgroundStyle _backgroundStyle = TableBackgrounds.For(ETableBackground.Forest);

    // Table grid: minor lines every 6", major every 12" (matches the game's inch measurements — a
    // major square is one charge move across). Lines are etched darker than the felt for an engraved
    // look rather than painted on top. Everything here is confined to the table rectangle by
    // construction, so it never bleeds onto terrain/objectives/models drawn after.
    private const float GridMinorInches = 6f;
    private const float GridMajorInches = 12f;

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

    // Right-column console: Log and Chat are independent TOGGLES (not exclusive tabs) -- with both on,
    // their lines are merged into one column in arrival order.
    private bool _showChat  = true;   // Chat source shown (button on the left)
    private bool _showLog   = true;   // Log source shown
    private bool _showDebug = false;  // Debug source shown (developer detail; off by default)
    private EChatMessageType _chatChannel = EChatMessageType.Global;
    private bool _chatUnread    = false;  // new chat arrived while Chat is toggled off / console collapsed
    private int  _lastChatCount = 0;      // for the unread check
    private GuiResolverOverlay? _resolverOverlay;
    private GuiOutstandingTaskDisplay? _taskDisplay;
    // #277 live decision previews: publisher streams the local resolver's ghosts/paths, remote
    // overlay draws every other player's. Both built at launch and carried on the resolver overlay.
    private Previews.PreviewPublisher? _previewPublisher;
    private Previews.RemotePreviewOverlay? _remotePreviews;
    private PresentationPlayer? _presentationPlayer;
    private AudioManager? _audio;
    private readonly TableTooltipOverlay _tooltipOverlay = new();
    private readonly TableHitTester      _hitTester      = new();
    private readonly MeasurementOverlay  _measurementOverlay = new();
    private readonly TacticalOverlay.TacticalOverlayController _tacticalOverlay = new();
    private readonly EscapeMenuOverlay _escapeMenu = new();
    private bool _inGame = false;
    private bool _closeRequested = false;
    private bool _resolverOverlayFaulted = false;

    /// <summary>
    /// Runs once inside <see cref="Run"/>, right after the window + ImGui exist and before the first
    /// frame. The no-lobby <c>--scenario</c> launch (#167) hooks its game wiring here: TransitionToGame
    /// attaches the tactical overlay, which creates GL resources (#162) and therefore needs a live
    /// window - calling it before Run() segfaults inside raylib (null GL function pointers).
    /// </summary>
    public Action? OnWindowReady;

    // Starts the "open a .fdgsave and resume it" flow (the same one the main menu's Load Game runs).
    // Program.cs assigns the shared implementation; the in-game menu's Load option invokes it after
    // tearing the current game down and returning to the menu.
    public Action? OnLoadGameRequested;

    // Fired at the end of ExitGame() — i.e. on every path that tears a running game down (#271).
    public Action? OnGameExited;
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

    // Subtle mottling over the felt: a small tileable Perlin-noise texture generated once, then tiled
    // across the table rect (repeat wrap) and composited additively at a low dark tint so it reads as
    // faint flecks (grass, sand grain, slab staining) rather than a flat surface. Lazily generated on
    // first draw; regenerated when the table background changes (each style has its own noise scale and
    // sample offset, so the patterns differ); unloaded on shutdown.
    private Texture2D _mottleTex;
    private bool      _mottleReady;
    private ETableBackground _mottleFor = ETableBackground.Forest;

    public RaylibRenderer()
    {
        _currentScreen = MainMenu;

        // In-game menu actions. Returning to the menu and quitting both tear the game down first; these
        // reuse the exact pair the game-over card uses (ExitGame + NavigateTo) so there's one teardown path.
        _escapeMenu.OnReturnToMainMenu = () => { ExitGame(); NavigateTo(MainMenu); };
        _escapeMenu.OnQuitToDesktop    = RequestClose;
        // Load ends the current game, returns to the menu, then runs the shared load-from-file flow.
        _escapeMenu.OnLoadGame = () => { ExitGame(); NavigateTo(MainMenu); OnLoadGameRequested?.Invoke(); };
    }

    public void NavigateTo(IAppScreen screen) => _currentScreen = screen;

    // The owning unit rides along with the colour so DrawModels can ask whether the model is actually in
    // play. A model belonging to an off-table unit (Ambush reserve, embarked, an Aircraft that flew off)
    // sits at the world origin, which maps to the table's bottom-left corner - it must not be drawn there.
    private readonly ConcurrentDictionary<IModel, (Color Color, IUnit Unit)> _placedModels = new();
    private bool _autoScroll = true;
    private int  _lastLogCount = 0;

    private record Layout(float Scale, int OriginX, int OriginY, int AreaW, int ScreenH);

    // In-game right column: a fixed-width strip on the right holding the resolver panel (top half) and the
    // log/chat console (bottom half). Everything left of it is the table viewport. Width is a fraction of
    // the window, floored so the panels stay usable on narrow windows and capped at half the width.
    private const float RightColumnFraction = 0.28f;
    private const int   RightColumnMinPx    = 340;
    private int RightColumnWidth(int screenW) =>
        _log == null ? 0 : Math.Clamp((int)(screenW * RightColumnFraction), RightColumnMinPx, screenW / 2);

    // Table view transform (#8): _zoom is a multiplier over the fit-to-viewport scale (1 = ~100% of the
    // viewport, up to MaxZoom); _pan is a pixel offset from the centered position. Both default to the
    // plain fit until the player zooms (Alt+wheel, toward the cursor) or pans (middle-drag).
    private const float MinZoom = 1f;   // fully zoomed out = table fills the viewport
    private const float MaxZoom = 3f;   // 300%
    // When zoomed in, allow panning this fraction of the table past each edge, so a strip of background
    // shows and it's clear you've hit the edge. Ignored at min zoom (the table stays locked centered).
    private const float PanEdgeMarginFraction = 0.05f;
    private float   _zoom = 1f;
    private Vector2 _pan  = Vector2.Zero;

    // Fit-to-viewport scale: the largest scale that shows the whole table inside the viewport (with a
    // margin). _zoom multiplies this; MinZoom==1 means "table fills the viewport".
    private static float FitScale(int viewportW, int screenH)
    {
        float fitX = (viewportW - MinMargin * 2f) / TableWIn;
        float fitY = (screenH   - MinMargin * 2f) / TableHIn;
        return Math.Max(1f, Math.Min(fitX, fitY));
    }

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
        // #265: the launched game's table surface rides the overlay (stamped in BuildGui). The mottle
        // texture is regenerated lazily on the next draw when this differs from what it was baked for.
        _background         = resolverOverlay?.TableBackground ?? ETableBackground.Forest;
        _backgroundStyle    = TableBackgrounds.For(_background);
        _tooltipOverlay.Attach(tableState, colorForPlayer, presentationPlayer);
        _escapeMenu.AttachSave(saveGameToJson);
        _measurementOverlay.Attach(tableState);
        // [overlay] messages are developer detail (rebuild-budget warnings) -> the Debug log category.
        _tacticalOverlay.Attach(tableState, msg => _log?.Add(msg, new TextColor(255, 180, 90, 255), isDebug: true),
            pid => { Color c = colorForPlayer(pid); return (c.R, c.G, c.B); },
            // #201: the launched game's cover setting rides the resolver overlay (stamped in BuildGui).
            coverProximityExceptions: resolverOverlay?.CoverProximityExceptions ?? true);
        _tacticalOverlay.AttachMovementResolver(resolverOverlay?.MovementResolver);
        // The menu's Options panel drives the tactical toggles (Threat, field anchor) and master volume.
        _escapeMenu.AttachOptions(_tacticalOverlay, _audio);
        // #277: preview sharing rides the overlay (built at launch by GameGuiWiring.AttachPreviews).
        _previewPublisher = resolverOverlay?.PreviewPublisher;
        _remotePreviews   = resolverOverlay?.RemotePreviews;
        _remotePreviews?.AttachColors(colorForPlayer);

        // Play a sound cue the moment each beat becomes active, in lockstep with its visual. Audio is
        // GUI-only and may be unavailable (then AudioManager no-ops), so this is best-effort.
        if (_presentationPlayer != null && _audio != null)
        {
            _presentationPlayer.BeatStarted += beat =>
            {
                string? cue = PresentationSoundCues.CueFor(beat);
                if (cue != null) _audio.Play(cue);
            };
            // #238: attacks sound once per VOLLEY (in step with each visible burst of shots/swings),
            // not once at beat start. #239: each cue is voiced by the weapon's effect set, and a
            // volley that lands something also sounds its impact when the shots arrive.
            _presentationPlayer.AttackVolleyStarted += attack =>
                _audio.Play(PresentationSoundCues.VolleyCue(attack));
            _presentationPlayer.AttackVolleyImpact += attack =>
                _audio.Play(PresentationSoundCues.ImpactCue(attack));
        }

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
        _escapeMenu.Close();
        _placedModels.Clear();
        lock (_terrainLock)    _terrain.Clear();
        lock (_objectivesLock) _objectives.Clear();

        _tableState            = null;
        _colorForPlayer        = null;
        _log                   = null;
        _playerMessageUI       = null;
        _chatInput             = "";
        _zoom                  = 1f;
        _pan                   = Vector2.Zero;
        _showChat              = true;
        _showLog               = true;
        _showDebug             = false;
        _chatChannel           = EChatMessageType.Global;
        _chatUnread            = false;
        _lastChatCount         = 0;
        _resolverOverlay       = null;
        _taskDisplay           = null;
        _previewPublisher      = null;
        _remotePreviews        = null;
        _presentationPlayer    = null;
        _resolverOverlayFaulted = false;
        _lastLogCount          = 0;
        _gameOverResult        = null;
        _inGame                = false;

        // Every game teardown funnels through here (game-over card, escape-menu quit-to-menu,
        // escape-menu load). Program.cs uses this to stop the public-listing heartbeat (#271).
        OnGameExited?.Invoke();
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
            _placedModels[model] = (_colorForPlayer(unit.PlayerID), unit);
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
        // Start maximized: the old flow resized a 1280x720 window up to near-monitor size WITHOUT
        // repositioning it, leaving it off-center and not quite filling the screen. The maximized
        // flag fills the work area properly (taskbar respected), and the window stays resizable;
        // 1280x720 remains the un-maximize/restore size.
        Raylib.SetConfigFlags(ConfigFlags.ResizableWindow | ConfigFlags.MaximizedWindow);
        Raylib.InitWindow(1280, 720, "Future of Dark Grimness");
        Raylib.SetTargetFPS(30);

        // Escape is not a quit key. It reads as "cancel the action I'm in the middle of" - several
        // resolvers already bind it that way - and raylib's default exit key made it tear the window
        // down instead. Quit via the main menu or the window close button.
        Raylib.SetExitKey(KeyboardKey.Null);

        int monitor   = Raylib.GetCurrentMonitor();
        int monitorH  = Raylib.GetMonitorHeight(monitor);

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
        // UI chrome cues (button clicks, toggles, canvas commits) share the same device; the static
        // UiSound facade lets any screen/resolver voice one without being handed the AudioManager.
        UiSound.Attach(_audio);

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

        OnWindowReady?.Invoke();

        while (!Raylib.WindowShouldClose() && !_closeRequested)
        {
            int screenW = Raylib.GetScreenWidth();
            int screenH = Raylib.GetScreenHeight();

            Raylib.BeginDrawing();
            Raylib.ClearBackground(Background);

            if (_inGame)
            {
                _presentationPlayer?.Update(Raylib.GetFrameTime());

                HandleTableViewInput(screenW, screenH);
                var layout = ComputeLayout(screenW, screenH);
                // Push the layout to the tactical overlay once per frame so both its canvas-pass draws
                // (below) and its ImGui-pass instruments read the same world<->screen mapping.
                _tacticalOverlay.UpdateLayout(layout.Scale, layout.OriginX, layout.OriginY, TableHIn);
                DrawTable(layout);
                if (ViewSettings.ShowGrid)
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

                // #274: cast success/failure, the per-target landing, and the assist streams that
                // swayed the roll — all world-space, over the models they belong to.
                if (_presentationPlayer != null &&
                    _presentationPlayer.TryGetActiveSpell(out var spellBeat, out var spellProgress))
                {
                    SpellOverlay.Draw(spellBeat, spellProgress, layout.Scale, layout.OriginX, layout.OriginY, TableHIn);
                }

                if (_presentationPlayer != null &&
                    _presentationPlayer.TryGetActiveDice(out var diceBeat, out var diceProgress, out var diceAlpha))
                {
                    // #245: the caption strip ghosts itself while the attack animation reaches into it.
                    Rectangle? diceAvoid = null;
                    if (_presentationPlayer.TryGetActiveAttack(out var diceAvoidAttack, out _))
                        diceAvoid = AttackOverlay.ScreenBounds(diceAvoidAttack,
                            layout.Scale, layout.OriginX, layout.OriginY, TableHIn, 48f);
                    DiceOverlay.Draw(diceBeat, diceProgress, diceAlpha, layout.AreaW, screenH, diceAvoid);
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

                // #277: the Notice/Toast tiers ride their own concurrent track, so they draw every frame
                // regardless of what holds the active slot - that is the whole point of them.
                if (_presentationPlayer != null)
                {
                    BannerOverlay.DrawHeld(_presentationPlayer.GetHeldBanners(), layout.AreaW, screenH);
                }

                DrawStatusHud(layout);

                // Right-column regions: resolver panel (top half) + log/chat console (bottom half).
                int rightW    = RightColumnWidth(screenW);
                int rightX    = screenW - rightW;
                int panelH    = (int)(screenH * ResolverPanelLayout.ScreenHeightFraction);
                ResolverPanelLayout.Set(rightX, 0, rightW, panelH);

                rlImGui.Begin();
                // Reset the Escape arbiter before any consumer runs this frame. Passing the menu's open
                // state mutes every other Escape consumer while the menu owns the key.
                EscapeRouter.BeginFrame(_escapeMenu.IsOpen);
                // Runs before the hit tester / resolvers so its Alt-measure WantCaptureMouse override
                // lands before they read that flag (see MeasurementOverlay).
                _measurementOverlay.UpdateLayout(layout.Scale, layout.OriginX, layout.OriginY, TableHIn);
                _measurementOverlay.Draw(screenW, screenH);
                _hitTester.Update(_tableState!, layout.Scale, layout.OriginX, layout.OriginY, TableHIn);
                // Overlay input (F toggle, hover timing, pins) runs after the hit tester so hover state
                // is fresh; heavy rebuilds happen in the next frame's DrawField.
                _tacticalOverlay.UpdateInput(Raylib.GetFrameTime(), _hitTester);
                DrawConsole(rightX, panelH, rightW, screenH - panelH);
                // Outstanding Tasks window hidden per user request; re-enable by restoring this draw call.
                // _taskDisplay?.Draw(screenW, screenH);
                _tooltipOverlay.UpdateLayout(layout.Scale, layout.OriginX, layout.OriginY, TableHIn);
                _tooltipOverlay.Draw(screenW, screenH, _hitTester, _resolverOverlay?.ActiveInteractionHandler);
                _resolverOverlay?.UpdateLayout(layout.Scale, layout.OriginX, layout.OriginY, TableHIn);
                // #277: other players' live previews (ghosts / planned paths) - same background
                // draw list layer as the local resolver's own ghosts, drawn regardless of what the
                // local player is doing (they're someone else's state).
                _remotePreviews?.UpdateLayout(layout.Scale, layout.OriginX, layout.OriginY, TableHIn);
                _remotePreviews?.Draw();
                // Hold interactive prompts until the animation queue drains, so the player always
                // sees movement / shots land before being asked to react.
                bool animating = _presentationPlayer?.IsAnimating ?? false;
                bool resolverShown = false;
                if (!_resolverOverlayFaulted && !animating)
                {
                    try
                    {
                        _resolverOverlay?.Draw(screenW, screenH);
                        resolverShown = _resolverOverlay?.HasAnyPending ?? false;
                    }
                    catch (Exception ex)
                    {
                        _resolverOverlayFaulted = true;
                        var errColor = new TextColor(255, 120, 120, 255);
                        _log?.Add($"[RESOLVER ERROR] {ex.GetType().Name}: {ex.Message}", errColor);
                        _log?.Add(ex.StackTrace ?? "(no stack trace)", errColor);
                    }
                }
                // Instruments draw AFTER the resolver so the movement resolver has published this frame's
                // ghost snapshot -- pips/counts/distance then track the ghost with no one-frame lag. On the
                // background draw list (above tokens, under windows), same layer as the ghosts they annotate.
                _tacticalOverlay.DrawInstruments(screenW, screenH);
                // #277: publish the local resolver's preview - after its Draw for the same
                // freshness reason as the instruments above (it reads the frame's ghost snapshot).
                _previewPublisher?.Tick(Raylib.GetTime());
                // Fill the resolver panel with an idle placeholder whenever nothing is prompting, so the
                // top-right region always reads as an intentional panel rather than empty space.
                if (!resolverShown) DrawIdleResolverPanel();
                DrawGameOverOverlay(screenW, screenH);

                // Standalone "Menu" button in the bottom-left corner (where the retired toolbar sat),
                // the discoverable way into the Esc menu. Drawn before the menu so its dim covers it.
                DrawMenuButton(screenH);

                // In-game menu (Esc). Opens only when no context claimed Escape this frame, and never
                // over the game-over card (that has its own Return to Main Menu button). Drawn last so
                // its dim + window sit above every other in-game overlay.
                bool justOpenedMenu = false;
                if (_gameOverResult == null && EscapeRouter.ShouldOpenMenu())
                {
                    _escapeMenu.Open();
                    justOpenedMenu = true;
                }
                _escapeMenu.Draw(screenW, screenH, justOpenedMenu);

                rlImGui.End();
            }
            else
            {
                rlImGui.Begin();
                _currentScreen.Draw(screenW, screenH);
                // A networked client can lose the host while still in the lobby (before any game exists);
                // its lobby view-model raises the same game-ended signal, so draw the overlay here too so
                // that pre-launch case isn't silently stuck (QF8). No-op when nothing has ended.
                DrawGameOverOverlay(screenW, screenH);
                rlImGui.End();
            }

            Raylib.EndDrawing();
        }

        rlImGui.Shutdown();
        if (_exclusionRTReady) Raylib.UnloadRenderTexture(_exclusionRT);
        if (_mottleReady) Raylib.UnloadTexture(_mottleTex);
        _audio?.Dispose();
        Raylib.CloseWindow();
    }

    private Layout ComputeLayout(int screenW, int screenH)
    {
        // The right column reserves width on the right; the table viewport is everything to its left.
        int rightW    = RightColumnWidth(screenW);
        int viewportW = Math.Max(1, screenW - rightW);

        // Fit-to-viewport scale (the #8 zoom-out clamp: the table fills ~100% of the viewport at _zoom==1).
        float scale = FitScale(viewportW, screenH) * _zoom;

        int tablePixW = (int)(TableWIn * scale);
        int tablePixH = (int)(TableHIn * scale);
        int originX   = (int)((viewportW - tablePixW) / 2f + _pan.X);
        int originY   = (int)((screenH   - tablePixH) / 2f + _pan.Y);

        // AreaW is the viewport width so centered overlays (status HUD, dice, banners) sit over the table,
        // not under the right column.
        return new Layout(scale, originX, originY, viewportW, screenH);
    }

    // Alt+wheel to zoom toward the cursor (clamped MinZoom..MaxZoom), middle-drag to pan. Runs at the top
    // of the frame, before ComputeLayout, so this frame renders with the updated transform. Zoom is gated
    // on the mouse being over the table viewport only (NOT WantCaptureMouse -- the Alt-held measurement
    // overlay raises that flag over the table, which would otherwise veto every zoom); pan additionally
    // respects WantCaptureMouse so dragging over the toolbar/panels doesn't scroll the board.
    // #277: Alt is the camera/measure modifier (Alt+wheel zoom, Alt+drag measure) so Ctrl+Wheel belongs
    // entirely to the group-formation cycle -- zoom keeps working while a group ghost is live.
    private void HandleTableViewInput(int screenW, int screenH)
    {
        int rightW    = RightColumnWidth(screenW);
        int viewportW = Math.Max(1, screenW - rightW);
        float fit     = FitScale(viewportW, screenH);

        float mx = Raylib.GetMouseX(), my = Raylib.GetMouseY();
        bool overViewport = mx < viewportW && my < screenH;

        // Alt + wheel: zoom, keeping the world point under the cursor pinned.
        bool alt    = Raylib.IsKeyDown(KeyboardKey.LeftAlt) || Raylib.IsKeyDown(KeyboardKey.RightAlt);
        float wheel = Raylib.GetMouseWheelMove();
        if (overViewport && alt && wheel != 0f)
        {
            float scaleOld   = fit * _zoom;
            float originXOld = (viewportW - TableWIn * scaleOld) / 2f + _pan.X;
            float originYOld = (screenH   - TableHIn * scaleOld) / 2f + _pan.Y;
            float worldXin   = (mx - originXOld) / scaleOld;   // inches from table origin under the cursor
            float worldYin   = (my - originYOld) / scaleOld;

            float newZoom  = Math.Clamp(_zoom * MathF.Pow(1.1f, wheel), MinZoom, MaxZoom);
            float scaleNew = fit * newZoom;
            _pan.X = mx - (viewportW - TableWIn * scaleNew) / 2f - worldXin * scaleNew;
            _pan.Y = my - (screenH   - TableHIn * scaleNew) / 2f - worldYin * scaleNew;
            _zoom  = newZoom;
        }

        // Middle-drag: pan (skip when an ImGui window owns the mouse).
        bool overUi = ImGui.GetIO().WantCaptureMouse;
        if (overViewport && !overUi && Raylib.IsMouseButtonDown(MouseButton.Middle))
        {
            Vector2 d = Raylib.GetMouseDelta();
            _pan.X += d.X;
            _pan.Y += d.Y;
        }

        // Bound the pan by the overflow of the scaled table past the viewport, plus (when zoomed in) a 5%
        // margin so a strip of background shows past the edge and it reads as the edge. At min zoom there's
        // no overflow and no margin, so the table stays locked centered.
        float scale = fit * _zoom;
        float overflowX = MathF.Max(0f, (TableWIn * scale - viewportW) / 2f);
        float overflowY = MathF.Max(0f, (TableHIn * scale - screenH)   / 2f);
        float marginX = _zoom > MinZoom ? PanEdgeMarginFraction * TableWIn * scale : 0f;
        float marginY = _zoom > MinZoom ? PanEdgeMarginFraction * TableHIn * scale : 0f;
        _pan.X = Math.Clamp(_pan.X, -(overflowX + marginX), overflowX + marginX);
        _pan.Y = Math.Clamp(_pan.Y, -(overflowY + marginY), overflowY + marginY);
    }

    // Border thickness (px) framing the table rect.
    private const float TableBorderThickness = 3f;

    private void DrawTable(Layout l)
    {
        int tw = (int)(TableWIn * l.Scale);
        int th = (int)(TableHIn * l.Scale);
        Raylib.DrawRectangle(l.OriginX, l.OriginY, tw, th, _backgroundStyle.Surface);
        DrawMottleTexture(l, tw, th);
        // A thin, defined frame around the felt (replaces the old edge vignette / "satin" sheen).
        Raylib.DrawRectangleLinesEx(new Rectangle(l.OriginX, l.OriginY, tw, th),
            TableBorderThickness, _backgroundStyle.Border);
    }

    // Tiles the mottle-noise texture across the table at a constant on-screen density (source rect larger
    // than the texture, repeat wrap) and blends it additively so only the lighter flecks show through.
    private void DrawMottleTexture(Layout l, int tw, int th)
    {
        EnsureMottleTexture();
        var src = new Rectangle(0, 0, tw, th);
        var dst = new Rectangle(l.OriginX, l.OriginY, tw, th);
        Raylib.BeginBlendMode(BlendMode.Additive);
        Raylib.DrawTexturePro(_mottleTex, src, dst, Vector2.Zero, 0f, _backgroundStyle.MottleTint);
        Raylib.EndBlendMode();
    }

    private void EnsureMottleTexture()
    {
        if (_mottleReady && _mottleFor == _background) return;
        if (_mottleReady) Raylib.UnloadTexture(_mottleTex);
        // 128px Perlin patch; the style's scale sets the grain (fine and organic for grass, coarse and
        // blotchy for concrete), its offset moves the sample window so no two surfaces share a pattern.
        // Bilinear + repeat so it tiles seamlessly and stays smooth when stretched.
        Image img = Raylib.GenImagePerlinNoise(128, 128,
            _backgroundStyle.MottleOffset, _backgroundStyle.MottleOffset, _backgroundStyle.MottleScale);
        _mottleTex = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        Raylib.SetTextureFilter(_mottleTex, TextureFilter.Bilinear);
        Raylib.SetTextureWrap(_mottleTex, TextureWrap.Repeat);
        _mottleReady = true;
        _mottleFor   = _background;
    }

    // Etched inch grid + a soft felt vignette, drawn only within the table rect (so it stays under
    // terrain/objectives/models, which draw afterward). Interior lines only — the border is the edge.
    private void DrawTableGrid(Layout l)
    {
        Color minor = _backgroundStyle.GridMinor;
        Color major = _backgroundStyle.GridMajor;

        int tw = (int)(TableWIn * l.Scale);
        int th = (int)(TableHIn * l.Scale);
        int x0 = l.OriginX, y0 = l.OriginY;
        int x1 = x0 + tw,   y1 = y0 + th;

        for (float xi = GridMinorInches; xi < TableWIn; xi += GridMinorInches)
        {
            int px = x0 + (int)(xi * l.Scale);
            Raylib.DrawLine(px, y0, px, y1, IsMajorGridLine(xi) ? major : minor);
        }
        for (float zi = GridMinorInches; zi < TableHIn; zi += GridMinorInches)
        {
            int py = y0 + (int)(zi * l.Scale);
            Raylib.DrawLine(x0, py, x1, py, IsMajorGridLine(zi) ? major : minor);
        }
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
            // Legibility patterns layered on top (hatch=difficult, chevrons=cover, crimson Xs=dangerous).
            TerrainPatternRenderer.Draw(terrain.Shape, terrain.TerrainType, l.Scale, l.OriginX, l.OriginY, TableHIn);
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

            // #250: follow the model's true base shape, like DrawModels does — a rectangle-based model
            // used to get a circular halo that contradicted the base drawn under it. Inflations are in
            // inches so they track zoom; the pulse ring's extra pixels convert back through the scale.
            const float HaloInflateIn = 0.18f; // just outside the base
            float pulseInflateIn = HaloInflateIn + (3f + 6f * pulse) / l.Scale;

            ModelBaseRenderer.DrawFilledRaylib(model.BaseShape, cx, cy, l.Scale, fill, ring, model.Facing, HaloInflateIn);
            ModelBaseRenderer.DrawOutlineRaylib(model.BaseShape, cx, cy, l.Scale, halo,
                thickness: 1f, inflateInches: pulseInflateIn, facing: model.Facing);
        }
    }

    private void DrawModels(Layout l)
    {
        foreach (var (model, (color, unit)) in _placedModels)
        {
            // Not in play: held in Ambush reserve, riding a transport, or an Aircraft that flew off the
            // edge. Its models are parked at the origin, which is the table's bottom-left corner.
            if (!unit.GetIsOnBattlefield()) continue;

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

            // Joined-Hero marker (#227): a white, dark-outlined star centred on the hero model's base. Drawn
            // last so it sits atop the fill; overlays the base rather than ringing it, so it never clashes
            // with the selection / hover halos or range rings.
            if (HeroMarkerRenderer.IsHeroModel(unit, model))
                HeroMarkerRenderer.DrawStarRaylib(cx, cy, model.BaseRadiusInches * l.Scale, a);
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

    // Small always-visible "Menu" button pinned to the bottom-left corner (pivot 0,1), the same spot the
    // old view toolbar occupied. Opens the in-game menu; Esc does the same. Auto-sized, faintly backed.
    private void DrawMenuButton(int screenH)
    {
        ImGui.SetNextWindowPos(new Vector2(8, screenH - 8), ImGuiCond.Always, new Vector2(0f, 1f));
        ImGui.SetNextWindowBgAlpha(0.70f);
        ImGui.Begin("##menubtn",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize |
            ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav);
        if (ImGui.Button("Menu"))
            _escapeMenu.Open();
        ImGui.End();
    }

    // Centered "Game Over" card shown once the game ends. The board stays visible behind it; the player
    // must click through to leave (no auto-return), at which point we tear down and return to the menu.
    // #235: draggable (position forced only when it appears) so the final board can be inspected.
    private void DrawGameOverOverlay(int screenW, int screenH)
    {
        string? result = _gameOverResult;
        if (result == null) return;

        var size = new Vector2(Math.Min(460f, screenW * 0.8f), 200f);
        ImGui.SetNextWindowPos(new Vector2((screenW - size.X) / 2f, (screenH - size.Y) / 2f), ImGuiCond.Appearing);
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
        ImGui.Begin("Game Over##overlay",
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoSavedSettings);

        ImGui.PushFont(LargeFont);
        CenteredText(size.X, "Game Over");
        ImGui.PopFont();

        ImGui.Spacing();
        // Center the result line when it fits; long results fall back to left-aligned wrapping.
        if (ImGui.CalcTextSize(result).X <= ImGui.GetContentRegionAvail().X)
            CenteredText(size.X, result);
        else
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

    // One line of text horizontally centered in a window of the given width (uses the current font).
    private static void CenteredText(float windowW, string text)
    {
        ImGui.SetCursorPosX(MathF.Max(0f, (windowW - ImGui.CalcTextSize(text).X) / 2f));
        ImGui.TextUnformatted(text);
    }

    // Log/chat console: fills the bottom half of the in-game right column. Log and Chat are independent
    // TOGGLES (Chat on the left); with both on, their lines merge into one column in arrival order (by the
    // shared LogEntry.Sequence). The engine GameLog is the Log source; the sender-coloured chat store is
    // the Chat source, which also shows the Global/Team channel toggle + input. Chat flags unread when a
    // message arrives while Chat is toggled off.
    private void DrawConsole(int x, int y, int w, int h)
    {
        if (_log == null) return;

        // Unread bookkeeping (every frame, regardless of what's shown).
        int chatCount = _playerMessageUI?.ChatLog.Count ?? 0;
        if (chatCount > _lastChatCount && !_showChat)
            _chatUnread = true;
        _lastChatCount = chatCount;

        ImGui.SetNextWindowPos(new Vector2(x, y), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(w, h), ImGuiCond.Always);
        ImGui.Begin("##console",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBringToFrontOnFocus);

        // Source toggles: Chat (left), then Log, then Debug (developer detail, off by default).
        DrawConsoleToggle((_chatUnread ? "Chat *" : "Chat") + "##chattoggle", ref _showChat, isChat: true);
        ImGui.SameLine();
        DrawConsoleToggle("Log##logtoggle", ref _showLog, isChat: false);
        ImGui.SameLine();
        bool debugWasOn = _showDebug;
        DrawConsoleToggle("Debug##debugtoggle", ref _showDebug, isChat: false);
        if (_showDebug != debugWasOn)
        {
            // #163 — the Debug view doubles as the rule-trace switch: evaluations run host-side, so on
            // the host this starts/stops trace generation (a client's toggle only controls its local
            // view of relayed lines). Assign only on change so a --trace-rules launch isn't clobbered
            // while the view happens to be closed.
            FDG.Rules.Dispatch.RuleTrace.Enabled = _showDebug;
        }

        ImGui.Separator();
        DrawConsoleContent();

        ImGui.End();
    }

    // Idle placeholder shown in the resolver panel (top-right) when no decision is pending, so the region
    // always reads as an intentional panel.
    private void DrawIdleResolverPanel()
    {
        if (_log == null) return;
        if (ResolverPanelLayout.BeginDocked("##idleresolver"))
        {
            ImGui.TextDisabled("No decision pending.");
            ImGui.Spacing();
            ImGui.TextDisabled("Prompts appear here on your turn.");
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
        // No HorizontalScrollbar: with it set, ImGui widens the child's work rect to the content size, which
        // is what RenderConsoleLine's TextWrapped wraps against - so nothing ever wrapped and every long line
        // grew the scrollbar instead. Without the flag the wrap width is the visible column and long lines
        // spill onto the next row, which is what a log/chat column should do.
        ImGui.BeginChild("##consolescroll", new Vector2(0, -inputH), ImGuiChildFlags.None,
            ImGuiWindowFlags.None);

        // The engine log stream carries both normal and Debug-tagged lines; keep whichever categories are
        // toggled on (Debug is a developer view, off by default).
        List<LogEntry>? logMsgs = null;
        if (_showLog || _showDebug)
        {
            logMsgs = _log!.Snapshot();
            logMsgs.RemoveAll(e => e.IsDebug ? !_showDebug : !_showLog);
        }
        List<LogEntry>? chatMsgs = (_showChat && _playerMessageUI != null) ? _playerMessageUI.ChatLog.Snapshot() : null;
        int ln = logMsgs?.Count ?? 0, cn = chatMsgs?.Count ?? 0;

        if (ln == 0 && cn == 0)
        {
            ImGui.TextDisabled(!_showLog && !_showChat && !_showDebug
                ? "Log, Debug and Chat hidden -- toggle one on above."
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
