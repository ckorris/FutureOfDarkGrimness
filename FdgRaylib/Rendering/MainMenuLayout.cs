namespace FdgRaylib.Rendering;

/// <summary>An axis-aligned screen rectangle in pixels.</summary>
internal readonly record struct ScreenRect(float X, float Y, float W, float H)
{
    public float Right => X + W;
    public float Bottom => Y + H;
}

/// <summary>
/// Where everything on the main menu goes (#448): the title top-left, the command panel down the left
/// side with its three button tiers, and the region right of it that the holographic war table is fitted
/// into. Pure arithmetic shared by <see cref="MainMenuScreen"/> (ImGui) and the background renderer, so
/// the table can never slide under the panel.
///
/// <para>Authored at 1920x1080 (the signed-off mockup) and scaled uniformly by the tighter of the two
/// axes, so a 4:3 or ultrawide window keeps the proportions and only the table region changes shape.</para>
/// </summary>
internal sealed class MainMenuLayout
{
    public float Scale { get; private init; }

    public float TitleX { get; private init; }
    public float TitleLine1Y { get; private init; }
    public float TitleLine2Y { get; private init; }
    public float TitlePx { get; private init; }
    public float UnderlineY { get; private init; }
    public float UnderlineRight { get; private init; }

    /// <summary>The translucent panel behind the buttons (corner brackets sit on its corners).</summary>
    public ScreenRect Panel { get; private init; }

    /// <summary>Host/Solo and Join.</summary>
    public IReadOnlyList<ScreenRect> Primary { get; private init; } = Array.Empty<ScreenRect>();

    /// <summary>Army Forge, Combat Calculator, Load Game.</summary>
    public IReadOnlyList<ScreenRect> Secondary { get; private init; } = Array.Empty<ScreenRect>();

    /// <summary>Options and Quit, side by side.</summary>
    public IReadOnlyList<ScreenRect> Tertiary { get; private init; } = Array.Empty<ScreenRect>();

    public float PrimaryFontPx { get; private init; }
    public float SecondaryFontPx { get; private init; }
    public float TertiaryFontPx { get; private init; }

    /// <summary>Region the war table is fitted into: right of the panel, clear of the screen edges.</summary>
    public ScreenRect TableRegion { get; private init; }

    public static MainMenuLayout Compute(int screenW, int screenH)
    {
        float s = MathF.Min(screenW / 1920f, screenH / 1080f);

        // Button column, top to bottom (1080p pixels): two primaries, a wider gap, three secondaries, a
        // gap, then the two tertiaries sharing one row.
        float colX = 70f * s, colW = 380f * s;
        float y = 250f * s;
        var primary = new List<ScreenRect>();
        for (int i = 0; i < 2; i++) { primary.Add(new ScreenRect(colX, y, colW, 78f * s)); y += (78f + 14f) * s; }
        y += 20f * s;
        var secondary = new List<ScreenRect>();
        for (int i = 0; i < 3; i++) { secondary.Add(new ScreenRect(colX, y, colW, 54f * s)); y += (54f + 12f) * s; }
        y += 10f * s;
        float half = (colW - 12f * s) / 2f;
        var tertiary = new List<ScreenRect>
        {
            new(colX, y, half, 44f * s),
            new(colX + half + 12f * s, y, half, 44f * s),
        };
        y += 44f * s;

        float pad = 20f * s;
        var panel = new ScreenRect(colX - pad, 250f * s - pad, colW + 2f * pad, y + pad - (250f * s - pad));

        float regionX = panel.Right + 70f * s;
        var region = new ScreenRect(regionX, 50f * s, MathF.Max(1f, screenW - 40f * s - regionX),
            MathF.Max(1f, screenH - 90f * s));

        return new MainMenuLayout
        {
            Scale = s,
            TitleX = 66f * s,
            TitleLine1Y = 44f * s,
            TitleLine2Y = 112f * s,
            TitlePx = 58f * s,
            UnderlineY = 196f * s,
            UnderlineRight = 610f * s,
            Panel = panel,
            Primary = primary,
            Secondary = secondary,
            Tertiary = tertiary,
            PrimaryFontPx = 34f * s,
            SecondaryFontPx = 24f * s,
            TertiaryFontPx = 20f * s,
            TableRegion = region,
        };
    }
}
