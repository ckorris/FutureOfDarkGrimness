using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using FDG.Network;
using FDG.Network.Connection;
using FDG.Network.Connection.Lobby;
using FdgRaylib.ListServer;
using ImGuiNET;

namespace FdgRaylib.Rendering;

public class ClientModal : IAppScreen
{
    public Action<ILobbyViewModel>? OnConnected;
    public Action? OnCancel;

    private string _yourName  = "Mrs. Client";
    private string _ipAddress = "127.0.0.1";
    private string _port      = NetworkProtocol.DefaultPort.ToString();
    private string _password  = "";
    private string _status    = "";
    private bool   _isConnecting = false;

    // ── Server browser state (#271). The browser is the default tab when a list server is
    // configured; direct connect lives on the second tab (and is the whole modal otherwise).
    private readonly object _browseLock = new();
    private IReadOnlyList<ServerListing> _listings = Array.Empty<ServerListing>();
    private string _browseStatus   = "";
    private bool   _isFetching     = false;
    private bool   _browseFetched  = false;
    private ServerListing? _pendingPasswordJoin = null;
    private long   _lastFetchMs    = 0;
    private ListServerClient? _browseClient;

    private const double BrowseAutoRefreshSeconds = 15;

    private const float DialogWidthFraction        = 0.30f;
    private const float DialogHeightFraction       = 0.44f;
    private const float BrowseDialogWidthFraction  = 0.52f;
    private const float BrowseDialogHeightFraction = 0.60f;

    // How long to wait for the host's accept/reject handshake before giving up (#075).
    private const double JoinTimeoutSeconds = 8.0;

