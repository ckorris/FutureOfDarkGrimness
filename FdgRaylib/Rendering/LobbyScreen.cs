using System.Numerics;
using FDG;
using FDG.EngineInterface;
using FDG.Network.Connection.Lobby;
using FDG.Network.Messages;
using FDG.Players;
using FDG.SaveLoad;
using FdgRaylib.Cli;
using FdgRaylib.Rendering.Resolvers;
using ImGuiNET;
using Newtonsoft.Json;
using Raylib_cs;
using TinyDialogsNet;

namespace FdgRaylib.Rendering;

public class LobbyScreen : IAppScreen
{
    public Action? OnBack;
    public Action<ITableState, Func<PlayerID, Color>, GameLog?, GuiResolverOverlay, GuiOutstandingTaskDisplay>? OnGameLaunched;

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

    private static readonly JsonSerializerSettings JsonSettings = new()
    {
        TypeNameHandling = TypeNameHandling.Auto,
    };

    private static readonly Color[] PlayerPalette =
        { Color.Blue, Color.Red, Color.Green, Color.Yellow };

    public void SetViewModel(ILobbyViewModel viewModel)
    {
        _viewModel?.Dispose();
        _viewModel = viewModel;
        _chatInput = "";
        _pendingGame = null;
        viewModel.OnLaunched += game => _pendingGame = game;
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

        // Scale UI elements up for the lobby — default ImGui font is on the small side at
        // these screen sizes. 1.2x roughly = "two sizes bigger" and grows buttons by the
        // same factor (since button height = font + frame padding).
        var io = ImGui.GetIO();
        float originalFontScale = io.FontGlobalScale;
        io.FontGlobalScale = originalFontScale * 1.2f;

        try
        {
            DrawScaled(screenW, screenH);
        }
        finally
        {
            io.FontGlobalScale = originalFontScale;
        }
    }

