using System.Numerics;
using FDG.Network.Connection;
using FDG.Network.Connection.Lobby;
using ImGuiNET;

namespace FdgRaylib.Rendering;

public class HostModal : IAppScreen
{
    public Action<ILobbyViewModel>? OnCreated;
    public Action? OnCancel;

    private string _yourName   = "Mr. Host";
    private string _serverName = "The Table";
    private string _password   = "";
    private string _error      = "";

    private const float DialogWidthFraction  = 0.30f;
    private const float DialogHeightFraction = 0.42f;

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
        _error = "";
        return true;
    }

    private void CreateServer()
    {
        FDGHost host = new FDGHost();
        _ = host.StartAsync();

        var viewModel = new LobbyViewModel_Host(_yourName, _serverName, _password, host);
        viewModel.TerrainLayout = FdgRaylib.Cli.TerrainLoader.BuildTestLayout();
        Reset();
        OnCreated?.Invoke(viewModel);
    }

    private void Reset()
    {
        _yourName   = "Mr. Host";
        _serverName = "The Table";
        _password   = "";
        _error      = "";
    }
}
