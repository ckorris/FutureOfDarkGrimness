using System.Numerics;
using FDG.Network;
using FDG.Network.Connection;
using FDG.Network.Connection.Lobby;
using FdgRaylib.ListServer;
using ImGuiNET;

namespace FdgRaylib.Rendering;

public class HostModal : IAppScreen
{
    // Second arg: the public-listing heartbeat (#271), or null when the host didn't tick "List
    // publicly" (or no list server is configured). Third arg: the UPnP port mapper (#271), always
    // created for a host (best-effort - being reachable is the point of hosting), never null. The
    // receiver (Program.cs) owns both lifetimes: dispose on lobby close / game end / app exit.
    public Action<ILobbyViewModel, PublicListingService?, NatPortMapper?>? OnCreated;
    public Action? OnCancel;

    private string _yourName      = "Mr. Host";
    private string _serverName    = "The Table";
    private string _password      = "";
    private string _port          = NetworkProtocol.DefaultPort.ToString();
    private bool   _listPublicly  = false;
    private string _error         = "";

    private const float DialogWidthFraction  = 0.30f;
    private const float DialogHeightFraction = 0.52f;

    public void Draw(int screenW, int screenH)
    {
        // Dark translucent backdrop
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0f, 0f, 0f, 0.6f));
        ImGui.Begin("##HostBackdrop",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleColor();

        float dw = screenW * DialogWidthFraction;
        float dh = screenH * DialogHeightFraction;
        float dx = (screenW - dw) * 0.5f;
        float dy = (screenH - dh) * 0.5f;
        float scale = Math.Min(screenW / 1920f, screenH / 1080f);

        ImGui.SetCursorPos(new Vector2(dx, dy));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.20f, 0.27f, 0.45f, 0.97f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f);
        ImGui.BeginChild("##HostDialog", new Vector2(dw, dh), ImGuiChildFlags.Borders,
            ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();

        float pad = 32f * scale;
        ImGui.SetCursorPos(new Vector2(pad, pad));

        // Title
        ImGui.PushFont(RaylibRenderer.LargeFont);
        CenterText("HOST SERVER", dw);
        ImGui.PopFont();

        ImGui.SetCursorPosX(pad);
        DrawLabeledInput("Your Name",   ref _yourName,   dw, scale);
        ImGui.SetCursorPosX(pad);
        DrawLabeledInput("Server Name", ref _serverName, dw, scale);
        ImGui.SetCursorPosX(pad);
        DrawLabeledInput("Password",    ref _password,   dw, scale, ImGuiInputTextFlags.Password);
        ImGui.SetCursorPosX(pad);
        // Listen port (#189). Default 6389; players behind a fixed-port conflict can change it.
        DrawLabeledInput("Port",        ref _port,       dw, scale);

        // Public listing (#271). Only offered when a list server is configured; hosting itself
        // never depends on the registry. Short clickable label (the full sentence was wider than
        // the dialog, so centering pushed the checkbox off the left edge); the warning rides below
        // as a caption.
        if (ListServerConfig.IsConfigured)
        {
            const string label = "List publicly";
            float checkW = ImGui.CalcTextSize(label).X + ImGui.GetFrameHeight() + 8f * scale;
            ImGui.SetCursorPosX(Math.Max(pad, (dw - checkW) * 0.5f));
            ImGui.Checkbox(label, ref _listPublicly);

            ImGui.SetWindowFontScale(0.85f * scale);
            CenterText("(your IP becomes visible to others)", dw);
            ImGui.SetWindowFontScale(1.0f * scale);
            ImGui.Spacing();
        }

        ImGui.SetWindowFontScale(1.0f * scale);
        CenterText(_error, dw);

        float btnW  = dw * 0.38f;
        float btnH  = 50f * scale;
        float gap   = dw * 0.04f;
        float firstX = (dw - btnW * 2 - gap) * 0.5f;

        float btnY = dh - pad - btnH;
        ImGui.SetCursorPos(new Vector2(firstX, btnY));

        if (ImGui.Button("CANCEL", new Vector2(btnW, btnH)))
        {
            Reset();
            OnCancel?.Invoke();
        }

        ImGui.SameLine(0, gap);
        if (ImGui.Button("CREATE", new Vector2(btnW, btnH)))
        {
            if (Validate())
                CreateServer();
        }

        ImGui.EndChild();
        ImGui.End();
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
        float pad = 32f * scale;

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

    private bool Validate()
    {
        if (string.IsNullOrWhiteSpace(_yourName) || string.IsNullOrWhiteSpace(_serverName))
        {
            _error = "Name and server name are required.";
            return false;
        }
        // Ports below 1024 are privileged/reserved; reject them and non-numbers up front (#189).
        if (!int.TryParse(_port.Trim(), out int port) || port < 1024 || port > 65535)
        {
            _error = "Port must be a number from 1024 to 65535.";
            return false;
        }
        _error = "";
        return true;
    }

    private void CreateServer()
    {
        int port = int.Parse(_port.Trim()); // Validated in Validate() before we get here.

        FDGHost host = new FDGHost(port: port);
        _ = host.StartAsync();

        var viewModel = new LobbyViewModel_Host(_yourName, _serverName, _password, host);

        // Best-effort UPnP port forwarding (#271), regardless of public listing: any internet host
        // benefits from an auto-opened port, and it is removed again on teardown. Failure is normal
        // (many routers disable UPnP) and never affects hosting.
        var mapper = new NatPortMapper(port);
        mapper.Start();

        // Start the public-listing heartbeat (#271) alongside the host, advertising the actual
        // listen port (#189). The password itself never leaves this machine - only a has-password
        // flag is advertised.
        PublicListingService? listing = null;
        if (_listPublicly && ListServerConfig.BaseUrl is string baseUrl)
        {
            listing = new PublicListingService(viewModel, baseUrl,
                hasPassword: !string.IsNullOrEmpty(_password), port: port);
        }

        Reset();
        OnCreated?.Invoke(viewModel, listing, mapper);
    }

    private void Reset()
    {
        _yourName     = "Mr. Host";
        _serverName   = "The Table";
        _password     = "";
        _port         = NetworkProtocol.DefaultPort.ToString();
        _listPublicly = false;
        _error        = "";
    }
}
