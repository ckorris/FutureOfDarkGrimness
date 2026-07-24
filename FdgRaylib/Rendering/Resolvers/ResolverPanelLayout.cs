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
