using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// Single-claim arbiter for the Escape key.
///
/// Escape means "cancel the innermost thing" - back out of a terrain placement, answer No to a Yes/No
/// prompt - and, when nothing is listening, "quit the app". Raylib's default exit key made Escape do
/// BOTH at once: cancelling a placement also tore the window down. <see cref="RaylibRenderer"/> now
/// clears the exit key and routes every Escape through here.
///
/// The frame's first <see cref="TryConsume"/> caller wins; everyone after it sees false. The quit
/// confirmation asks last (after all screens, overlays, and resolvers have drawn), so it only fires on
/// an Escape that nothing else wanted.
/// </summary>
internal static class EscapeGate
{
    private static bool _consumed;

    /// <summary>
    /// Call once at the top of each frame, before anything can claim the key. Pass
    /// <paramref name="lockedOut"/> when the quit confirmation is already up: the gate starts the frame
    /// pre-claimed so no resolver behind the dimmer can steal the Escape that is meant to dismiss it.
    /// </summary>
    public static void BeginFrame(bool lockedOut = false) => _consumed = lockedOut;

    /// <summary>
    /// True exactly once per frame, for the first caller, when Escape went down this frame and no text
    /// field is capturing keys. Later callers in the same frame get false even if Escape is still down.
    /// </summary>
    public static bool TryConsume()
    {
        if (_consumed) return false;
        if (ImGui.GetIO().WantTextInput) return false;
        if (!ImGui.IsKeyPressed(ImGuiKey.Escape)) return false;

        _consumed = true;
        return true;
    }
}
