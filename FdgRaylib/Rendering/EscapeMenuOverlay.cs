using System.Numerics;
using FDG.SaveLoad;
using FdgRaylib.Audio;
using ImGuiNET;
using TinyDialogsNet;

namespace FdgRaylib.Rendering;

/// <summary>
/// The in-game menu, opened with Escape (or the on-screen "Menu" button) while a game is running.
///
/// <para>Deliberately NOT a pause: multiplayer plus the engine running on a background thread mean the
/// game does not stop while this is open — the table stays visible behind a dim and animations keep
/// playing. Gameplay input is suppressed via <see cref="EscapeRouter.MenuOpen"/> checks at the canvas
/// and hotkey sites; the full-screen dim window also blocks clicks from reaching windows behind it.</para>
///
/// <para>Contents: Resume, Save Game (host only), Load Game (host only), Return to Main Menu, Quit to
/// Desktop. Options and the toolbar retirement land in #246 S3.</para>
/// </summary>
public sealed class EscapeMenuOverlay
{
    private enum Confirm { None, ReturnToMenu, Quit, LoadGame }
    private Confirm _confirm = Confirm.None;
    private bool _inOptions;

    public bool IsOpen { get; private set; }

    // Wired by the renderer (each captures ExitGame/NavigateTo/RequestClose/the load flow).
    public Action? OnReturnToMainMenu;
    public Action? OnQuitToDesktop;
    public Action? OnLoadGame;

    // Serializes the live game to a .fdgsave string. Non-null only on the host (a client can't save yet
    // — that's #054); a null func drives the disabled Save/Load rows.
    private Func<string?>? _saveGameToJson;
    public void AttachSave(Func<string?>? saveGameToJson) => _saveGameToJson = saveGameToJson;
    private bool IsHost => _saveGameToJson != null;

    private static readonly FileFilter SaveFilter = new(
        $"Saved Game (*{GameSaveFile.EXTENSION_WITH_PERIOD})",
        new[] { $"*{GameSaveFile.EXTENSION_WITH_PERIOD}" });

    // Options-panel dependencies: the tactical overlay (Threat + field-anchor toggles) and the audio
    // manager (master volume). Wired in TransitionToGame; audio may be null/disabled.
    private TacticalOverlay.TacticalOverlayController? _tactical;
    private AudioManager? _audio;
    public void AttachOptions(TacticalOverlay.TacticalOverlayController tactical, AudioManager? audio)
    {
        _tactical = tactical;
        _audio    = audio;
    }

    public void Open()  { IsOpen = true;  _confirm = Confirm.None; _inOptions = false; }
    public void Close() { IsOpen = false; _confirm = Confirm.None; _inOptions = false; }

    /// <param name="justOpened">
    /// True on the frame the renderer opened the menu from an Escape press. That same press is still
    /// "down" for the rest of the frame (IsKeyPressed is edge-triggered but frame-wide), so the
    /// close-on-Escape check is skipped this frame to avoid opening and closing in one press.
    /// </param>
    public void Draw(int screenW, int screenH, bool justOpened)
    {
        if (!IsOpen) return;

        if (!justOpened && ImGui.IsKeyPressed(ImGuiKey.Escape, repeat: false))
        {
            // Escape steps back one level: out of a confirm, then out of Options, then out of the menu.
            if (_confirm != Confirm.None) _confirm = Confirm.None;
            else if (_inOptions) _inOptions = false;
            else Close();   // fall through so the popup gets its CloseCurrentPopup this frame
        }

        // #248 feedback: a true ImGui modal. The previous hand-rolled dim window carried
        // NoBringToFrontOnFocus, so the console/resolver windows (whichever had been focused after it)
        // stayed ABOVE the dim and the sidebars remained clickable while the menu was open. A modal
        // popup blocks input to every other window by construction and dims the whole viewport.
        if (IsOpen && !ImGui.IsPopupOpen("##escmenu"))
            ImGui.OpenPopup("##escmenu");

        // Options is wider (slider + longer labels) than the plain button column.
        float menuW = _inOptions ? MathF.Min(440f, screenW * 0.9f) : MathF.Min(360f, screenW * 0.8f);
        ImGui.SetNextWindowPos(new Vector2(screenW * 0.5f, screenH * 0.5f), ImGuiCond.Always,
            new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(menuW, 0f), ImGuiCond.Always);
        ImGui.PushStyleColor(ImGuiCol.ModalWindowDimBg, new Vector4(0f, 0f, 0f, 0.55f));

        bool visible = true;   // NoTitleBar hides the implicit close button this ref would add
        if (ImGui.BeginPopupModal("##escmenu", ref visible,
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings))
        {
            if (IsOpen)
            {
                if (_confirm != Confirm.None) DrawConfirm();
                else if (_inOptions) DrawOptions();
                else DrawMainList();
            }

            // Closed this frame (Esc above, Resume, or a confirmed action) — release the modal.
            if (!IsOpen) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }

        ImGui.PopStyleColor();
    }

