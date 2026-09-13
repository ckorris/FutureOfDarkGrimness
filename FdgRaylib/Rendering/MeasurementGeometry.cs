using FDG;

namespace FdgRaylib.Rendering;

/// <summary>
/// The display-independent core of the ruler overlay (#251): what "edge distance" means when a ruler end
/// is snapped to a model.
///
/// <para>Measured against true base SHAPES, never by subtracting scalar radii.
/// <see cref="IModel.BaseRadiusInches"/> is a rectangle's INSCRIBED radius — half its lesser side (#149) —
/// so radius arithmetic treats a 60x35mm bike as a 35mm disc and overstates every gap involving an
/// elongated base, by up to (halfLength - halfWidth) per model. The ruler must agree with what the rules
/// engine measures when it resolves charges and ranges, so it calls the same geometry.</para>
///
/// Split out from <see cref="MeasurementOverlay"/> so it is testable without ImGui or an
/// <see cref="ITableState"/> — the overlay keeps the input handling and drawing.
/// </summary>
internal static class MeasurementGeometry
{
    /// <summary>
    /// Base-to-base distance for a ruler whose ends are described by an optional base shape each (null =
    /// that end is a free point on the table, not snapped to a model). Returns null when NEITHER end is a
    /// model, in which case the ruler has only a centre-to-centre reading to show.
    ///
    /// Never negative: overlapping or touching bases read 0, matching
    /// <see cref="DistanceUtilities"/>'s clamping.
    /// </summary>
    public static float? EdgeDistanceInches(
        IBaseShape? shapeA, Position posA, Float2 facingA,
        IBaseShape? shapeB, Position posB, Float2 facingB)
    {
        if (shapeA != null && shapeB != null)
        {
            // Both ends on a model: exact, facing-aware footprint-to-footprint gap — the same call the
            // engine uses for charge/range legality.
            float gap = DistanceUtilities.GetBaseToBaseDistanceInches_2D(posA, posB, shapeA, facingA, shapeB, facingB);
            return gap < 0f ? 0f : gap;
        }

        // Exactly one end on a model: shape-to-point surface distance from that base to the free point.
        if (shapeA != null)
            return Clamp0(BaseShapeGeometry.SurfaceDistanceToPoint2D(shapeA, posA, facingA, posB));
        if (shapeB != null)
            return Clamp0(BaseShapeGeometry.SurfaceDistanceToPoint2D(shapeB, posB, facingB, posA));

        return null;
    }

    /// <summary>
    /// Horizontal distance from <paramref name="cursor"/> to the base's surface — 0 when the cursor is on
    /// or inside it. The ruler snaps on this rather than on a circle of the inscribed radius, which
    /// under-snapped every rectangle and made the long ends of a bike or tank base unclickable.
    /// </summary>
    public static float SnapDistanceInches(IBaseShape shape, Position center, Float2 facing, Position cursor) =>
        BaseShapeGeometry.SurfaceDistanceToPoint2D(shape, center, facing, cursor);

    private static float Clamp0(float v) => v < 0f ? 0f : v;
}
