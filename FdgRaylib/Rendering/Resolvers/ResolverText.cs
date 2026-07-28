using System.Collections.Generic;
using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// Text measuring for the resolver panels that paint their option rows onto a draw list rather than
/// letting ImGui lay them out (<see cref="GuiSelectionResolver{T}"/>, <see cref="GuiStringSelectionResolver"/>).
/// Those rows are one invisible button with text drawn over it, so nothing may touch the ImGui cursor —
/// which rules out <c>PushTextWrapPos</c> and leaves the wrapping to be done by hand.
/// </summary>
internal static class ResolverText
{
    /// <summary>
    /// Word-wraps <paramref name="text"/> to <paramref name="maxWidth"/>, returning at least one line.
    /// Text drawn at a reduced size passes its <paramref name="scale"/> (width scales ~linearly with font
    /// size), so widths measured at the main font size still land — this avoids a dependency on the
    /// per-size CalcTextSizeA overload. A single word longer than the width gets its own overlong line
    /// rather than being split mid-word.
    /// </summary>
    public static List<string> Wrap(string text, float maxWidth, float scale = 1f)
    {
        var lines = new List<string>();
        if (string.IsNullOrEmpty(text)) { lines.Add(""); return lines; }

        string cur = "";
        foreach (string word in text.Split(' '))
        {
            string test = cur.Length == 0 ? word : cur + " " + word;
            if (ImGui.CalcTextSize(test).X * scale > maxWidth && cur.Length > 0)
            {
                lines.Add(cur);
                cur = word;
            }
            else cur = test;
        }
        lines.Add(cur);
        return lines;
    }
}
