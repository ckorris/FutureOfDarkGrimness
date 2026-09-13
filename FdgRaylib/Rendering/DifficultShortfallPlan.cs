using FDG.Stages;

namespace FdgRaylib.Rendering;

/// <summary>
/// #317: the display-independent core of the difficult-terrain "snap back" explanation. When the movement
/// preview's ghost is shortened by the difficult-terrain clamp (#155), the resolver also draws the pose the
/// ghost WOULD have taken in light gray, linked to the real ghost by a dotted line — this decides whether
/// that phantom is worth drawing at all, and what the label beside it says.
///
/// <para>Split out from <see cref="Resolvers.GuiDefineMovementResolver"/> so the show/hide rule and the
/// wording are testable without ImGui; the resolver keeps the drawing.</para>
/// </summary>
internal static class DifficultShortfallPlan
{
    /// <summary>Below this the phantom would sit on top of the real ghost and the dotted link would be a
    /// smudge — not worth the clutter, and the snap-back isn't what confused the player at that size.</summary>
    internal const float MIN_SHORTFALL_INCHES = 0.15f;

    /// <summary>Whether to draw the would-be phantom, and the two label lines beside it.</summary>
    internal readonly record struct Hint(bool Show, string Header, string Detail);

    internal static readonly Hint None = new Hint(false, string.Empty, string.Empty);

    /// <summary>
    /// <paramref name="kind"/> is why the difficult-terrain clamp shortened the segment (it is the clamp's
    /// own verdict, so the two cases can't drift apart from what actually happened), and
    /// <paramref name="shortfallInches"/> is how much travel it cost — the distance between where the ghost
    /// sits and where it would have sat. <paramref name="capInches"/> is the difficult-terrain move cap.
    /// </summary>
    internal static Hint Build(MovementUtilities.EDifficultClampKind kind, float shortfallInches, float capInches)
    {
        if (kind == MovementUtilities.EDifficultClampKind.NotLimited) return None;
        if (shortfallInches < MIN_SHORTFALL_INCHES) return None;

        // Two different reasons, two different sentences: a model moving THROUGH difficult terrain has its
        // whole move held to the cap, while one that already spent the cap can't enter the piece at all.
        // Saying "can only move 6 inches" to someone who has already moved 6 inches explains nothing.
        string detail = kind == MovementUtilities.EDifficultClampKind.StoppedShortOfEdge
            ? $"Cannot enter - {FormatInches(capInches)}\" used"
            : $"Can only move {FormatInches(capInches)}\"";
        return new Hint(true, "Difficult Terrain", detail);
    }

    /// <summary>Whole inches read as whole inches ("6", not "6.0"); anything else keeps one decimal.</summary>
    internal static string FormatInches(float value)
    {
        float frac = value - MathF.Floor(value);
        if (frac < 0.05f || frac > 0.95f) return MathF.Round(value).ToString("0");
        return value.ToString("0.0");
    }
}
