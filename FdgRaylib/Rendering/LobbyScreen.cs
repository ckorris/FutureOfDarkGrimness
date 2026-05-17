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

        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("Lobby",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse);

        float margin       = 10f;
        float settingsW    = screenW * 0.25f;
        float mainW        = screenW - settingsW - margin * 3;
        float fontSize     = ImGui.GetFontSize();
        float framePadY    = ImGui.GetStyle().FramePadding.Y;
        float naturalBtnH  = fontSize + framePadY * 2;
        float headerH      = MathF.Max(40f, naturalBtnH + 12f);
        float chatInputH   = MathF.Max(30f, naturalBtnH + 6f);
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
        DrawPlayerList(mainW);
        ImGui.EndChild();

        // ── Chat Log ──────────────────────────────────────────────────────────
        float chatY = playerListY + playerListH + margin;
        ImGui.SetCursorPos(new Vector2(margin, chatY));
        ImGui.BeginChild("##chatlog", new Vector2(mainW, chatH), ImGuiChildFlags.Borders,
            ImGuiWindowFlags.HorizontalScrollbar);
        DrawChatLog();
        ImGui.EndChild();

        // ── Chat Input + Send ─────────────────────────────────────────────────
        float chatInputY = screenH - margin - chatInputH;
        float sendBtnW   = 60f;
        ImGui.SetCursorPos(new Vector2(margin, chatInputY));
        ImGui.SetNextItemWidth(mainW - sendBtnW - margin);
        if (ImGui.InputText("##chatinput", ref _chatInput, 512, ImGuiInputTextFlags.EnterReturnsTrue))
            SubmitChat();

        ImGui.SameLine();
        if (ImGui.Button("Send", new Vector2(sendBtnW, chatInputH)) &&
            !string.IsNullOrWhiteSpace(_chatInput))
            SubmitChat();

        // ── Settings + Launch (right panel) ───────────────────────────────────
        float rightX = margin * 2 + mainW;
        ImGui.SetCursorPos(new Vector2(rightX, margin));
        ImGui.BeginChild("##settings", new Vector2(settingsW, rightH - chatInputH - margin),
            ImGuiChildFlags.Borders);
        DrawSettings(settingsW);
        ImGui.EndChild();

        ImGui.SetCursorPos(new Vector2(rightX, margin + rightH - chatInputH));
        ImGui.BeginChild("##launch", new Vector2(settingsW, chatInputH), ImGuiChildFlags.Borders);
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
        DrawIntField("Terrain Count",  _viewModel.TerrainCount,  _viewModel.SetTerrainCount);
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
        if (ImGui.Button("LAUNCH", avail))
        {
            if (!_viewModel.TryLaunchGame(out string? fail))
                Console.WriteLine($"Launch failed: {fail}");
        }

        ImGui.EndDisabled();
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
