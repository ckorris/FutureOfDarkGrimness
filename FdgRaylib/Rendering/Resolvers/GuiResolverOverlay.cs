namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// Holds all GUI resolvers and draws whichever one is currently waiting for player input.
/// Call Draw() every frame from the game render loop.
/// </summary>
public class GuiResolverOverlay
{
    private readonly List<IGuiResolver> _resolvers = new();

    public void Register(IGuiResolver resolver) => _resolvers.Add(resolver);

    public void UpdateLayout(float scale, int originX, int originY, float tableH)
    {
        foreach (IGuiResolver r in _resolvers)
            if (r is IGuiCanvasOverlay c) c.UpdateLayout(scale, originX, originY, tableH);
    }

    /// <summary>
    /// Returns the ICanvasInteractionHandler for the currently active resolver, if it
    /// implements one. Null when no resolver is pending or the active one doesn't opt in.
    /// </summary>
    public ICanvasInteractionHandler? ActiveInteractionHandler
    {
        get
        {
            foreach (IGuiResolver r in _resolvers)
                if (r.HasPendingRequest)
                    return r as ICanvasInteractionHandler;
            return null;
        }
    }

    /// <summary>
    /// The currently active resolver's enemy-exclusion zone (Ambush reserve placement), if it opts in.
    /// Null when no resolver is pending or the active one imposes no enemy-distance constraint.
    /// </summary>
    public IEnemyExclusionProvider? ActiveEnemyExclusion
    {
        get
        {
            foreach (IGuiResolver r in _resolvers)
                if (r.HasPendingRequest)
                    return r as IEnemyExclusionProvider;
            return null;
        }
    }

    public void Draw(int screenW, int screenH)
    {
        foreach (IGuiResolver r in _resolvers)
        {
            if (r.HasPendingRequest)
            {
                r.Draw(screenW, screenH);
                return; // only one at a time
            }
        }
    }
}