    private void DrawMainList()
    {
        DrawTitle("Menu");
        ImGui.Spacing();

        if (FullWidthButton("Resume")) Close();

        // Save / Load are host actions. A client sees them disabled with a one-line reason (client-side
        // save is #054); the host resumes/saves the whole game.
        if (IsHost)
        {
            if (FullWidthButton("Save Game")) HandleSaveGame();
            if (FullWidthButton("Load Game")) _confirm = Confirm.LoadGame;
        }
        else
        {
            ImGui.BeginDisabled();
            FullWidthButton("Save Game");
            FullWidthButton("Load Game");
            ImGui.EndDisabled();
            ImGui.TextDisabled("Host controls saving and loading.");
            ImGui.Spacing();
        }

        if (FullWidthButton("Options")) _inOptions = true;
        if (FullWidthButton("Return to Main Menu")) _confirm = Confirm.ReturnToMenu;
        if (FullWidthButton("Quit to Desktop")) _confirm = Confirm.Quit;
    }

    private void DrawOptions()
    {
        DrawTitle("Options");
        ImGui.Spacing();

        SectionHeader("Display");
        ImGui.Checkbox("Unit labels (L)", ref ViewSettings.ShowLabels);
        ImGui.Checkbox("Table grid", ref ViewSettings.ShowGrid);
        ImGui.Checkbox("Show all tokens (T, dev)", ref ViewSettings.ShowAllTokens);

        if (_tactical != null)
        {
            SectionHeader("Tactical overlay");
            ImGui.Checkbox("Weapon reach (V)", ref ViewSettings.ShowReachOverlay);
            ImGui.TextDisabled("Ghosts while moving/placing; any unit you hover.");

            bool threat = _tactical.ThreatToggledOn;
            if (ImGui.Checkbox("Threat frontiers (F)", ref threat))
                _tactical.ToggleThreat();

            bool anchorSelf = TacticalOverlay.TacticalOverlayConfig.GhostAnchoredField;
            if (ImGui.Checkbox("Anchor field on my position", ref anchorSelf))
            {
                TacticalOverlay.TacticalOverlayConfig.GhostAnchoredField = anchorSelf;
                _tactical.InvalidateFieldCache();
            }
            ImGui.TextDisabled("Off = the target's weapon ranges.");
        }

        SectionHeader("Audio");
        if (_audio != null && _audio.Enabled)
        {
            float vol = _audio.MasterVolume;
            if (ImGui.SliderFloat("Master volume", ref vol, 0f, 1f, "%.2f"))
                _audio.SetMasterVolume(vol);
        }
        else
        {
            ImGui.TextDisabled("Audio unavailable.");
        }

        SectionHeader("Controls");
        ImGui.TextDisabled("Alt+drag: measure");
        ImGui.TextDisabled("Alt+wheel: zoom");
        ImGui.TextDisabled("Middle-drag: pan");
        ImGui.TextDisabled("L labels   T tokens   F threat");
        ImGui.TextDisabled("G: cycle formation (during moves)");
        ImGui.TextDisabled("Enter: auto-assign / confirm");
        ImGui.TextDisabled("Backspace: undo / back out");
        ImGui.TextDisabled("Esc: open this menu");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        if (FullWidthButton("Back")) _inOptions = false;
    }

    private void DrawConfirm()
    {
        (string title, string question) = _confirm switch
        {
            Confirm.ReturnToMenu => ("Return to Main Menu",
                "Leave this game and return to the main menu? This ends the game for all players."),
            Confirm.LoadGame => ("Load Game",
                "Load a saved game? This ends the current game for all players, then opens a file to resume."),
            _ => ("Quit to Desktop",
                "Quit to desktop? This ends the game for all players."),
        };

        DrawTitle(title);
        ImGui.Spacing();
        ImGui.PushTextWrapPos(0f);
        ImGui.TextUnformatted(question);
        ImGui.PopTextWrapPos();
        ImGui.Spacing();

        if (FullWidthButton("Yes"))
        {
            Confirm which = _confirm;
            Close();
            switch (which)
            {
                case Confirm.ReturnToMenu: OnReturnToMainMenu?.Invoke(); break;
                case Confirm.LoadGame:     OnLoadGame?.Invoke();        break;
                default:                   OnQuitToDesktop?.Invoke();   break;
            }
            return;
        }
        if (FullWidthButton("Back")) _confirm = Confirm.None;
    }

    // Serialize the game and write it to a player-chosen .fdgsave (native save dialog). No-op if the
    // game can't be serialized or the dialog is canceled. Moved here from the retired table toolbar.
    private void HandleSaveGame()
    {
        string? json = _saveGameToJson?.Invoke();
        if (json == null) return;

        var (canceled, path) = TinyDialogs.SaveFileDialog("Save Game", "", SaveFilter);
        if (canceled || string.IsNullOrWhiteSpace(path)) return;

        if (!path.EndsWith(GameSaveFile.EXTENSION_WITH_PERIOD, StringComparison.OrdinalIgnoreCase))
            path += GameSaveFile.EXTENSION_WITH_PERIOD;

        File.WriteAllText(path, json);
    }

    // A small labelled divider between Options sections (SeparatorText isn't in this ImGui binding).
    private static void SectionHeader(string text)
    {
        ImGui.Spacing();
        ImGui.TextDisabled(text);
        ImGui.Separator();
        ImGui.Spacing();
    }

    private static void DrawTitle(string text)
    {
        ImGui.PushFont(RaylibRenderer.LargeFont);
        float w = ImGui.GetContentRegionAvail().X;
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, (w - ImGui.CalcTextSize(text).X) * 0.5f));
        ImGui.TextUnformatted(text);
        ImGui.PopFont();
    }

    private static bool FullWidthButton(string label)
    {
        bool clicked = ImGui.Button(label, new Vector2(ImGui.GetContentRegionAvail().X, 40f));
        ImGui.Spacing();
        return clicked;
    }
}