    public void Draw(int screenW, int screenH)
    {
        bool browserAvailable = ListServerConfig.IsConfigured;

        // Dark translucent backdrop
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0f, 0f, 0f, 0.6f));
        ImGui.Begin("##ClientBackdrop",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleColor();

        // The browser needs a wider dialog than the three direct-connect fields.
        float widthFraction  = browserAvailable ? BrowseDialogWidthFraction  : DialogWidthFraction;
        float heightFraction = browserAvailable ? BrowseDialogHeightFraction : DialogHeightFraction;

        float dw    = screenW * widthFraction;
        float dh    = screenH * heightFraction;
        float dx    = (screenW - dw) * 0.5f;
        float dy    = (screenH - dh) * 0.5f;
        float scale = Math.Min(screenW / 1920f, screenH / 1080f);

        ImGui.SetCursorPos(new Vector2(dx, dy));
        // Charcoal panel from the app theme (same tone as the lobby's panels) so the dialog reads as the
        // same surface as the rest of the UI, not a bespoke blue slab.
        ImGui.PushStyleColor(ImGuiCol.ChildBg, ImGuiTheme.DialogPanelBg);
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f);
        ImGui.BeginChild("##ClientDialog", new Vector2(dw, dh), ImGuiChildFlags.Borders,
            ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();

        float pad = 32f * scale;

        // Title — light-blue header accent, echoing the lobby's section headers.
        ImGui.PushFont(RaylibRenderer.LargeFont);
        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiTheme.HeaderAccent);
        CenterText(browserAvailable ? "JOIN GAME" : "CONNECT TO SERVER", dw);
        ImGui.PopStyleColor();
        ImGui.PopFont();

        if (browserAvailable)
        {
            if (ImGui.BeginTabBar("##JoinTabs"))
            {
                if (ImGui.BeginTabItem("SERVER BROWSER"))
                {
                    DrawBrowseTab(dw, dh, scale, pad);
                    ImGui.EndTabItem();
                }

                if (ImGui.BeginTabItem("DIRECT CONNECT"))
                {
                    DrawDirectTab(dw, dh, scale, pad);
                    ImGui.EndTabItem();
                }

                ImGui.EndTabBar();
            }
        }
        else
        {
            DrawDirectTab(dw, dh, scale, pad);
        }

        ImGui.EndChild();
        ImGui.End();
    }

    // ── Server browser tab (#271) ──────────────────────────────────────────────────────────

    private void DrawBrowseTab(float dw, float dh, float scale, float pad)
    {
        // First open (or first open after Reset) fetches immediately; after that, refresh on a
        // timer or the button.
        if (!_browseFetched)
        {
            _browseFetched = true;
            _ = RefreshServersAsync();
        }
        else if (!_isFetching &&
                 Environment.TickCount64 - _lastFetchMs > BrowseAutoRefreshSeconds * 1000)
        {
            _ = RefreshServersAsync();
        }

        ImGui.Spacing();
        ImGui.SetCursorPosX(pad);
        ImGui.SetNextItemWidth(dw * 0.35f);
        ImGui.InputText("##BrowseName", ref _yourName, 64);
        ImGui.SameLine();
        ImGui.Text("Your Name");

        IReadOnlyList<ServerListing> listings;
        lock (_browseLock) listings = _listings;

        // Fixed anchors for the bottom rows so the status line never lands under the buttons (the
        // table shrinks to leave room for both).
        float btnH     = 50f * scale;
        float buttonsY = dh - pad - btnH;
        float statusY  = buttonsY - 30f * scale;
        float tableTop = ImGui.GetCursorPosY();
        float tableH   = Math.Max(60f * scale, statusY - tableTop - 8f * scale);
        if (ImGui.BeginTable("##Servers", 6,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY,
                new Vector2(dw - pad * 2f, tableH)))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Server", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("Players", ImGuiTableColumnFlags.WidthFixed, 70f * scale);
            ImGui.TableSetupColumn("Access", ImGuiTableColumnFlags.WidthFixed, 90f * scale);
            ImGui.TableSetupColumn("Build", ImGuiTableColumnFlags.WidthFixed, 90f * scale);
            ImGui.TableSetupColumn("Port", ImGuiTableColumnFlags.WidthFixed, 70f * scale);
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, 70f * scale);
            ImGui.TableHeadersRow();

            foreach (ServerListing listing in listings)
            {
                ImGui.TableNextRow();
                ImGui.PushID(listing.ServerId);

                bool compatible = listing.ProtocolVersion == NetworkProtocol.Version &&
                                  string.Equals(listing.TypeMapHash, NetworkProtocol.LocalTypeMapHash,
                                      StringComparison.OrdinalIgnoreCase);
                bool inGame   = listing.State == "in-game";
                bool joinable = compatible && !inGame && !_isConnecting;

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(inGame ? $"{listing.Name} (in game)" : listing.Name);

                ImGui.TableNextColumn();
                ImGui.TextUnformatted($"{listing.PlayerCount}/{listing.MaxPlayers}");

                ImGui.TableNextColumn();
                ImGui.TextUnformatted(listing.HasPassword ? "Password" : "Open");

                ImGui.TableNextColumn();
                if (compatible) ImGui.TextUnformatted("OK");
                else ImGui.TextColored(new Vector4(1f, 0.6f, 0.3f, 1f), "Mismatch");

                ImGui.TableNextColumn();
                // The registry TCP-probes the host's port at registration; null means it couldn't
                // probe (e.g. local dev), which is unknown rather than bad.
                ImGui.TextUnformatted(listing.Reachable switch
                {
                    true => "Open",
                    false => "Blocked?",
                    null => "?",
                });

                ImGui.TableNextColumn();
                ImGui.BeginDisabled(!joinable);
                if (ImGui.Button("JOIN", new Vector2(-1f, 0f)))
                    JoinListing(listing);
                ImGui.EndDisabled();

                ImGui.PopID();
            }

            ImGui.EndTable();
        }

        string statusLine = _isFetching ? "Refreshing..."
            : _browseStatus.Length > 0 ? _browseStatus
            : listings.Count == 0 ? "No servers are currently listed."
            : _status;
        ImGui.SetCursorPos(new Vector2(0f, statusY));
        CenterText(statusLine, dw);

        float btnW  = dw * 0.24f;
        float gap   = dw * 0.04f;
        float firstX = (dw - btnW * 2 - gap) * 0.5f;
        ImGui.SetCursorPos(new Vector2(firstX, buttonsY));

        if (ImGui.Button("CANCEL", new Vector2(btnW, btnH)))
        {
            Reset();
            OnCancel?.Invoke();
        }

        ImGui.SameLine(0, gap);
        ImGui.BeginDisabled(_isFetching);
        if (ImGui.Button("REFRESH", new Vector2(btnW, btnH)))
            _ = RefreshServersAsync();
        ImGui.EndDisabled();

        DrawPasswordPopup();
    }

    private void JoinListing(ServerListing listing)
    {
        _ipAddress = listing.Host;
        _port = listing.Port.ToString(); // the browser join uses the advertised port (#189)

        if (listing.HasPassword)
        {
            // Ask for the password in place (the popup is opened by DrawPasswordPopup at dialog
            // scope - opening it here, inside the row's PushID, would put it under a different
            // ImGui ID). The password only ever travels to the host in the join greeting - the
            // list server never sees it.
            _pendingPasswordJoin = listing;
            _password = "";
            return;
        }

        _password = "";
        _ = AttemptConnect();
    }

    private void DrawPasswordPopup()
    {
        if (_pendingPasswordJoin == null) return;

        if (!ImGui.IsPopupOpen("##JoinPassword"))
            ImGui.OpenPopup("##JoinPassword");

        if (ImGui.BeginPopupModal("##JoinPassword", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.Text($"Password for {_pendingPasswordJoin.Name}:");
            ImGui.SetNextItemWidth(260f);
            bool submitted = ImGui.InputText("##JoinPasswordInput", ref _password, 64,
                ImGuiInputTextFlags.Password | ImGuiInputTextFlags.EnterReturnsTrue);

            if (ImGui.Button("JOIN", new Vector2(120f, 0f)) || submitted)
            {
                _pendingPasswordJoin = null;
                ImGui.CloseCurrentPopup();
                _ = AttemptConnect();
            }
            ImGui.SameLine();
            if (ImGui.Button("CANCEL", new Vector2(120f, 0f)))
            {
                _pendingPasswordJoin = null;
                _password = "";
                ImGui.CloseCurrentPopup();
            }
            ImGui.EndPopup();
        }
    }

    private async Task RefreshServersAsync()
    {
        if (_isFetching) return;
        string? baseUrl = ListServerConfig.BaseUrl;
        if (baseUrl == null) return;

        _isFetching = true;
        _browseStatus = "";
        try
        {
            _browseClient ??= new ListServerClient(baseUrl);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            IReadOnlyList<ServerListing> fetched = await _browseClient.GetServersAsync(timeout.Token)
                .ConfigureAwait(false);
            lock (_browseLock) _listings = fetched;
        }
        catch (Exception)
        {
            _browseStatus = "Could not reach the server list.";
        }
        finally
        {
            _lastFetchMs = Environment.TickCount64;
            _isFetching = false;
        }
    }

    // ── Direct connect tab (the pre-#271 modal body) ───────────────────────────────────────

    private void DrawDirectTab(float dw, float dh, float scale, float pad)
    {
        ImGui.BeginDisabled(_isConnecting);
        ImGui.SetCursorPosX(pad);
        DrawLabeledInput("Your Name",  ref _yourName,  dw, scale);
        ImGui.SetCursorPosX(pad);
        // "Host Address", not "IP Address", and no CharsDecimal filter - that flag blocked both letters and
        // the colons in an IPv6 literal, so a DNS name (DuckDNS / No-IP for a host on a dynamic IP) or a
        // Tailscale hostname couldn't be typed (QF10).
        DrawLabeledInput("Host Address", ref _ipAddress, dw, scale);
        ImGui.SetCursorPosX(pad);
        // Connect port (#189). Matches the host's listen port; a browser join fills this in from the
        // listing, so a manual entry is only needed for a direct connect to a non-default port.
        DrawLabeledInput("Port", ref _port, dw, scale);
        ImGui.SetCursorPosX(pad);
        DrawLabeledInput("Password", ref _password, dw, scale, ImGuiInputTextFlags.Password);
        ImGui.EndDisabled();

        CenterText(_status, dw);

        float btnW  = dw * 0.24f;
        float btnH  = 50f * scale;
        float gap   = dw * 0.04f;
        float firstX = (dw - btnW * 2 - gap) * 0.5f;
        float btnY  = dh - pad - btnH - (ListServerConfig.IsConfigured ? 40f * scale : 0f);
        ImGui.SetCursorPos(new Vector2(firstX, btnY));

        // CANCEL is always available
        if (ImGui.Button("CANCEL", new Vector2(btnW, btnH)))
        {
            Reset();
            OnCancel?.Invoke();
        }

        ImGui.SameLine(0, gap);
        ImGui.BeginDisabled(_isConnecting);
        if (ImGui.Button("CONNECT", new Vector2(btnW, btnH)))
            _ = AttemptConnect();
        ImGui.EndDisabled();
    }

    private static void CenterText(string txt, float availWidth)
    {
        if (string.IsNullOrEmpty(txt)) return;
        Vector2 size = ImGui.CalcTextSize(txt);
        ImGui.SetCursorPosX((availWidth - size.X) * 0.5f);
        ImGui.Text(txt);
    }

    private static void DrawLabeledInput(string label, ref string buffer, float availWidth,
        float scale, ImGuiInputTextFlags flags = ImGuiInputTextFlags.None)
    {
        ImGui.SetWindowFontScale(1.3f * scale);
        CenterText(label, availWidth);
        ImGui.SetWindowFontScale(1.0f * scale);

        float fieldW = availWidth * 0.75f;
        float fieldX = (availWidth - fieldW) * 0.5f;
        ImGui.SetCursorPosX(fieldX);
        ImGui.SetNextItemWidth(fieldW);
        ImGui.InputText($"##{label}", ref buffer, 64, flags);
        ImGui.Spacing();
    }

    private async Task AttemptConnect()
    {
        if (string.IsNullOrWhiteSpace(_yourName))
        {
            _status = "Player name can't be empty.";
            return;
        }

        if (string.IsNullOrWhiteSpace(_ipAddress))
        {
            _status = "Host address can't be empty.";
            return;
        }

        if (!int.TryParse(_port.Trim(), out int port) || port < 1024 || port > 65535)
        {
            _status = "Port must be a number from 1024 to 65535.";
            return;
        }

        _isConnecting = true;
        _status = "Resolving host...";

        try
        {
            IPAddress? ip = await ResolveHostAsync(_ipAddress).ConfigureAwait(false);
            if (ip == null)
            {
                _status = "Could not find that host address.";
                return;
            }

            _status = "Attempting to connect...";

            FDGClient client = new();
            bool connected = await client.ConnectAsync(ip, port).ConfigureAwait(false);
            if (!connected)
            {
                _status = "Failed to connect.";
                return;
            }

            // Joining the lobby is a two-step handshake (#075): the client greets with its protocol
            // version + store type-map fingerprint, and the host either assigns a PlayerID (accept) or
            // returns a readable rejection (incompatible build). Wait for that outcome here so a rejection
            // surfaces in this modal instead of half-joining the lobby.
            _status = "Joining lobby...";
            var viewModel = new LobbyViewModel_Client(_yourName, client, _password);

            Task<string?> joinTask = viewModel.JoinResultTask;
            Task winner = await Task.WhenAny(joinTask, Task.Delay(TimeSpan.FromSeconds(JoinTimeoutSeconds)))
                .ConfigureAwait(false);

            if (winner != joinTask)
            {
                _status = "Timed out waiting for the server to accept the join.";
                viewModel.Dispose();
                client.Disconnect();
                return;
            }

            string? rejectReason = await joinTask.ConfigureAwait(false);
            if (rejectReason != null)
            {
                _status = rejectReason;
                viewModel.Dispose();
                client.Disconnect();
                return;
            }

            Reset();
            OnConnected?.Invoke(viewModel);
        }
        finally
        {
            _isConnecting = false;
        }
    }

    // Turns whatever the host typed into a connectable address: an IPv4/IPv6 literal passes straight
    // through; anything else is resolved as a DNS name. FDGHost listens on IPAddress.Any (IPv4 only), so a
    // resolved name must yield an IPv4 address to actually connect (QF10).
    private static async Task<IPAddress?> ResolveHostAsync(string hostText)
    {
        string trimmed = hostText.Trim();
        if (string.IsNullOrEmpty(trimmed)) return null;

        if (IPAddress.TryParse(trimmed, out IPAddress? literal))
            return literal;

        try
        {
            IPAddress[] resolved = await Dns.GetHostAddressesAsync(trimmed).ConfigureAwait(false);
            return resolved.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork);
        }
        catch
        {
            return null;
        }
    }

    private void Reset()
    {
        _yourName      = "Mrs. Client";
        _ipAddress     = "127.0.0.1";
        _port          = NetworkProtocol.DefaultPort.ToString();
        _password      = "";
        _status        = "";
        _isConnecting  = false;
        _browseFetched = false;
        _browseStatus  = "";
        _pendingPasswordJoin = null;
        lock (_browseLock) _listings = Array.Empty<ServerListing>();
    }
}
