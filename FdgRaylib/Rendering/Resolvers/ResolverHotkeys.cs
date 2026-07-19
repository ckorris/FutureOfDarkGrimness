using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// #248: keyboard hotkeys for resolver option lists. Letter hotkeys go on action-style string menus
/// (Choose Action, pre-attack): the built-in five actions keep pinned left-hand letters so muscle
/// memory survives options graying out; rule-added actions (Disembark, Teleport, custom rule names)
/// draw from a left-hand pool in display order.
///
/// All checks are edge-only (repeat: false, #240's stuck-key rule) and muted while a text field is
/// focused or the in-game Esc menu is open (the menu owns the keyboard, mirroring the canvas-input
/// suppression in #246).
/// </summary>
internal static class ResolverHotkeys
{
    // Pinned letters for the built-in action names (user sign-off, #248). Matched by full option
    // name, so they only ever bind on menus that actually contain these actions.
    private static readonly (string Name, char Key)[] FixedActionKeys =
    {
        (FDG.Stages.ChooseActionStage.MOVEMENT_CHOICE_NAME, 'W'),
        (FDG.Stages.ChooseActionStage.CHARGE_CHOICE_NAME,   'C'),
        (FDG.Stages.ChooseActionStage.SHOOT_CHOICE_NAME,    'S'),
        (FDG.Stages.ChooseActionStage.CAST_CHOICE_NAME,     'A'),
        (FDG.Stages.ChooseActionStage.PASS_CHOICE_NAME,     'X'),
    };

    // Left-hand pool for everything else, assigned in display order. Deliberately excludes the five
    // pinned letters. Options past the pool's end simply get no hotkey (click still works).
    private static readonly char[] Pool = { 'Q', 'E', 'R', 'T', 'D', 'F', 'G', 'Z', 'V', 'B' };

    /// <summary>
    /// Assign a hotkey letter to each option (null = none). Options whose name is one of the five
    /// built-in actions keep their pinned letter; the rest take pool letters in display order.
    /// </summary>
    public static char?[] AssignLetters(IReadOnlyList<string> options)
    {
        var letters = new char?[options.Count];
        int poolNext = 0;
        for (int i = 0; i < options.Count; i++)
        {
            char? pinned = null;
            foreach (var (name, key) in FixedActionKeys)
                if (name == options[i]) { pinned = key; break; }

            if (pinned != null) letters[i] = pinned;
            else if (poolNext < Pool.Length) letters[i] = Pool[poolNext++];
        }
        return letters;
    }

    /// <summary>Edge-only press check for a letter hotkey (A-Z), muted while typing or while the
    /// in-game menu is open.</summary>
    public static bool IsLetterPressed(char letter)
    {
        if (KeysMuted) return false;
        return ImGui.IsKeyPressed(ImGuiKey.A + (letter - 'A'), repeat: false);
    }

    private static bool KeysMuted => ImGui.GetIO().WantTextInput || EscapeRouter.MenuOpen;
}
