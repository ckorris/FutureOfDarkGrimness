using System.Numerics;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// ImGui.Button / Checkbox wrappers that voice a <see cref="UiSound"/> cue on click, so menu screens get
/// consistent audio feedback without every call site repeating the play call. Each method returns the same
/// bool the underlying ImGui call does; the cue fires only on the frame it returns true.
///
/// <para>Pick the method by ACTION CLASS, not by widget: <see cref="Navigate"/> for neutral menu
/// navigation, <see cref="Confirm"/> for a positive/forward commit (Launch, Connect, Create), <see
/// cref="Back"/> for back/cancel. In-game resolver panels are voiced centrally in
/// <c>ResolverButtons</c> instead.</para>
/// </summary>
internal static class UiButton
{
    public static bool Navigate(string label, Vector2 size)
    {
        bool clicked = ImGui.Button(label, size);
        if (clicked) UiSound.Navigate();
        return clicked;
    }

    public static bool Navigate(string label)
    {
        bool clicked = ImGui.Button(label);
        if (clicked) UiSound.Navigate();
        return clicked;
    }

    public static bool Confirm(string label, Vector2 size)
    {
        bool clicked = ImGui.Button(label, size);
        if (clicked) UiSound.Confirm();
        return clicked;
    }

    public static bool Confirm(string label)
    {
        bool clicked = ImGui.Button(label);
        if (clicked) UiSound.Confirm();
        return clicked;
    }

    public static bool Back(string label, Vector2 size)
    {
        bool clicked = ImGui.Button(label, size);
        if (clicked) UiSound.Back();
        return clicked;
    }

    public static bool Back(string label)
    {
        bool clicked = ImGui.Button(label);
        if (clicked) UiSound.Back();
        return clicked;
    }

    /// <summary>SmallButton voiced with the neutral Navigate tone (roster/copy/pick actions).</summary>
    public static bool NavigateSmall(string label)
    {
        bool clicked = ImGui.SmallButton(label);
        if (clicked) UiSound.Navigate();
        return clicked;
    }

    /// <summary>Checkbox that ticks on flip (either direction).</summary>
    public static bool Checkbox(string label, ref bool value)
    {
        bool changed = ImGui.Checkbox(label, ref value);
        if (changed) UiSound.Toggle();
        return changed;
    }
}
