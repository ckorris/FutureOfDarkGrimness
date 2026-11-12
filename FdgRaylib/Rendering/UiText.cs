using System.Numerics;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// #398: text that is DATA, not a format string.
///
/// <para><c>ImGui.Text</c>, <c>TextColored</c>, <c>TextDisabled</c> and friends take a printf FORMAT
/// string. Hand one a value carrying a '%' - "40.7%", a book that names a rule "50% cover" - and ImGui
/// reads the '%' as a conversion specifier and prints whatever happens to sit next on the varargs
/// stack. That is not a theoretical hazard: the percentage columns added in round 5 rendered as
/// "6.8??" followed by fragments of unrelated engine strings ("any remainder.", "0 skips terrain
/// placeme"), which is this bug reading the process's own memory and printing it on screen.</para>
///
/// <para><c>TextUnformatted</c> has no format pass, so it is the only safe way to print a string the
/// program did not write itself. These wrappers are it, with the colour handling that made the format
/// versions attractive in the first place.</para>
/// </summary>
internal static class UiText
{
    /// <summary>One line in <paramref name="color"/>, with no format interpretation.</summary>
    internal static void Colored(Vector4 color, string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.TextUnformatted(text);
        ImGui.PopStyleColor();
    }

    /// <summary>The theme's disabled tone - <c>ImGui.TextDisabled</c> without the format pass.</summary>
    internal static void Disabled(string text) =>
        Colored(ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled], text);

    /// <summary>A one-shot tooltip - exactly what <c>ImGui.SetTooltip</c> does (begin, one line, end),
    /// without the format pass. Deliberately unwrapped, like <c>SetTooltip</c>: the callers that need a
    /// wrap push their own width (see <c>RuleHoverText.ShowTooltip</c>).</summary>
    internal static void Tooltip(string text)
    {
        ImGui.BeginTooltip();
        ImGui.TextUnformatted(text);
        ImGui.EndTooltip();
    }

    /// <summary>Wrapped at the content edge - <c>ImGui.TextWrapped</c> without the format pass.</summary>
    internal static void Wrapped(string text)
    {
        ImGui.PushTextWrapPos(0f);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
    }
}
