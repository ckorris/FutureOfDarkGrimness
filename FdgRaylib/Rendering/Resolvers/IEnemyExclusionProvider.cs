using FDG;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// Implemented by a GUI resolver that, while active, forbids placement within a fixed distance of enemy
/// models (Ambush reserve arrival). Exposes the enemy model centres and the exclusion radius so the
/// renderer can draw the no-go region as a single blended blob on the table canvas.
/// </summary>
public interface IEnemyExclusionProvider
{
    /// <summary>
    /// When an enemy-distance constraint is currently in effect, outputs the living enemy model centres
    /// (world inches) and the exclusion radius in inches, and returns true. Returns false otherwise
    /// (no pending request, or a request without an enemy-distance constraint).
    /// </summary>
    bool TryGetEnemyExclusion(out IReadOnlyList<Position> enemyCenters, out float radiusInches);
}
