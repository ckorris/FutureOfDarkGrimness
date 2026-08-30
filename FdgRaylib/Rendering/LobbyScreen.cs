using System.Numerics;
using FDG;
using FDG.Ai;
using FDG.EngineInterface;
using FDG.Network.Connection.Lobby;
using FDG.Network.Messages;
using FDG.Players;
using FDG.SaveLoad;
using FdgRaylib.Cli;
using FdgRaylib.Config;
using FdgRaylib.Rendering.Presentation;
using FdgRaylib.Rendering.Resolvers;
using ImGuiNET;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text.Json;
using FDG.Rules.Serialization;
using Raylib_cs;
using TinyDialogsNet;

namespace FdgRaylib.Rendering;

public class LobbyScreen : IAppScreen
{
    public Action? OnBack;
    public Action<ITableState, Func<PlayerID, Color>, GameLog?, GuiResolverOverlay, GuiOutstandingTaskDisplay, PresentationPlayer, Func<string?>?, GuiPlayerMessageUI>? OnGameLaunched;
    public Action<string>? OnGameEnded;

    // The structured end-of-game record, forwarded from the host's FDGServer (#192). Fires just BEFORE
    // OnGameEnded, while the game state is still whole - #187's recovery save is written from here.
    // Never fires on a client (only the host has an FDGServer).
    public Action<GameResult>? OnGameCompleted;

    private ILobbyViewModel? _viewModel;
    private string _chatInput = "";

    private IFDGGame? _pendingGame;

    private static readonly FileFilter ArmyFilter = new(
        $"Army List (*{ArmyListFile.EXTENSION_WITH_PERIOD})",
        new[] { $"*{ArmyListFile.EXTENSION_WITH_PERIOD}" });

    private static readonly FileFilter TerrainFilter = new(
        $"Terrain Layout (*{TerrainLayoutFile.EXTENSION_WITH_PERIOD})",
        new[] { $"*{TerrainLayoutFile.EXTENSION_WITH_PERIOD}" });

    private string? _lastLaunchError;
    private IReadOnlyList<string> _launchProblems = Array.Empty<string>();

    // #372/#388 starter armies. The folder scan is process-wide and starts the first time a lobby opens
    // (SetViewModel touches it), because the armies folder doesn't change under us and re-reading 12 MB
    // of JSON per lobby would be waste. The ROTATION is per-lobby, so a fresh lobby re-rolls freely.
    private static readonly Lazy<ArmyCatalog> SharedArmyCatalog =
        new(() => new ArmyCatalog(), LazyThreadSafetyMode.ExecutionAndPublication);

    private BotArmyPicker? _botArmyPicker;

    // Slots this lobby has already handed a starter army. Keyed by PlayerID, not by "does the slot
    // have an army", because AddAiPlayer gives every bot a 100-pt "Test Army" stub - there is no
    // unassigned state to watch for. Also stops a re-roll or a hand-picked Load Army from being
    // immediately overwritten on the next frame.
    private readonly HashSet<Guid> _autoArmiedSlots = new();

    // Host connection info shown so the host knows what address to share (QF9). The port mirrors
    // CommandProtocol.TEMP_PORT (internal to the engine assembly, so it's restated here).
    private const int ListenPort = 6389;
    private string? _lanAddresses;                 // computed once, lazily
    private volatile string _publicAddress = "checking...";
    private bool _publicFetchStarted;

    // Light-blue accent used to make section/column headers read as headers. Shared with the
    // Host/Client dialogs via ImGuiTheme so every screen uses one accent.
    private static readonly Vector4 HeaderAccent = ImGuiTheme.HeaderAccent;

    public void SetViewModel(ILobbyViewModel viewModel)
    {
        _viewModel?.Dispose();
        _viewModel = viewModel;
        _chatInput = "";
        _pendingGame = null;

        // #372: start the armies scan as soon as a lobby exists (it runs on a background task) and give
        // this lobby its own rotation, so re-opening one starts the army cycle over.
        _botArmyPicker = null;
        _autoArmiedSlots.Clear();
        _ = SharedArmyCatalog.Value;
        viewModel.OnLaunched += game => _pendingGame = game;
        // Fires on the engine thread; the renderer just records it and reacts on the main thread.
        viewModel.OnGameEnded += result => OnGameEnded?.Invoke(result);
        // Also the engine thread, and deliberately handled synchronously there: the recovery save must be
        // taken before anything reacts to the game ending (#187).
        viewModel.OnGameCompleted += result => OnGameCompleted?.Invoke(result);

        // #310: a fresh host lobby opens on the settings the last hosted game used (saved config), not
        // on the engine defaults. No-op for a client or a resume - ApplyTo gates on both, since a
        // resumed game launches from its own saved settings.
        UserConfig.Current.HostSettings.ApplyTo(viewModel);

        // Pick a random terrain type (cosmetic table surface) each time a fresh lobby opens, so every
        // new game leads with a different-looking board instead of always Forest. Host-only (the setting
        // is host-owned and broadcast to clients) and skipped on resume (saved games keep their surface).
        // Deliberately NOT remembered in the config (#310) - randomizing it is the whole point.
        if (viewModel.HasHostPrivileges && !viewModel.IsResumeMode)
        {
            var backgrounds = Enum.GetValues<ETableBackground>();
            viewModel.SetTableBackground(backgrounds[Random.Shared.Next(backgrounds.Length)]);
        }
    }

    public void Draw(int screenW, int screenH)
    {
        // Consume a pending launch on the main thread
        if (_pendingGame != null)
        {
            HandleLaunch(_pendingGame);
            _pendingGame = null;
        }

        if (_viewModel == null) return;

        DrawScaled(screenW, screenH);
    }

    // How much smaller the window is than the monitor the fonts were baked for, clamped to
    // [0.5, 1]. 1 when fullscreen/maximized; shrinks proportionally as the window does.
    private static float WindowToMonitorScale(int screenW, int screenH)
    {
        int monitor = Raylib.GetCurrentMonitor();
        float monW = Raylib.GetMonitorWidth(monitor);
        float monH = Raylib.GetMonitorHeight(monitor);
        if (monW <= 0f || monH <= 0f) return 1f;
        return Math.Clamp(MathF.Min(screenW / monW, screenH / monH), 0.5f, 1f);
    }

