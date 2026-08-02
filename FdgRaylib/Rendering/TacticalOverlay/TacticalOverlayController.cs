using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FDG;
using FDG.Players;
using FDG.Rules.Foundation;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FdgRaylib.Placement;
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
    private GuiResolverOverlay? _resolverOverlay;   // #230: source of the active placement's ghosts
    private RulesProbe? _probe;
    private ThreatFrontierCache? _threat;
    private System.Action<string>? _warn;

    private readonly FidelitySampler _sampler = new();
    // The inflated enemy discs from the last threat rebuild, so the sampler's rules-truth predicate can
    // check point-in-reach without re-deriving reach.
    private List<ThreatDisc> _lastThreatDiscs = new();

    // ---- Opportunity field / pins (P3) -----------------------------------------------------------
    private FieldMask? _bandMask;
    private FieldMask? _shadowMask;   // 1 = no LoS to the target from here (P4)
    private FieldMask? _coverMask;    // 1 = shot to the target passes through cover (P4)
    private FieldCompositor? _field;      // CPU reference renderer (fallback + harness ground truth)
    private GpuFieldRenderer? _gpuField;  // GPU rasterizer (default path; null if init failed)
    private IUnit? _fieldMovingUnit;  // context of the last field build, for the fidelity sampler
    private IUnit? _fieldTargetUnit;

    private sealed class PinnedTarget
    {
        public readonly IUnit Unit;
        // Index into AccentPalette. POSITIONAL: kept equal to the pin's slot in _pins (reindexed on
        // pin/unpin), so a single pin is always the lead accent and colours don't drift with history.
        public int Accent;
        public PinnedTarget(IUnit unit, int accent) { Unit = unit; Accent = accent; }
    }

    private readonly List<PinnedTarget> _pins = new();
    private int _focusIndex = -1;                 // index into _pins; -1 when none
    private DefineMovementPathRequest? _lastSeenRequest; // pins are scoped to one move job

    // Hover preview: an enemy hovered with no pins shows a transient dimmed target-anchored field.
    private IUnit? _hoverCandidate;
    private double _hoverElapsed;

    // #247: the unit under the cursor this frame, captured in UpdateInput and consumed by DrawField as the
    // top-priority field anchor. Null while the pointer is over an ImGui panel.
    private IUnit? _hoverUnit;

    // #247: which anchor won last frame. Replaces the retired GhostAnchoredField flag for the two places
    // that needed to know whether the drawn field is TARGET-anchored — the fidelity sampler's field
    // channels and the band snap — because that was always the real question, not what mode was selected.
    private FieldAnchorKind _lastAnchorKind = FieldAnchorKind.None;

    private long _lastFieldSig;
    private bool _fieldBuiltOnce;
    // #230: true between DrawField painting a field and the next ClearFieldPictures. The ImGui-pass
    // instruments (band labels) run after the canvas pass, so this tells them a picture is on screen
    // without their having to re-derive which anchor produced it.
    private bool _fieldActive;
    // True when _shadowMask/_coverMask hold a CURRENT target-anchored classification (ClassifyInto ran
    // for the live pin) -- the precondition for the sampler's field channels. False in ghost mode.
    private bool _fieldMasksValid;

    private readonly record struct BandLabel(Float2 World, string Text, int Accent);
    private List<BandLabel> _bandLabels = new();

    // Context of the last field build, so the fidelity sampler can reconstruct the band/LoS/cover truth.
    private List<BandSpec> _lastFieldBands = new();
    private float _lastShooterRadius;

    // Per-frame blocker cache: DrawPips and DrawCountRows both need each pin's terrain+model blockers, and
    // DrawInstruments (which clears this) always runs before DrawPanelSection in the frame, so one build
    // per pin per frame suffices.
    private readonly Dictionary<IUnit, List<ITerrain>> _frameBlockers = new();

    private List<ITerrain> BlockersFor(IUnit movingUnit, IUnit pinUnit)
    {
        if (!_frameBlockers.TryGetValue(pinUnit, out List<ITerrain>? b))
            _frameBlockers[pinUnit] = b = _probe!.BuildBlockers(movingUnit, pinUnit);
        return b;
    }

    // World<->screen for the current frame, pushed once per frame right after ComputeLayout so both
    // the Raylib-pass draws and the ImGui-pass instruments read the same values (spec: no camera --
    // pan/zoom is just a different Layout, never a rebuild).
    private float _scale;
    private int   _originX;
    private int   _originY;
    private float _tableH;

    // Idle inspection toggle (F). Threat also shows automatically during a move job regardless of this.

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

    // Idle-click isolation (P7): one enemy's frontier brightens while the aggregate dims. Idle only.

    // Secondary-pin contours (P7): each non-focused pin's longest-range boundary in its accent. Reuses a
    // scratch mask for the disc-union march (isolation and secondaries never run in the same frame -- one
    // is idle, the other a move job).
    private FieldMask? _scratchMask;
    private readonly List<(int accent, List<List<Float2>> polylines)> _secondaryContours = new();

    // Band boundary rings for the focused field, as VECTOR polylines drawn on top of the fill texture at a
    // fixed screen thickness -- so the range lines stay consistently thin at any zoom (playtest feedback),
    // unlike a texture-baked ring that bilinear-stretches into variable chunkiness. Empty in ghost mode
    // (marching every frame would be too costly; that mode is fill-only).
    private List<List<Float2>> _fieldRings = new();


    // Maps a unit to its team color (fill/rings/labels/pips), so the field reads as the SELECTED unit's
    // own threat -- red when an enemy is picked ("where I'm in danger"), the mover's color in Self mode.
    private Func<PlayerID, (byte r, byte g, byte b)>? _teamColor;
    private (byte r, byte g, byte b) TeamAccent(IUnit unit) =>
        _teamColor?.Invoke(unit.PlayerID) ?? TacticalOverlayConfig.AccentPalette[0];

    // The current field's accent color (the selected unit's team color); labels and pips read it so all
    // of the field's marks share one identity.
    private (byte r, byte g, byte b) _fieldAccentCol = TacticalOverlayConfig.AccentPalette[0];

    /// <summary>Wires the live game world. Called from RaylibRenderer.TransitionToGame.</summary>
    /// <param name="coverProximityExceptions">The launched game's #201 cover setting; the probe's
    /// pips apply it so they match the engine's cover stage (the field texture stays raw - see
    /// <see cref="PolarSightMap"/>).</param>
    public void Attach(ITableState tableState, System.Action<string>? warn = null,
        Func<PlayerID, (byte r, byte g, byte b)>? teamColor = null,
        bool coverProximityExceptions = true)
    {
        _tableState = tableState;
        _warn       = warn;
        _teamColor  = teamColor;
        _probe      = new RulesProbe(tableState, coverProximityExceptions);

        int w = (int)MathF.Ceiling(GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES  * TacticalOverlayConfig.TexelsPerInch);
        int h = (int)MathF.Ceiling(GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES * TacticalOverlayConfig.TexelsPerInch);
        _threat     = new ThreatFrontierCache(w, h, TacticalOverlayConfig.TexelsPerInch);
        _bandMask    = new FieldMask(w, h, TacticalOverlayConfig.TexelsPerInch);
        _shadowMask  = new FieldMask(w, h, TacticalOverlayConfig.TexelsPerInch);
        _coverMask   = new FieldMask(w, h, TacticalOverlayConfig.TexelsPerInch);
        _scratchMask = new FieldMask(w, h, TacticalOverlayConfig.TexelsPerInch);
        _field       = new FieldCompositor(w, h);

        // GPU field path (default). Attach runs on the main thread with the window live (TransitionToGame
        // fires from the render loop), so GL resource creation is safe here. Failure = CPU fallback.
        _gpuField = new GpuFieldRenderer(w, h, TacticalOverlayConfig.TexelsPerInch);
        if (!_gpuField.TryInit())
        {
            _gpuField = null;
            _warn?.Invoke("[overlay] GPU field unavailable - using CPU field renderer");
        }

        _threatBuiltOnce = false;
        _lastThreatSig   = 0;
        ClearPins();
    }

    /// <summary>
    /// Wires the movement resolver so the controller can read the active move job (pull model, thread
    /// safe) and the resolver can route enemy clicks / draw the pin panel section. Null when no GUI
    /// movement resolver exists (headless never reaches here). Called from TransitionToGame.
    /// </summary>
    public void AttachMovementResolver(GuiDefineMovementResolver? resolver)
    {
        _moveResolver = resolver;
        resolver?.SetTacticalOverlay(this);
    }

    /// <summary>
    /// #230: wires the resolver overlay so the controller can ask which resolver is pending and whether it
    /// offers ghosts to anchor the field on (<see cref="IGhostFieldSource"/>). Kept as the overlay rather
    /// than a resolver list so a new opt-in resolver needs no change here. Called from TransitionToGame.
    /// </summary>
    public void AttachResolverOverlay(GuiResolverOverlay? overlay) => _resolverOverlay = overlay;

    /// <summary>Drops every per-game reference and cached picture. Called from ExitGame.</summary>
    public void Detach()
    {
        _moveResolver?.SetTacticalOverlay(null);
        _field?.Dispose();
        _gpuField?.Dispose();
        _gpuField     = null;
        _field        = null;
        _bandMask     = null;
        _shadowMask   = null;
        _coverMask    = null;
        _scratchMask  = null;
        _tableState   = null;
        _moveResolver = null;
        _resolverOverlay = null;
        _probe        = null;
        _threat       = null;
        _warn         = null;
        _secondaryContours.Clear();
        _fieldActive = false;
        _lastAnchorKind = FieldAnchorKind.None;
        _hoverUnit = null;
        ClearPins();
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
        if (_tableState == null || _field == null || _bandMask == null || _probe == null) return;

        // #247: one contest decides the anchor, so exactly one field can be on screen. Everything the
        // decision needs is gathered here; FieldAnchorPlan owns the priority order.
        DefineMovementPathRequest? req = _moveResolver?.ActiveRequest;
        IGhostFieldSource? placement = req == null ? _resolverOverlay?.ActiveGhostField : null;
        IUnit? placeUnit = null;
        IReadOnlyDictionary<IModel, Position>? placeGhosts = null;
        if (placement != null && placement.TryGetGhostField(out IUnit pu, out var pg))
        {
            placeUnit = pu;
            placeGhosts = pg;
        }
        bool placementGhosts = placeUnit != null;
        IUnit? hoverUnit = ResolveHoverAnchor(req, placeUnit);
        (IUnit? target, int accent, float alphaScale) = req != null
            ? ResolveFieldTarget(req)
            : (null, 0, 1f);

        FieldAnchorKind anchor = FieldAnchorPlan.Resolve(
            showReach: ViewSettings.ShowReachOverlay,
            hoverAvailable: hoverUnit != null,
            moveJobActive: req != null,
            pinnedTargetAvailable: target != null,
            placementGhostsAvailable: placementGhosts);
        _lastAnchorKind = anchor;

        bool drew;
        switch (anchor)
        {
            case FieldAnchorKind.Hover:
                // Stationary anchor -> the signature gate inside turns this into one rebuild per hovered
                // unit rather than one per frame.
                drew = RebuildGhostField(hoverUnit!, LivePositions(hoverUnit!), req: null);
                break;

            case FieldAnchorKind.Ghost when req != null:
                drew = RebuildGhostField(req.UnitDataBinding.GetValue(), _moveResolver!.GhostPositions, req);
                break;

            case FieldAnchorKind.Ghost:
                drew = RebuildGhostField(placeUnit!, placeGhosts!, req: null);
                break;

            case FieldAnchorKind.Target:
                RebuildFieldIfNeeded(req!, target!, accent, alphaScale);
                drew = true;
                break;

            default:
                ClearFieldPictures();
                return;
        }

        if (!drew) { ClearFieldPictures(); return; }
        _fieldActive = true;
        if (_gpuField != null && TacticalOverlayConfig.UseGpuField)
            _gpuField.DrawComposite(_originX, _originY,
                GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES, _scale);
        else
            _field.Draw(_originX, _originY,
                GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES, _scale);
    }

    // ---- Ghost-anchored field (H4): "what can I hit from here", per frame --------------------------

    /// <summary>
    /// #247 — the unit under the cursor, when inspecting it should take the field over whatever else is on
    /// screen. Null when nothing is hovered, or when the hovered unit is the very one whose ghosts are
    /// live: its models still stand at their ORIGINAL positions, so anchoring there mid-aim would replace
    /// the picture the player is using with one about where they used to be. That exclusion is also what
    /// keeps the field from flickering as the cursor transits its own unit, now that the hover dwell is
    /// disabled (see <see cref="TacticalOverlayConfig.HoverPreviewDelaySeconds"/>).
    /// </summary>
    private IUnit? ResolveHoverAnchor(DefineMovementPathRequest? req, IUnit? placementUnit)
    {
        IUnit? hovered = _hoverUnit;
        if (hovered == null) return null;

        IUnit? ghosting = req != null ? req.UnitDataBinding.GetValue() : placementUnit;
        if (ghosting != null && ReferenceEquals(ghosting, hovered)) return null;

        return hovered;
    }

    /// <summary>Where a unit's living, deployed models actually stand — the anchor for a hover field.</summary>
    private static Dictionary<IModel, Position> LivePositions(IUnit unit)
    {
        var positions = new Dictionary<IModel, Position>();
        foreach (IModel m in unit.Models)
        {
            if (!m.GetIsAlive()) continue;
            Position p = m.Position;
            if (p.x == 0f && p.z == 0f) continue;   // undeployed / in reserve
            positions[m] = p;
        }
        return positions;
    }

    // Approximations, deliberate and documented: the reach discs inflate by each model's own base
    // radius plus the DEFAULT 28mm target radius (there is no specific target); LoS blockers exclude
    // only the mover's team (a real shot at unit X would also ignore X's own models). Pips stay the
    // per-target truth. Band labels are suppressed (they'd ride the cursor); pins keep chips/counts and
    // their secondary contours.
    //
    // #230: `req` is null for a placement- or hover-anchored build. Everything it feeds is pin-related —
    // the per-weapon range overrides and the secondary contours — and pins only exist during a move job,
    // so a null request simply takes the no-pin path rather than needing an equivalent.
    //
    // #247: signature-gated. Real ghosts move every frame, so their signature changes every frame and this
    // rebuilds exactly as it always did; a HOVERED unit is stationary, so its signature holds and the whole
    // rebuild — polar sight maps and the GPU upload, the expensive parts — is skipped after the first
    // frame. The per-frame/cached split is emergent, not a mode.
    //
    // Returns false when there is nothing to draw (no ranged weapons, or no model on the table).
    private bool RebuildGhostField(IUnit movingUnit, IReadOnlyDictionary<IModel, Position> ghosts,
        DefineMovementPathRequest? req)
    {
        // Distinct effective ranges -> nested bands (vs the focused pin if any, else raw ranges).
        IUnit? rangeTarget = req != null && _pins.Count > 0 && _focusIndex >= 0 && _focusIndex < _pins.Count
            ? _pins[_focusIndex].Unit : null;
        var byRange = new Dictionary<float, List<GpuFieldRenderer.BandDisc>>();
        var byRangeNames = new Dictionary<float, Dictionary<string, int>>();   // for band labels ("4x Rifle")
        var sources = new List<Position>();

        foreach (IModel m in movingUnit.Models)
        {
            if (!m.GetIsAlive()) continue;
            if (!ghosts.TryGetValue(m, out Position gp)) continue;
            if (gp.x == 0f && gp.z == 0f) continue;
            sources.Add(gp);

            foreach (Weapon w in m.Weapons)
            {
                if (w.RangeInches <= 0f) continue;
                float eff = rangeTarget != null
                    ? EffectiveRange(req!, w.Name, rangeTarget.ID, w.RangeInches)
                    : w.RangeInches;
                if (!byRange.TryGetValue(eff, out List<GpuFieldRenderer.BandDisc>? discs))
                    byRange[eff] = discs = new List<GpuFieldRenderer.BandDisc>();
                discs.Add(new GpuFieldRenderer.BandDisc(gp.x, gp.z,
                    eff + m.BaseRadiusInches + TacticalOverlayConfig.DefaultReferenceRadiusInches));
                if (!byRangeNames.TryGetValue(eff, out Dictionary<string, int>? names))
                    byRangeNames[eff] = names = new Dictionary<string, int>();
                names[w.Name] = names.GetValueOrDefault(w.Name) + 1;
            }
        }

        if (byRange.Count == 0 || sources.Count == 0) { ClearFieldPictures(); return false; }

        // #247: everything below is the expensive half (polar sight maps + GPU upload). Skip it whole when
        // nothing that shapes the picture has moved — which is every frame after the first for a hovered
        // unit, and no frame at all for live ghosts.
        long signature = ComputeGhostFieldSignature(movingUnit, sources, byRange.Keys);
        if (_fieldActive && signature == _lastGhostFieldSig) return true;
        _lastGhostFieldSig = signature;

        var ranges = byRange.Keys.ToList();
        ranges.Sort();
        var perBand = new List<(BandSpec band, List<GpuFieldRenderer.BandDisc> discs)>(ranges.Count);
        for (int i = 0; i < ranges.Count; i++)
        {
            string names = string.Join(" / ",
                byRangeNames[ranges[i]].OrderBy(kv => kv.Key).Select(kv => $"{kv.Value}x {kv.Key}"));
            var band = new BandSpec(ranges[i], (byte)(ranges.Count - i), $"{ranges[i]:0.#}\" {names}");
            perBand.Add((band, byRange[ranges[i]]));
        }

        // LoS maps from the ghost positions; blockers exclude only the mover's team (see note above).
        List<ITerrain> relevant = _probe!.BuildBlockers(movingUnit, movingUnit)
            .Where(t => t.TerrainType.HasFlag(ETerrainType.Blocking) || t.TerrainType.HasFlag(ETerrainType.Cover))
            .ToList();
        var maps = new List<PolarSightMap>();
        if (relevant.Count > 0)
            foreach (Position s in sources)
                maps.Add(PolarSightMap.Build(s, relevant, TacticalOverlayConfig.PolarBuckets));

        (byte r, byte g, byte b) accentCol = TeamAccent(movingUnit);   // Self mode: the mover's team color
        _fieldAccentCol = accentCol;

        var sw = System.Diagnostics.Stopwatch.StartNew();
        bool gpuActive = _gpuField != null && TacticalOverlayConfig.UseGpuField;
        if (gpuActive)
        {
            _gpuField!.RebuildDiscs(perBand, maps, accentCol, 1f);
        }
        else
        {
            // CPU fallback: functional but per-frame heavy (classify + compose + upload); the GPU path
            // is the intended engine for this mode.
            _bandMask!.Clear();
            var orderedCpu = new List<(BandSpec band, List<GpuFieldRenderer.BandDisc> discs)>(perBand);
            orderedCpu.Sort((a, b) => a.band.Value.CompareTo(b.band.Value));
            foreach ((BandSpec band, List<GpuFieldRenderer.BandDisc> discs) in orderedCpu)
                foreach (GpuFieldRenderer.BandDisc d in discs)
                    _bandMask.RasterizeDiscMax(d.X, d.Z, d.RadiusInches, band.Value);
            PolarSightMap.ClassifyInto(_bandMask, _shadowMask!, _coverMask!, maps);
            _field!.Compose(_bandMask, _shadowMask!, _coverMask!, accentCol, 1f);
        }
        sw.Stop();

        // Per-frame mode: throttle the budget warning so a slow fallback doesn't spam the log.
        if (sw.Elapsed.TotalMilliseconds > TacticalOverlayConfig.RebuildBudgetMs &&
            Environment.TickCount64 - _lastGhostWarnMs > 5000)
        {
            _lastGhostWarnMs = Environment.TickCount64;
            _warn?.Invoke($"[overlay] ghost field rebuild {sw.Elapsed.TotalMilliseconds:0}ms/frame " +
                          $"(budget {TacticalOverlayConfig.RebuildBudgetMs:0}ms) - consider Field: GPU");
        }

        // Band labels: one per range, placed analytically on each ring toward the board centre (no mask
        // ray-march -- the GPU path has no CPU band mask). They ride the ghosts as the unit moves.
        float gcx = 0f, gcz = 0f;
        foreach (Position s in sources) { gcx += s.x; gcz += s.z; }
        gcx /= sources.Count; gcz /= sources.Count;
        float dirx = GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES * 0.5f - gcx;
        float dirz = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES * 0.5f - gcz;
        float dlen = MathF.Sqrt(dirx * dirx + dirz * dirz);
        if (dlen < 1e-3f) { dirx = 0f; dirz = 1f; } else { dirx /= dlen; dirz /= dlen; }
        float labelR = _probe.ModalBaseRadius(movingUnit) + TacticalOverlayConfig.DefaultReferenceRadiusInches;
        var ghostLabels = new List<BandLabel>(perBand.Count);
        foreach ((BandSpec band, _) in perBand)
        {
            float rr = band.RangeInches + labelR;
            ghostLabels.Add(new BandLabel(new Float2(gcx + dirx * rr, gcz + dirz * rr), band.Label, 0));
        }
        _bandLabels = ghostLabels;
        _fieldRings = new List<List<Float2>>();              // GPU draws rings; per-frame CPU marching too costly
        _lastFieldBands = perBand.Select(pb => pb.band).ToList();  // distance readout still promotes
        _fieldMasksValid = false;  // ghost-anchored masks must not feed the target-anchored sampler truth
        InvalidateField();  // leaving ghost mode must force a target-anchored rebuild
        if (req != null) BuildSecondaryContours(req, movingUnit, _probe.ModalBaseRadius(movingUnit));
        else _secondaryContours.Clear();   // #230: no pins during a placement/hover, so no secondary rings
        return true;
    }

    /// <summary>
    /// #247 — what makes this ghost field's picture what it is: the anchor unit, where its sources stand
    /// (quantized to 0.1", so sub-inch presentation glide doesn't thrash rebuilds), its distinct effective
    /// ranges, and the blockers that shape LoS. Blockers matter because a hover field is otherwise
    /// perfectly stable: a model walking between the hovered unit and open ground must repaint the shadow.
    /// </summary>
    private long ComputeGhostFieldSignature(IUnit anchor, List<Position> sources, IEnumerable<float> ranges)
    {
        unchecked
        {
            long h = 1469598103934665603L;
            void Mix(long v) => h = (h ^ v) * 1099511628211L;

            Mix(anchor.ID.GetHashCode());
            foreach (Position s in sources) { Mix((long)(s.x * 10f)); Mix((long)(s.z * 10f)); }
            foreach (float r in ranges) Mix((long)(r * 100f));

            // Every other unit's models are potential LoS blockers; the anchor's own are excluded from
            // BuildBlockers, so they can't change this picture and are skipped here too.
            foreach (IUnit u in _tableState!.Units.Objects)
            {
                if (ReferenceEquals(u, anchor)) continue;
                foreach (IModel m in u.Models)
                {
                    if (!m.GetIsAlive()) continue;
                    Position p = m.Position;
                    if (p.x == 0f && p.z == 0f) continue;
                    Mix((long)(p.x * 4f)); Mix((long)(p.z * 4f));
                }
            }
            return h;
        }
    }

    private long _lastGhostFieldSig;

    private long _lastGhostWarnMs;

    private void ClearFieldPictures()
    {
        _field?.Clear();
        _gpuField?.Clear();
        _bandLabels = new List<BandLabel>();
        _secondaryContours.Clear();
        _fieldRings = new List<List<Float2>>();
        _fieldMasksValid = false;
        _fieldActive = false;
        // Nothing is on screen, so nothing is target-anchored: the band snap and the sampler's field
        // channels must not act on the anchor that was about to draw but didn't.
        _lastAnchorKind = FieldAnchorKind.None;
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

        // #247: the red threat frontiers are GONE as a visual — the reach field answers "what threatens
        // this ground" in one vocabulary now, and hovering an enemy shows its reach directly, so a second
        // red contour layer on its own F toggle was outdated clutter. The threat DISCS are still built,
        // because the frontier snap and the "inside enemy reach" readout are computed from them; both are
        // move-job-only, so the rebuild is too (it used to also run whenever the toggle was on).
        if (moveJobActive) RebuildThreatIfNeeded();

        // The band rings ride with the field, whatever anchored it (#247 — this was gated on a move job,
        // so placement and hover fields drew the fill wash with no boundary rings. The rings ARE the band
        // delineation; the fill is deliberately a light atmosphere layer, so without them the picture
        // loses most of its information).
        if (_fieldActive)
        {
            // GPU draws rings in screen space (constant thin width, every anchor); the CPU vector rings
            // are the fallback (target-anchored only) and are empty when the GPU handles them.
            if (_gpuField != null && TacticalOverlayConfig.UseGpuField && _gpuField.RingsReady)
                _gpuField.DrawRings(_originX, _originY,
                    GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES, _scale);
            else
                DrawFieldRings();
        }

        // Secondary-pin contours are pin-only, so they stay scoped to a move job.
        if (moveJobActive) DrawSecondaryContours();
    }

    // Band boundary rings for the focused field, as consistent-thickness vector lines in the lead accent.
    private void DrawFieldRings()
    {
        if (_fieldRings.Count == 0) return;
        (byte r, byte g, byte b) = TacticalOverlayConfig.AccentPalette[0];
        var col = new Color(r, g, b, (byte)(TacticalOverlayConfig.BandBoundaryAlpha * 255f));
        foreach (List<Float2> poly in _fieldRings)
            DrawWorldPolyline(poly, col, TacticalOverlayConfig.BandBoundaryThicknessPx);
    }

    private void DrawSecondaryContours()
    {
        foreach ((int accent, List<List<Float2>> polylines) in _secondaryContours)
        {
            (byte r, byte g, byte b) = TacticalOverlayConfig.AccentPalette[accent % TacticalOverlayConfig.AccentPalette.Length];
            var col = new Color(r, g, b, (byte)(0.9f * 255f));
            foreach (List<Float2> poly in polylines)
                DrawWorldPolyline(poly, col, TacticalOverlayConfig.BandBoundaryThicknessPx);
        }
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
        // Hotkeys are muted while the in-game menu owns input (#246).
        bool wantKeys = !io.WantCaptureKeyboard && !EscapeRouter.MenuOpen;
        // #247: V is the master reach toggle, handled here rather than in any one resolver so it works
        // identically while moving, placing, and idle. The placement panel's checkbox drives the same flag.
        if (wantKeys && ImGui.IsKeyPressed(TacticalOverlayConfig.ReachToggleKey))
            ViewSettings.ShowReachOverlay = !ViewSettings.ShowReachOverlay;

        // #247: the hover anchor. Captured here (input phase, hover state fresh) and consumed by DrawField
        // on the next canvas pass. No dwell — see TacticalOverlayConfig.HoverPreviewDelaySeconds.
        _hoverUnit = io.WantCaptureMouse ? null : hitTester.HoveredUnit;

        if (wantKeys && ImGui.IsKeyPressed(TacticalOverlayConfig.FidelitySamplerKey))
        {
            _sampler.Enabled = !_sampler.Enabled;
            // The sampler's field channels read the CPU masks, which the GPU-default path skips
            // computing -- force a rebuild so they materialize while sampling.
            InvalidateField();
            _warn?.Invoke($"[overlay] fidelity sampler {(_sampler.Enabled ? "ON" : "OFF")}");
        }

        // Pins are scoped to one move job -- clear when the job changes or ends.
        DefineMovementPathRequest? req = _moveResolver?.ActiveRequest;
        if (!ReferenceEquals(req, _lastSeenRequest))
        {
            _lastSeenRequest = req;
            ClearPins();
        }

        if (req != null)
        {
            // Esc clears the pin (single-pin: no focus cycling; no move-cancel exists today -- plan C1).
            // Routed through EscapeRouter (#246) so a pin claims Esc before the in-game menu can open.
            if (!io.WantCaptureKeyboard && _pins.Count > 0 && EscapeRouter.TryConsumeEscape())
                ClearPins();

            UpdateHover(frameTimeSeconds, hitTester, req);
        }
        else
        {
            _hoverCandidate = null;
            _hoverElapsed   = 0;
            // #247: idle threat-frontier isolation (click an enemy to brighten its contour) went with the
            // frontiers themselves — hovering a unit now shows its reach directly, which is the same
            // question asked better.
        }
    }

    /// <summary>
    /// Draws per-frame instruments on the ImGui background draw list: pips, band labels, the live
    /// summary readout, the promoted measurement line, and ghost red-tint. All values here come from
    /// RulesProbe, never the texture.
    /// </summary>
    public void DrawInstruments(int screenW, int screenH)
    {
        if (_tableState == null) return;
        _frameBlockers.Clear();

        DrawBandLabels();

        DefineMovementPathRequest? req = _moveResolver?.ActiveRequest;
        if (req != null)
        {
            IUnit movingUnit = req.UnitDataBinding.GetValue();
            DrawPips(req, movingUnit);
            // DrawGhostThreatTint (red "this ghost is inside enemy threat" ring) was REPLACED by the
            // team-colored opportunity field: a ghost sitting inside the enemy's field reads the danger
            // directly. Re-enable this call if a discrete per-ghost danger marker is wanted again.
            // DrawGhostThreatTint(movingUnit);
            DrawDistanceReadout(req, movingUnit);
        }

        if (_sampler.Enabled)
            RunAndDrawFidelitySampler(screenW, screenH);
    }

    // ---- Fidelity sampler (spec section 6) -------------------------------------------------------

    private void RunAndDrawFidelitySampler(int screenW, int screenH)
    {
        if (_threat == null || _probe == null) return;

        // Ensure the masks reflect current state even when the frontier isn't being shown, so the sampler
        // works as a standalone debug check. Idempotent when the signature is unchanged.
        RebuildThreatIfNeeded();
        List<ThreatDisc> discs = _lastThreatDiscs;

        var channels = new List<FidelitySampler.Channel>
        {
            new("threat-charge",
                (x, z) => _threat.SampleChargeInside(x, z),
                (x, z) => AnyThreatDisc(discs, x, z, charge: true)),
            new("threat-shoot",
                (x, z) => _threat.SampleShootInside(x, z),
                (x, z) => AnyThreatDisc(discs, x, z, charge: false)),
        };

        // Opportunity-field channels: only when the CPU masks hold a current TARGET-ANCHORED
        // classification (ghost mode's truth is different geometry; GPU-only frames skip the masks).
        if (_bandMask != null && _shadowMask != null && _coverMask != null &&
            _fieldTargetUnit != null && _fieldMovingUnit != null &&
            _fieldMasksValid && _lastAnchorKind == FieldAnchorKind.Target)
        {
            IUnit target = _fieldTargetUnit;
            float longest = _lastFieldBands.Count > 0 ? _lastFieldBands.Max(b => b.RangeInches) : 0f;
            List<ITerrain> blockers = _probe.BuildBlockers(_fieldMovingUnit, target);

            bool BandTruth(float x, float z)
            {
                if (longest <= 0f) return false;
                foreach (IModel m in target.Models)
                {
                    if (!m.GetIsAlive()) continue;
                    Position p = m.Position;
                    if (p.x == 0f && p.z == 0f) continue;
                    float r = longest + _lastShooterRadius + m.BaseRadiusInches;
                    float dx = x - p.x, dz = z - p.z;
                    if (dx * dx + dz * dz <= r * r) return true;
                }
                return false;
            }

            channels.Add(new FidelitySampler.Channel("field-band",
                (x, z) => _bandMask.SampleAt(x, z) > 0,
                BandTruth));
            channels.Add(new FidelitySampler.Channel("field-los",
                (x, z) => _bandMask.SampleAt(x, z) > 0 && _shadowMask.SampleAt(x, z) == 0,
                (x, z) => BandTruth(x, z) && _probe.BestSight(new Position(x, z), target, blockers) != ESightLineEffect.Blocking));
            channels.Add(new FidelitySampler.Channel("field-cover",
                (x, z) => _bandMask.SampleAt(x, z) > 0 && _shadowMask.SampleAt(x, z) == 0 && _coverMask.SampleAt(x, z) > 0,
                (x, z) => BandTruth(x, z) && _probe.BestSight(new Position(x, z), target, blockers) == ESightLineEffect.Cover));
        }

        FidelitySampler.Report report = _sampler.Run(
            GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES,
            GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES,
            2f, channels);

        DrawSamplerMarkers(report);
        DrawSamplerSummary(report, channels.Count, screenW);
    }

    private static bool AnyThreatDisc(List<ThreatDisc> discs, float x, float z, bool charge)
    {
        foreach (ThreatDisc d in discs)
        {
            float r = charge ? d.ChargeRadius : d.ShootRadius;
            if (r <= 0f) continue;
            float dx = x - d.X, dz = z - d.Z;
            if (dx * dx + dz * dz <= r * r) return true;
        }
        return false;
    }

    // Exact point-in-reach against the enemy discs (the rules-consistent computation), NOT a sample of the
    // approximate threat mask -- the "inside a frontier" determination the player acts on (ghost red-tint,
    // the incoming-threat row, the snap-outside test) must not read the picture (spec section 0).
    private bool GhostInThreat(float x, float z, out bool charge, out bool shoot)
    {
        charge = false;
        shoot = false;
        foreach (ThreatDisc d in _lastThreatDiscs)
        {
            float dx = x - d.X, dz = z - d.Z;
            float d2 = dx * dx + dz * dz;
            if (d2 <= d.ChargeRadius * d.ChargeRadius) charge = true;
            if (d.ShootRadius > 0f && d2 <= d.ShootRadius * d.ShootRadius) shoot = true;
            if (charge && shoot) break;
        }
        return charge || shoot;
    }

    private void DrawSamplerMarkers(FidelitySampler.Report report)
    {
        ImDrawListPtr dl = ImGui.GetBackgroundDrawList();
        uint col = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0f, 1f, 0.95f)); // magenta X
        const float rad = 4f;
        foreach (FidelitySampler.Mismatch mm in report.Points)
        {
            Vector2 c = WorldToScreen(new Float2(mm.X, mm.Z));
            dl.AddLine(c + new Vector2(-rad, -rad), c + new Vector2(rad, rad), col, 1.5f);
            dl.AddLine(c + new Vector2(-rad, rad), c + new Vector2(rad, -rad), col, 1.5f);
        }
    }

    private void DrawSamplerSummary(FidelitySampler.Report report, int channelCount, int screenW)
    {
        ImGui.SetNextWindowPos(new Vector2(screenW - 260f, 8f), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.80f);
        ImGui.Begin("##fidelity",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoTitleBar |
            ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav);

        ImGui.TextUnformatted($"Fidelity sampler (F10)   {report.SampleCount} pts @ 2\"");
        ImGui.Separator();
        foreach ((string name, int mismatches) in report.PerChannel)
        {
            float pct = report.SampleCount > 0 ? 100f * mismatches / report.SampleCount : 0f;
            ImGui.TextUnformatted($"{name,-14} {mismatches,4}  ({pct:0.0}%)");
        }
        ImGui.Separator();
        ImGui.TextUnformatted($"overall mismatch: {report.MismatchPercent(channelCount):0.0}%");
        ImGui.TextDisabled("edge-texel noise is expected");
        ImGui.End();
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
            discs.AddRange(BuildUnitDiscs(u, refRadius));
        _lastThreatDiscs = discs;

        float eps = 1f / TacticalOverlayConfig.TexelsPerInch; // simplify a hair under a texel
        var sw = System.Diagnostics.Stopwatch.StartNew();
        // The polylines still feed SnapInputPoint's frontier snap; nothing draws them since #247.
        _threat!.Rebuild(discs, eps);
        sw.Stop();
        if (sw.Elapsed.TotalMilliseconds > TacticalOverlayConfig.RebuildBudgetMs)
            _warn?.Invoke($"[overlay] threat rebuild {sw.Elapsed.TotalMilliseconds:0}ms " +
                          $"(budget {TacticalOverlayConfig.RebuildBudgetMs:0}ms, {discs.Count} discs)");
    }

    private List<ThreatDisc> BuildUnitDiscs(IUnit u, float refRadius)
    {
        var discs = new List<ThreatDisc>();
        (float charge, float shoot) = _probe!.ThreatReach(u);
        foreach (IModel m in u.Models)
        {
            if (!m.GetIsAlive()) continue;
            Position p = m.Position;
            if (p.x == 0f && p.z == 0f) continue;
            float er = m.BaseRadiusInches;
            discs.Add(new ThreatDisc(p.x, p.z, charge + er + refRadius, shoot > 0f ? shoot + er + refRadius : 0f));
        }
        return discs;
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
        var result = new List<IUnit>();
        foreach (IUnit u in _tableState!.Progress.UnactivatedUnits)
        {
            if (!IsEnemyOf(refPlayer, u)) continue;
            if (!u.GetIsAlive()) continue;
            if (!u.GetIsOnBattlefield()) continue;
            if (u.Tokens.HasToken(TokenType.Shaken)) continue;
            result.Add(u);
        }
        return result;
    }

    // A cheap FNV-1a-style hash over everything that changes the threat picture; a rebuild fires only
    // when it differs from the last. Position quantized to 0.1" so sub-inch presentation glide doesn't
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
                    // 0.1" quantization: fine enough that a sub-0.5" enemy shift (pile-in nudge, push) still
                    // rebuilds the discs that GhostInThreat / SnapInputPoint read, yet coarse enough not to
                    // thrash on presentation glide.
                    Mix((long)(p.x * 10f));
                    Mix((long)(p.z * 10f));
                }
            }
            return h;
        }
    }

    // ---- Opportunity field: pins -----------------------------------------------------------------

    /// <summary>
    /// Routes an enemy click during a move job to pin/unpin (spec section 3): clicking an unpinned enemy
    /// pins + focuses it, clicking a pinned enemy unpins it. Returns true when it consumed the click, so
    /// the move resolver doesn't also treat it as a waypoint. No-op outside a move job.
    /// </summary>
    public bool TryHandleEnemyClick(IUnit unit)
    {
        if (_moveResolver?.ActiveRequest == null) return false;

        // Single pin: clicking the pinned enemy unpins; clicking any other REPLACES it.
        if (_pins.Count > 0 && ReferenceEquals(_pins[0].Unit, unit))
        {
            ClearPins();
        }
        else
        {
            _pins.Clear();
            Pin(unit);
        }
        return true;
    }

    /// <summary>
    /// The enemy every instrument (field, rings, pips, counts, distance) currently reflects: the HOVERED
    /// enemy while one is hovered (so hovering shows its radius regardless of the pin), else the pinned
    /// enemy. Accent is always the lead cyan -- a single target at a time.
    /// </summary>
    private IUnit? ActiveTargetUnit()
    {
        if (_hoverCandidate != null && _hoverElapsed >= TacticalOverlayConfig.HoverPreviewDelaySeconds)
            return _hoverCandidate;
        return _pins.Count > 0 ? _pins[0].Unit : null;
    }

    private void Pin(IUnit unit)
    {
        _pins.Add(new PinnedTarget(unit, _pins.Count));   // accent = slot; newest is focused
        _focusIndex = _pins.Count - 1;
        InvalidateField();
    }

    private void ClearPins()
    {
        _pins.Clear();
        _focusIndex     = -1;
        _hoverCandidate = null;
        _hoverElapsed   = 0;
        InvalidateField();
    }

    private void InvalidateField() => _fieldBuiltOnce = false;

    /// <summary>Forces the next DrawField to rebuild (used by the toolbar's GPU/CPU field toggle).</summary>
    public void InvalidateFieldCache() => InvalidateField();

    /// <summary>Chips row + live eligible-count rows + incoming-threat row for the move panel (spec
    /// section 4). Called from the resolver's DrawInfoPanel, so it runs inside that ImGui window and reads
    /// the same-frame ghost snapshot.</summary>
    public void DrawPanelSection()
    {
        if (_pins.Count > 0)
        {
            ImGui.Separator();
            (byte r, byte g, byte b) = TacticalOverlayConfig.AccentPalette[0];
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(r / 255f, g / 255f, b / 255f, 1f));
            ImGui.TextUnformatted($"Pinned: {_pins[0].Unit.Name}");
            ImGui.PopStyleColor();
            ImGui.SameLine();
            if (ImGui.SmallButton("unpin##pinx")) ClearPins();
        }

        // Counts follow the active target (hovered enemy, else the pin), so they match the field on screen.
        DrawCountRows();
        DrawThreatRow();
    }

    // Per pin, per weapon type across the moving unit: eligible-shot count at committed positions -> at the
    // pending ghost positions ("Rifles vs Grunts: 7->9"). Eligible = a lit or hatched pip (in range with
    // LoS), computed live via RulesProbe -- never the field texture.
    private void DrawCountRows()
    {
        DefineMovementPathRequest? req = _moveResolver?.ActiveRequest;
        if (req == null || _probe == null) return;
        IUnit? targetUnit = ActiveTargetUnit();
        if (targetUnit == null) return;
        IUnit movingUnit = req.UnitDataBinding.GetValue();
        IReadOnlyDictionary<IModel, Position> committed = _moveResolver!.CommittedPositions;
        IReadOnlyDictionary<IModel, Position> ghosts    = _moveResolver.GhostPositions;

        List<ITerrain> blockers = BlockersFor(movingUnit, targetUnit);
        (byte r, byte g, byte b) = TacticalOverlayConfig.AccentPalette[0];
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(r / 255f, g / 255f, b / 255f, 1f));

        var weaponNames = new SortedSet<string>();
        foreach (IModel m in movingUnit.Models)
            if (m.GetIsAlive())
                foreach (Weapon w in m.Weapons)
                    if (w.RangeInches > 0f) weaponNames.Add(w.Name);

        foreach (string wname in weaponNames)
        {
            int before = 0, after = 0;
            foreach (IModel m in movingUnit.Models)
            {
                if (!m.GetIsAlive()) continue;
                Weapon? w = null;
                foreach (Weapon cand in m.Weapons)
                    if (cand.RangeInches > 0f && cand.Name == wname) { w = cand; break; }
                if (w == null) continue;

                float eff = EffectiveRange(req, wname, targetUnit.ID, w.RangeInches);
                Position cp = committed.TryGetValue(m, out Position c) ? c : m.Position;
                Position gp = ghosts.TryGetValue(m, out Position g2) ? g2 : m.Position;
                if (_probe.EvaluatePip(m, cp, m.Facing, targetUnit, eff, blockers) != PipState.Dim) before++;
                if (_probe.EvaluatePip(m, gp, m.Facing, targetUnit, eff, blockers) != PipState.Dim) after++;
            }
            ImGui.TextUnformatted($"{wname} vs {targetUnit.Name}: {before}->{after}");
        }
        ImGui.PopStyleColor();
    }

    private void DrawThreatRow()
    {
        DefineMovementPathRequest? req = _moveResolver?.ActiveRequest;
        if (req == null || _threat == null || _moveResolver == null) return;
        IUnit movingUnit = req.UnitDataBinding.GetValue();
        IReadOnlyDictionary<IModel, Position> ghosts = _moveResolver.GhostPositions;

        bool charge = false, shoot = false;
        foreach (IModel m in movingUnit.Models)
        {
            if (!m.GetIsAlive()) continue;
            Position gp = ghosts.TryGetValue(m, out Position g) ? g : m.Position;
            if (gp.x == 0f && gp.z == 0f) continue;
            if (GhostInThreat(gp.x, gp.z, out bool c, out bool s)) { charge |= c; shoot |= s; }
        }

        if (charge || shoot)
        {
            (byte r, byte g, byte b) = TacticalOverlayConfig.ThreatColor;
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(r / 255f, g / 255f, b / 255f, 1f));
            ImGui.TextUnformatted($"! Inside enemy {(charge ? "charge" : "shoot")} reach");
            ImGui.PopStyleColor();
        }
    }

    // ---- Opportunity field: build ----------------------------------------------------------------

    private (IUnit? target, int accent, float alphaScale) ResolveFieldTarget(DefineMovementPathRequest req)
    {
        // Hover overrides the pin so you can peek any enemy's radius; both draw at full opacity (the lead
        // cyan accent). Everything else follows ActiveTargetUnit, which uses the same rule.
        IUnit? active = ActiveTargetUnit();
        return active != null ? (active, 0, 1f) : (null, 0, 0f);
    }

    private void RebuildFieldIfNeeded(DefineMovementPathRequest req, IUnit target, int accent, float alphaScale)
    {
        IUnit movingUnit = req.UnitDataBinding.GetValue();
        float shooterRadius = _probe!.ModalBaseRadius(movingUnit);
        // The rings are the SELECTED enemy's own weapon ranges (their threat); the disc inflation stays the
        // mover's radius (where MY base can sit relative to their reach). Base ranges -- no mover-vs-target
        // override applies to the enemy's own guns.
        List<BandSpec> bands = BuildBands(req, target, overrideTarget: null);

        long sig = ComputeFieldSignature(movingUnit, target, accent, alphaScale, bands);
        if (_fieldBuiltOnce && sig == _lastFieldSig) return;
        _lastFieldSig   = sig;
        _fieldBuiltOnce = true;

        var targets = new List<FieldTargetModel>();
        foreach (IModel m in target.Models)
        {
            if (!m.GetIsAlive()) continue;
            Position p = m.Position;
            if (p.x == 0f && p.z == 0f) continue;
            targets.Add(new FieldTargetModel(p.x, p.z, m.BaseRadiusInches));
        }

        if (bands.Count == 0 || targets.Count == 0)
        {
            ClearFieldPictures();
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        OpportunityFieldBuilder.Build(_bandMask!, targets, shooterRadius, bands);
        List<PolarSightMap> maps = BuildSightMaps(movingUnit, target);
        (byte r, byte g, byte b) accentCol = TeamAccent(target);   // selected enemy's team color
        _fieldAccentCol = accentCol;

        bool gpuActive = _gpuField != null && TacticalOverlayConfig.UseGpuField;
        if (gpuActive)
            _gpuField!.Rebuild(targets, shooterRadius, bands, maps, accentCol, alphaScale);
        // The CPU masks are needed when the CPU path draws, and by the fidelity sampler's claim
        // channels; skip the texel loop entirely when neither wants them.
        bool classify = !gpuActive || _sampler.Enabled;
        if (classify)
            PolarSightMap.ClassifyInto(_bandMask!, _shadowMask!, _coverMask!, maps);
        _fieldMasksValid = classify;
        if (!gpuActive)
            _field!.Compose(_bandMask!, _shadowMask!, _coverMask!, accentCol, alphaScale);

        _bandLabels        = BuildBandLabels(movingUnit, targets, bands, accent);
        // Skip the CPU marching-squares rings when the GPU draws rings in screen space.
        _fieldRings        = (gpuActive && _gpuField!.RingsReady) ? new List<List<Float2>>() : BuildFieldRings(bands);
        _fieldMovingUnit   = movingUnit;
        _fieldTargetUnit   = target;
        _lastFieldBands    = bands;
        _lastShooterRadius = shooterRadius;
        BuildSecondaryContours(req, movingUnit, shooterRadius);
        sw.Stop();
        if (sw.Elapsed.TotalMilliseconds > TacticalOverlayConfig.RebuildBudgetMs)
            _warn?.Invoke($"[overlay] field rebuild {sw.Elapsed.TotalMilliseconds:0}ms " +
                          $"(budget {TacticalOverlayConfig.RebuildBudgetMs:0}ms)");
    }

    // The moving unit's deduplicated effective weapon ranges vs the target, as nested bands: shortest
    // range gets the highest value (innermost), so max-blend keeps the best band at each texel.
    // Bands come from the SELECTED unit's own weapons: in Target mode that's the enemy (their threat --
    // "where I'm in danger"), in Self mode the mover. Range overrides (mover-vs-target modifiers from the
    // request) only apply when weaponSource is the mover shooting overrideTarget; the enemy's own guns use
    // base range.
    private List<BandSpec> BuildBands(DefineMovementPathRequest req, IUnit weaponSource, IUnit? overrideTarget)
    {
        // Per effective range, count each weapon name across the unit so the label can show "5x Rifle".
        var byRange = new Dictionary<float, Dictionary<string, int>>();
        foreach (IModel m in weaponSource.Models)
        {
            if (!m.GetIsAlive()) continue;
            foreach (Weapon w in m.Weapons)
            {
                if (w.RangeInches <= 0f) continue;
                float eff = overrideTarget != null
                    ? EffectiveRange(req, w.Name, overrideTarget.ID, w.RangeInches)
                    : w.RangeInches;
                if (!byRange.TryGetValue(eff, out Dictionary<string, int>? counts))
                    byRange[eff] = counts = new Dictionary<string, int>();
                counts[w.Name] = counts.GetValueOrDefault(w.Name) + 1;
            }
        }
        if (byRange.Count == 0) return new List<BandSpec>();

        var ranges = byRange.Keys.ToList();
        ranges.Sort();                    // ascending
        int k = ranges.Count;
        var bands = new List<BandSpec>(k);
        for (int i = 0; i < k; i++)
        {
            float range = ranges[i];
            byte value  = (byte)(k - i);  // shortest -> highest (inner)
            string names = string.Join(" / ",
                byRange[range].OrderBy(kv => kv.Key).Select(kv => $"{kv.Value}x {kv.Key}"));
            bands.Add(new BandSpec(range, value, $"{range:0.#}\" {names}"));
        }
        return bands;
    }

    // The band boundary rings, one boundary per distinct band value (v=1 is the outer edge of the whole
    // field, v=2..k the internal band transitions). CPU marching squares -> world polylines, drawn as
    // fixed-thickness vector lines. Only called on a target-mode rebuild (rare), never per frame.
    private List<List<Float2>> BuildFieldRings(List<BandSpec> bands)
    {
        var rings = new List<List<Float2>>();
        if (_bandMask == null) return rings;

        byte maxV = 0;
        foreach (BandSpec b in bands) if (b.Value > maxV) maxV = b.Value;
        float eps = 1f / TacticalOverlayConfig.TexelsPerInch;
        for (byte v = 1; v <= maxV; v++)
            rings.AddRange(MarchingSquares.Extract(_bandMask, v, eps));
        return rings;
    }

    private static float EffectiveRange(DefineMovementPathRequest req, string weaponName, UnitID targetId, float baseRange)
    {
        foreach (WeaponRangeOverride o in req.WeaponRangeOverrides)
            if (o.WeaponName == weaponName && o.EnemyUnitId.Equals(targetId))
                return o.EffectiveRangeInches;
        return baseRange;
    }

    // One label pill per band, on that band's outer boundary on the side the mover approaches from.
    // Placement by ray-march (plan decision D4 fallback, adopted for speed): walk from outside the field
    // toward the target-unit centroid along the mover->target line and mark the first cell that achieves
    // the band ("within range R" = cell value >= band value, since inner bands hold higher values). A few
    // hundred mask samples per band vs the old full-grid marching-squares scan. Labels are cosmetic; the
    // half-texel step is plenty.
    private List<BandLabel> BuildBandLabels(IUnit movingUnit, List<FieldTargetModel> targets,
        List<BandSpec> bands, int accent)
    {
        var labels = new List<BandLabel>();
        if (targets.Count == 0) return labels;

        float tx = 0f, tz = 0f;
        foreach (FieldTargetModel t in targets) { tx += t.X; tz += t.Z; }
        tx /= targets.Count; tz /= targets.Count;

        Float2 mover = MovingCentroid(movingUnit);
        float dx = mover.X - tx, dz = mover.Y - tz;
        float len = MathF.Sqrt(dx * dx + dz * dz);
        if (len < 1e-3f) { dx = 1f; dz = 0f; len = 1f; }   // degenerate: pick +X
        dx /= len; dz /= len;

        // Start beyond any possible reach (table diagonal) and march inward.
        float maxReach = MathF.Sqrt(
            GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES * GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES +
            GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES * GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES);
        float step = 0.5f / TacticalOverlayConfig.TexelsPerInch;

        foreach (BandSpec band in bands)
        {
            for (float t = maxReach; t >= 0f; t -= step)
            {
                float x = tx + dx * t, z = tz + dz * t;
                if (_bandMask!.SampleAt(x, z) >= band.Value)
                {
                    labels.Add(new BandLabel(new Float2(x, z), band.Label, accent));
                    break;
                }
            }
        }
        return labels;
    }

    // Each non-focused pin contributes a single boundary contour -- the moving unit's longest-range band
    // vs that pin -- in its own accent (spec section 3/5). No fill/shadows/hatching.
    private void BuildSecondaryContours(DefineMovementPathRequest req, IUnit movingUnit, float shooterRadius)
    {
        _secondaryContours.Clear();
        if (_pins.Count <= 1) return;

        float eps = 1f / TacticalOverlayConfig.TexelsPerInch;
        for (int i = 0; i < _pins.Count; i++)
        {
            if (i == _focusIndex) continue;
            PinnedTarget pin = _pins[i];
            float longest = LongestEffectiveRange(req, movingUnit, pin.Unit);
            if (longest <= 0f) continue;

            _scratchMask!.Clear();
            bool any = false;
            foreach (IModel m in pin.Unit.Models)
            {
                if (!m.GetIsAlive()) continue;
                Position p = m.Position;
                if (p.x == 0f && p.z == 0f) continue;
                _scratchMask.RasterizeDiscMax(p.x, p.z, longest + shooterRadius + m.BaseRadiusInches, 1);
                any = true;
            }
            if (!any) continue;
            _secondaryContours.Add((pin.Accent, MarchingSquares.Extract(_scratchMask, 1, eps)));
        }
    }

    private float LongestEffectiveRange(DefineMovementPathRequest req, IUnit movingUnit, IUnit target)
    {
        float longest = 0f;
        foreach (IModel m in movingUnit.Models)
        {
            if (!m.GetIsAlive()) continue;
            foreach (Weapon w in m.Weapons)
            {
                if (w.RangeInches <= 0f) continue;
                float eff = EffectiveRange(req, w.Name, target.ID, w.RangeInches);
                if (eff > longest) longest = eff;
            }
        }
        return longest;
    }

    private static Float2 MovingCentroid(IUnit unit)
    {
        float sx = 0, sz = 0; int n = 0;
        foreach (IModel m in unit.Models)
        {
            if (!m.GetIsAlive()) continue;
            Position p = m.Position;
            if (p.x == 0f && p.z == 0f) continue;
            sx += p.x; sz += p.z; n++;
        }
        return n > 0 ? new Float2(sx / n, sz / n) : new Float2(0f, 0f);
    }

    private long ComputeFieldSignature(IUnit movingUnit, IUnit target, int accent, float alphaScale, List<BandSpec> bands)
    {
        unchecked
        {
            long h = 1469598103934665603L;
            void Mix(long v) => h = (h ^ v) * 1099511628211L;

            Mix(movingUnit.ID.GetHashCode());
            Mix(target.ID.GetHashCode());
            Mix(accent);
            Mix((long)(alphaScale * 100f));
            foreach (BandSpec b in bands) { Mix((long)(b.RangeInches * 100f)); Mix(b.Value); }

            foreach (IModel m in target.Models)
            {
                if (!m.GetIsAlive()) continue;
                Position p = m.Position;
                if (p.x == 0f && p.z == 0f) continue;
                Mix((long)(p.x * 4f)); Mix((long)(p.z * 4f));
            }
            // The field is target-anchored (bands + LoS depend on the target, terrain, and third-party
            // blockers -- the mover's own models are excluded from blockers). So the mover's position is
            // deliberately NOT in the signature: it must not trigger the expensive field/visibility rebuild
            // on every committed step. Only band-label placement uses the centroid, and a label sitting at
            // its build-time point on the (unchanged) band edge is cosmetic.
            return h;
        }
    }

    // One PolarSightMap per living, placed target model, built from the Blocking/Cover-relevant pieces
    // (terrain + model-base blockers, assembled exactly as the shooting stages do). The maps feed BOTH
    // renderers: the CPU path classifies texels through PolarSightMap.ClassifyInto (shared with the
    // harness), the GPU path draws them as lit-region fans. Empty when nothing on the table can block
    // or cover -> everything lit, no per-texel work at all. The fidelity sampler referees the maps
    // against the REAL EvaluateSightLine (which pips/counts still call). v1 ignores blocker HEIGHT, as
    // the engine does.
    private List<PolarSightMap> BuildSightMaps(IUnit movingUnit, IUnit target)
    {
        var maps = new List<PolarSightMap>();
        List<ITerrain> relevant = _probe!.BuildBlockers(movingUnit, target)
            .Where(t => t.TerrainType.HasFlag(ETerrainType.Blocking) || t.TerrainType.HasFlag(ETerrainType.Cover))
            .ToList();
        if (relevant.Count == 0) return maps;

        foreach (IModel m in target.Models)
        {
            if (!m.GetIsAlive()) continue;
            Position p = m.Position;
            if (p.x == 0f && p.z == 0f) continue;
            maps.Add(PolarSightMap.Build(p, relevant, TacticalOverlayConfig.PolarBuckets));
        }
        return maps;
    }

    // #312: one team-aware rule for the whole app - a unit belonging to ANOTHER PLAYER ON YOUR TEAM is
    // not an enemy, and so must never wear a threat ring or a can-hit / can-charge indicator.
    private bool IsEnemyOf(PlayerID refPlayer, IUnit unit)
        => TeamAwareness.IsEnemyUnit(_tableState!, refPlayer, unit);

    private void UpdateHover(double dt, TableHitTester hitTester, DefineMovementPathRequest req)
    {
        IUnit? hovered = hitTester.HoveredUnit;
        // Hover an enemy to see its radius, REGARDLESS of the pin (ActiveTargetUnit lets it override).
        bool eligible = hovered != null && IsEnemyOf(req.TargetPlayerID, hovered);
        if (eligible)
        {
            if (ReferenceEquals(hovered, _hoverCandidate)) _hoverElapsed += dt;
            else { _hoverCandidate = hovered; _hoverElapsed = 0; }
        }
        else
        {
            _hoverCandidate = null;
            _hoverElapsed   = 0;
        }
    }

    private static uint U32(float r, float g, float b, float a) =>
        ImGui.ColorConvertFloat4ToU32(new Vector4(r, g, b, a));

    // ---- Snapping (spec section 4) ---------------------------------------------------------------

    /// <summary>
    /// Magnetically adjusts the point the ghost is following: within 0.4" of a focused-pin band boundary
    /// it snaps just inside (so the move lands in range), within 0.4" of a threat frontier it snaps just
    /// outside; band wins if both apply. Alt (held) disables it. Returns the point unchanged when nothing
    /// is in range. NOTE (deviation): snapping the ghost's input point -- rather than only the committed
    /// drop -- lets the snapped position flow through the resolver's existing clamp/overlap/budget/cohesion
    /// validation unchanged, so it can never propose an illegal move (spec: snapped positions must still
    /// pass placement validation). The tradeoff is the ghost is magnetic on approach, not only on release.
    /// </summary>
    public Float2 SnapInputPoint(Float2 intended)
    {
        if (_moveResolver?.ActiveRequest == null) return intended;
        if (ImGui.GetIO().KeyAlt) return intended;

        // Band snap (pin) takes precedence over threat snap -- but only when the drawn field is actually
        // the target-anchored one: otherwise the bands on screen are around the MOVER (with the mover's
        // ranges), so snapping to pin-centred circles that aren't drawn would jump the cursor to nowhere
        // the player can see.
        if (_lastAnchorKind == FieldAnchorKind.Target &&
            _pins.Count > 0 && _focusIndex >= 0 && _focusIndex < _pins.Count && _lastFieldBands.Count > 0)
        {
            IUnit focus = _pins[_focusIndex].Unit;
            IModel? nearest = null;
            float nd2 = float.MaxValue;
            float ncx = 0, ncz = 0;
            foreach (IModel m in focus.Models)
            {
                if (!m.GetIsAlive()) continue;
                Position p = m.Position;
                if (p.x == 0f && p.z == 0f) continue;
                float dx = intended.X - p.x, dz = intended.Y - p.z;
                float d2 = dx * dx + dz * dz;
                if (d2 < nd2) { nd2 = d2; nearest = m; ncx = p.x; ncz = p.z; }
            }

            if (nearest != null)
            {
                float centerDist = MathF.Sqrt(nd2);
                if (centerDist > 1e-4f)
                {
                    float tr = nearest.BaseRadiusInches;
                    foreach (BandSpec b in _lastFieldBands)
                    {
                        // Field bands are circle-approx (center-to-center = range + shooterR + targetR); the
                        // real base-edge is validated live by the pip + the resolver clamps. Rectangle bases
                        // may be a texel off here -- acceptable, the pip tells the truth.
                        float boundaryCenter = b.RangeInches + _lastShooterRadius + tr;
                        if (MathF.Abs(centerDist - boundaryCenter) <= TacticalOverlayConfig.SnapEpsilonInches)
                        {
                            float target = boundaryCenter - TacticalOverlayConfig.SnapInsideMarginInches;
                            float t = target / centerDist;
                            return new Float2(ncx + (intended.X - ncx) * t, ncz + (intended.Y - ncz) * t);
                        }
                    }
                }
            }
        }

        // Threat snap: nudge just outside the nearest frontier when the point is inside one (exact
        // point-in-reach, not a mask sample -- this decides where the model lands).
        if (_threat != null && GhostInThreat(intended.X, intended.Y, out _, out _))
        {
            if (TryNearestFrontierPoint(intended, out Float2 np, out float dist) &&
                dist <= TacticalOverlayConfig.SnapEpsilonInches)
            {
                float dx = np.X - intended.X, dz = np.Y - intended.Y;
                float len = MathF.Sqrt(dx * dx + dz * dz);
                if (len > 1e-4f)
                {
                    float m = TacticalOverlayConfig.SnapInsideMarginInches / len;
                    return new Float2(np.X + dx * m, np.Y + dz * m); // just past the boundary, outward
                }
            }
        }

        return intended;
    }

    private bool TryNearestFrontierPoint(Float2 p, out Float2 nearest, out float dist)
    {
        nearest = default;
        dist = float.MaxValue;
        if (_threat == null) return false;

        float bestD = float.MaxValue;
        Float2 bestP = default;

        void Scan(List<List<Float2>> polys)
        {
            foreach (List<Float2> poly in polys)
                for (int i = 0; i < poly.Count - 1; i++)
                {
                    Float2 a = poly[i], b = poly[i + 1];
                    float abx = b.X - a.X, abz = b.Y - a.Y;
                    float len2 = abx * abx + abz * abz;
                    float t = len2 < 1e-9f ? 0f : ((p.X - a.X) * abx + (p.Y - a.Y) * abz) / len2;
                    t = System.Math.Clamp(t, 0f, 1f);
                    float px = a.X + t * abx, pz = a.Y + t * abz;
                    float dx = p.X - px, dz = p.Y - pz;
                    float d = MathF.Sqrt(dx * dx + dz * dz);
                    if (d < bestD) { bestD = d; bestP = new Float2(px, pz); }
                }
        }

        Scan(_threat.ChargePolylines);
        Scan(_threat.ShootPolylines);

        if (bestD == float.MaxValue) return false;
        nearest = bestP;
        dist = bestD;
        return true;
    }

    // Per-model pips along each ghost's lower edge: one per (pin, distinct ranged weapon). Recomputed via
    // RulesProbe every frame during the drag (spec section 4: correctness over caching).
    private void DrawPips(DefineMovementPathRequest req, IUnit movingUnit)
    {
        if (_moveResolver == null || _probe == null) return;
        IUnit? targetUnit = ActiveTargetUnit();
        if (targetUnit == null) return;
        IReadOnlyDictionary<IModel, Position> ghosts = _moveResolver.GhostPositions;
        List<ITerrain> blockers = BlockersFor(movingUnit, targetUnit);

        ImDrawListPtr dl = ImGui.GetBackgroundDrawList();
        foreach (IModel m in movingUnit.Models)
        {
            if (!m.GetIsAlive()) continue;
            // Only draw where the resolver has published a ghost this session: on the first frame / while a
            // presentation animation holds the resolver's Draw, the snapshot is empty -- don't render pips
            // at the model's start position (they'd disagree with the panel counts).
            if (!ghosts.TryGetValue(m, out Position gp)) continue;
            if (gp.x == 0f && gp.z == 0f) continue;

            var weaponGroups = m.Weapons.Where(w => w.RangeInches > 0f).GroupBy(w => w.Name).ToList();
            if (weaponGroups.Count == 0) continue;

            var pips = new List<(int accent, PipState state)>();
            foreach (var wg in weaponGroups)
            {
                float eff = EffectiveRange(req, wg.Key, targetUnit.ID, wg.First().RangeInches);
                PipState st = _probe.EvaluatePip(m, gp, m.Facing, targetUnit, eff, blockers);
                pips.Add((0, st));   // single target -> lead accent
            }
            DrawPipRow(dl, gp, m.BaseRadiusInches, pips);
        }
    }

    private void DrawPipRow(ImDrawListPtr dl, Position ghostPos, float baseRadiusIn, List<(int accent, PipState state)> pips)
    {
        if (pips.Count == 0) return;
        Vector2 c = WorldToScreen(new Float2(ghostPos.x, ghostPos.z));
        float rPx  = TacticalOverlayConfig.PipRadiusPx;
        float step = rPx * 2f + 2f;
        float baseY = c.Y + baseRadiusIn * _scale + rPx + 2f;
        float x0 = c.X - (pips.Count - 1) * step * 0.5f;

        for (int i = 0; i < pips.Count; i++)
        {
            (int accent, PipState state) = pips[i];
            (byte r, byte g, byte b) = TacticalOverlayConfig.AccentPalette[accent % TacticalOverlayConfig.AccentPalette.Length];
            var pc = new Vector2(x0 + i * step, baseY);
            uint solid = U32(r / 255f, g / 255f, b / 255f, 1f);

            switch (state)
            {
                case PipState.Lit:
                    dl.AddCircleFilled(pc, rPx, solid);
                    break;
                case PipState.Hatched:
                    dl.AddCircleFilled(pc, rPx, U32(r / 255f, g / 255f, b / 255f, 0.55f));
                    dl.AddCircle(pc, rPx, solid, 12, 1.5f);
                    dl.AddLine(pc + new Vector2(-rPx, rPx), pc + new Vector2(rPx, -rPx), U32(0f, 0f, 0f, 0.8f), 1f);
                    break;
                default: // Dim
                    dl.AddCircle(pc, rPx, U32(r / 255f, g / 255f, b / 255f, 0.35f), 12, 1.2f);
                    break;
            }
        }
    }

    // Red emphasis ring around any ghost sitting inside a threat frontier (spec section 4: ghost outlines
    // tint red while inside any frontier).
    private void DrawGhostThreatTint(IUnit movingUnit)
    {
        if (_threat == null || _moveResolver == null) return;
        IReadOnlyDictionary<IModel, Position> ghosts = _moveResolver.GhostPositions;

        ImDrawListPtr dl = ImGui.GetBackgroundDrawList();
        (byte r, byte g, byte b) = TacticalOverlayConfig.GhostInsideThreatTint;
        uint col = U32(r / 255f, g / 255f, b / 255f, 0.9f);

        foreach (IModel m in movingUnit.Models)
        {
            if (!m.GetIsAlive()) continue;
            if (!ghosts.TryGetValue(m, out Position gp)) continue;
            if (gp.x == 0f && gp.z == 0f) continue;
            if (GhostInThreat(gp.x, gp.z, out _, out _))
            {
                Vector2 c = WorldToScreen(new Float2(gp.x, gp.z));
                // #250: the emphasis ring follows the model's true base shape.
                ModelBaseRenderer.DrawOutlineImGui(dl, m.BaseShape, c, _scale, col,
                    thickness: 2f, inflateInches: 2f / _scale, facing: m.Facing);
            }
        }
    }

    // Distance readout on the ghost nearest the cursor: base-edge distance to the nearest active-target
    // model. Within 0.5" of a band boundary it promotes to a labeled measurement line, so an edge case
    // is never resolved by squinting at the texture (spec section 4 / invariant).
    private void DrawDistanceReadout(DefineMovementPathRequest req, IUnit movingUnit)
    {
        if (_moveResolver == null) return;
        IUnit? focus = ActiveTargetUnit();
        if (focus == null) return;
        IReadOnlyDictionary<IModel, Position> ghosts = _moveResolver.GhostPositions;
        Vector2 mouse = ImGui.GetIO().MousePos;

        IModel? ghostModel = null;
        Position ghostPos = default;
        float bestPx = float.MaxValue;
        foreach (IModel m in movingUnit.Models)
        {
            if (!m.GetIsAlive()) continue;
            if (!ghosts.TryGetValue(m, out Position gp)) continue;
            if (gp.x == 0f && gp.z == 0f) continue;
            float d = Vector2.Distance(WorldToScreen(new Float2(gp.x, gp.z)), mouse);
            if (d < bestPx) { bestPx = d; ghostModel = m; ghostPos = gp; }
        }
        if (ghostModel == null) return;

        IModel? nearestTarget = null;
        float bestDist = float.MaxValue;
        foreach (IModel tm in focus.Models)
        {
            if (!tm.GetIsAlive()) continue;
            Position tp = tm.Position;
            if (tp.x == 0f && tp.z == 0f) continue;
            float d = DistanceUtilities.GetBaseToBaseDistanceInches_3D(
                ghostPos, tp, ghostModel.BaseShape, ghostModel.Facing, tm.BaseShape, tm.Facing);
            if (d < bestDist) { bestDist = d; nearestTarget = tm; }
        }
        if (nearestTarget == null) return;

        float longest = _lastFieldBands.Count > 0 ? _lastFieldBands.Max(b => b.RangeInches) : 0f;
        bool inRange = bestDist <= longest;
        uint col = inRange ? U32(0.4f, 0.95f, 0.4f, 1f) : U32(0.9f, 0.9f, 0.9f, 0.95f);

        float? boundary = null;
        foreach (BandSpec bnd in _lastFieldBands)
            if (System.Math.Abs(bestDist - bnd.RangeInches) <= TacticalOverlayConfig.MeasurementPromoteInches)
            { boundary = bnd.RangeInches; break; }

        ImDrawListPtr dl = ImGui.GetBackgroundDrawList();
        Vector2 ghostScreen = WorldToScreen(new Float2(ghostPos.x, ghostPos.z));
        string txt = boundary.HasValue ? $"{bestDist:0.0}\" / {boundary.Value:0}\"" : $"{bestDist:0.0}\"";

        // The "promote to a labeled measurement line" behaviour drew a line from the ghost to the nearest
        // enemy whenever the ghost sat within MeasurementPromoteInches of a band boundary (an enemy gun's
        // range edge). With the team-colored field showing those ranges directly it just read as a stray
        // line near the range edge, so it's disabled -- the distance TEXT (which still shows "/ 24\"" near a
        // boundary) carries the same information. Re-enable this block for the measurement line.
        // if (boundary.HasValue)
        // {
        //     Vector2 targetScreen = WorldToScreen(new Float2(nearestTarget.Position.x, nearestTarget.Position.z));
        //     dl.AddLine(ghostScreen, targetScreen, col, 1.5f);
        // }

        Vector2 size = ImGui.CalcTextSize(txt);
        Vector2 at = ghostScreen + new Vector2(-size.X * 0.5f, -(ghostModel.BaseRadiusInches * _scale) - size.Y - 4f);
        dl.AddText(at + new Vector2(1, 1), U32(0f, 0f, 0f, 0.8f), txt);
        dl.AddText(at, col, txt);
    }

    private void DrawBandLabels()
    {
        // #230: gated on "a field was drawn this frame", not on a move job. The band captions name the
        // weapon behind each ring ("24in 5x Rifle"), which is most of the field's readability, and a
        // placement-anchored field has no move request to check.
        if (_bandLabels.Count == 0 || !_fieldActive) return;

        ImDrawListPtr dl = ImGui.GetBackgroundDrawList();
        uint textCol = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 1f));
        foreach (BandLabel bl in _bandLabels)
        {
            (byte r, byte g, byte b) = _fieldAccentCol;   // selected unit's team color
            uint bgCol = ImGui.ColorConvertFloat4ToU32(new Vector4(r / 255f, g / 255f, b / 255f, 0.88f));

            Vector2 at   = WorldToScreen(bl.World);
            Vector2 size = ImGui.CalcTextSize(bl.Text);
            var pad = new Vector2(5f, 2f);
            dl.AddRectFilled(at - size * 0.5f - pad, at + size * 0.5f + pad, bgCol, 3f);
            dl.AddText(at - size * 0.5f, textCol, bl.Text);
        }
    }

    // ---- Contour drawing (Raylib) ----------------------------------------------------------------


    private Vector2 WorldToScreen(Float2 w) =>
        new(_originX + w.X * _scale, _originY + (_tableH - w.Y) * _scale);

    /// <summary>
    /// Draws a world-space polyline (band rings, secondary pin contours). Solid only: the dashed variant
    /// existed for the shoot-reach threat frontier, which #247 removed, so the dash-marching branch and
    /// its two config constants went with it rather than sitting here uncalled.
    /// </summary>
    private void DrawWorldPolyline(List<Float2> poly, Color col, float thickness)
    {
        for (int i = 0; i < poly.Count - 1; i++)
            Raylib.DrawLineEx(WorldToScreen(poly[i]), WorldToScreen(poly[i + 1]), thickness, col);
    }
}
