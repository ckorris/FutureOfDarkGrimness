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

    // #412: the usage-statistics notice (opens by itself until acknowledged).
    private readonly PrivacyDialog _privacy = new();

    // #412: the same Options panel as the in-game menu, in a modal over the buttons.
    private bool _optionsOpen;

    public void SetModalOpen(bool open) => _modalOpen = open;

    /// <summary>#448: the holographic war table plays behind the menu.</summary>
    public bool ShowsMenuBackground => true;

    /// <summary>Opens the privacy notice or the Options popup without a click, for the offscreen
    /// <c>--menu-preview</c> render (#448). Never called in a normal run.</summary>
    internal void OpenForPreview(string which)
    {
        if (which == "options") _optionsOpen = true;
        else if (which == "privacy") _privacy.ForceOpen();
    }

    public void Draw(int screenW, int screenH)
    {
        // #448: no window background - the war table (MenuBackgroundRenderer) is already on screen
        // underneath; this window only carries the title, the command panel and the disclaimer.
        var layout = MainMenuLayout.Compute(screenW, screenH);
        float s = layout.Scale;
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.Begin("##MainMenu",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoBackground | ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoBringToFrontOnFocus);
        ImGui.PopStyleVar();

        // Prefer the large baked menu font (bold, #448) and scale DOWN from it (crisp); fall back to
        // stretching the body font if the font atlas didn't load.
        bool useMenuFont = RaylibRenderer.MenuFontPx > 0f;
        if (useMenuFont) ImGui.PushFont(RaylibRenderer.MenuBoldFont);
        float bakedPx = useMenuFont ? RaylibRenderer.MenuFontPx : ImGui.GetFontSize();
        void FontPx(float px) => ImGui.SetWindowFontScale(px / bakedPx);

        // Title: both lines the same size, left-aligned over the panel, underlined.
        var dl = ImGui.GetWindowDrawList();
        FontPx(layout.TitlePx);
        ImGui.PushStyleColor(ImGuiCol.Text, HoloUi.TitleText);
        ImGui.SetCursorPos(new Vector2(layout.TitleX, layout.TitleLine1Y));
        ImGui.TextUnformatted("FUTURE of");
        ImGui.SetCursorPos(new Vector2(layout.TitleX, layout.TitleLine2Y));
        ImGui.TextUnformatted("DARK GRIMNESS");
        ImGui.PopStyleColor();
        dl.AddLine(new Vector2(layout.TitleX + 4f * s, layout.UnderlineY), new Vector2(layout.UnderlineRight, layout.UnderlineY),
            ImGui.ColorConvertFloat4ToU32(new Vector4(HoloUi.Cyan.X, HoloUi.Cyan.Y, HoloUi.Cyan.Z, 0.8f)), MathF.Max(1f, 2f * s));

        ScreenRect panel = layout.Panel;
        HoloUi.DrawPanel(dl, new Vector2(panel.X, panel.Y), new Vector2(panel.Right, panel.Bottom), s);

        // Every entry clicks with the neutral Navigate tone except Quit, which recedes with the Back tone.
        // The tiers carry the hierarchy instead of #398's action-class tint: the two ways into a game,
        // then the tools, then housekeeping.
        bool Entry(ScreenRect r, string label, EHoloTier tier, float fontPx, UiButton.EClass sound = UiButton.EClass.Go)
        {
            ImGui.SetCursorPos(new Vector2(r.X, r.Y));
            FontPx(fontPx);
            return HoloUi.Button(label, new Vector2(r.W, r.H), tier, sound, s);
        }

        ImGui.BeginDisabled(_modalOpen || _privacy.IsOpen || _optionsOpen);
        if (Entry(layout.Primary[0], "HOST/SOLO", EHoloTier.Primary, layout.PrimaryFontPx)) OnHostClicked?.Invoke();
        if (Entry(layout.Primary[1], "JOIN", EHoloTier.Primary, layout.PrimaryFontPx)) OnClientClicked?.Invoke();
        // Army Builder stays hidden (OnArmyBuilderClicked is still wired): bringing it back means a
        // fourth Secondary slot in MainMenuLayout and an Entry call here.
        if (Entry(layout.Secondary[0], "Army Forge", EHoloTier.Secondary, layout.SecondaryFontPx)) OnArmyForgeClicked?.Invoke();
        if (Entry(layout.Secondary[1], "Combat Calculator", EHoloTier.Secondary, layout.SecondaryFontPx)) OnCombatCalculatorClicked?.Invoke();
        if (Entry(layout.Secondary[2], "Load Game", EHoloTier.Secondary, layout.SecondaryFontPx)) OnLoadGameClicked?.Invoke();
        if (Entry(layout.Tertiary[0], "Options", EHoloTier.Tertiary, layout.TertiaryFontPx)) _optionsOpen = true;
        if (Entry(layout.Tertiary[1], "Quit", EHoloTier.Tertiary, layout.TertiaryFontPx, UiButton.EClass.Back)) OnQuitClicked?.Invoke();
        ImGui.EndDisabled();

        ImGui.SetWindowFontScale(1.0f);
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

        // Drawn outside the menu window so its modal sits over everything (and blocks the menu) on its own.
        if (!_modalOpen) _privacy.Draw(screenW, screenH);
        if (_optionsOpen) DrawOptions(screenW, screenH);
    }

    private void DrawOptions(int screenW, int screenH)
    {
        const string popupId = "Options##mainmenu";
        if (!ImGui.IsPopupOpen(popupId)) ImGui.OpenPopup(popupId);

        // Sized to the screen rather than the in-game menu's compact column: half the width, height
        // fitted to the content but capped (it scrolls past that), text in the crisp menu font at about
        // 2.6% of the screen height.
        float w = Math.Clamp(screenW * 0.5f, MathF.Min(440f, screenW * 0.9f), screenW * 0.9f);
        ImGui.SetNextWindowSize(new Vector2(w, 0f), ImGuiCond.Always);
        ImGui.SetNextWindowSizeConstraints(new Vector2(w, 0f), new Vector2(w, screenH * 0.9f));
        ImGui.SetNextWindowPos(new Vector2(screenW * 0.5f, screenH * 0.5f), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        bool useMenuFont = RaylibRenderer.MenuFontPx > 0f;
        if (useMenuFont) ImGui.PushFont(RaylibRenderer.MenuFont);
        // No title bar: Begin draws it before the font scale below can apply, so it would come out at
        // the menu font's full baked size. The heading is drawn inside instead. #448: wears the command
        // panel's look (HoloUi) like the menu behind it.
        float holoScale = HoloUi.ScaleFor(screenW, screenH);
        HoloUi.PushModalStyle(holoScale);
        bool open = ImGui.BeginPopupModal(popupId,
            ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoTitleBar);
        HoloUi.PopModalStyle();
        if (!open)
        {
            if (useMenuFont) ImGui.PopFont();
            return;
        }
        HoloUi.DrawWindowBrackets(holoScale);
        float bodyPx = MathF.Max(18f, screenH * 0.026f);
        if (useMenuFont) ImGui.SetWindowFontScale(bodyPx * 1.5f / RaylibRenderer.MenuFontPx);
        float headingW = ImGui.CalcTextSize("Options").X;
        ImGui.SetCursorPosX((ImGui.GetWindowWidth() - headingW) * 0.5f);
        ImGui.TextUnformatted("Options");
        if (useMenuFont) ImGui.SetWindowFontScale(bodyPx / RaylibRenderer.MenuFontPx);

        OptionsPanel.Draw(UiSound.Audio, inGame: false);

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        if (HoloUi.Button("Back", new Vector2(ImGui.GetContentRegionAvail().X, 0f), EHoloTier.Secondary,
                UiButton.EClass.Back, holoScale, centered: true)
            || ImGui.IsKeyPressed(ImGuiKey.Escape, repeat: false))
        {
            ViewSettings.Persist();
            _optionsOpen = false;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
        if (useMenuFont) ImGui.PopFont();
    }
}