    private void DrawScaled(int screenW, int screenH)
    {
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("Lobby",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar);

        // Fonts are baked for the monitor at startup and the panel scales below are tuned for a
        // fullscreen window; a smaller window scales the lobby down proportionally instead of
        // overflowing (truncated columns, oversized rows). This must ride the per-window
        // SetWindowFontScale calls: io.FontGlobalScale is latched at NewFrame, so setting it inside
        // Draw and restoring before returning never rendered (the old 1.2x was a silent no-op).
        float ws         = WindowToMonitorScale(screenW, screenH);
        float panelScale = 1.5f * ws;  // the "~50% bigger" panels, as tuned at fullscreen

        float margin       = 10f;
        // Floor keeps the settings rows usable at small window sizes; the main column absorbs the rest.
        float settingsW    = MathF.Max(280f, screenW * 0.25f);
        float mainW        = screenW - settingsW - margin * 3;
        float baseFontSize = ImGui.GetFontSize();       // baked font px, unaffected by ws
        float fontSize     = baseFontSize * ws;         // layout metric the lobby's rows derive from
        float framePadY    = ImGui.GetStyle().FramePadding.Y;
        float naturalBtnH  = fontSize + framePadY * 2;
        float headerH      = MathF.Max(40f, naturalBtnH + 12f);
        // Chat-input row (and Launch button on the right) ~50% taller than the standard
        // button height so the action area at the bottom is easier to hit.
        float chatInputH   = MathF.Max(45f, (naturalBtnH + 6f) * 1.5f);
        float rightH       = screenH - margin * 2;
        float innerH       = screenH - margin * 2 - headerH - chatInputH - margin * 2;
        float playerListH  = innerH * 0.55f;
        float chatH        = innerH - playerListH - margin;

        // ── Header ────────────────────────────────────────────────────────────
        ImGui.SetCursorPos(new Vector2(margin, margin));
        // NoScrollbar: the Back button + window padding just overflow headerH, which would otherwise
        // draw a phantom scrollbar right next to Back.
        ImGui.BeginChild("##header", new Vector2(mainW, headerH), ImGuiChildFlags.Borders,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        float headerFontH  = headerH * 0.65f;
        float headerScale  = headerFontH / baseFontSize; // window-scale factor that renders at headerFontH px
        ImGui.SetWindowFontScale(headerScale);
        ImGui.SetCursorPosY((headerH - headerFontH) * 0.5f);
        ImGui.TextUnformatted(_viewModel.ServerName);
        ImGui.SetWindowFontScale(ws);

        // CalcTextSize ignores SetWindowFontScale, so apply ws to the measured width by hand.
        float backW = (ImGui.CalcTextSize("Back").X + 36f) * ws;
        float backH = headerH - 8f;
        ImGui.SetCursorPos(new Vector2(mainW - backW - 4f, 4f));
        if (UiButton.Back("Back", new Vector2(backW, backH)))
        {
            PersistHostSettings();
            OnBack?.Invoke();
        }

        ImGui.EndChild();

        // ── Player List ───────────────────────────────────────────────────────
        float playerListY = margin + headerH + margin;
        ImGui.SetCursorPos(new Vector2(margin, playerListY));
        ImGui.BeginChild("##players", new Vector2(mainW, playerListH), ImGuiChildFlags.Borders);
        ImGui.SetWindowFontScale(panelScale);  // rows + font ~50% bigger than the rest of the lobby
        DrawPlayerList(mainW);
        ImGui.EndChild();

        // ── Chat Log ──────────────────────────────────────────────────────────
        float chatY = playerListY + playerListH + margin;
        ImGui.SetCursorPos(new Vector2(margin, chatY));
        ImGui.BeginChild("##chatlog", new Vector2(mainW, chatH), ImGuiChildFlags.Borders,
            ImGuiWindowFlags.HorizontalScrollbar);
        ImGui.SetWindowFontScale(panelScale);  // match the player + settings panels
        DrawChatLog();
        ImGui.EndChild();

        // ── Chat Input + Send ─────────────────────────────────────────────────
        // Wrapped in a child so SetWindowFontScale applies locally — matches the chat log + launch button.
        float chatInputY = screenH - margin - chatInputH;
        ImGui.SetCursorPos(new Vector2(margin, chatInputY));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.BeginChild("##chatrow", new Vector2(mainW, chatInputH), ImGuiChildFlags.None,
            ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleVar();
        ImGui.SetWindowFontScale(panelScale);

        // Size the Send button to its text at the row's scaled font, so it doesn't clip to "Ser".
        // (CalcTextSize ignores SetWindowFontScale; ws applied by hand, same as the Back button.)
        float sendBtnW = (ImGui.CalcTextSize("Send").X + 36f) * ws;

        // InputText height = font + 2*FramePadding.Y. Recompute padding against the now-scaled font size.
        float scaledFontSize  = ImGui.GetFontSize();
        float inputVerticalPad = MathF.Max(framePadY, (chatInputH - scaledFontSize) * 0.5f);
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding,
            new Vector2(ImGui.GetStyle().FramePadding.X, inputVerticalPad));
        ImGui.SetNextItemWidth(mainW - sendBtnW - margin);
        if (ImGui.InputText("##chatinput", ref _chatInput, 512, ImGuiInputTextFlags.EnterReturnsTrue))
            SubmitChat();
        ImGui.PopStyleVar();

        ImGui.SameLine();
        if (UiButton.Navigate("Send", new Vector2(sendBtnW, chatInputH)) &&
            !string.IsNullOrWhiteSpace(_chatInput))
            SubmitChat();

        ImGui.EndChild();

        // ── Settings + Launch (right panel) ───────────────────────────────────
        float rightX = margin * 2 + mainW;
        ImGui.SetCursorPos(new Vector2(rightX, margin));
        ImGui.BeginChild("##settings", new Vector2(settingsW, rightH - chatInputH - margin),
            ImGuiChildFlags.Borders);
        ImGui.SetWindowFontScale(panelScale);  // settings fields ~50% bigger than the rest of the lobby
        DrawSettings(settingsW);
        ImGui.EndChild();

        ImGui.SetCursorPos(new Vector2(rightX, margin + rightH - chatInputH));
        // No border on the launch child — the button itself fills the panel and provides the visual edge.
        ImGui.BeginChild("##launch", new Vector2(settingsW, chatInputH), ImGuiChildFlags.None);
        ImGui.SetWindowFontScale(panelScale);  // match the chat row + other panels
        DrawLaunch(settingsW, chatInputH);
        ImGui.EndChild();

        ImGui.End();
    }

    private void DrawPlayerList(float panelW)
    {
        AutoArmyNewSlots();

        IReadOnlyList<LobbyPlayerInfoSummary> players = _viewModel!.PlayerInfos;

        // Rows 50% taller: the extra height comes from cell padding (applied top+bottom each row).
        Vector2 cellPad = ImGui.GetStyle().CellPadding;
        float rowH = ImGui.GetTextLineHeight() + cellPad.Y * 2f;
        ImGui.PushStyleVar(ImGuiStyleVar.CellPadding, new Vector2(cellPad.X, cellPad.Y + rowH * 0.25f));

        // #221: effective colour per row this frame, resolved from the SYNCED picks (ColorIndex rides
        // LobbyPlayerInfoSummary through the lobby protocol, so every machine computes the same result) -
        // explicit picks win, unpicked rows fill with the free defaults in palette order.
        var chosen = new int?[players.Count];
        for (int i = 0; i < players.Count; i++)
            chosen[i] = players[i].ColorIndex >= 0 ? players[i].ColorIndex : null;
        int[] effectiveColor = PlayerColorOptions.ResolveIndices(chosen);

        if (ImGui.BeginTable("##ptable", 8,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Name",    ImGuiTableColumnFlags.WidthStretch, 0.16f);
            ImGui.TableSetupColumn("Type",    ImGuiTableColumnFlags.WidthStretch, 0.07f);
            ImGui.TableSetupColumn("Army",    ImGuiTableColumnFlags.WidthStretch, 0.15f);
            ImGui.TableSetupColumn("Faction", ImGuiTableColumnFlags.WidthStretch, 0.12f);
            ImGui.TableSetupColumn("Pts",     ImGuiTableColumnFlags.WidthStretch, 0.06f);
            ImGui.TableSetupColumn("Team",    ImGuiTableColumnFlags.WidthStretch, 0.10f);
            ImGui.TableSetupColumn("Color",   ImGuiTableColumnFlags.WidthStretch, 0.12f);
            // #372: every row now carries Load Army AND Random Army, so Actions takes the width back
            // from Army/Faction (both of which wrap gracefully; a clipped button does not).
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthStretch, 0.22f);
            ImGui.PushStyleColor(ImGuiCol.Text, HeaderAccent);
            ImGui.TableHeadersRow();
            ImGui.PopStyleColor();

            for (int i = 0; i < players.Count; i++)
            {
                LobbyPlayerInfoSummary info = players[i];
                ImGui.TableNextRow();

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(info.PlayerName);

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(info.PlayerType.ToString());

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(info.ArmyListSummary.ArmyName);

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(info.ArmyListSummary.FactionName);

                ImGui.TableNextColumn();
                DrawPointsCell(info.ArmyListSummary, _viewModel.ArmyPoints);

                ImGui.TableNextColumn();
                DrawTeamCell(i, info, players.Count);

                ImGui.TableNextColumn();
                DrawColorCell(i, info, players, effectiveColor);

                ImGui.TableNextColumn();
                if (_viewModel.IsResumeMode)
                {
                    // Re-crew a saved slot. Host only; Local/bots today (networked client assignment TBD).
                    ImGui.BeginDisabled(!_viewModel.HasHostPrivileges);
                    if (UiButton.NavigateSmall($"Local##{i}"))
                        _viewModel.SetSavedSlotPlayerType(info.PlayerID, EPlayerType.Local);
                    ImGui.SameLine();
                    if (UiButton.NavigateSmall($"Tactician##{i}"))
                        _viewModel.SetSavedSlotPlayerType(info.PlayerID, EPlayerType.AI, EAiProfile.Tactician);
                    ImGui.SameLine();
                    if (UiButton.NavigateSmall($"DerpBot##{i}"))
                        _viewModel.SetSavedSlotPlayerType(info.PlayerID, EPlayerType.AI, EAiProfile.SoloRules);
                    ImGui.EndDisabled();
                }
                else
                {
                    bool canModify = _viewModel.CheckCanModifyPlayerIDInfo(info.PlayerID);
                    ImGui.BeginDisabled(!canModify);
                    if (UiButton.NavigateSmall($"Load Army##{i}"))
                        TryLoadArmyForPlayer(info.PlayerID);
                    ImGui.EndDisabled();

                    // #372: roll a random army from the folder. Offered on EVERY row, not just bots -
                    // it is the fastest way for a human to get a legal list too. Who may press it is
                    // decided entirely by CheckCanModifyPlayerIDInfo, the same gate Load Army uses: the
                    // host owns its own and the bots' rows but not a connected client's, and a client
                    // owns only its own. So nobody can roll another player's army, and a client cannot
                    // roll for a bot.
                    ImGui.SameLine();
                    ImGui.BeginDisabled(!canModify || !ArmyCatalogReady);
                    if (UiButton.NavigateSmall($"Random Army##{i}"))
                        AssignRandomArmy(info.PlayerID);
                    ImGui.EndDisabled();
                    // AllowWhenDisabled: the greyed-out case is the one that most needs explaining.
                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                        ImGui.SetTooltip(RandomArmyTooltip(canModify, info));
                }
            }

            ImGui.EndTable();
        }

        ImGui.PopStyleVar(); // CellPadding (taller rows)

        // #378: GDF and AoF armies may meet (owner ruling: warn, never block - points and core rules
        // are compatible). An army with no GameSystem field is a GDF one (pre-#378 files).
        if (MixedSystemWarning(players.Select(p => p.ArmyListSummary)) is string mixed)
            ImGui.TextColored(new Vector4(0.90f, 0.80f, 0.35f, 1f), mixed);

        // Slots are fixed when resuming a saved game, so no add/remove there.
        if (_viewModel.HasHostPrivileges && !_viewModel.IsResumeMode)
        {
            ImGui.Spacing();
            if (UiButton.Navigate("Add Local Player"))
                _viewModel.AddLocalPlayer();
            ImGui.SameLine();
            // #191 A6: two bot flavors - the Tactician (challenge AI) and the legacy
            // solo-rules bot, rechristened DerpBot (Chris's naming).
            if (UiButton.Navigate("Add Tactician Bot"))
                _viewModel.AddAiPlayer(EAiProfile.Tactician);
            ImGui.SameLine();
            if (UiButton.Navigate("Add DerpBot"))
                _viewModel.AddAiPlayer(EAiProfile.SoloRules);
        }
    }

    /// <summary>#378: the mixed-system lobby note, or null when every assigned army is from one game
    /// system. ImGui-free so the wording and the absent-means-GDF rule are testable.</summary>
    internal static string? MixedSystemWarning(IEnumerable<ArmyListSummary> summaries)
    {
        List<string> systems = summaries.Where(s => s.IsAssigned)
            .Select(s => FDG.ArmyBuilding.GameSystems.Normalize(s.GameSystem))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (systems.Count <= 1) return null;
        return "! Mixed game systems: " + string.Join(" + ", systems.Select(SystemLabel))
            + ". Points and core rules are compatible - launch if that is the plan.";
    }

    private static string SystemLabel(string slug) => slug switch
    {
        FDG.ArmyBuilding.GameSystems.GrimdarkFuture => "Grimdark Future",
        FDG.ArmyBuilding.GameSystems.AgeOfFantasy => "Age of Fantasy",
        _ => slug,
    };

    // #372/#388 ---------------------------------------------------------------------------------------
    // Starter armies. A new slot arrives unplayable either way - a bot carries the hard-coded 100-pt
    // "Test Army" stub (engine-side, in LobbyViewModel_Host.AddAiPlayer) and a human carries no army at
    // all (the host silently substituted the same stub at launch) - so the lobby swaps in a real list
    // from the armies folder as soon as the scan is in. App-side on purpose: the armies FOLDER is an app
    // concept (ArmyPaths), and the engine has no business reading the user's disk.

    private static bool ArmyCatalogReady => SharedArmyCatalog.Value.IsLoaded;

    /// <summary>Why the Random Army button is or isn't available on a given row.</summary>
    private static string RandomArmyTooltip(bool canModify, LobbyPlayerInfoSummary info)
    {
        if (!canModify)
            return $"{info.PlayerName} picks their own army - only they can change it.";
        if (!ArmyCatalogReady)
            return "Reading the armies folder...";
        return "Roll another army from the armies folder, closest to the points limit\n" +
               "first. Skips armies other players are using, never repeats one until\n" +
               "this slot has seen them all, and never picks one over the limit unless\n" +
               "the folder has nothing legal.";
    }

    /// <summary>#388: does this row still need a starter army rolled onto it? Pure, so the rule is
    /// testable away from ImGui.</summary>
    /// <param name="canModify">Whether this machine may write the slot's army at all
    /// (<see cref="ILobbyViewModel.CheckCanModifyPlayerIDInfo"/>): the host owns its own row, its local
    /// humans and the bots, and a client owns only its own row. Nobody rolls for a remote player.</param>
    /// <param name="alreadyServed">The slot has been handed an army - rolled or hand-picked - this
    /// lobby.</param>
    internal static bool NeedsStarterArmy(EPlayerType playerType, bool armyAssigned, bool canModify,
        bool alreadyServed)
    {
        if (!canModify || alreadyServed) return false;

        // A human holding a list keeps it: an army loaded before the folder scan came back, or one that
        // rode in with the slot. A bot has no such state to read - AddAiPlayer stamps every bot with the
        // stub, so a bot row is ALWAYS "assigned" and only alreadyServed can tell a fresh one apart.
        return playerType == EPlayerType.AI || !armyAssigned;
    }

    /// <summary>Hands a starter army to every slot this machine owns and hasn't served yet: on the host
    /// its own row, its local humans and the bots; on a client, its own row (#388 - bots only before).
    /// A remote player's row is never touched, and the whole pass is skipped when resuming, where the
    /// saved armies are the point. No-op until the background scan finishes, and no-op forever if there
    /// is no armies folder to scan.</summary>
    private void AutoArmyNewSlots()
    {
        if (_viewModel!.IsResumeMode) return;
        if (!ArmyCatalogReady) return;

        IReadOnlyList<LobbyPlayerInfoSummary> players = _viewModel.PlayerInfos;

        foreach (LobbyPlayerInfoSummary info in players)
        {
            if (!NeedsStarterArmy(info.PlayerType, info.ArmyListSummary.IsAssigned,
                    _viewModel.CheckCanModifyPlayerIDInfo(info.PlayerID),
                    _autoArmiedSlots.Contains(info.PlayerID.ID)))
            {
                continue;
            }

            AssignRandomArmy(info.PlayerID); //Marks the slot served.
        }

        // A slot that left must not keep its rotation alive. Pruned unconditionally rather than behind a
        // count comparison: a bot leaving as a human joins keeps the count level, and the roster is at
        // most eight rows, so there is nothing to save by guessing.
        var live = players.Select(p => p.PlayerID.ID).ToHashSet();
        foreach (Guid gone in _autoArmiedSlots.Where(id => !live.Contains(id)).ToList())
        {
            _autoArmiedSlots.Remove(gone);
            _botArmyPicker?.Forget(gone);
        }
    }

    /// <summary>Picks and loads the next army for one slot - one being seeded, or any player pressing
    /// Random Army. Silently does nothing when the folder holds no readable armies, or when the chosen
    /// file fails to load, so the slot keeps whatever it had.</summary>
    private void AssignRandomArmy(PlayerID playerID)
    {
        // A rolled army is a deliberate pick, exactly like a hand-loaded one: mark the slot served so
        // AutoArmyNewSlots cannot overwrite the roll on the next frame. It is also what keeps a client
        // from rolling its own row twice while the army it just sent is still in flight to the host.
        _autoArmiedSlots.Add(playerID.ID);

        _botArmyPicker ??= new BotArmyPicker(SharedArmyCatalog.Value.Entries);

        // What everyone ELSE is holding, by the same name|faction|points identity the catalog uses - the
        // roster is the only view that covers remote clients, whose army file path never crosses the wire.
        // Read fresh rather than from the caller's snapshot (#388): the host applies an army update to
        // its roster synchronously, so when one pass seeds several slots at once - the lobby's own row
        // and a bot that was added before the folder scan landed - each pick sees the ones before it and
        // they no longer all land on the same army.
        var inUseByOthers = _viewModel!.PlayerInfos
            .Where(p => p.PlayerID != playerID && p.ArmyListSummary.IsAssigned)
            .Select(p => ArmyCatalogEntry.KeyFor(
                p.ArmyListSummary.ArmyName, p.ArmyListSummary.FactionName, p.ArmyListSummary.PointCost))
            .ToHashSet();

        if (_botArmyPicker.PickNext(playerID.ID, _viewModel.ArmyPoints, inUseByOthers) is not { } pick)
            return;

        if (LoadArmyFile(pick.Path) is { } army) _viewModel.UpdateArmyListFile(playerID, army);
    }

    /// <summary>Reads a .fdgarmy off disk, or null if it can't be read. Deserialized as BuiltArmyFile so a
    /// Forge-built army keeps its embedded book + selections - the #153 launch gate validates them
    /// host-side. A hand-authored army just leaves them null.</summary>
    private static FDG.ArmyBuilding.BuiltArmyFile? LoadArmyFile(string path)
    {
        try
        {
            return File.Exists(path)
                ? JsonSerializer.Deserialize<FDG.ArmyBuilding.BuiltArmyFile>(File.ReadAllText(path), RuleJson.Options)
                : null;
        }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException)
        {
            return null;
        }
    }

