using System.Numerics;
using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// Shared button styles that give resolver action panels a consistent visual hierarchy:
///   Primary       -- the final/commit action (Done, Fire, Confirm): accented, and also fires on Enter.
///   Destructive   -- an irreversible action (Clear, Restart): red-tinted; callers separate it visually.
///   Deemphasized  -- a low-stakes action (Skip, Stay): dim, so it recedes.
/// Ordinary secondary actions just use ImGui.Button. The Confirm-key hint ("(Enter/Space)", #295) is
/// baked into Primary from <see cref="ResolverKeybinds.Confirm"/> so the shortcut is discoverable on
/// the button itself and can never drift from the key that is actually bound.
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
    /// Primary / commit button. Accented; appends the Confirm-key hint and also returns true when the
    /// Confirm key is pressed -- unless a text field is focused (so typing in chat can't trip it) or the
    /// button is disabled. Pass <paramref name="enabled"/> = false to gray it out and ignore the key.
    ///
    /// <para>
    /// #240: key shortcuts on commit/cancel actions must use <c>repeat: false</c>. A key whose release
    /// is missed (focus change mid-press, a sticky key on old hardware) reads as held-down for the rest
    /// of the session, and with repeat enabled ImGui re-fires "pressed" every repeat interval — every
    /// panel with the shortcut then commits itself the moment it appears (a session-long "Move skips
    /// like Pass" bug traced to exactly this). Edge-only detection caps a stuck key at one spurious
    /// fire ever; a deliberate fresh tap still works, and nobody wants key-repeat on a commit button.
    /// </para>
    /// </summary>
    public static bool Primary(string label, Vector2 size, bool enabled = true, bool bindConfirm = true)
    {
        string shown = bindConfirm ? $"{label}  {ResolverKeybinds.Confirm.Parenthetical}" : label;
        ImGui.PushStyleColor(ImGuiCol.Button, PrimaryBg);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, PrimaryHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, PrimaryActive);
        if (!enabled) ImGui.BeginDisabled();
        bool clicked = ImGui.Button(shown, size);
        if (!enabled) ImGui.EndDisabled();
        ImGui.PopStyleColor(3);

        // The binding owns the edge-only rule and the typing / Esc-menu muting (#240, #248).
        bool confirmKey = enabled && bindConfirm && ResolverKeybinds.Confirm.IsPressed();
        // A commit (mouse OR the Confirm shortcut) gets the positive/forward tone.
        if (clicked || confirmKey) UiSound.Confirm();
        return clicked || confirmKey;
    }

    /// <summary>Red-tinted button for irreversible actions. Callers should set it apart (separator/row).</summary>
    public static bool Destructive(string label, Vector2 size)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, DestructiveBg);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, DestructiveHovered);
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, DestructiveActive);
        bool clicked = ImGui.Button(label, size);
        ImGui.PopStyleColor(3);
        // Irreversible action -> the low buzz warn tone.
        if (clicked) UiSound.Warn();
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
        // Low-stakes / recede (Skip, Stay) -> the soft back tone.
        if (clicked) UiSound.Back();
        return clicked;
    }
}
