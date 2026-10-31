using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// Frame-scoped arbiter for the Escape key while a game is in progress. Escape already means "cancel
/// the current context" in several resolvers/overlays (a yes/no dialog answers No, an armed placement
/// backs out, tactical pins clear), and the raylib exit key is deliberately disabled. The in-game menu
/// must therefore open only when nothing else claimed Escape this frame.
///
/// <para>Each context calls <see cref="TryConsumeEscape"/> when — and only when — it actually has
/// something to cancel. The first caller in draw order wins (innermost context, since inner overlays
/// draw last... see the renderer's call sequence). After every context has run, the renderer calls
/// <see cref="ShouldOpenMenu"/>; it returns true only if Escape went unclaimed. While the menu is open
/// it owns Escape outright, so every other consumer — and a fresh open — is muted.</para>
///
/// <para>Not thread-safe: all calls happen on the render (main) thread within one ImGui frame.</para>
/// </summary>
public static class EscapeRouter
{
    private static bool _consumed;
    private static bool _menuOpen;

    /// <summary>Reset per in-game frame, before any Escape consumer runs.</summary>
    public static void BeginFrame(bool menuOpen)
    {
        _consumed = false;
        _menuOpen = menuOpen;
    }

    /// <summary>
    /// True while the in-game menu is open this frame. Input-suppression gate for the canvas-click and
    /// hotkey paths that aren't naturally blocked by an ImGui window (they read raw mouse/keys).
    /// </summary>
    public static bool MenuOpen => _menuOpen;

    /// <summary>
    /// Claim this frame's Escape press for a context that has something to cancel. Returns true at most
    /// once per frame, and never while the menu is open. Call it only when the context's own cancel
    /// condition holds (dialog visible / placement armed / pins exist), so idle contexts leave Escape
    /// for the menu.
    /// </summary>
    public static bool TryConsumeEscape()
    {
        if (_menuOpen || _consumed) return false;
        if (!ImGui.IsKeyPressed(ImGuiKey.Escape, repeat: false)) return false;
        _consumed = true;
        return true;
    }

    /// <summary>True when Escape was pressed this frame and no context claimed it — open the menu.</summary>
    public static bool ShouldOpenMenu()
    {
        if (_menuOpen || _consumed) return false;
        return ImGui.IsKeyPressed(ImGuiKey.Escape, repeat: false);
    }
}
