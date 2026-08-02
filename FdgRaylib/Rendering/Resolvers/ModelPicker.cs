using FDG;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// #295: which of a unit's models the pointer is over. Single-model movement and consolidation switch
/// models by clicking the model directly (Space used to cycle; Space now confirms), and both draw a hover
/// highlight to advertise it -- so the highlight and the click MUST agree, which they only do while one
/// implementation answers both.
///
/// #310: the hit test takes each model's POSE -- the position and facing its ghost is drawn at -- not the
/// model itself. A model with committed waypoints is hit-tested where its final ghost stands, so its
/// vacated start slot is free ground for waypoint placement. (The old start-position test made a click on
/// a vacated slot silently re-select the model that had left it instead of placing a waypoint there --
/// models the player believed they had moved never got one.) The facing makes the test exact for rotated
/// rectangular bases, matching the oriented outline the ghost draws.
/// </summary>
internal static class ModelPicker
{
    /// <summary>
    /// The model whose base footprint, placed at its pose, contains the table point -- nearest centre
    /// first so overlapping bases resolve the same way the tooltip's <see cref="TableHitTester"/> does.
    /// Null when the point is over bare table.
    /// </summary>
    public static IModel? HitTest(IEnumerable<(IModel model, Position at, Float2 facing)> poses,
        float xInches, float zInches)
    {
        IModel? hit = null;
        float bestDist = float.MaxValue;
        foreach ((IModel model, Position at, Float2 facing) in poses)
        {
            float dx = xInches - at.x;
            float dz = zInches - at.z;
            // Rotate the world offset into the base-local frame (local +Z = facing), mirroring
            // BaseShapeGeometry.SurfaceDistanceToPoint2D; a zero facing means forward (identity).
            Float2 f = facing.X == 0f && facing.Y == 0f ? new Float2(0f, 1f) : facing;
            float localZ = dx * f.X + dz * f.Y;
            float localX = dx * f.Y - dz * f.X;
            float d2 = dx * dx + dz * dz;
            if (model.BaseShape.ContainsLocalPoint(localX, localZ) && d2 < bestDist) { hit = model; bestDist = d2; }
        }
        return hit;
    }
}
