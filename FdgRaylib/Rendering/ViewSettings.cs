namespace FdgRaylib.Rendering;

/// <summary>
/// Global in-game view toggles, shared between the canvas overlays that read them and the in-game
/// menu's Options panel that sets them (#246). Static because there is one board on screen at a time
/// and these are display preferences, not per-game state — they persist across games like the grid
/// toggle always has.
/// </summary>
public static class ViewSettings
{
    /// <summary>Unit-name labels on the table (hotkey L).</summary>
    public static bool ShowLabels = true;

    /// <summary>Etched grid + felt vignette under the table (was RaylibRenderer.ShowGrid).</summary>
    public static bool ShowGrid = true;

    /// <summary>Dev toggle (hotkey T): reveal Invisible bookkeeping tokens in chips/tooltips.</summary>
    public static bool ShowAllTokens = false;

    /// <summary>
    /// #230 (hotkey V): anchor the tactical overlay's opportunity field on a placement's ghosts, so a
    /// deployment / ambush arrival / teleport spot can be judged by what it would reach — LoS and cover
    /// included — before it is committed. On by default; the placement panel carries the same toggle.
    /// </summary>
    public static bool ShowPlacementRanges = true;
}
