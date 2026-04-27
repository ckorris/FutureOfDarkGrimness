namespace FdgRaylib.Rendering.Resolvers;

public interface IGuiResolver
{
    bool HasPendingRequest { get; }
    void Draw(int screenW, int screenH);
}
