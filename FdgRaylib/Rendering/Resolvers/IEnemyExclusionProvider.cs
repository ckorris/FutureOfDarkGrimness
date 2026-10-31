using FDG;
using FDG.StageResolution.Requests;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// Implemented by a GUI resolver that, while active, forbids placement near enemy models (Ambush reserve
/// arrival). Exposes the no-go geometry as discs so the renderer can draw the region as a single blended
/// blob on the table canvas: <c>keepOut</c> discs union into the blob (the flat over-9" rule as one disc
/// per live enemy model, plus Repel Ambushers' larger per-model discs — #197 P22), and <c>waivers</c>
/// (Ambush Beacon regions) are erased back out of it, because inside one every enemy-distance restriction
/// is void.
/// </summary>
public interface IEnemyExclusionProvider
{
    /// <summary>
    /// When an enemy-distance constraint is currently in effect, outputs the keep-out discs and the
    /// waiver discs (world inches) and returns true. Returns false otherwise (no pending request, or a
    /// request without an enemy-distance constraint).
    /// </summary>
    bool TryGetEnemyExclusion(out IReadOnlyList<PlacementDisc> keepOut,
        out IReadOnlyList<PlacementDisc> waivers);
}
