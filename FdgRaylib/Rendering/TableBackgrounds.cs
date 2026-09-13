using FDG;
using Raylib_cs;

namespace FdgRaylib.Rendering;

/// <summary>
/// Look-up from the lobby's <see cref="ETableBackground"/> (#265) to the colours the renderer paints
/// the board with. Purely cosmetic: the surface fill, the etched inch-grid tints, the trim around the
/// edge, and the Perlin mottling laid over the felt.
///
/// <para>Every style keeps the Forest original's structure so the board reads the same way on any
/// surface: the grid lines are a darker, desaturated relative of the surface (etched, not painted),
/// and the mottle tint is a dark colour blended ADDITIVELY, so it lifts the lighter flecks of the
/// noise and leaves the darker ones alone. Keep mottle tints dark - an additive bright tint blows a
/// pale surface (Ice, Desert) out to white.</para>
/// </summary>
public readonly record struct TableBackgroundStyle(
    Color Surface,
    Color GridMinor,
    Color GridMajor,
    Color Border,
    Color MottleTint,
    float MottleScale,
    int   MottleOffset);

public static class TableBackgrounds
{
    /// <summary>Label for the lobby dropdown - the enum names are identifiers, some of which need a
    /// hyphen to read right. ASCII only (the ImGui atlas bakes no wider glyphs).</summary>
    public static string Label(ETableBackground background) => background switch
    {
        ETableBackground.MarsLike => "Mars-Like",
        _                         => background.ToString(),
    };

    public static TableBackgroundStyle For(ETableBackground background) => background switch
    {
        // Green felt with grassy flecks - the original board, unchanged, and what every pre-#265
        // save loads as.
        ETableBackground.Forest => new TableBackgroundStyle(
            Surface:     new Color(40, 100, 40, 255),
            GridMinor:   new Color(33, 85, 33, 80),
            GridMajor:   new Color(24, 66, 24, 150),
            Border:      new Color(150, 105, 55, 255),
            MottleTint:  new Color(22, 44, 22, 60),
            MottleScale: 5f,
            MottleOffset: 0),

        // Sand. Coarser mottle for wind-blown drifts; the trim goes darker than Forest's so it still
        // frames a pale surface.
        ETableBackground.Desert => new TableBackgroundStyle(
            Surface:     new Color(196, 166, 110, 255),
            GridMinor:   new Color(148, 122, 78, 90),
            GridMajor:   new Color(124, 99, 58, 150),
            Border:      new Color(122, 92, 52, 255),
            MottleTint:  new Color(42, 32, 14, 60),
            MottleScale: 7f,
            MottleOffset: 128),

        // Snow/ice. Broad, smooth mottling (low scale) so it reads as drifts and pressure ridges
        // rather than grain, tinted cold.
        ETableBackground.Ice => new TableBackgroundStyle(
            Surface:     new Color(198, 216, 232, 255),
            GridMinor:   new Color(150, 176, 200, 90),
            GridMajor:   new Color(118, 148, 180, 150),
            Border:      new Color(108, 138, 168, 255),
            MottleTint:  new Color(18, 28, 44, 55),
            MottleScale: 3f,
            MottleOffset: 256),

        // Rusty red regolith.
        ETableBackground.MarsLike => new TableBackgroundStyle(
            Surface:     new Color(142, 74, 48, 255),
            GridMinor:   new Color(112, 56, 36, 90),
            GridMajor:   new Color(88, 42, 26, 150),
            Border:      new Color(92, 58, 42, 255),
            MottleTint:  new Color(52, 24, 10, 65),
            MottleScale: 6f,
            MottleOffset: 384),

        // Concrete. Coarse mottle for slab staining, and steel trim - the warm brown edge reads as a
        // mistake against grey.
        ETableBackground.Urban => new TableBackgroundStyle(
            Surface:     new Color(98, 98, 103, 255),
            GridMinor:   new Color(78, 78, 84, 100),
            GridMajor:   new Color(58, 58, 64, 160),
            Border:      new Color(126, 126, 134, 255),
            MottleTint:  new Color(28, 28, 32, 70),
            MottleScale: 9f,
            MottleOffset: 512),

        // Dead earth - dusty grey-brown, between Desert and Urban.
        ETableBackground.Barren => new TableBackgroundStyle(
            Surface:     new Color(118, 106, 86, 255),
            GridMinor:   new Color(96, 85, 66, 90),
            GridMajor:   new Color(74, 66, 50, 150),
            Border:      new Color(92, 80, 60, 255),
            MottleTint:  new Color(34, 30, 20, 60),
            MottleScale: 6f,
            MottleOffset: 640),

        _ => For(ETableBackground.Forest),
    };
}
