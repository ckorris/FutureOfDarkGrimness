using FDG;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// Optional interface for GUI resolvers that want to respond to hover and click events
/// on unit models on the table canvas.
///
/// The tooltip overlay checks the active resolver each frame: if it implements this
/// interface, GetHoverLabel is appended to the standard tooltip and HandleClick is
/// called when the player clicks a model.
///
/// Existing resolvers that do their own canvas interaction (movement, placement) do not
/// need to implement this — it is purely opt-in for unit-targeting interactions.
/// </summary>
public interface ICanvasInteractionHandler
{
    /// <summary>
    /// Called each frame while the cursor is over a unit model.
    /// Return a non-null string to append a line to the tooltip — e.g. "✓ Valid target"
    /// or "✗ Out of range (24\")".  Return null to show no extra line.
    /// </summary>
    string? GetHoverLabel(IUnit unit, IModel model);

    /// <summary>
    /// Called when the player left-clicks on a unit model on the canvas.
    /// Only fired when ImGui is not capturing the mouse.
    /// </summary>
    void HandleClick(IUnit unit, IModel model);
}
