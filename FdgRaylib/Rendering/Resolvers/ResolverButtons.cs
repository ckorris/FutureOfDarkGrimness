using System.Numerics;
using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// Shared button styles that give resolver action panels a consistent visual hierarchy:
///   Primary       -- the final/commit action (Done, Fire, Confirm): accented, and also fires on Enter.
///   Destructive   -- an irreversible action (Clear, Restart): red-tinted; callers separate it visually.
///   Deemphasized  -- a low-stakes action (Skip, Stay): dim, so it recedes.
/// Ordinary secondary actions just use ImGui.Button. The "(Enter)" hint is baked into Primary so the
/// keyboard shortcut is discoverable on the button itself.
/// </summary>
internal static class ResolverButtons
{
    private static readonly Vector4 PrimaryBg      = new(0.20f, 0.48f, 0.26f, 1f);
    private static readonly Vector4 PrimaryHovered = new(0.27f, 0.60f, 0.33f, 1f);
    private static readonly Vector4 PrimaryActive  = new(0.16f, 0.40f, 0.22f, 1f);

    private static readonly Vector4 DestructiveBg      = new(0.42f, 0.16f, 0.16f, 1f);
    private static readonly Vector4 DestructiveHovered = new(0.58f, 0.22f, 0.22f, 1f);
    private static readonly Vector4 DestructiveActive  = new(0.34f, 0.12f, 0.12f, 1f);

    /// <summary>
    /// Primary / commit button. Accented; appends an "(Enter)" hint and also returns true when Enter (or
    /// keypad Enter) is pressed -- unless a text field is focused (so typing in chat can't trip it) or the
    /// button is disabled. Pass <paramref name="enabled"/> = false to gray it out and ignore Enter.
    /// </summary>
    public static bool Primary(string label, Vector2 size, bool enabled = true, bool bindEnter = true)
    {
        string shown = bindEnter ? $"{label}  (Enter)" : label;
        ImGui.PushStyleColor(ImGuiCol.Button, PrimaryBg);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, PrimaryHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, PrimaryActive);
        if (!enabled) ImGui.BeginDisabled();
        bool clicked = ImGui.Button(shown, size);
        if (!enabled) ImGui.EndDisabled();
        ImGui.PopStyleColor(3);

        bool enter = enabled && bindEnter && !ImGui.GetIO().WantTextInput
                     && (ImGui.IsKeyPressed(ImGuiKey.Enter) || ImGui.IsKeyPressed(ImGuiKey.KeypadEnter));
        return clicked || enter;
    }

    /// <summary>Red-tinted button for irreversible actions. Callers should set it apart (separator/row).</summary>
    public static bool Destructive(string label, Vector2 size)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, DestructiveBg);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, DestructiveHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, DestructiveActive);
        bool clicked = ImGui.Button(label, size);
        ImGui.PopStyleColor(3);
        return clicked;
    }

    /// <summary>Dim button for low-stakes actions, so they recede next to the primary action.</summary>
    public static bool Deemphasized(string label, Vector2 size)
    {
        Vector4 btn = ImGui.GetStyle().Colors[(int)ImGuiCol.Button];
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(btn.X, btn.Y, btn.Z, btn.W * 0.55f));
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.66f, 0.66f, 0.70f, 1f));
        bool clicked = ImGui.Button(label, size);
        ImGui.PopStyleColor(2);
        return clicked;
    }
}
