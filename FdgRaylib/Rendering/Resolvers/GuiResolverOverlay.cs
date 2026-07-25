namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// Holds all GUI resolvers and draws whichever one is currently waiting for player input.
/// Call Draw() every frame from the game render loop.
/// </summary>
public class GuiResolverOverlay
{
    private readonly List<IGuiResolver> _resolvers = new();

    public void Register(IGuiResolver resolver) => _resolvers.Add(resolver);

    /// <summary>
    /// The launched game's #201 cover-proximity-exceptions setting, stamped by
    /// <c>ResolverRegistryFactory.BuildGui</c>. Rides the overlay because the renderer receives it
    /// at TransitionToGame and hands it to the tactical overlay's rules probe - keeping every
    /// client-side cover preview on the same setting the host's CoverCheckStage rolls with.
    /// </summary>
    public bool CoverProximityExceptions { get; set; } = true;

    /// <summary>
    /// The launched game's #265 table background (lobby setting, synced to every player, saved with
    /// the game), stamped by <c>ResolverRegistryFactory.BuildGui</c>. Rides the overlay for the same
    /// reason the cover setting above does: it is a per-launch setting the renderer needs, and the
    /// overlay is what TransitionToGame already receives. Nothing in the resolvers reads it.
    /// </summary>
    public FDG.ETableBackground TableBackground { get; set; } = FDG.ETableBackground.Forest;

    /// <summary>
    /// The movement resolver in this overlay, if one is registered (always, in a GUI build). The
    /// tactical overlay pulls the active move job off it and routes enemy pin-clicks back to it, so
    /// the renderer wires the two together once at game start.
    /// </summary>
    public GuiDefineMovementResolver? MovementResolver
    {
        get
        {
            foreach (IGuiResolver r in _resolvers)
                if (r is GuiDefineMovementResolver m) return m;
            return null;
        }
    }

    /// <summary>True while any resolver is waiting on the player — the resolver panel is showing a prompt.</summary>
    public bool HasAnyPending
    {
        get
        {
            foreach (IGuiResolver r in _resolvers)
                if (r.HasPendingRequest) return true;
            return false;
        }
    }

    /// <summary>
    /// True while a pending resolver's group-formation ghost is consuming Ctrl+Wheel (#275). The
    /// renderer's Ctrl+wheel zoom checks this each frame and yields, so cycling a formation never
    /// zooms the table underneath it.
    /// </summary>
    public bool FormationWheelActive
    {
        get
        {
            foreach (IGuiResolver r in _resolvers)
                if (r is IFormationWheelConsumer c && c.FormationWheelActive) return true;
            return false;
        }
    }

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
