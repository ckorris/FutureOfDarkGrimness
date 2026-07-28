using System.Numerics;
using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// The screen-space rectangle that docked resolver panels fill: the top of the in-game right column
/// (the rest is the log/chat console). <see cref="RaylibRenderer"/> refreshes it every frame from
/// the current layout; each GUI resolver pins its window to it via <see cref="BeginDocked"/> instead of
/// floating as a popup. Content taller than the region scrolls (a vertical scrollbar appears).
/// </summary>
public static class ResolverPanelLayout
{
    /// <summary>
    /// Share of the screen height the resolver panel takes; the console gets the remainder. The prompts
    /// are the thing being read and acted on - the shooting resolver splits this height into three stacked
    /// scrolling sections (weapon / target / detail) - so the panel gets the larger share and the console
    /// keeps the bottom 40%.
    /// </summary>
    public const float ScreenHeightFraction = 0.60f;

    public static float X { get; private set; }
    public static float Y { get; private set; }
    public static float W { get; private set; } = 360f;
    public static float H { get; private set; } = 400f;

    public static void Set(float x, float y, float w, float h)
    {
        X = x; Y = y; W = w; H = h;
    }

    /// <summary>Inner content width available after the window's frame padding (for wrap/right-align math).</summary>
    public static float ContentWidth => W - ImGui.GetStyle().WindowPadding.X * 2f;

    /// <summary>
    /// #298 — how many text lines tall a one-line option button is. The panels used to hardcode 28-32px
    /// rows, but the font is <c>18f * uiScale</c> (<see cref="RaylibRenderer"/>), up to 25px on a 4K
    /// display: the label filled the button edge to edge and every list read as a cramped stack of text.
    /// This is the shoot panel's long-standing row multiple, now shared by every option list so the
    /// buttons are the same size everywhere and scale with the UI rather than with nothing.
    /// </summary>
    public const float OptionRowLineMultiple = 2.4f;

    /// <summary>
    /// Height of a one-line option button given the live font's line height. Pure arithmetic so the
    /// budget is unit-testable (the drawing around it is hand-verified); the parameterless
    /// <see cref="OptionRowHeight()"/> reads the live font for callers inside a frame.
    /// </summary>
    public static float OptionRowHeight(float textLineHeight) => textLineHeight * OptionRowLineMultiple;

    /// <inheritdoc cref="OptionRowHeight(float)"/>
    public static float OptionRowHeight() => OptionRowHeight(ImGui.GetTextLineHeight());

    /// <summary>
    /// Multiple of the line height for a secondary action button (Back, Cancel, Skip, Undo, Restart) — a
    /// shade under a full option row, so it reads as subordinate without going back to being a sliver.
    /// </summary>
    public const float ActionRowLineMultiple = 2.0f;

    /// <inheritdoc cref="ActionRowLineMultiple"/>
    public static float ActionRowHeight(float textLineHeight) => textLineHeight * ActionRowLineMultiple;

    /// <inheritdoc cref="ActionRowLineMultiple"/>
    public static float ActionRowHeight() => ActionRowHeight(ImGui.GetTextLineHeight());

    /// <summary>
    /// Pins the next window to the resolver panel region and begins it with docked flags (no move/resize,
    /// stays put, vertical scrollbar when content overflows). Pass an id with a leading "##" to hide the
    /// title bar, or a human title (e.g. "Shoot: Warriors") to show one. Returns the ImGui.Begin result.
    /// </summary>
    public static bool BeginDocked(string idOrTitle)
    {
        ImGui.SetNextWindowPos(new Vector2(X, Y), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(W, H), ImGuiCond.Always);
        return ImGui.Begin(idOrTitle,
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoSavedSettings);
    }
}
