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
    public static void DrawMeter(float fraction, float width, Vector4 fill) =>
        DrawMeter(fraction, width, fill, spent: null);

    /// <summary>
    /// As above, but paints the part that is GONE in <paramref name="spent"/> rather than leaving it
    /// empty. Two segments say more than one: the filled part is what survives, the spent part is what
    /// the attack took off, and the eye compares them without reading a number.
    /// </summary>
    public static void DrawMeter(float fraction, float width, Vector4 fill, Vector4? spent)
    {
        float height = MathF.Max(4f, ImGui.GetTextLineHeight() * 0.55f);
        float rounding = height * 0.5f;
        Vector2 pos = ImGui.GetCursorScreenPos();
        ImDrawListPtr dl = ImGui.GetWindowDrawList();

        dl.AddRectFilled(pos, pos + new Vector2(width, height),
            ImGui.GetColorU32(ImGuiTheme.InkWell), rounding);

        float filled = width * Math.Clamp(fraction, 0f, 1f);

        if (spent is { } spentColor && filled < width - 0.5f)
            dl.AddRectFilled(pos + new Vector2(filled, 0f), pos + new Vector2(width, height),
                ImGui.GetColorU32(spentColor), rounding,
                filled <= 0.5f ? ImDrawFlags.RoundCornersAll : ImDrawFlags.RoundCornersRight);

        // Below the rounding diameter a rounded rect degenerates into a dot; draw a sliver instead so a
        // nearly-dead defender still shows something rather than blinking out.
        if (filled > 0.5f)
            dl.AddRectFilled(pos, pos + new Vector2(MathF.Max(filled, rounding), height),
                ImGui.GetColorU32(fill), rounding,
                filled >= width - 0.5f ? ImDrawFlags.RoundCornersAll : ImDrawFlags.RoundCornersLeft);

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

    /// <summary>
    /// The two-tone stat pill from the printed army list (#329): the label on a coloured field, the
    /// value on a dark well, one rounded outline. Promoted out of <c>ArmyListOverlay</c> for #398 so the
    /// Combat Calculator's unit columns wear the same badge as the in-game list rather than a private
    /// imitation of it. Reserves its own layout space.
    /// </summary>
    public static void DrawPill(string label, string value, Vector4 labelBg)
    {
        ImDrawListPtr dl = ImGui.GetWindowDrawList();
        Vector2 pos = ImGui.GetCursorScreenPos();
        float pad = PillPad;
        float inset = MathF.Max(1f, ImGui.GetStyle().FramePadding.Y * 0.5f);
        float h = ImGui.GetTextLineHeight() + (inset * 2f);
        float labelW = ImGui.CalcTextSize(label).X + (2f * pad);
        float valueW = ImGui.CalcTextSize(value).X + (2f * pad);
        float rounding = h * 0.25f;

        dl.AddRectFilled(pos, pos + new Vector2(labelW + valueW, h),
            ImGui.GetColorU32(ImGuiTheme.InkWell), rounding);
        dl.AddRectFilled(pos, pos + new Vector2(labelW, h),
            ImGui.GetColorU32(labelBg), rounding, ImDrawFlags.RoundCornersLeft);
        dl.AddText(pos + new Vector2(pad, inset), ImGui.GetColorU32(PillText), label);
        dl.AddText(pos + new Vector2(labelW + pad, inset), ImGui.GetColorU32(PillText), value);

        ImGui.Dummy(new Vector2(labelW + valueW, h));
    }

    /// <summary>The width <see cref="DrawPill"/> will occupy, for callers that centre a row of them.</summary>
    public static float PillWidth(string label, string value) =>
        ImGui.CalcTextSize(label).X + ImGui.CalcTextSize(value).X + (4f * PillPad);

    /// <summary>The gap between a pill's edge and its text, and between two pills side by side. Both were
    /// flat pixel counts (7 and 8), which is a hairline on the 4K display the app scales its style for;
    /// derived from the style they now track whatever <c>ScaleAllSizes</c> did at startup.</summary>
    public static float PillPad => MathF.Max(3f, ImGui.GetStyle().FramePadding.X * 1.2f);

    /// <inheritdoc cref="PillPad"/>
    public static float PillGap => MathF.Max(2f, ImGui.GetStyle().ItemSpacing.X * 0.5f);

    /// <summary>
    /// #398: the size of a chrome button - Back, Cancel, Swap, Load list. The controls a player aims at
    /// without looking deserve more than ImGui's default hug-the-text button, and the extra room is
    /// expressed in the CURRENT font's terms so it grows with the display's UI scale instead of being a
    /// pixel count tuned for one monitor. Text after "##" is an ImGui id, not a label, so it is not
    /// measured.
    /// </summary>
    public static Vector2 ButtonSize(string label) =>
        new(ImGui.CalcTextSize(label, true).X + (ImGui.GetStyle().FramePadding.X * 4f),
            ImGui.GetFrameHeight() * 1.35f);

    private static readonly Vector4 PillText = new(1f, 1f, 1f, 1f);
}
