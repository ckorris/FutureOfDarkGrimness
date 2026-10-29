using System.Numerics;
using ImGuiNET;

namespace FdgRaylib.Rendering;

public class MainMenuScreen : IAppScreen
{
    public Action? OnHostClicked;
    public Action? OnClientClicked;
    public Action? OnArmyBuilderClicked;
    public Action? OnArmyForgeClicked;
    public Action? OnCombatCalculatorClicked;
    public Action? OnLoadGameClicked;
    public Action? OnQuitClicked;

    private bool _modalOpen = false;

    // #406: the usage-statistics notice (opens by itself until acknowledged).
    private readonly PrivacyDialog _privacy = new();

    // #406: the same Options panel as the in-game menu, in a modal over the buttons.
    private bool _optionsOpen;

    public void SetModalOpen(bool open) => _modalOpen = open;

    public void Draw(int screenW, int screenH)
    {
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.11f, 0.11f, 0.11f, 1f));
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);

        ImGui.Begin("##MainMenu",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar);

        // The button column shares out the band between the title and the disclaimer, so adding a
        // button shrinks them all a little instead of pushing Quit off the bottom. Capped at the
        // original size (0.08 tall, 0.03 gap), which is what six buttons got.
        const int buttonCount = 7;
        const float columnTop = 0.24f, columnBottom = 0.90f;
        float slot        = screenH * (columnBottom - columnTop) / buttonCount;
        float btnW        = screenW * 0.40f;
        float btnH        = MathF.Min(screenH * 0.08f, slot * 0.75f);
        float gapY        = MathF.Min(screenH * 0.03f, slot - btnH);

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
        float startY  = screenH * columnTop;

        // Navigation buttons click with the neutral Navigate tone; Quit recedes with the Back tone - but
        // every entry keeps the SAME colour: these are seven peers in a list, not an action beside its
        // cancel, so #398's action-class tint is asked to sit this one out.
        void DrawButton(string label, Action? action, int order, bool back = false)
        {
            ImGui.SetCursorPos(new Vector2(centerX, startY + order * (btnH + gapY)));
            ImGui.SetWindowFontScale(btnFontScale);
            bool clicked = back
                ? UiButton.BackInList(label, new Vector2(btnW, btnH))
                : UiButton.Navigate(label, new Vector2(btnW, btnH));
            if (clicked) action?.Invoke();
            ImGui.SetWindowFontScale(1.0f);
        }

        ImGui.BeginDisabled(_modalOpen || _privacy.IsOpen || _optionsOpen);
        DrawButton("Host",         OnHostClicked,        0);
        DrawButton("Client",       OnClientClicked,      1);
        // Army Builder button temporarily hidden - re-enable by uncommenting this line and bumping the
        // order arguments below back up by one (Army Forge -> 3, Combat Calculator -> 4, Load Game -> 5,
        // Options -> 6, Quit -> 7) and buttonCount above to 8.
        // DrawButton("Army Builder", OnArmyBuilderClicked, 2);
        DrawButton("Army Forge",        OnArmyForgeClicked,        2);
        DrawButton("Combat Calculator", OnCombatCalculatorClicked, 3);
        DrawButton("Load Game",         OnLoadGameClicked,         4);
        DrawButton("Options",           () => _optionsOpen = true, 5);
        DrawButton("Quit",              OnQuitClicked,             6, back: true);
        ImGui.EndDisabled();

        if (useMenuFont) ImGui.PopFont();

        // Fan-project disclaimer, small but legible, centered along the bottom edge. Drawn in the body
        // font (menu font already popped) so it stays modest next to the title/buttons.
        const string disclaimer =
            "This game is a fan-made project and is not an official product of OnePageRules.";
        Vector2 discSize = ImGui.CalcTextSize(disclaimer);
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.62f, 0.62f, 0.62f, 1f));
        ImGui.SetCursorPos(new Vector2((screenW - discSize.X) * 0.5f, screenH - discSize.Y - screenH * 0.02f));
        ImGui.TextUnformatted(disclaimer);
        ImGui.PopStyleColor();

        ImGui.End();
        ImGui.PopStyleColor();

        // Drawn outside the menu window so its modal sits over everything (and blocks the menu) on its own.
        if (!_modalOpen) _privacy.Draw(screenW, screenH);
        if (_optionsOpen) DrawOptions(screenW, screenH);
    }

    private void DrawOptions(int screenW, int screenH)
    {
        const string popupId = "Options##mainmenu";
        if (!ImGui.IsPopupOpen(popupId)) ImGui.OpenPopup(popupId);

        ImGui.SetNextWindowSize(new Vector2(MathF.Min(440f, screenW * 0.9f), 0f), ImGuiCond.Always);
        ImGui.SetNextWindowPos(new Vector2(screenW * 0.5f, screenH * 0.5f), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        if (!ImGui.BeginPopupModal(popupId, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
                                            ImGuiWindowFlags.AlwaysAutoResize))
            return;

        OptionsPanel.Draw(UiSound.Audio, inGame: false);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        if (UiButton.Back("Back", new Vector2(ImGui.GetContentRegionAvail().X, 0f))
            || ImGui.IsKeyPressed(ImGuiKey.Escape, repeat: false))
        {
            ViewSettings.Persist();
            _optionsOpen = false;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }
}
