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

    /// <summary>The number key pressed this frame mapped to a list index — 1-9 pick options 0-8, 0
    /// picks option 9 (the tenth). Top row and keypad both bind. -1 when none (or list shorter).</summary>
    public static int PressedNumberIndex(int optionCount)
    {
        if (KeysMuted) return -1;
        int n = Math.Min(optionCount, 10);
        for (int i = 0; i < n; i++)
        {
            ImGuiKey digit = i == 9 ? ImGuiKey._0 : ImGuiKey._1 + i;
            ImGuiKey pad   = i == 9 ? ImGuiKey.Keypad0 : ImGuiKey.Keypad1 + i;
            if (ImGui.IsKeyPressed(digit, repeat: false) || ImGui.IsKeyPressed(pad, repeat: false))
                return i;
        }
        return -1;
    }

    /// <summary>Label prefix advertising an option's number key ("[1] ".."[9] ", "[0] " for the
    /// tenth); empty past ten (click/arrows still work).</summary>
    public static string NumberPrefix(int index) =>
        index < 9 ? $"[{index + 1}] " : index == 9 ? "[0] " : "";

    /// <summary>The literal digit pressed this frame (0-9, top row or keypad), or -1 — for panels
    /// where the digit IS the quantity (cast assist: 0 = don't spend, N = spend N tokens), as opposed
    /// to <see cref="PressedNumberIndex"/>'s 1-based list positions.</summary>
    public static int PressedDigit()
    {
        if (KeysMuted) return -1;
        for (int d = 0; d <= 9; d++)
        {
            if (ImGui.IsKeyPressed(ImGuiKey._0 + d, repeat: false)
                || ImGui.IsKeyPressed(ImGuiKey.Keypad0 + d, repeat: false))
                return d;
        }
        return -1;
    }

    /// <summary>Up/Down arrow movement this frame (+1 down, -1 up). Key-repeat is deliberately ON —
    /// holding an arrow walks a long list; #240's edge-only rule is for commit/cancel keys.</summary>
    public static int ArrowDelta()
    {
        if (KeysMuted) return 0;
        int delta = 0;
        if (ImGui.IsKeyPressed(ImGuiKey.DownArrow)) delta++;
        if (ImGui.IsKeyPressed(ImGuiKey.UpArrow)) delta--;
        return delta;
    }

    /// <summary>Left/Right arrow movement this frame (+1 right, -1 left), repeat on — used by
    /// two-pane pickers (shoot: weapons) where vertical arrows own the other list.</summary>
    public static int HorizontalArrowDelta()
    {
        if (KeysMuted) return 0;
        int delta = 0;
        if (ImGui.IsKeyPressed(ImGuiKey.RightArrow)) delta++;
        if (ImGui.IsKeyPressed(ImGuiKey.LeftArrow)) delta--;
        return delta;
    }

    /// <summary>Edge-only commit key — Enter/keypad-Enter/Space as of #295. The keys and the label text
    /// that advertises them both live in <see cref="ResolverKeybinds.Confirm"/>.</summary>
    public static bool IsConfirmPressed() => ResolverKeybinds.Confirm.IsPressed();

    /// <summary>Edge-only Backspace — the universal resolver back/cancel key. Esc deliberately does NOT
    /// cancel resolvers (it opens the in-game menu): mid-move you can reach Options without your Esc
    /// press first undoing the plan (#248 playtest feedback). Cancel key, so repeat: false per #240.</summary>
    public static bool IsBackPressed() => ResolverKeybinds.Back.IsPressed();

    private static bool KeysMuted => ImGui.GetIO().WantTextInput || EscapeRouter.MenuOpen;
}

/// <summary>
/// #248: per-resolver keyboard state for a list of valid options: number keys instant-pick, Up/Down
/// move a highlight (wrapping), the Confirm key commits the highlight. One instance per resolver; call
/// <see cref="Update"/> once per frame from Draw. Resets itself when the request object changes, so
/// a stale highlight never carries into the next decision. Main-thread only (Draw-scoped).
/// </summary>
internal sealed class KeyboardListNav
{
    private object? _trackedRequest;

    /// <summary>Currently highlighted valid-option index, or -1 when the keyboard hasn't navigated.</summary>
    public int Index { get; private set; } = -1;

    /// <summary>Process this frame's keys. Returns the valid-option index to pick NOW (a number key,
    /// or Confirm on the highlight), or -1. <paramref name="moved"/> is true when arrows changed the
    /// highlight this frame — scroll it into view.</summary>
    public int Update(object request, int validCount, out bool moved)
    {
        moved = false;
        if (!ReferenceEquals(request, _trackedRequest)) { _trackedRequest = request; Index = -1; }
        if (validCount <= 0) { Index = -1; return -1; }
        if (Index >= validCount) Index = validCount - 1;

        int number = ResolverHotkeys.PressedNumberIndex(validCount);
        if (number >= 0) return number;

        int delta = ResolverHotkeys.ArrowDelta();
        if (delta != 0)
        {
            Index = Index < 0
                ? (delta > 0 ? 0 : validCount - 1)
                : (Index + delta + validCount) % validCount;
            moved = true;
        }

        if (Index >= 0 && ResolverHotkeys.IsConfirmPressed()) return Index;
        return -1;
    }
}
