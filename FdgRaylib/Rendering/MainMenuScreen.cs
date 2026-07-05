using System.Numerics;
using ImGuiNET;

namespace FdgRaylib.Rendering;

public class MainMenuScreen : IAppScreen
{
    public Action? OnHostClicked;
    public Action? OnClientClicked;
    public Action? OnArmyBuilderClicked;
    public Action? OnArmyForgeClicked;
    public Action? OnLoadGameClicked;
    public Action? OnQuitClicked;

    private bool _modalOpen = false;

    public void SetModalOpen(bool open) => _modalOpen = open;

    public void Draw(int screenW, int screenH)
    {
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.11f, 0.11f, 0.11f, 1f));
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);

        ImGui.Begin("##MainMenu",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar);

        float btnW        = screenW * 0.40f;
        float btnH        = screenH * 0.08f;
        float gapY        = screenH * 0.03f;

        // Prefer the large baked menu font and scale DOWN from it (crisp); fall back to stretching the
        // body font if the font atlas didn't load. Factors are chosen to match the previous on-screen sizes.
        bool  useMenuFont = RaylibRenderer.MenuFontPx > 0f;
        if (useMenuFont) ImGui.PushFont(RaylibRenderer.MenuFont);
        float titleScale   = useMenuFont ? (screenH * 0.072f) / RaylibRenderer.MenuFontPx : screenH * 0.004f;
        float btnFontScale = useMenuFont ? (btnH * 0.55f)     / RaylibRenderer.MenuFontPx : btnH / 26f;

        ImGui.SetWindowFontScale(titleScale);
        string title = "FUTURE of DARK GRIMNESS";
        Vector2 titleSize = ImGui.CalcTextSize(title);
        ImGui.SetCursorPos(new Vector2((screenW - titleSize.X) * 0.5f, screenH * 0.10f));
        ImGui.Text(title);
        ImGui.SetWindowFontScale(1.0f);

        float centerX = (screenW - btnW) * 0.5f;
        float startY  = screenH * 0.28f;

        void DrawButton(string label, Action? action, int order)
        {
            ImGui.SetCursorPos(new Vector2(centerX, startY + order * (btnH + gapY)));
            ImGui.SetWindowFontScale(btnFontScale);
            if (ImGui.Button(label, new Vector2(btnW, btnH)))
                action?.Invoke();
            ImGui.SetWindowFontScale(1.0f);
        }

        ImGui.BeginDisabled(_modalOpen);
        DrawButton("Host",         OnHostClicked,        0);
        DrawButton("Client",       OnClientClicked,      1);
        DrawButton("Army Builder", OnArmyBuilderClicked, 2);
        DrawButton("Army Forge",   OnArmyForgeClicked,   3);
        DrawButton("Load Game",    OnLoadGameClicked,    4);
        DrawButton("Quit",         OnQuitClicked,        5);
        ImGui.EndDisabled();

        if (useMenuFont) ImGui.PopFont();

        ImGui.End();
        ImGui.PopStyleColor();
    }
}
