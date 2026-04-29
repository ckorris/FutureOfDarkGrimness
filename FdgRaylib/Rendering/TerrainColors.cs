using FDG;
using Raylib_cs;

namespace FdgRaylib.Rendering;

/// <summary>
/// Maps ETerrainType flag combinations to (fill, outline) colors. Picks the most
/// rules-relevant flag for fill (impassible/blocking dominate cover/difficult since they
/// hard-stop movement and LoS). Adds saturation if dangerous is also set.
/// </summary>
internal static class TerrainColors
{
    public static (Color fill, Color outline) For(ETerrainType flags)
    {
        Color fill;
        Color outline;

        if (flags.HasFlag(ETerrainType.Blocking) || flags.HasFlag(ETerrainType.Impassible))
        {
            //Solid stone — dark gray with strong outline.
            fill    = new Color(70, 70, 75, 220);
            outline = new Color(20, 20, 25, 255);
        }
        else if (flags.HasFlag(ETerrainType.Cover) && flags.HasFlag(ETerrainType.Difficult))
        {
            //Forest — translucent green.
            fill    = new Color(40, 110, 50, 130);
            outline = new Color(20, 60, 25, 220);
        }
        else if (flags.HasFlag(ETerrainType.Cover))
        {
            //Sandbags / low cover — sand color.
            fill    = new Color(160, 140, 90, 160);
            outline = new Color(90, 75, 40, 220);
        }
        else if (flags.HasFlag(ETerrainType.Difficult))
        {
            //Rubble / mud — muddy brown.
            fill    = new Color(110, 80, 50, 150);
            outline = new Color(60, 40, 20, 220);
        }
        else
        {
            //Fallback — light gray.
            fill    = new Color(120, 120, 120, 140);
            outline = new Color(40, 40, 40, 220);
        }

        if (flags.HasFlag(ETerrainType.Dangerous))
        {
            //Tint outline red to flag the danger; keep underlying fill so type still reads.
            outline = new Color(180, 30, 30, 255);
        }

        return (fill, outline);
    }
}