    private void DrawScaled(int screenW, int screenH)
    {
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("Lobby",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar);

        float margin       = 10f;
        float settingsW    = screenW * 0.25f;
        float mainW        = screenW - settingsW - margin * 3;
        float fontSize     = ImGui.GetFontSize();
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
        ImGui.BeginChild("##header", new Vector2(mainW, headerH), ImGuiChildFlags.Borders);

        float headerFontH  = headerH * 0.65f;
        float headerScale  = headerFontH / fontSize;
        ImGui.SetWindowFontScale(headerScale);
        ImGui.SetCursorPosY((headerH - headerFontH) * 0.5f);
        ImGui.TextUnformatted(_viewModel.ServerName);
        ImGui.SetWindowFontScale(1f);

        float backW = 80f;
        float backH = headerH - 8f;
        ImGui.SameLine();
        ImGui.SetCursorPos(new Vector2(mainW - backW - 4f, 4f));
        if (ImGui.Button("Back", new Vector2(backW, backH)))
            OnBack?.Invoke();

        ImGui.EndChild();

        // ── Player List ───────────────────────────────────────────────────────
        float playerListY = margin + headerH + margin;
        ImGui.SetCursorPos(new Vector2(margin, playerListY));
        ImGui.BeginChild("##players", new Vector2(mainW, playerListH), ImGuiChildFlags.Borders);
        ImGui.SetWindowFontScale(1.5f);  // rows + font ~50% bigger than the rest of the lobby
        DrawPlayerList(mainW);
        ImGui.EndChild();

        // ── Chat Log ──────────────────────────────────────────────────────────
        float chatY = playerListY + playerListH + margin;
        ImGui.SetCursorPos(new Vector2(margin, chatY));
        ImGui.BeginChild("##chatlog", new Vector2(mainW, chatH), ImGuiChildFlags.Borders,
            ImGuiWindowFlags.HorizontalScrollbar);
        ImGui.SetWindowFontScale(1.5f);  // match the player + settings panels
        DrawChatLog();
        ImGui.EndChild();

        // ── Chat Input + Send ─────────────────────────────────────────────────
        // Wrapped in a child so SetWindowFontScale applies locally — matches the chat log + launch button.
        float chatInputY = screenH - margin - chatInputH;
        float sendBtnW   = 60f;
        ImGui.SetCursorPos(new Vector2(margin, chatInputY));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.BeginChild("##chatrow", new Vector2(mainW, chatInputH), ImGuiChildFlags.None,
            ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleVar();
        ImGui.SetWindowFontScale(1.5f);

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
        if (ImGui.Button("Send", new Vector2(sendBtnW, chatInputH)) &&
            !string.IsNullOrWhiteSpace(_chatInput))
            SubmitChat();

        ImGui.EndChild();

        // ── Settings + Launch (right panel) ───────────────────────────────────
        float rightX = margin * 2 + mainW;
        ImGui.SetCursorPos(new Vector2(rightX, margin));
        ImGui.BeginChild("##settings", new Vector2(settingsW, rightH - chatInputH - margin),
            ImGuiChildFlags.Borders);
        ImGui.SetWindowFontScale(1.5f);  // settings fields ~50% bigger than the rest of the lobby
        DrawSettings(settingsW);
        ImGui.EndChild();

        ImGui.SetCursorPos(new Vector2(rightX, margin + rightH - chatInputH));
        // No border on the launch child — the button itself fills the panel and provides the visual edge.
        ImGui.BeginChild("##launch", new Vector2(settingsW, chatInputH), ImGuiChildFlags.None);
        ImGui.SetWindowFontScale(1.5f);  // match the chat row + other panels
        DrawLaunch(settingsW, chatInputH);
        ImGui.EndChild();

        ImGui.End();
    }

    private void DrawPlayerList(float panelW)
    {
        IReadOnlyList<LobbyPlayerInfoSummary> players = _viewModel!.PlayerInfos;

        if (ImGui.BeginTable("##ptable", 6,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Name",    ImGuiTableColumnFlags.WidthStretch, 0.18f);
            ImGui.TableSetupColumn("Type",    ImGuiTableColumnFlags.WidthStretch, 0.08f);
            ImGui.TableSetupColumn("Army",    ImGuiTableColumnFlags.WidthStretch, 0.22f);
            ImGui.TableSetupColumn("Faction", ImGuiTableColumnFlags.WidthStretch, 0.18f);
            ImGui.TableSetupColumn("Pts",     ImGuiTableColumnFlags.WidthStretch, 0.08f);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthStretch, 0.26f);
            ImGui.TableHeadersRow();

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
                bool overPoints = info.ArmyListSummary.PointCost > _viewModel.ArmyPoints;
                if (overPoints) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.3f, 0.3f, 1f));
                ImGui.TextUnformatted(info.ArmyListSummary.PointCost.ToString());
                if (overPoints) ImGui.PopStyleColor();

                ImGui.TableNextColumn();
                bool canModify = _viewModel.CheckCanModifyPlayerIDInfo(info.PlayerID);
                ImGui.BeginDisabled(!canModify);
                if (ImGui.SmallButton($"Load Army##{i}"))
                    TryLoadArmyForPlayer(info.PlayerID);
                ImGui.EndDisabled();
            }

