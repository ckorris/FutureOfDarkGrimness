using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FDG;
using FDG.Players;
using FDG.Rules.Foundation;
using FDG.StageResolution.Requests;
using FdgRaylib.Rendering.Resolvers;
using ImGuiNET;
using Raylib_cs;

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
    private RulesProbe? _probe;
    private ThreatFrontierCache? _threat;
    private System.Action<string>? _warn;

    // World<->screen for the current frame, pushed once per frame right after ComputeLayout so both
    // the Raylib-pass draws and the ImGui-pass instruments read the same values (spec: no camera --
    // pan/zoom is just a different Layout, never a rebuild).
    private float _scale;
    private int   _originX;
    private int   _originY;
    private float _tableH;

    // Idle inspection toggle (F). Threat also shows automatically during a move job regardless of this.
    private bool _threatToggledOn;

    // Threat rebuild is driven by a per-frame signature poll: cheap to compute, and a rebuild fires only
    // when it changes (an enemy activated / lost models / became Shaken, a new round, the reference or an
    // enemy position moved). This is the "event-driven, never per-frame" rule implemented as a change
    // check rather than a web of subscriptions -- identical on host and client, no cross-thread handlers.
    private long _lastThreatSig;
    private bool _threatBuiltOnce;

    // Last-known reference (the player threat is measured against) so it survives brief gaps between a
    // move job ending and the next activation being known.
    private PlayerID? _lastRefPlayer;
    private float _lastRefRadius = TacticalOverlayConfig.DefaultReferenceRadiusInches;

    public bool ThreatToggledOn => _threatToggledOn;
    public void ToggleThreat() => _threatToggledOn = !_threatToggledOn;

    /// <summary>Wires the live game world. Called from RaylibRenderer.TransitionToGame.</summary>
    public void Attach(ITableState tableState, System.Action<string>? warn = null)
    {
        _tableState = tableState;
        _warn       = warn;
        _probe      = new RulesProbe(tableState);

        int w = (int)MathF.Ceiling(GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES  * TacticalOverlayConfig.TexelsPerInch);
        int h = (int)MathF.Ceiling(GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES * TacticalOverlayConfig.TexelsPerInch);
        _threat = new ThreatFrontierCache(w, h, TacticalOverlayConfig.TexelsPerInch);

        _threatBuiltOnce = false;
        _lastThreatSig   = 0;
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
        _probe        = null;
        _threat       = null;
        _warn         = null;
        _threatToggledOn = false;
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
        // Opportunity field lands in P3.
    }

    /// <summary>
    /// Draws threat frontiers and secondary-pin contours as polylines. Slots between terrain and
    /// objectives so frontiers are never hidden by terrain (spec section 5 order). Threat auto-shows
    /// during a move job; otherwise it is the F toggle's inspection layer.
    /// </summary>
    public void DrawContours()
    {
        if (_tableState == null || _threat == null || _probe == null) return;

        bool moveJobActive = _moveResolver?.ActiveRequest != null;
        bool visible = moveJobActive || _threatToggledOn;
        if (!visible) return;

        RebuildThreatIfNeeded();
        DrawThreatPolylines();
    }

    // ---- ImGui pass (instruments) ----------------------------------------------------------------

    /// <summary>
    /// Handles overlay input (F toggle, hover timing, pin focus/clear) and marks rebuild triggers.
    /// Runs after TableHitTester.Update so hover state is fresh. Cheap -- heavy rebuilds happen in
    /// DrawContours/DrawField on the next frame's canvas pass.
    /// </summary>
    public void UpdateInput(double frameTimeSeconds, TableHitTester hitTester)
    {
        if (_tableState == null) return;

        ImGuiIOPtr io = ImGui.GetIO();
        if (!io.WantCaptureKeyboard && ImGui.IsKeyPressed(TacticalOverlayConfig.ThreatToggleKey))
            _threatToggledOn = !_threatToggledOn;
    }

    /// <summary>
    /// Draws per-frame instruments on the ImGui background draw list: pips, band labels, the live
    /// summary readout, the promoted measurement line, and ghost red-tint. All values here come from
    /// RulesProbe, never the texture.
    /// </summary>
    public void DrawInstruments(int screenW, int screenH)
    {
        if (_tableState == null) return;
        // Pips / readouts / measurement land in P5.
    }

    // ---- Threat rebuild --------------------------------------------------------------------------

    private void RebuildThreatIfNeeded()
    {
        (PlayerID? refPlayer, float refRadius, _) = ResolveReference();
        if (refPlayer == null) { _threat!.Clear(); return; }

        List<IUnit> enemies = QualifyingEnemies(refPlayer.Value);
        long sig = ComputeThreatSignature(refPlayer, refRadius, enemies);
        if (_threatBuiltOnce && sig == _lastThreatSig) return;
        _lastThreatSig   = sig;
        _threatBuiltOnce = true;

        var discs = new List<ThreatDisc>();
        foreach (IUnit u in enemies)
        {
            (float charge, float shoot) = _probe!.ThreatReach(u);
            foreach (IModel m in u.Models)
            {
                if (!m.GetIsAlive()) continue;
                Position p = m.Position;
                if (p.x == 0f && p.z == 0f) continue;

                float er = m.BaseRadiusInches;
                float chargeR = charge + er + refRadius;
                float shootR  = shoot > 0f ? shoot + er + refRadius : 0f;
                discs.Add(new ThreatDisc(p.x, p.z, chargeR, shootR));
            }
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        // Simplify a hair under a texel so contours stay smooth but vertex counts stay sane for dashing.
        _threat!.Rebuild(discs, 1f / TacticalOverlayConfig.TexelsPerInch);
        sw.Stop();
        if (sw.Elapsed.TotalMilliseconds > TacticalOverlayConfig.RebuildBudgetMs)
            _warn?.Invoke($"[overlay] threat rebuild {sw.Elapsed.TotalMilliseconds:0}ms " +
                          $"(budget {TacticalOverlayConfig.RebuildBudgetMs:0}ms, {discs.Count} discs)");
    }

    /// <summary>
    /// Resolves the player threat is measured against and the reference base radius used to inflate
    /// discs. During a move job that's the moving player + moving unit's modal radius; otherwise the
    /// activating unit's; falling back to the last known so a brief gap doesn't blank the frontier.
    /// </summary>
    private (PlayerID? player, float radius, bool moveJobActive) ResolveReference()
    {
        DefineMovementPathRequest? req = _moveResolver?.ActiveRequest;
        if (req != null)
        {
            IUnit unit = req.UnitDataBinding.GetValue();
            _lastRefPlayer = req.TargetPlayerID;
            _lastRefRadius = _probe!.ModalBaseRadius(unit);
            return (_lastRefPlayer, _lastRefRadius, true);
        }

        IUnit? active = _tableState!.Progress.ActivatingUnit;
        if (active != null)
        {
            _lastRefPlayer = active.PlayerID;
            _lastRefRadius = _probe!.ModalBaseRadius(active);
            return (_lastRefPlayer, _lastRefRadius, false);
        }

        return (_lastRefPlayer, _lastRefRadius, false);
    }

    /// <summary>
    /// The enemy units that project threat this round: unactivated, hostile to the reference player,
    /// alive, on the battlefield, and not Shaken (Shaken units must idle when they activate, so they
    /// threaten nothing -- spec section 2).
    /// </summary>
    private List<IUnit> QualifyingEnemies(PlayerID refPlayer)
    {
        ITeam? refTeam = _tableState!.Teams.Objects.FirstOrDefault(t => t.IsPlayerOnTeam(refPlayer));

        var result = new List<IUnit>();
        foreach (IUnit u in _tableState.Progress.UnactivatedUnits)
        {
            bool enemy = refTeam != null ? !refTeam.IsPlayerOnTeam(u.PlayerID) : !u.PlayerID.Equals(refPlayer);
            if (!enemy) continue;
            if (!u.GetIsAlive()) continue;
            if (!u.GetIsOnBattlefield()) continue;
            if (u.Tokens.HasToken(TokenType.Shaken)) continue;
            result.Add(u);
        }
        return result;
    }

    // A cheap FNV-1a-style hash over everything that changes the threat picture; a rebuild fires only
    // when it differs from the last. Position quantized to ~0.5" so sub-inch presentation glide doesn't
    // thrash rebuilds while a genuine move still trips it.
    private long ComputeThreatSignature(PlayerID? refPlayer, float refRadius, List<IUnit> enemies)
    {
        unchecked
        {
            long h = 1469598103934665603L;
            void Mix(long v) => h = (h ^ v) * 1099511628211L;

            Mix(_tableState!.Progress.RoundCount ?? -1);
            Mix(refPlayer?.GetHashCode() ?? 0);
            Mix((long)(refRadius * 100f));

            foreach (IUnit u in enemies)
            {
                Mix(u.ID.GetHashCode());
                foreach (IModel m in u.Models)
                {
                    if (!m.GetIsAlive()) continue;
                    Position p = m.Position;
                    if (p.x == 0f && p.z == 0f) continue;
                    Mix((long)(p.x * 2f));
                    Mix((long)(p.z * 2f));
                }
            }
            return h;
        }
    }

    // ---- Contour drawing (Raylib) ----------------------------------------------------------------

    private void DrawThreatPolylines()
    {
        (byte r, byte g, byte b) = TacticalOverlayConfig.ThreatColor;
        var col = new Color(r, g, b, (byte)(TacticalOverlayConfig.ThreatContourAlpha * 255f));

        foreach (List<Float2> poly in _threat!.ChargePolylines)
            DrawWorldPolyline(poly, col, TacticalOverlayConfig.ThreatContourThicknessPx, dashed: false);
        foreach (List<Float2> poly in _threat.ShootPolylines)
            DrawWorldPolyline(poly, col, TacticalOverlayConfig.ThreatContourThicknessPx, dashed: true);
    }

    private Vector2 WorldToScreen(Float2 w) =>
        new(_originX + w.X * _scale, _originY + (_tableH - w.Y) * _scale);

    private void DrawWorldPolyline(List<Float2> poly, Color col, float thickness, bool dashed)
    {
        if (poly.Count < 2) return;

        if (!dashed)
        {
            for (int i = 0; i < poly.Count - 1; i++)
                Raylib.DrawLineEx(WorldToScreen(poly[i]), WorldToScreen(poly[i + 1]), thickness, col);
            return;
        }

        // Dashed: march screen-space arc length, carrying the dash phase across vertices so the pattern
        // reads continuous around the whole contour.
        float dashLen = TacticalOverlayConfig.ThreatDashLengthPx;
        float gapLen  = TacticalOverlayConfig.ThreatDashGapPx;
        bool on = true;
        float phase = 0f;

        for (int i = 0; i < poly.Count - 1; i++)
        {
            Vector2 a = WorldToScreen(poly[i]);
            Vector2 b = WorldToScreen(poly[i + 1]);
            float segLen = Vector2.Distance(a, b);
            if (segLen < 1e-3f) continue;
            Vector2 dir = (b - a) / segLen;

            float pos = 0f;
            while (pos < segLen)
            {
                float span = on ? dashLen - phase : gapLen - phase;
                float step = MathF.Min(span, segLen - pos);
                if (on)
                    Raylib.DrawLineEx(a + dir * pos, a + dir * (pos + step), thickness, col);
                pos   += step;
                phase += step;
                if (phase >= (on ? dashLen : gapLen) - 1e-4f) { on = !on; phase = 0f; }
            }
        }
    }
}
