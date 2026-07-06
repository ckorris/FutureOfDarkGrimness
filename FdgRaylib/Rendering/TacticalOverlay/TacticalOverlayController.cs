using FDG;
using FdgRaylib.Rendering.Resolvers;

namespace FdgRaylib.Rendering.TacticalOverlay;

/// <summary>
/// Owns the tactical overlay feature (spec section 7): threat frontiers, opportunity fields, pins,
/// and the per-frame instruments. Renderer-owned, constructed once and <see cref="Attach"/>ed per
/// game -- it draws in two passes to hit the spec's layering:
/// <list type="bullet">
/// <item><see cref="DrawField"/> / <see cref="DrawContours"/> run in the Raylib canvas pass (field
/// under terrain, contours above it) and rebuild their cached geometry only when a trigger fired.</item>
/// <item><see cref="UpdateInput"/> / <see cref="DrawInstruments"/> run in the ImGui pass (pips,
/// labels, readouts on the background draw list, above tokens and under windows).</item>
/// </list>
///
/// THE INVARIANT (spec section 0): the field texture and contour polylines are approximate pictures.
/// Every authoritative determination the player acts on -- pip state, counts, snap validity, the
/// promoted measurement -- is computed by calling the engine's real rules through <see cref="RulesProbe"/>,
/// never by sampling a texel. This class holds the pictures; instruments hold a <see cref="RulesProbe"/>.
/// </summary>
public class TacticalOverlayController
{
    private ITableState? _tableState;
    private GuiDefineMovementResolver? _moveResolver;

    // World<->screen for the current frame, pushed once per frame right after ComputeLayout so both
    // the Raylib-pass draws and the ImGui-pass instruments read the same values (spec: no camera --
    // pan/zoom is just a different Layout, never a rebuild).
    private float _scale;
    private int   _originX;
    private int   _originY;
    private float _tableH;

    /// <summary>Wires the live game world. Called from RaylibRenderer.TransitionToGame.</summary>
    public void Attach(ITableState tableState)
    {
        _tableState = tableState;
    }

    /// <summary>
    /// Wires the movement resolver so the controller can read the active move job (pull model, thread
    /// safe) and the resolver can route enemy clicks / draw the pin panel section. Null when no GUI
    /// movement resolver exists (headless never reaches here). Called from TransitionToGame.
    /// </summary>
    public void AttachMovementResolver(GuiDefineMovementResolver? resolver)
    {
        _moveResolver = resolver;
    }

    /// <summary>Drops every per-game reference and cached picture. Called from ExitGame.</summary>
    public void Detach()
    {
        _tableState   = null;
        _moveResolver = null;
    }

    public void UpdateLayout(float scale, int originX, int originY, float tableH)
    {
        _scale   = scale;
        _originX = originX;
        _originY = originY;
        _tableH  = tableH;
    }

    // ---- Raylib pass (canvas) --------------------------------------------------------------------

    /// <summary>
    /// Draws the cached opportunity-field texture, rebuilding it first if a trigger fired. Slots
    /// between the etched grid and terrain so the field reads under terrain (spec section 5 order).
    /// </summary>
    public void DrawField()
    {
        if (_tableState == null) return;
    }

    /// <summary>
    /// Draws threat frontiers and secondary-pin contours as polylines. Slots between terrain and
    /// objectives so frontiers are never hidden by terrain (spec section 5 order).
    /// </summary>
    public void DrawContours()
    {
        if (_tableState == null) return;
    }

    // ---- ImGui pass (instruments) ----------------------------------------------------------------

    /// <summary>
    /// Handles overlay input (F toggle, hover timing, pin focus/clear) and marks rebuild triggers.
    /// Runs after TableHitTester.Update so hover state is fresh. Cheap -- heavy rebuilds happen in
    /// DrawField on the next frame's canvas pass.
    /// </summary>
    public void UpdateInput(double frameTimeSeconds, TableHitTester hitTester)
    {
        if (_tableState == null) return;
    }

    /// <summary>
    /// Draws per-frame instruments on the ImGui background draw list: pips, band labels, the live
    /// summary readout, the promoted measurement line, and ghost red-tint. All values here come from
    /// RulesProbe, never the texture.
    /// </summary>
    public void DrawInstruments(int screenW, int screenH)
    {
        if (_tableState == null) return;
    }
}
