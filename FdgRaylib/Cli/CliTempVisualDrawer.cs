using FDG;
using FDG.TempVisuals;
using System.Drawing;
using System.Numerics;

namespace FdgRaylib.Cli;

/// <summary>
/// No-op drawer for headless / CLI mode. Temp visuals (attack indicators, movement
/// previews, etc.) are a rendering concern and have no meaning on the console.
/// </summary>
public class CliTempVisualDrawer : ITempVisualDrawer
{
    public void AddVisual(ITempVisual visual) { }
    public void UpdateVisualTransform(Guid id, Position position, Quaternion rotation, Vector3 scale) { }
    public void UpdateVisualColor(Guid id, Color color) { }
    public void RemoveVisual(Guid id) { }
    public void ClearAllVisuals() { }
}
