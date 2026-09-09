using System;
using System.Numerics;
using ImGuiNET;
using Raylib_cs;

namespace FdgRaylib.Rendering;

/// <summary>
/// #398: small painted widgets shared by more than one screen - the pieces that are drawn on the draw
/// list rather than composed from ImGui controls, kept in one place so they look the same everywhere.
///
/// <para>Sizes are expressed in text-line units, never raw pixels: the app scales its whole style for
/// the display (<c>ScaleAllSizes</c> at startup), so a hard-coded 10px bar that looks right on a 1080p
/// laptop is a hairline on the 4K desktop this is tuned for.</para>
/// </summary>
public static class UiChrome
{
    /// <summary>
    /// A horizontal meter: a dark well with <paramref name="fraction"/> of it filled. Reserves its own
    /// layout space, so it participates in normal ImGui flow.
    /// </summary>
    /// <param name="fraction">0..1; clamped, so a caller cannot overdraw the track.</param>
    public static void DrawMeter(float fraction, float width, Vector4 fill)
    {
        float height = MathF.Max(4f, ImGui.GetTextLineHeight() * 0.55f);
        float rounding = height * 0.5f;
        Vector2 pos = ImGui.GetCursorScreenPos();
        ImDrawListPtr dl = ImGui.GetWindowDrawList();

        dl.AddRectFilled(pos, pos + new Vector2(width, height),
            ImGui.GetColorU32(ImGuiTheme.InkWell), rounding);

        float filled = width * Math.Clamp(fraction, 0f, 1f);
        // Below the rounding diameter a rounded rect degenerates into a dot; draw a sliver instead so a
        // nearly-dead defender still shows something rather than blinking out.
        if (filled > 0.5f)
            dl.AddRectFilled(pos, pos + new Vector2(MathF.Max(filled, rounding), height),
                ImGui.GetColorU32(fill), rounding);

        ImGui.Dummy(new Vector2(width, height));
    }

    /// <summary>
    /// A modifier chip - the same visual beat the dice overlay uses when a roll resolves, so a threshold
    /// explained in the calculator ("Quality 4+", "Stealth -1") looks like the one the player watches
    /// land in a live game. Reserves its own layout space; put it after a <c>SameLine</c>.
    /// </summary>
    public static void DrawChip(string text)
    {
        Vector2 padding = new(ImGui.GetStyle().FramePadding.X, ImGui.GetStyle().FramePadding.Y * 0.5f);
        Vector2 size = ImGui.CalcTextSize(text) + (padding * 2f);
        Vector2 pos = ImGui.GetCursorScreenPos();
        ImDrawListPtr dl = ImGui.GetWindowDrawList();

        dl.AddRectFilled(pos, pos + size, ImGui.GetColorU32(ChipBg), size.Y * 0.175f);
        dl.AddText(pos + padding, ImGui.GetColorU32(ChipText), text);

        ImGui.Dummy(size);
    }

    // The chip palette, defined once. The dice overlay paints its chips with Raylib on the canvas and
    // this paints them with ImGui in a panel, so the two cannot share a draw call - but they can and do
    // share the colours, which is what makes a calculator chip and a live-roll chip read as the same
    // object. Change them here and both surfaces move together.
    private const byte ChipBgR = 45, ChipBgG = 45, ChipBgB = 52, ChipBgA = 230;
    private const byte ChipFgR = 210, ChipFgG = 210, ChipFgB = 215;

    public static Color ChipBackgroundRaylib => new(ChipBgR, ChipBgG, ChipBgB, ChipBgA);
    public static Color ChipForegroundRaylib => new(ChipFgR, ChipFgG, ChipFgB, (byte)255);

    private static readonly Vector4 ChipBg =
        new(ChipBgR / 255f, ChipBgG / 255f, ChipBgB / 255f, ChipBgA / 255f);
    private static readonly Vector4 ChipText =
        new(ChipFgR / 255f, ChipFgG / 255f, ChipFgB / 255f, 1f);
}