    private static readonly Vector4 OverPointsColor  = new(1f, 0.3f, 0.3f, 1f);
    private static readonly Vector4 UnderPointsColor = new(1f, 0.85f, 0.25f, 1f);

    // The Pts cell. Red over the limit (the #153 launch gate flags it), yellow well under it. The rule
    // itself is LobbyPointsStatus.Classify so it can be unit-tested away from ImGui.
    private static void DrawPointsCell(ArmyListSummary summary, int pointsLimit)
    {
        ELobbyPointsStatus status =
            LobbyPointsStatus.Classify(summary.IsAssigned, summary.PointCost, pointsLimit);

        if (status != ELobbyPointsStatus.Ok)
            ImGui.PushStyleColor(ImGuiCol.Text,
                status == ELobbyPointsStatus.Over ? OverPointsColor : UnderPointsColor);
        ImGui.TextUnformatted(summary.PointCost.ToString());
        if (status != ELobbyPointsStatus.Ok) ImGui.PopStyleColor();

        if (status == ELobbyPointsStatus.Under && ImGui.IsItemHovered())
            ImGui.SetTooltip($"{pointsLimit - summary.PointCost} points under the {pointsLimit} limit.");
    }

    // #221: the colour cell - a swatch of the row's effective colour + a dropdown of the 8 palette options,
    // classic-RTS style. Any colour that is currently another row's - explicit pick OR assigned default -
    // is disabled ("(taken)"), so no pick ever changes someone else's colour. Picks write through the view
    // model (host: applies + rebroadcasts; client: requests its own row from the host), so they sync to
    // every machine. Gated by the same modify-permission as Load Army (host: everyone; client: itself).
    private void DrawColorCell(int rowIdx, LobbyPlayerInfoSummary info,
        IReadOnlyList<LobbyPlayerInfoSummary> players, int[] effectiveColor)
    {
        var (curName, curColor) = PlayerColorOptions.Options[effectiveColor[rowIdx]];
        var swatch = new Vector4(curColor.R / 255f, curColor.G / 255f, curColor.B / 255f, 1f);

        float sz = ImGui.GetTextLineHeight();
        ImGui.ColorButton($"##csw{rowIdx}", swatch,
            ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoPicker, new Vector2(sz, sz));
        ImGui.SameLine();

        bool canModify = _viewModel!.CheckCanModifyPlayerIDInfo(info.PlayerID);
        ImGui.BeginDisabled(!canModify);
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.BeginCombo($"##color{rowIdx}", curName))
        {
            for (int j = 0; j < PlayerColorOptions.Count; j++)
            {
                bool takenByOther = PlayerColorOptions.IsTakenByAnother(effectiveColor, rowIdx, j);

                var (name, col) = PlayerColorOptions.Options[j];
                var itemSwatch = new Vector4(col.R / 255f, col.G / 255f, col.B / 255f, 1f);
                ImGui.ColorButton($"##cswi{rowIdx}_{j}", itemSwatch,
                    ImGuiColorEditFlags.NoTooltip | ImGuiColorEditFlags.NoPicker, new Vector2(sz, sz));
                ImGui.SameLine();
                if (ImGui.Selectable(takenByOther ? $"{name} (taken)" : name, j == effectiveColor[rowIdx],
                        takenByOther ? ImGuiSelectableFlags.Disabled : ImGuiSelectableFlags.None))
                    _viewModel.SetPlayerColor(info.PlayerID, j);
            }
            ImGui.EndCombo();
        }
        ImGui.EndDisabled();
    }

    // #255: the team cell - a dropdown of Team 1..Team N, N = player count capped at the highest team
    // the engine defines (#188; as many teams as players, multiple players per team is the point, but
    // launch is blocked while ALL share one team). Offering a team past the cap would have written an
    // undefined ETeamOption that the host then rejected as out of range. The
    // current value renders faithfully even when out of range (a stale team left by a departed player -
    // kept by design). Picks write through the view model (host: applies + rebroadcasts; client:
    // requests its own row from the host), same gate as Load Army / Color. Disabled when resuming -
    // saved games keep their saved teams.
    private void DrawTeamCell(int rowIdx, LobbyPlayerInfoSummary info, int playerCount)
    {
        bool canModify = _viewModel!.CheckCanModifyPlayerIDInfo(info.PlayerID) && !_viewModel.IsResumeMode;
        ImGui.BeginDisabled(!canModify);
        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
        if (ImGui.BeginCombo($"##team{rowIdx}", $"Team {(int)info.TeamNumber}"))
        {
            for (int n = 1; n <= Math.Min(playerCount, TeamOptions.MaxTeamNumber); n++)
            {
                if (ImGui.Selectable($"Team {n}", n == (int)info.TeamNumber))
                    _viewModel.SetPlayerTeam(info.PlayerID, (ETeamOption)n);
            }
            ImGui.EndCombo();
        }
        ImGui.EndDisabled();
    }

    private void DrawChatLog()
    {
        IReadOnlyList<LobbyChatMessage> msgs = _viewModel!.ChatMessages;
        foreach (LobbyChatMessage msg in msgs)
        {
            ImGui.PushTextWrapPos();
            ImGui.TextUnformatted($"[{msg.SendingPlayerName}] {msg.Message}");
            ImGui.PopTextWrapPos();
        }
        if (ImGui.GetScrollY() >= ImGui.GetScrollMaxY())
            ImGui.SetScrollHereY(1f);
    }

    private void DrawSettings(float panelW)
    {
        bool isHost = _viewModel!.HasHostPrivileges;
        // #265: resuming a save launches from the SAVED settings - the setup ones are already spent and
        // the rules ones would change a game in progress (see GameSettings.WithResumeOverridesFrom). The
        // engine ignores edits to them either way; disabling the controls stops the panel implying
        // otherwise. The battlefield is the one exception, drawn live below.
        bool locked = !isHost || _viewModel.IsResumeMode;

        float innerPad = 8f;
        ImGui.SetCursorPos(new Vector2(innerPad, innerPad));

        // Host-only: what address to give players. Drawn outside the disabled block so the copy buttons work.
        if (isHost)
        {
            DrawHostConnectionInfo(panelW - innerPad * 2);
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
        }

        if (isHost && _viewModel.IsResumeMode)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, HeaderAccent);
            ImGui.TextWrapped("Resuming a save - settings are fixed, except the battlefield.");
            ImGui.PopStyleColor();
        }

        ImGui.BeginDisabled(locked);

        DrawIntField("Army Points",    _viewModel.ArmyPoints,    _viewModel.SetArmyPoints,
            tooltip: "The point budget each player's army is built to. Armies over this limit are\n" +
                     "flagged before launch. Higher points means bigger battles.",
            step: 250);
        DrawEnumCombo("Terrain Mode",  _viewModel.TerrainPlacementMode, _viewModel.SetTerrainPlacementMode,
            debugLast: new[] { ETerrainPlacementMode.AutoFromLayout },
            // #301: explicit order keeps the two Alternating modes adjacent (the enum appends
            // AlternatingPoints after LoadFromFile to keep the wire/save values stable).
            explicitOrder: new[]
            {
                ETerrainPlacementMode.AutoFromLayout,
                ETerrainPlacementMode.Alternating,
                ETerrainPlacementMode.AlternatingPoints,
                ETerrainPlacementMode.LoadFromFile,
            },
            displayName: TerrainModeLabel,
            tooltip: "How terrain is put on the board.\n" +
                     "Alternating: One Per: players take turns placing one piece at a time\n" +
                     "(set the count below).\n" +
                     "Alternating: Points: pieces cost 1-3 points by size; players take turns\n" +
                     "spending a per-turn allowance from a shared total (set both below), so one\n" +
                     "big piece or several small ones per turn.\n" +
                     "Load From File: place a saved layout verbatim.\n" +
                     "Auto From Layout: the server places a built-in default pool (fast, for testing).");

        // Conditional sub-options under Terrain Mode.
        switch (_viewModel.TerrainPlacementMode)
        {
            case ETerrainPlacementMode.Alternating:
                DrawTerrainCountSlider(_viewModel.TerrainCount, _viewModel.SetTerrainCount);
                break;
            case ETerrainPlacementMode.AlternatingPoints:
                DrawTerrainPointsSliders(_viewModel);
                break;
            case ETerrainPlacementMode.LoadFromFile:
                DrawTerrainLayoutPicker(_viewModel.TerrainLayoutPath, _viewModel.SetTerrainLayoutPath);
                break;
        }

        DrawEnumCombo("Objective Mode", _viewModel.ObjectivePlacementMode, _viewModel.SetObjectivePlacementMode,
            debugLast: new[] { EObjectivePlacementMode.AutoPlaced },
            displayName: ObjectiveModeLabel,
            tooltip: "Where the objective markers go. Objectives decide the winner.\n" +
                     "Auto-Placed: the server places them in balanced spots, no input needed.\n" +
                     "Player-Placed: players take turns placing markers.");

        DrawEnumCombo("Randomness",    _viewModel.RandomnessType, _viewModel.SetRandomnessType,
            tooltip: "How dice are resolved.\n" +
                     "Realistic: real dice are rolled - whole hits, swingy and unpredictable.\n" +
                     "Probabilistic: results use expected values - smoother, less luck.");
        DrawEnumCombo("Shooting",      _viewModel.ShootingMode,   _viewModel.SetShootingMode,
            displayName: ShootingModeLabel,
            tooltip: "When a shooting unit commits to its targets.\n" +
                     "One At A Time: fire a weapon, see what it killed, then aim the next one.\n" +
                     "Declare First: aim every weapon before any dice are rolled. Shots aimed\n" +
                     "at a unit that an earlier weapon wipes out are lost.");
        // Turn Style is deliberately NOT offered (#373): ETurnStyle.BoltAction is declared in the engine's
        // GameSettings but nothing in the state machine reads TurnStyle, so the dropdown advertised a
        // rule that never fired. The setting (and its config/save plumbing) stays put for when the
        // random weighted-activation order is actually built; only the lobby control is gone.

        ImGui.EndDisabled();

        // #265 table surface: cosmetic only, but a synced lobby setting so everyone sees one board.
        // Host-gated only - being cosmetic is exactly why a resumed game may still re-pick it.
        ImGui.BeginDisabled(!isHost);
        DrawEnumCombo("Battlefield",   _viewModel.TableBackground, _viewModel.SetTableBackground,
            displayName: TableBackgrounds.Label,
            tooltip: "The terrain type / table surface (Forest, Desert, Ice, Mars-Like, Urban, Barren).\n" +
                     "Cosmetic - no effect on terrain placement or any rule. Randomized when the lobby opens.");
        ImGui.EndDisabled();

        ImGui.BeginDisabled(locked);

        // #201 cover proximity house rules (default on). Inside the disabled block: host-only.
        bool coverProximity = _viewModel.CoverProximityExceptions;
        if (UiButton.Checkbox("Cover Proximity Rules", ref coverProximity))
            _viewModel.SetCoverProximityExceptions(coverProximity);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("House rule (default on): cover the shooter's muzzle hugs (< 2in to the exit)\n" +
                             "grants nothing unless the target hugs the same piece, and cover shared by\n" +
                             "shooter and target grants nothing when they are closer than 6in.");

        // #384 see-through-allies house rule (default off = official rules).
        bool seeThroughAllies = _viewModel.SeeThroughFriendlyUnits;
        if (UiButton.Checkbox("See-Through Allies", ref seeThroughAllies))
            _viewModel.SetSeeThroughFriendlyUnits(seeThroughAllies);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("House rule (default off): shots see through ALL friendly units.\n" +
                             "Off (official rules): only the shooting unit's own models and the target\n" +
                             "unit are ignored for line of sight - every other unit, friendly or\n" +
                             "enemy, blocks it.");

        // #384 unlimited split fire house rule (default off).
        bool unlimitedSplitFire = _viewModel.UnlimitedSplitFire;
        if (UiButton.Checkbox("Unlimited Split Fire", ref unlimitedSplitFire))
            _viewModel.SetUnlimitedSplitFire(unlimitedSplitFire);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("House rule (default off): a shooting unit may split its fire between any\n" +
                             "number of enemy units. Off: at most 2 distinct units per shoot action.");

        ImGui.EndDisabled();
    }

    // After a label + SameLine, size the upcoming control to exactly the space left in the row, so
    // every settings row ends flush at the panel edge whatever the window size. (The old blanket
    // PushItemWidth(panelW) ignored the label already occupying part of the row, so wide labels
    // pushed their controls off the panel - and off the window at small sizes.)
    private static void FillRowItemWidth() =>
        ImGui.SetNextItemWidth(MathF.Max(60f, ImGui.GetContentRegionAvail().X));

    // Shows the addresses a remote player would type into "Host Address", plus the listen port (QF9). LAN
    // is computed once; the public IP is fetched once in the background and filled in when it returns.
    private void DrawHostConnectionInfo(float contentW)
    {
        if (!_publicFetchStarted)
        {
            _publicFetchStarted = true;
            _lanAddresses = ComputeLanAddresses();
            StartPublicIpFetch();
        }

        ImGui.PushStyleColor(ImGuiCol.Text, HeaderAccent);
        ImGui.TextWrapped("Connection (share with players)");
        ImGui.PopStyleColor();

        DrawCopyableAddress("LAN:    ", _lanAddresses ?? "unavailable", "lanip");
        DrawCopyableAddress("Public: ", _publicAddress, "pubip");
        ImGui.TextUnformatted($"Port:   {ListenPort}");
    }

    // One "label address [Copy]" row with Copy pinned to the panel's right edge; a long address
    // slides under the button instead of pushing it off the panel at narrow window sizes. The full
    // string is copied regardless of how much of it is visible.
    private static void DrawCopyableAddress(string label, string address, string id)
    {
        ImGui.TextUnformatted($"{label}{address}");
        float copyW = ImGui.CalcTextSize("Copy").X + ImGui.GetStyle().FramePadding.X * 2f;
        ImGui.SameLine(MathF.Max(0f, ImGui.GetCursorPosX() + ImGui.GetContentRegionAvail().X - copyW));
        if (UiButton.NavigateSmall($"Copy##{id}")) ImGui.SetClipboardText(address);
    }

    // Up, non-loopback IPv4 unicast addresses - the LAN addresses a same-network player can reach.
    private static string ComputeLanAddresses()
    {
        var addresses = new List<string>();
        try
        {
            foreach (NetworkInterface ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.OperationalStatus != OperationalStatus.Up) continue;
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;

                foreach (UnicastIPAddressInformation ua in ni.GetIPProperties().UnicastAddresses)
                {
                    if (ua.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    if (IPAddress.IsLoopback(ua.Address)) continue;
                    addresses.Add(ua.Address.ToString());
                }
            }
        }
        catch
        {
            // Interface enumeration can throw on some platforms/permissions; fall through to "unavailable".
        }

        return addresses.Count > 0 ? string.Join(", ", addresses.Distinct()) : "unavailable";
    }

    // One-shot best-effort public-IP lookup. async void is deliberate: it's a fire-and-forget UI fetch with
    // its own try/catch, and _publicAddress is volatile so the main thread picks up the result next frame.
    private async void StartPublicIpFetch()
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
            string ip = (await http.GetStringAsync("https://api.ipify.org").ConfigureAwait(false)).Trim();
            _publicAddress = string.IsNullOrWhiteSpace(ip) ? "unavailable" : ip;
        }
        catch
        {
            _publicAddress = "unavailable";
        }
    }

    private void DrawLaunch(float panelW, float panelH)
    {
        bool canLaunch = _viewModel!.HasHostPrivileges;
        ImGui.BeginDisabled(!canLaunch);

        Vector2 avail = ImGui.GetContentRegionAvail();
        // Reserve space for the inline error line below the button when present.
        float errorLineH = _lastLaunchError != null ? ImGui.GetTextLineHeightWithSpacing() + 4f : 0f;
        Vector2 buttonSize = new Vector2(avail.X, MathF.Max(0f, avail.Y - errorLineH));
        bool resume = _viewModel.IsResumeMode;
        if (UiButton.Confirm(resume ? "RESUME" : "LAUNCH", buttonSize))
        {
            // #153 launch gate (decision 9): validation Errors in any loaded army raise a confirm dialog
            // (warn + host override) instead of launching straight away. Resume skips the gate — the
            // armies are already in play.
            IReadOnlyList<string> problems = resume
                ? Array.Empty<string>()
                : _viewModel.ValidateArmiesForLaunch();
            if (problems.Count > 0)
            {
                _launchProblems = problems;
                ImGui.OpenPopup("Launch anyway?");
            }
            else
            {
                DoLaunch(resume);
            }
        }

        DrawLaunchConfirm();

        if (_lastLaunchError != null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.4f, 0.4f, 1f));
            ImGui.TextWrapped(_lastLaunchError);
            ImGui.PopStyleColor();
        }

        ImGui.EndDisabled();
    }

    private void DoLaunch(bool resume)
    {
        string? fail;
        bool started = resume ? _viewModel!.TryResumeGame(out fail) : _viewModel!.TryLaunchGame(out fail);
        _lastLaunchError = started ? null : (fail ?? "Launch failed.");

        if (started) PersistHostSettings();
    }

    // #310: the settings panel as the host left it becomes the default for the next hosted game.
    // Written at the two points a lobby ends - launching and backing out - rather than on every edit,
    // so dragging a slider doesn't write the file once per value. Host-only and never on a resume:
    // a resume lobby's controls are locked to the save's settings, which aren't this player's picks.
    private void PersistHostSettings()
    {
        if (_viewModel == null || !_viewModel.HasHostPrivileges || _viewModel.IsResumeMode) return;

        UserConfig.Current.HostSettings = HostGameSettings.CaptureFrom(_viewModel);
        UserConfig.Save();
    }

    // #153 launch gate: lists each army's hard legality problems; Cancel is the default action, and
    // "Launch anyway" is the explicit house-rules override.
    private void DrawLaunchConfirm()
    {
        bool open = true;
        if (!ImGui.BeginPopupModal("Launch anyway?", ref open, ImGuiWindowFlags.AlwaysAutoResize)) return;

        ImGui.TextUnformatted("Some armies have problems:");
        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.4f, 0.4f, 1f));
        foreach (string problem in _launchProblems)
            ImGui.TextWrapped(problem);
        ImGui.PopStyleColor();
        ImGui.Spacing();

        // Cancel first and focused — the safe default. #240: edge-only Esc (stuck-key safe).
        if (UiButton.Back("Cancel", new Vector2(140f, 0f)) || ImGui.IsKeyPressed(ImGuiKey.Escape, repeat: false))
        {
            ImGui.CloseCurrentPopup();
        }
        ImGui.SetItemDefaultFocus();
        ImGui.SameLine();
        if (UiButton.Confirm("Launch anyway", new Vector2(140f, 0f)))
        {
            ImGui.CloseCurrentPopup();
            DoLaunch(resume: false);
        }

        ImGui.EndPopup();
    }

    private static void DrawTerrainCountSlider(int current, Action<int> setter)
    {
        ImGui.TextUnformatted("Terrain Count");
        ImGui.SameLine();
        FillRowItemWidth();
        int v = current;
        if (ImGui.SliderInt("##TerrainCount", ref v, 0, FDG.Stages.PlaceTerrainStage.MaxAlternatingPieceCount) && v != current)
            setter(v);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("How many terrain pieces the players place, one at a time, alternating.\n" +
                             "More pieces means a denser, more cover-heavy board.");
    }

    private static string ObjectiveModeLabel(EObjectivePlacementMode mode) => mode switch
    {
        EObjectivePlacementMode.AutoPlaced => "Auto-Placed",
        EObjectivePlacementMode.PlayerPlaced => "Player-Placed",
        _ => mode.ToString(),
    };

    // #371. Spelled out rather than left as the enum name, which would read "OneAtATime".
    private static string ShootingModeLabel(EShootingMode mode) => mode switch
    {
        EShootingMode.OneAtATime => "One At A Time",
        EShootingMode.DeclareFirst => "Declare First",
        _ => mode.ToString(),
    };

    private static string TerrainModeLabel(ETerrainPlacementMode mode) => mode switch
    {
        ETerrainPlacementMode.AutoFromLayout => "Auto From Layout",
        ETerrainPlacementMode.Alternating => "Alternating: One Per",
        ETerrainPlacementMode.AlternatingPoints => "Alternating: Points",
        ETerrainPlacementMode.LoadFromFile => "Load From File",
        _ => mode.ToString(),
    };

    // #301 Alternating: Points - the two knobs shown only in that mode.
    private static void DrawTerrainPointsSliders(ILobbyViewModel viewModel)
    {
        ImGui.TextUnformatted("Total Points");
        ImGui.SameLine();
        FillRowItemWidth();
        int total = viewModel.TerrainPointsTotal;
        int t = total;
        if (ImGui.SliderInt("##TerrainPointsTotal", ref t, 0, FDG.Stages.PlaceTerrainStage.MaxPointsTotal) && t != total)
            viewModel.SetTerrainPointsTotal(t);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("The total terrain points the players place between them, dealt out in\n" +
                             "placing order a turn's worth at a time - whoever wins the roll-off gets\n" +
                             "any remainder. 0 skips terrain placement entirely.");

        ImGui.TextUnformatted("Points Per Turn");
        ImGui.SameLine();
        FillRowItemWidth();
        int perTurn = viewModel.TerrainPointsPerTurn;
        int p = perTurn;
        if (ImGui.SliderInt("##TerrainPointsPerTurn", ref p, 1, FDG.Stages.PlaceTerrainStage.MaxPointsPerTurn) && p != perTurn)
            viewModel.SetTerrainPointsPerTurn(p);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("How many terrain points each player spends on their turn - one big piece\n" +
                             "or several small ones. A piece costing more than this can still open a\n" +
                             "turn; the difference comes out of the player's next turn.");
    }

    private static void DrawTerrainLayoutPicker(string? current, Action<string?> setter)
    {
        ImGui.TextUnformatted("Layout File");
        string display = string.IsNullOrEmpty(current) ? "(none selected)" : Path.GetFileName(current);
        ImGui.SameLine();
        if (UiButton.Navigate($"{display}##LayoutPick", new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
        {
            var (canceled, paths) = TinyDialogs.OpenFileDialog("Load Terrain Layout", "", false, TerrainFilter);
            if (!canceled)
            {
                string? path = paths?.FirstOrDefault();
                if (!string.IsNullOrEmpty(path)) setter(path);
            }
        }
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("Pick a saved terrain layout file. Its pieces are placed on the board\n" +
                             "verbatim - no roll-off, no alternating placement.");
    }

    private void SubmitChat()
    {
        string msg = _chatInput.Trim();
        if (!string.IsNullOrEmpty(msg))
            _viewModel!.SendMessage(msg);
        _chatInput = "";
    }

    private void TryLoadArmyForPlayer(PlayerID playerID)
    {
        var (canceled, paths) = TinyDialogs.OpenFileDialog("Load Army", ArmyPaths.DefaultDialogPath, false, ArmyFilter);
        if (canceled) return;

        string path = paths?.FirstOrDefault() ?? "";
        if (LoadArmyFile(path) is not { } loaded) return;

        // #372: a hand-picked army marks the slot as served, so AutoArmyNewSlots never overwrites it on a
        // later frame (it would, for a slot whose army was loaded before the folder scan came back).
        _autoArmiedSlots.Add(playerID.ID);
        _viewModel!.UpdateArmyListFile(playerID, loaded);
    }

    private void HandleLaunch(IFDGGame game)
    {
        var infos = _viewModel?.PlayerInfos ?? [];
        var players = infos.Select(info => (info.PlayerID, info.PlayerName)).ToList();

        // Host-only save hook (work item #054 will add client-initiated saving).
        Func<string?>? saveGame = _viewModel != null && _viewModel.CanSaveGame ? _viewModel.SaveGameToJson : null;

        // Shared with the --scenario direct launch (#167); the wiring lives in GameGuiWiring.
        // #221: colour picks come from the SYNCED roster (ColorIndex rides LobbyPlayerInfoSummary), so
        // host and clients all launch with the same colours; unpicked slots fill with the free defaults.
        var colorPicks = infos.ToDictionary(info => info.PlayerID, info => info.ColorIndex);
        GameGuiWiring.Launch(game, players, saveGame,
            (tableState, colorForPlayer, log, overlay, taskDisplay, presentationPlayer, save, playerMessageUI) =>
                OnGameLaunched?.Invoke(tableState, colorForPlayer, log, overlay, taskDisplay, presentationPlayer, save, playerMessageUI),
            colorChoiceForPlayer: pid => colorPicks.TryGetValue(pid, out int idx) && idx >= 0 ? idx : null,
            // #201: the synced lobby setting (host-set, broadcast to clients) so previews match the engine.
            coverProximityExceptions: _viewModel!.CoverProximityExceptions,
            // #265: likewise synced, so host and clients play on the same-looking board.
            tableBackground: _viewModel!.TableBackground,
            // #384: likewise synced, so every player's fire-line previews match the engine's LoS.
            seeThroughFriendlyUnits: _viewModel!.SeeThroughFriendlyUnits);
    }

    private static void DrawIntField(string label, int current, Action<int> setter, string? tooltip = null,
        int step = 1)
    {
        ImGui.TextUnformatted(label);
        ImGui.SameLine();
        FillRowItemWidth();
        int v = current;
        if (ImGui.InputInt($"##{label}", ref v, step, step * 4) && v != current)
            setter(Math.Max(0, v));
        if (tooltip != null && ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);
    }

    // Draws a labeled enum dropdown. `debugLast` names values that are debug conveniences (e.g. the
    // pre-placed / auto-placed setup shortcuts): in a Debug build they keep their declared position -
    // first, for fast iteration - while in a Release build they move to the end so a normal game doesn't
    // lead with them. `explicitOrder` overrides the raw enum-value order entirely (#301: lets related
    // modes sit together when wire-compat forced a new value to the end of the enum); debugLast still
    // applies on top of it. `displayName` supplies a friendly label per value (falls back to the raw
    // enum name). This is presentation only: the enum, saves, and network wire are identical across
    // build types (#243).
    private static void DrawEnumCombo<TEnum>(string label, TEnum current, Action<TEnum> setter,
        TEnum[]? debugLast = null, Func<TEnum, string>? displayName = null, string? tooltip = null,
        TEnum[]? explicitOrder = null)
        where TEnum : struct, Enum
    {
        ImGui.TextUnformatted(label);
        ImGui.SameLine();
        FillRowItemWidth();

        TEnum[] values = OrderComboValues(debugLast, explicitOrder);
        string[] labels = values.Select(v => displayName?.Invoke(v) ?? v.ToString()).ToArray();
        int idx = Math.Max(0, Array.IndexOf(values, current));
        if (ImGui.Combo($"##{label}", ref idx, labels, labels.Length))
            setter(values[idx]);
        if (tooltip != null && ImGui.IsItemHovered())
            ImGui.SetTooltip(tooltip);
    }

    private static TEnum[] OrderComboValues<TEnum>(TEnum[]? debugLast, TEnum[]? explicitOrder = null)
        where TEnum : struct, Enum
    {
        TEnum[] all = explicitOrder ?? Enum.GetValues<TEnum>();
#if DEBUG
        return all;
#else
        if (debugLast is null || debugLast.Length == 0)
            return all;
        var debug = new HashSet<TEnum>(debugLast);
        return all.Where(v => !debug.Contains(v)).Concat(all.Where(v => debug.Contains(v))).ToArray();
#endif
    }
}
