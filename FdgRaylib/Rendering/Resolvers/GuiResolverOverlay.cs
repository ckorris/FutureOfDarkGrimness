namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// Holds all GUI resolvers and draws whichever one is currently waiting for player input.
/// Call Draw() every frame from the game render loop.
/// </summary>
public class GuiResolverOverlay
{
    private readonly List<IGuiResolver> _resolvers = new();

    public void Register(IGuiResolver resolver) => _resolvers.Add(resolver);

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
