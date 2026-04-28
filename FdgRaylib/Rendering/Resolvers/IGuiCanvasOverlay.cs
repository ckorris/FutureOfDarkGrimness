namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// Optional extension for IGuiResolvers that need to draw on the Raylib table canvas.
/// RaylibRenderer calls UpdateLayout each frame before Draw() so pixel↔inch conversion is current.
/// </summary>
public interface IGuiCanvasOverlay
{
    void UpdateLayout(float scale, int originX, int originY, float tableH);
}
