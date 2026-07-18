using System.Numerics;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// The in-game menu, opened with Escape (or the on-screen "Menu" button) while a game is running.
///
/// <para>Deliberately NOT a pause: multiplayer plus the engine running on a background thread mean the
/// game does not stop while this is open — the table stays visible behind a dim and animations keep
/// playing. Gameplay input is suppressed via <see cref="EscapeRouter.MenuOpen"/> checks at the canvas
/// and hotkey sites; the full-screen dim window also blocks clicks from reaching windows behind it.</para>
///
/// <para>S1 scope: Resume / Return to Main Menu / Quit to Desktop, the latter two behind a confirm.
/// Save / Load / Options rows land in later slices (#246).</para>
/// </summary>
public sealed class EscapeMenuOverlay
{
    private enum Confirm { None, ReturnToMenu, Quit }
    private Confirm _confirm = Confirm.None;

    public bool IsOpen { get; private set; }

    // Wired by the renderer (each captures ExitGame/NavigateTo/RequestClose).
    public Action? OnReturnToMainMenu;
    public Action? OnQuitToDesktop;

    public void Open()  { IsOpen = true;  _confirm = Confirm.None; }
    public void Close() { IsOpen = false; _confirm = Confirm.None; }

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
            // Escape steps back one level: out of a confirm first, then out of the menu entirely.
            if (_confirm != Confirm.None) _confirm = Confirm.None;
            else { Close(); return; }
        }

        DrawDimBlocker(screenW, screenH);

        float menuW = MathF.Min(360f, screenW * 0.8f);
        ImGui.SetNextWindowPos(new Vector2(screenW * 0.5f, screenH * 0.5f), ImGuiCond.Always,
            new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(new Vector2(menuW, 0f), ImGuiCond.Always);
        ImGui.Begin("##escmenu",
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings);

        if (_confirm == Confirm.None) DrawMainList();
        else DrawConfirm();

        ImGui.End();
    }

    // A full-screen window that dims the board and, being drawn just under the menu window, eats mouse
    // input everywhere the menu itself doesn't cover — so clicks can't fall through to the console or a
    // resolver dialog behind it.
    private static void DrawDimBlocker(int screenW, int screenH)
    {
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        // Flat, edge-to-edge: no border or rounding so the dim doesn't frame the screen.
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 0f);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowBorderSize, 0f);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0f, 0f, 0f, 0.55f));
        ImGui.Begin("##escmenu_dim",
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings |
            ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoNavFocus |
            ImGuiWindowFlags.NoScrollbar);
        ImGui.End();
        ImGui.PopStyleColor();
        ImGui.PopStyleVar(2);
    }

    private void DrawMainList()
    {
        DrawTitle("Menu");
        ImGui.Spacing();

        if (FullWidthButton("Resume")) Close();
        if (FullWidthButton("Return to Main Menu")) _confirm = Confirm.ReturnToMenu;
        if (FullWidthButton("Quit to Desktop")) _confirm = Confirm.Quit;
    }

    private void DrawConfirm()
    {
        string question = _confirm == Confirm.ReturnToMenu
            ? "Leave this game and return to the main menu? This ends the game for all players."
            : "Quit to desktop? This ends the game for all players.";

        DrawTitle(_confirm == Confirm.ReturnToMenu ? "Return to Main Menu" : "Quit to Desktop");
        ImGui.Spacing();
        ImGui.PushTextWrapPos(0f);
        ImGui.TextUnformatted(question);
        ImGui.PopTextWrapPos();
        ImGui.Spacing();

        if (FullWidthButton("Yes"))
        {
            Confirm which = _confirm;
            Close();
            if (which == Confirm.ReturnToMenu) OnReturnToMainMenu?.Invoke();
            else OnQuitToDesktop?.Invoke();
            return;
        }
        if (FullWidthButton("Back")) _confirm = Confirm.None;
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
