using System.Numerics;
using FDG;
using FDG.Ai;
using FDG.EngineInterface;
using FDG.Network.Connection.Lobby;
using FDG.Network.Messages;
using FDG.Players;
using FDG.SaveLoad;
using FdgRaylib.Cli;
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

    // Host connection info shown so the host knows what address to share (QF9). The port mirrors
    // CommandProtocol.TEMP_PORT (internal to the engine assembly, so it's restated here).
    private const int ListenPort = 6389;
    private string? _lanAddresses;                 // computed once, lazily
    private volatile string _publicAddress = "checking...";
    private bool _publicFetchStarted;

    // Light-blue accent (matches ImGuiTheme) used to make section/column headers read as headers.
    private static readonly Vector4 HeaderAccent = new(0.50f, 0.73f, 1.0f, 1f);

    public void SetViewModel(ILobbyViewModel viewModel)
    {
        _viewModel?.Dispose();
        _viewModel = viewModel;
        _chatInput = "";
        _pendingGame = null;
        viewModel.OnLaunched += game => _pendingGame = game;
        // Fires on the engine thread; the renderer just records it and reacts on the main thread.
        viewModel.OnGameEnded += result => OnGameEnded?.Invoke(result);
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
        // NoScrollbar: the Back button + window padding just overflow headerH, which would otherwise
        // draw a phantom scrollbar right next to Back.
        ImGui.BeginChild("##header", new Vector2(mainW, headerH), ImGuiChildFlags.Borders,
            ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);

        float headerFontH  = headerH * 0.65f;
        float headerScale  = headerFontH / fontSize;
        ImGui.SetWindowFontScale(headerScale);
        ImGui.SetCursorPosY((headerH - headerFontH) * 0.5f);
        ImGui.TextUnformatted(_viewModel.ServerName);
        ImGui.SetWindowFontScale(1f);

        float backW = ImGui.CalcTextSize("Back").X + 36f; // fit the text at the current scale
        float backH = headerH - 8f;
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
        ImGui.SetCursorPos(new Vector2(margin, chatInputY));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, Vector2.Zero);
        ImGui.BeginChild("##chatrow", new Vector2(mainW, chatInputH), ImGuiChildFlags.None,
            ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleVar();
        ImGui.SetWindowFontScale(1.5f);

        // Size the Send button to its text at the row's scaled font, so it doesn't clip to "Ser".
        float sendBtnW = ImGui.CalcTextSize("Send").X + 36f;

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

        if (ImGui.BeginTable("##ptable", 7,
            ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingStretchProp))
        {
            ImGui.TableSetupColumn("Name",    ImGuiTableColumnFlags.WidthStretch, 0.17f);
            ImGui.TableSetupColumn("Type",    ImGuiTableColumnFlags.WidthStretch, 0.07f);
            ImGui.TableSetupColumn("Army",    ImGuiTableColumnFlags.WidthStretch, 0.20f);
            ImGui.TableSetupColumn("Faction", ImGuiTableColumnFlags.WidthStretch, 0.16f);
            ImGui.TableSetupColumn("Pts",     ImGuiTableColumnFlags.WidthStretch, 0.06f);
            ImGui.TableSetupColumn("Color",   ImGuiTableColumnFlags.WidthStretch, 0.13f);
            ImGui.TableSetupColumn("Actions", ImGuiTableColumnFlags.WidthStretch, 0.21f);
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
                bool overPoints = info.ArmyListSummary.PointCost > _viewModel.ArmyPoints;
                if (overPoints) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1f, 0.3f, 0.3f, 1f));
                ImGui.TextUnformatted(info.ArmyListSummary.PointCost.ToString());
                if (overPoints) ImGui.PopStyleColor();

                ImGui.TableNextColumn();
                DrawColorCell(i, info, players, effectiveColor);

                ImGui.TableNextColumn();
                if (_viewModel.IsResumeMode)
                {
                    // Re-crew a saved slot. Host only; Local/bots today (networked client assignment TBD).
                    ImGui.BeginDisabled(!_viewModel.HasHostPrivileges);
                    if (ImGui.SmallButton($"Local##{i}"))
                        _viewModel.SetSavedSlotPlayerType(info.PlayerID, EPlayerType.Local);
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"Tactician##{i}"))
                        _viewModel.SetSavedSlotPlayerType(info.PlayerID, EPlayerType.AI, EAiProfile.Tactician);
                    ImGui.SameLine();
                    if (ImGui.SmallButton($"DerpBot##{i}"))
                        _viewModel.SetSavedSlotPlayerType(info.PlayerID, EPlayerType.AI, EAiProfile.SoloRules);
                    ImGui.EndDisabled();
                }
                else
                {
                    bool canModify = _viewModel.CheckCanModifyPlayerIDInfo(info.PlayerID);
                    ImGui.BeginDisabled(!canModify);
                    if (ImGui.SmallButton($"Load Army##{i}"))
                        TryLoadArmyForPlayer(info.PlayerID);
                    ImGui.EndDisabled();
                }
            }

            ImGui.EndTable();
        }

        ImGui.PopStyleVar(); // CellPadding (taller rows)

        // Slots are fixed when resuming a saved game, so no add/remove there.
        if (_viewModel.HasHostPrivileges && !_viewModel.IsResumeMode)
        {
            ImGui.Spacing();
            if (ImGui.Button("Add Local Player"))
                _viewModel.AddLocalPlayer();
            ImGui.SameLine();
            // #191 A6: two bot flavors - the Tactician (challenge AI) and the legacy
            // solo-rules bot, rechristened DerpBot (Chris's naming).
            if (ImGui.Button("Add Tactician Bot"))
                _viewModel.AddAiPlayer(EAiProfile.Tactician);
            ImGui.SameLine();
            if (ImGui.Button("Add DerpBot"))
                _viewModel.AddAiPlayer(EAiProfile.SoloRules);
        }
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

        ImGui.BeginDisabled(!isHost);
        ImGui.PushItemWidth(panelW - innerPad * 2);

        DrawIntField("Army Points",    _viewModel.ArmyPoints,    _viewModel.SetArmyPoints);
        DrawEnumCombo("Terrain Mode",  _viewModel.TerrainPlacementMode, _viewModel.SetTerrainPlacementMode,
            debugLast: new[] { ETerrainPlacementMode.AutoFromLayout });

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

        DrawEnumCombo("Objective Mode", _viewModel.ObjectivePlacementMode, _viewModel.SetObjectivePlacementMode,
            debugLast: new[] { EObjectivePlacementMode.AutoPlaced },
            displayName: ObjectiveModeLabel);

        DrawEnumCombo("Randomness",    _viewModel.RandomnessType, _viewModel.SetRandomnessType);
        DrawEnumCombo("Turn Style",    _viewModel.TurnStyle,      _viewModel.SetTurnStyle);

        // #201 cover proximity house rules (default on). Inside the disabled block: host-only.
        bool coverProximity = _viewModel.CoverProximityExceptions;
        if (ImGui.Checkbox("Cover Proximity Rules", ref coverProximity))
            _viewModel.SetCoverProximityExceptions(coverProximity);
        if (ImGui.IsItemHovered())
            ImGui.SetTooltip("House rule (default on): cover the shooter's muzzle hugs (< 2in to the exit)\n" +
                             "grants nothing unless the target hugs the same piece, and cover shared by\n" +
                             "shooter and target grants nothing when they are closer than 6in.");

        ImGui.PopItemWidth();
        ImGui.EndDisabled();
    }

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
        ImGui.TextUnformatted("Connection (share with players)");
        ImGui.PopStyleColor();

        string lan = _lanAddresses ?? "unavailable";
        ImGui.TextUnformatted($"LAN:    {lan}");
        ImGui.SameLine();
        if (ImGui.SmallButton("Copy##lanip")) ImGui.SetClipboardText(lan);

        string pub = _publicAddress;
        ImGui.TextUnformatted($"Public: {pub}");
        ImGui.SameLine();
        if (ImGui.SmallButton("Copy##pubip")) ImGui.SetClipboardText(pub);

        ImGui.TextUnformatted($"Port:   {ListenPort}");
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
        if (ImGui.Button(resume ? "RESUME" : "LAUNCH", buttonSize))
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
        if (ImGui.Button("Cancel", new Vector2(140f, 0f)) || ImGui.IsKeyPressed(ImGuiKey.Escape, repeat: false))
        {
            ImGui.CloseCurrentPopup();
        }
        ImGui.SetItemDefaultFocus();
        ImGui.SameLine();
        if (ImGui.Button("Launch anyway", new Vector2(140f, 0f)))
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
        int v = current;
        if (ImGui.SliderInt("##TerrainCount", ref v, 0, FDG.Stages.PlaceTerrainStage.MaxAlternatingPieceCount) && v != current)
            setter(v);
    }

    private static string ObjectiveModeLabel(EObjectivePlacementMode mode) => mode switch
    {
        EObjectivePlacementMode.AutoPlaced => "Auto-Placed",
        EObjectivePlacementMode.PlayerPlaced => "Player-Placed",
        _ => mode.ToString(),
    };

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

        // Deserialize as BuiltArmyFile so a Forge-built army keeps its embedded book + selections — the
        // #153 launch gate validates them host-side. A hand-authored army just leaves them null.
        var loaded = JsonSerializer.Deserialize<FDG.ArmyBuilding.BuiltArmyFile>(File.ReadAllText(path), RuleJson.Options);
        if (loaded is null) return;

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
            colorChoiceForPlayer: pid => colorPicks.TryGetValue(pid, out int idx) && idx >= 0 ? idx : null);
    }

    private static void DrawIntField(string label, int current, Action<int> setter)
    {
        ImGui.TextUnformatted(label);
        ImGui.SameLine();
        int v = current;
        if (ImGui.InputInt($"##{label}", ref v) && v != current)
            setter(Math.Max(0, v));
    }

    // Draws a labeled enum dropdown. `debugLast` names values that are debug conveniences (e.g. the
    // pre-placed / auto-placed setup shortcuts): in a Debug build they keep their declared position -
    // first, for fast iteration - while in a Release build they move to the end so a normal game doesn't
    // lead with them. `displayName` supplies a friendly label per value (falls back to the raw enum name).
    // This is presentation only: the enum, saves, and network wire are identical across build types (#243).
    private static void DrawEnumCombo<TEnum>(string label, TEnum current, Action<TEnum> setter,
        TEnum[]? debugLast = null, Func<TEnum, string>? displayName = null)
        where TEnum : struct, Enum
    {
        ImGui.TextUnformatted(label);
        ImGui.SameLine();

        TEnum[] values = OrderComboValues(debugLast);
        string[] labels = values.Select(v => displayName?.Invoke(v) ?? v.ToString()).ToArray();
        int idx = Math.Max(0, Array.IndexOf(values, current));
        if (ImGui.Combo($"##{label}", ref idx, labels, labels.Length))
            setter(values[idx]);
    }

    private static TEnum[] OrderComboValues<TEnum>(TEnum[]? debugLast) where TEnum : struct, Enum
    {
        TEnum[] all = Enum.GetValues<TEnum>();
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
