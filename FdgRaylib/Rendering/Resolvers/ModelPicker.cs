using FDG;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// #295: which of a unit's models the pointer is over. Single-model movement and consolidation switch
/// models by clicking the model directly (Space used to cycle; Space now confirms), and both draw a hover
/// highlight to advertise it -- so the highlight and the click MUST agree, which they only do while one
/// implementation answers both.
/// </summary>
internal static class ModelPicker
{
    /// <summary>
    /// The model whose base footprint contains the table point, nearest centre first so overlapping bases
    /// resolve the same way the tooltip's <see cref="TableHitTester"/> does. Null when the point is over
    /// bare table. Exact for circular bases; rectangles share the codebase's axis-aligned limitation via
    /// <c>BaseShape.ContainsLocalPoint</c>.
    /// </summary>
    public static IModel? HitTest(IEnumerable<IModel> models, float xInches, float zInches)
    {
        IModel? hit = null;
        float bestDist = float.MaxValue;
        foreach (IModel model in models)
        {
            float dx = xInches - model.Position.x;
            float dz = zInches - model.Position.z;
            float d2 = dx * dx + dz * dz;
            if (model.BaseShape.ContainsLocalPoint(dx, dz) && d2 < bestDist) { hit = model; bestDist = d2; }
        }
        return hit;
    }
}
