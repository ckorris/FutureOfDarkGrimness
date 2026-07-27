using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// #295: one named INTENT ("commit this panel", "back out of it") bound to the physical keys behind it,
/// plus the text that advertises it in the UI. Panels ask for the intent instead of naming keys, so
/// rebinding -- or adding a second key to an existing intent, which is exactly what #295 did when Space
/// was freed up and joined Confirm -- is a one-line edit in <see cref="ResolverKeybinds"/> and every
/// button label, tooltip and hint line follows automatically.
///
/// <para>Panel-local keys (an option list's number keys, a resolver's own R / G / Y) stay where they are:
/// this table is for bindings shared across resolvers, which are the ones that go stale in text.</para>
/// </summary>
internal sealed class ResolverKeybind
{
    private readonly ImGuiKey[] _keys;

    internal ResolverKeybind(string name, string hint, params ImGuiKey[] keys)
    {
        Name  = name;
        Hint  = hint;
        _keys = keys;
    }

    /// <summary>Intent name, for diagnostics.</summary>
    public string Name { get; }

    /// <summary>How the binding reads in UI text: "Enter/Space", "Backspace".</summary>
    public string Hint { get; }

    /// <summary>The hint parenthesized, for appending to a button label: "(Enter/Space)".</summary>
    public string Parenthetical => $"({Hint})";

    /// <summary>The physical keys, in advertised order.</summary>
    public IReadOnlyList<ImGuiKey> Keys => _keys;

    /// <summary>
    /// True on the frame one of the bound keys goes down.
    ///
    /// <para>Edge-only by default (#240): a key whose release is missed -- focus change mid-press, a
    /// sticky key -- reads as held for the rest of the session, and with repeat on ImGui re-fires
    /// "pressed" every repeat interval, so every panel carrying the binding commits itself the moment it
    /// appears. Pass <paramref name="repeat"/> = true only for non-commit intents where holding the key
    /// should keep walking (Back undoing a path point at a time).</para>
    ///
    /// <para>Muted while a text field has focus (typing in chat must never commit a panel) or the in-game
    /// Esc menu is open -- the menu blocks clicks, but manual key checks would otherwise still fire
    /// behind the dim (#248).</para>
    /// </summary>
    public bool IsPressed(bool repeat = false)
    {
        if (ImGui.GetIO().WantTextInput || EscapeRouter.MenuOpen) return false;
        foreach (ImGuiKey key in _keys)
            if (ImGui.IsKeyPressed(key, repeat)) return true;
        return false;
    }
}

/// <summary>
/// The resolver-wide binding table (#295). Two intents today; add here rather than hard-coding a key
/// (and its label text) in a panel.
/// </summary>
internal static class ResolverKeybinds
{
    /// <summary>
    /// Commit the panel: Done, Fire, Cast, Confirm, the highlighted list row. Space joined Enter in #295,
    /// freed by moving single-model switching onto a direct click on the model.
    /// </summary>
    public static readonly ResolverKeybind Confirm =
        new("Confirm", "Enter/Space", ImGuiKey.Enter, ImGuiKey.KeypadEnter, ImGuiKey.Space);

    /// <summary>
    /// Undo the last step / back out of the panel. Esc is deliberately NOT bound: it opens the in-game
    /// menu, so you can reach Options mid-plan without the same press first discarding the plan (#248).
    /// </summary>
    public static readonly ResolverKeybind Back = new("Back", "Backspace", ImGuiKey.Backspace);
}
