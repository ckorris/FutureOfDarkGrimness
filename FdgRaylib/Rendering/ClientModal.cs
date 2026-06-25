using System.Net;
using System.Numerics;
using FDG.Network.Connection;
using FDG.Network.Connection.Lobby;
using ImGuiNET;

namespace FdgRaylib.Rendering;

public class ClientModal : IAppScreen
{
    public Action<ILobbyViewModel>? OnConnected;
    public Action? OnCancel;

    private string _yourName  = "Mrs. Client";
    private string _ipAddress = "127.0.0.1";
    private string _status    = "";
    private bool   _isConnecting = false;

    private const float DialogWidthFraction  = 0.30f;
    private const float DialogHeightFraction = 0.38f;

    // How long to wait for the host's accept/reject handshake before giving up (#075).
    private const double JoinTimeoutSeconds = 8.0;

    public void Draw(int screenW, int screenH)
    {
        // Dark translucent backdrop
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0f, 0f, 0f, 0.6f));
        ImGui.Begin("##ClientBackdrop",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleColor();

        float dw    = screenW * DialogWidthFraction;
        float dh    = screenH * DialogHeightFraction;
        float dx    = (screenW - dw) * 0.5f;
        float dy    = (screenH - dh) * 0.5f;
        float scale = Math.Min(screenW / 1920f, screenH / 1080f);

        ImGui.SetCursorPos(new Vector2(dx, dy));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.20f, 0.27f, 0.45f, 0.97f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f);
        ImGui.BeginChild("##ClientDialog", new Vector2(dw, dh), ImGuiChildFlags.Borders,
            ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();

        float pad = 32f * scale;

        ImGui.PushFont(RaylibRenderer.LargeFont);
        CenterText("CONNECT TO SERVER", dw);
        ImGui.PopFont();

        ImGui.BeginDisabled(_isConnecting);
        ImGui.SetCursorPosX(pad);
        DrawLabeledInput("Your Name",  ref _yourName,  dw, scale);
        ImGui.SetCursorPosX(pad);
        DrawLabeledInput("IP Address", ref _ipAddress, dw, scale, ImGuiInputTextFlags.CharsDecimal);
        ImGui.EndDisabled();

        CenterText(_status, dw);

        float btnW  = dw * 0.38f;
        float btnH  = 50f * scale;
        float gap   = dw * 0.04f;
        float firstX = (dw - btnW * 2 - gap) * 0.5f;
        float btnY  = dh - pad - btnH;
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

        if (!IPAddress.TryParse(_ipAddress, out IPAddress? ip))
        {
            _status = "Invalid IP address format.";
            return;
        }

        _isConnecting = true;
        _status = "Attempting to connect...";

        FDGClient client = new();
        try
        {
            bool connected = await client.ConnectAsync(ip).ConfigureAwait(false);
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
            var viewModel = new LobbyViewModel_Client(_yourName, client);

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

    private void Reset()
    {
        _yourName     = "Mrs. Client";
        _ipAddress    = "127.0.0.1";
        _status       = "";
        _isConnecting = false;
    }
}
