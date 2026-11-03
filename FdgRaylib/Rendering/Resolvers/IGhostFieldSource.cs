using FDG;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// #230 — implemented by a GUI resolver whose pending ghosts can anchor the tactical overlay's
/// ghost-anchored opportunity field ("what can I hit from here": per-model weapon-range bands from the
/// pending positions, with LoS and cover taken from those positions — see
/// <c>TacticalOverlayController.RebuildGhostField</c>).
///
/// <para>The field was built for movement and reached the moving unit's ghosts through
/// <c>GuiDefineMovementResolver</c> directly. Placement wants the same picture for the same reason
/// (#230: judge what a deployment / ambush / teleport spot threatens before committing), and the models
/// aren't on the table yet, so nothing else can show it. This is the seam that lets any resolver with
/// pending positions offer them, rather than the overlay knowing about each resolver by type.</para>
///
/// <para>Read on the main thread from the Raylib canvas pass, which runs BEFORE the resolver's own Draw
/// in the same frame — so the positions are one frame old, exactly as the movement resolver's always
/// have been. Imperceptible on a cursor-following picture, and it keeps the reader off the engine
/// thread's state.</para>
/// </summary>
public interface IGhostFieldSource
{
    /// <summary>
    /// When this resolver currently has ghosts worth anchoring a field on, outputs the unit they belong
    /// to and their pending positions by model, and returns true. Returns false otherwise — no pending
    /// request, nothing drawn on the table this frame, or a request placing something that isn't a
    /// unit's models (objectives, terrain).
    /// </summary>
    bool TryGetGhostField(out IUnit unit, out IReadOnlyDictionary<IModel, Position> ghosts);
}