            ImGui.EndTable();
        }

        if (_viewModel.HasHostPrivileges)
        {
            ImGui.Spacing();
            if (ImGui.Button("Add Local Player"))
                _viewModel.AddLocalPlayer();
            ImGui.SameLine();
            if (ImGui.Button("Add AI Player"))
                _viewModel.AddAiPlayer();
        }
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
        ImGui.BeginDisabled(!isHost);

        float innerPad = 8f;
        ImGui.SetCursorPos(new Vector2(innerPad, innerPad));
        ImGui.PushItemWidth(panelW - innerPad * 2);

        DrawIntField("Army Points",    _viewModel.ArmyPoints,    _viewModel.SetArmyPoints);
        DrawEnumCombo("Terrain Mode",  _viewModel.TerrainPlacementMode, _viewModel.SetTerrainPlacementMode);

        // Conditional sub-options under Terrain Mode.
        switch (_viewModel.TerrainPlacementMode)
        {
            case ETerrainPlacementMode.Alternating:
                DrawTerrainCountSlider(_viewModel.TerrainCount, _viewModel.SetTerrainCount);
                break;
            case ETerrainPlacementMode.LoadFromFile:
                DrawTerrainLayoutPicker(_viewModel.TerrainLayoutPath, _viewModel.SetTerrainLayoutPath);
                break;
        }

        DrawEnumCombo("Randomness",    _viewModel.RandomnessType, _viewModel.SetRandomnessType);
        DrawEnumCombo("Turn Style",    _viewModel.TurnStyle,      _viewModel.SetTurnStyle);

        ImGui.PopItemWidth();
        ImGui.EndDisabled();
    }

    private void DrawLaunch(float panelW, float panelH)
    {
        bool canLaunch = _viewModel!.HasHostPrivileges;
        ImGui.BeginDisabled(!canLaunch);

        Vector2 avail = ImGui.GetContentRegionAvail();
        // Reserve space for the inline error line below the button when present.
        float errorLineH = _lastLaunchError != null ? ImGui.GetTextLineHeightWithSpacing() + 4f : 0f;
        Vector2 buttonSize = new Vector2(avail.X, MathF.Max(0f, avail.Y - errorLineH));
        if (ImGui.Button("LAUNCH", buttonSize))
        {
            if (!_viewModel.TryLaunchGame(out string? fail))
                _lastLaunchError = fail ?? "Launch failed.";
            else
                _lastLaunchError = null;
        }

        if (_lastLaunchError != null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.4f, 0.4f, 1f));
            ImGui.TextWrapped(_lastLaunchError);
            ImGui.PopStyleColor();
        }

        ImGui.EndDisabled();
    }

    private static void DrawTerrainCountSlider(int current, Action<int> setter)
    {
        ImGui.TextUnformatted("Terrain Count");
        ImGui.SameLine();
        int v = current;
        if (ImGui.SliderInt("##TerrainCount", ref v, 1, FDG.Stages.PlaceTerrainStage.MaxAlternatingPieceCount) && v != current)
            setter(v);
    }

    private static void DrawTerrainLayoutPicker(string? current, Action<string?> setter)
    {
        ImGui.TextUnformatted("Layout File");
        string display = string.IsNullOrEmpty(current) ? "(none selected)" : Path.GetFileName(current);
        ImGui.SameLine();
        if (ImGui.Button($"{display}##LayoutPick", new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
        {
            var (canceled, paths) = TinyDialogs.OpenFileDialog("Load Terrain Layout", "", false, TerrainFilter);
            if (!canceled)
            {
                string? path = paths?.FirstOrDefault();
                if (!string.IsNullOrEmpty(path)) setter(path);
            }
        }
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
        var (canceled, paths) = TinyDialogs.OpenFileDialog("Load Army", "", false, ArmyFilter);
        if (canceled) return;

        string path = paths?.FirstOrDefault() ?? "";
        if (!File.Exists(path)) return;

        var loaded = JsonConvert.DeserializeObject<ArmyListFile>(File.ReadAllText(path), JsonSettings);
        if (loaded is null) return;

        _viewModel!.UpdateArmyListFile(playerID, loaded);
    }

    private void HandleLaunch(IFDGGame game)
    {
        var log   = new GameLog();
        var logUI = new GuiLogMessageUI(log);
        var (resolvers, overlay) = ResolverRegistryFactory.BuildGui(game.TableState);

        var taskDisplay = new GuiOutstandingTaskDisplay();
        game.AssignInterfaces(logUI, new CliPlayerMessageUI(), resolvers, new CliTempVisualDrawer(), outstandingTaskDisplay: taskDisplay);

        var colors  = new Dictionary<PlayerID, Color>();
        var players = _viewModel?.PlayerInfos ?? [];
        for (int i = 0; i < players.Count; i++)
            colors[players[i].PlayerID] = PlayerPalette[i % PlayerPalette.Length];

        OnGameLaunched?.Invoke(game.TableState, pid => colors.GetValueOrDefault(pid, Color.White), log, overlay, taskDisplay);
    }

    private static void DrawIntField(string label, int current, Action<int> setter)
    {
        ImGui.TextUnformatted(label);
        ImGui.SameLine();
        int v = current;
        if (ImGui.InputInt($"##{label}", ref v) && v != current)
            setter(Math.Max(0, v));
    }

    private static void DrawEnumCombo<TEnum>(string label, TEnum current, Action<TEnum> setter)
        where TEnum : struct, Enum
    {
        ImGui.TextUnformatted(label);
        ImGui.SameLine();
        string[] names = Enum.GetNames<TEnum>();
        int idx = Math.Max(0, Array.IndexOf(names, current.ToString()));
        if (ImGui.Combo($"##{label}", ref idx, names, names.Length))
            setter((TEnum)Enum.Parse(typeof(TEnum), names[idx]));
    }
}
