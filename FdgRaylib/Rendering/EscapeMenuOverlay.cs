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
/// <para>Contents: Resume, Save Game (host only), Load Game (host only), Options, Report a Bug (#226),
/// Return to Main Menu, Quit to Desktop. (The army list deliberately has no
/// row here - the always-visible bottom-left "Army Lists (L)" button beside "Menu (Esc)" is its
/// discoverable home, owner-ruled 2026-08-02.)</para>
/// </summary>
public sealed class EscapeMenuOverlay
{
    private enum Confirm { None, ReturnToMenu, Quit, LoadGame }
    private Confirm _confirm = Confirm.None;
    private bool _inOptions;
    private bool _inBugReport;

    // Bug-report view state (#226). The description survives leaving and re-entering the view
    // within a session (typing it is the expensive part); it clears when a send starts, because
    // from then on the text lives in the written report file.
    private string _bugDescription = "";
    private bool _bugSent;

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

    // The bug reporter (#226), one per launched game like the save hook above; null hides the row.
    private BugReport.BugReporter? _bugReporter;
    public void AttachBugReport(BugReport.BugReporter? bugReporter) => _bugReporter = bugReporter;

    public void Open()  { IsOpen = true;  _confirm = Confirm.None; _inOptions = false; _inBugReport = false; }
    public void Close() { IsOpen = false; _confirm = Confirm.None; _inOptions = false; _inBugReport = false; }

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
            // Escape steps back one level: out of a confirm, then out of a sub-view, then the menu.
            if (_confirm != Confirm.None) _confirm = Confirm.None;
            else if (_inOptions) _inOptions = false;
            else if (_inBugReport) _inBugReport = false;
            else Close();   // fall through so the popup gets its CloseCurrentPopup this frame
        }

        // #248 feedback: a true ImGui modal. The previous hand-rolled dim window carried
        // NoBringToFrontOnFocus, so the console/resolver windows (whichever had been focused after it)
        // stayed ABOVE the dim and the sidebars remained clickable while the menu was open. A modal
        // popup blocks input to every other window by construction and dims the whole viewport.
        if (IsOpen && !ImGui.IsPopupOpen("##escmenu"))
            ImGui.OpenPopup("##escmenu");

        // Options and the bug-report box are wider than the plain button column.
        float menuW = _inOptions || _inBugReport
            ? MathF.Min(440f, screenW * 0.9f) : MathF.Min(360f, screenW * 0.8f);
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
                else if (_inBugReport) DrawBugReport();
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
        if (_bugReporter != null && FullWidthButton("Report a Bug"))
        {
            _inBugReport = true;
            _bugSent = false;
        }
        if (FullWidthButton("Return to Main Menu")) _confirm = Confirm.ReturnToMenu;
        if (FullWidthButton("Quit to Desktop")) _confirm = Confirm.Quit;
    }

    private void DrawOptions()
    {
        DrawTitle("Options");
        ImGui.Spacing();

        SectionHeader("Display");
        ImGui.Checkbox("Unit labels (N)", ref ViewSettings.ShowLabels);
        ImGui.Checkbox("Table grid", ref ViewSettings.ShowGrid);
        ImGui.Checkbox("Show all tokens (T, dev)", ref ViewSettings.ShowAllTokens);
        ImGui.Checkbox("Victory fireworks", ref ViewSettings.ShowVictoryFireworks);

        // #344: how long a settled dice panel stays up. Shown as a multiplier of the tuned default rather
        // than in seconds - the actual lifetime varies with the roll (a panel carrying modifier chips
        // already lingers longer), so a seconds figure would be a lie for most rolls.
        ImGui.SliderFloat("Dice popup time", ref ViewSettings.DiceLingerScale,
            ViewSettings.DiceLingerMin, ViewSettings.DiceLingerMax, "%.2fx");
        ImGui.TextDisabled("1.00x is the default. Hovering the dice always freezes them.");

        if (_tactical != null)
        {
            SectionHeader("Tactical overlay");
            ImGui.Checkbox("Weapon reach (V)", ref ViewSettings.ShowReachOverlay);
            ImGui.TextDisabled("Ghosts while moving/placing; any unit you hover.");

            // #247: "Threat frontiers (F)" and "Anchor field on my position" are both gone. The red
            // frontier layer was outdated once the reach field could answer the same question in one
            // vocabulary, and pinning an enemy is now the gesture that asks for the target-anchored
            // picture -- so there is no second layer to toggle and no mode left to set.
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
        ImGui.TextDisabled("L: army lists (all players)");
        ImGui.TextDisabled("N labels   T tokens   F threat");
        // #326: G toggles Group/Single -- it is Ctrl+wheel that cycles the formation (#277). The old line
        // said "G: cycle formation", which named the wrong key for the wrong action.
        ImGui.TextDisabled("G: group / single mode (during moves)");
        ImGui.TextDisabled("Ctrl+wheel: cycle formation (group moves)");
        ImGui.TextDisabled($"{Resolvers.ResolverHotkeys.CycleHint}: pick model (single moves)");
        ImGui.TextDisabled("Click a model: switch to it (single moves)");
        ImGui.TextDisabled($"{Resolvers.ResolverKeybinds.Confirm.Hint}: auto-assign / confirm");
        ImGui.TextDisabled($"{Resolvers.ResolverKeybinds.Back.Hint}: undo / back out");
        ImGui.TextDisabled("Esc: open this menu");

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        if (FullWidthButton("Back")) _inOptions = false;
    }

    // The bug-report view (#226): a description box, one Send button, and an honest status line.
    // Send always writes a local copy first, then uploads in the background - the menu stays
    // open and usable while the upload runs, and the status line follows it frame by frame.
    private void DrawBugReport()
    {
        DrawTitle("Report a Bug");
        ImGui.Spacing();

        ImGui.PushTextWrapPos(0f);
        ImGui.TextUnformatted("What happened? What did you expect instead?");
        ImGui.PopTextWrapPos();
        ImGui.InputTextMultiline("##bugdesc", ref _bugDescription, 4000,
            new Vector2(ImGui.GetContentRegionAvail().X, 120f));
        ImGui.Spacing();

        ImGui.PushTextWrapPos(0f);
        ImGui.TextDisabled("Sends your notes with the game log, recent crash log, and (as host) " +
            $"a full game save to the developer. A copy is kept in {BugReport.BugReportStore.DirectoryName}/ " +
            "next to the game.");
        ImGui.PopTextWrapPos();
        ImGui.Spacing();

        bool inFlight = _bugReporter!.UploadState == BugReport.BugReporter.EUploadState.InFlight;
        ImGui.BeginDisabled(inFlight || _bugDescription.Trim().Length == 0);
        if (FullWidthButton("Send Report"))
        {
            _bugReporter.Send(_bugDescription.Trim());
            _bugDescription = "";
            _bugSent = true;
        }
        ImGui.EndDisabled();

        if (_bugSent) DrawBugReportStatus();

        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();
        if (FullWidthButton("Back")) _inBugReport = false;
    }

    private void DrawBugReportStatus()
    {
        ImGui.PushTextWrapPos(0f);

        string? localPath = _bugReporter!.LastLocalPath;
        if (localPath != null) ImGui.TextUnformatted($"Saved to {localPath}");
        else ImGui.TextUnformatted("Could not save a local copy.");

        switch (_bugReporter.UploadState)
        {
            case BugReport.BugReporter.EUploadState.InFlight:
                ImGui.TextUnformatted("Uploading...");
                break;
            case BugReport.BugReporter.EUploadState.Succeeded:
                ImGui.TextUnformatted("Report sent. Thank you!");
                break;
            case BugReport.BugReporter.EUploadState.Failed:
                ImGui.TextUnformatted($"Upload failed ({_bugReporter.LastUploadError}). " +
                    "Please send the saved file to the developer.");
                break;
            case BugReport.BugReporter.EUploadState.NotConfigured:
                ImGui.TextUnformatted("No report server configured - please send the saved file " +
                    "to the developer.");
                break;
        }

        ImGui.PopTextWrapPos();
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
        float textW = ImGui.CalcTextSize(text).X;
        // #329 polish: "Return to Main Menu" overran the fixed-width menu at larger UI scales and
        // clipped mid-word. Shrink the title to fit instead of widening the window - the button
        // column stays compact and every confirm title self-fits at any scale.
        float scale = textW > w ? w / textW : 1f;
        if (scale < 1f) ImGui.SetWindowFontScale(scale);
        ImGui.SetCursorPosX(ImGui.GetCursorPosX() + MathF.Max(0f, (w - textW * scale) * 0.5f));
        ImGui.TextUnformatted(text);
        if (scale < 1f) ImGui.SetWindowFontScale(1f);
        ImGui.PopFont();
    }

    private static bool FullWidthButton(string label)
    {
        bool clicked = ImGui.Button(label, new Vector2(ImGui.GetContentRegionAvail().X, 40f));
        ImGui.Spacing();
        return clicked;
    }
}
