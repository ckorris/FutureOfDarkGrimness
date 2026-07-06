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

    private readonly FidelitySampler _sampler = new();
    // The inflated enemy discs from the last threat rebuild, so the sampler's rules-truth predicate can
    // check point-in-reach without re-deriving reach.
    private List<ThreatDisc> _lastThreatDiscs = new();

    // ---- Opportunity field / pins (P3) -----------------------------------------------------------
    private FieldMask? _bandMask;
    private FieldCompositor? _field;

    private sealed class PinnedTarget
    {
        public readonly IUnit Unit;
        public readonly int Accent;   // index into AccentPalette
        public PinnedTarget(IUnit unit, int accent) { Unit = unit; Accent = accent; }
    }

    private readonly List<PinnedTarget> _pins = new();
    private int _focusIndex = -1;                 // index into _pins; -1 when none
    private DefineMovementPathRequest? _lastSeenRequest; // pins are scoped to one move job

    // Hover preview: an enemy hovered ~150ms with no pins shows a transient dimmed field.
    private IUnit? _hoverCandidate;
    private double _hoverElapsed;

    private long _lastFieldSig;
    private bool _fieldBuiltOnce;

    private readonly record struct BandLabel(Float2 World, string Text, int Accent);
    private List<BandLabel> _bandLabels = new();

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
        _threat   = new ThreatFrontierCache(w, h, TacticalOverlayConfig.TexelsPerInch);
        _bandMask = new FieldMask(w, h, TacticalOverlayConfig.TexelsPerInch);
        _field    = new FieldCompositor(w, h);

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

    /// <summary>Drops every per-game reference and cached picture. Called from ExitGame.</summary>
    public void Detach()
    {
        _moveResolver?.SetTacticalOverlay(null);
        _field?.Dispose();
        _field        = null;
        _bandMask     = null;
        _tableState   = null;
        _moveResolver = null;
        _probe        = null;
        _threat       = null;
        _warn         = null;
        _threatToggledOn = false;
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

        // The opportunity field exists only during the local player's move job (spec section 3).
        DefineMovementPathRequest? req = _moveResolver?.ActiveRequest;
        if (req == null) { _field.Clear(); _bandLabels = new List<BandLabel>(); return; }

        (IUnit? target, int accent, float alphaScale) = ResolveFieldTarget(req);
        if (target == null) { _field.Clear(); _bandLabels = new List<BandLabel>(); return; }

        RebuildFieldIfNeeded(req, target, accent, alphaScale);
        _field.Draw(_originX, _originY,
            GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES, GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES, _scale);
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

        if (!io.WantCaptureKeyboard && ImGui.IsKeyPressed(TacticalOverlayConfig.FidelitySamplerKey))
        {
            _sampler.Enabled = !_sampler.Enabled;
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
            // Tab cycles pin focus; Esc clears pins (no move-cancel exists today -- plan C1).
            if (!io.WantCaptureKeyboard && ImGui.IsKeyPressed(TacticalOverlayConfig.FocusCycleKey))
                CycleFocus();
            if (!io.WantCaptureKeyboard && _pins.Count > 0 && ImGui.IsKeyPressed(TacticalOverlayConfig.ClearPinsKey))
                ClearPins();

            UpdateHover(frameTimeSeconds, hitTester, req);
        }
        else
        {
            _hoverCandidate = null;
            _hoverElapsed   = 0;
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

        DrawBandLabels();
        // Pips / readouts / measurement land in P5.

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
        _lastThreatDiscs = discs;

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

    // ---- Opportunity field: pins -----------------------------------------------------------------

    /// <summary>
    /// Routes an enemy click during a move job to pin/unpin (spec section 3): clicking an unpinned enemy
    /// pins + focuses it, clicking a pinned enemy unpins it. Returns true when it consumed the click, so
    /// the move resolver doesn't also treat it as a waypoint. No-op outside a move job.
    /// </summary>
    public bool TryHandleEnemyClick(IUnit unit, IModel model)
    {
        if (_moveResolver?.ActiveRequest == null) return false;

        int existing = _pins.FindIndex(p => ReferenceEquals(p.Unit, unit));
        if (existing >= 0) Unpin(existing);
        else               Pin(unit);
        return true;
    }

    private void Pin(IUnit unit)
    {
        _pins.Add(new PinnedTarget(unit, NextFreeAccent()));
        _focusIndex = _pins.Count - 1;   // newest pin is focused
        InvalidateField();
    }

    private void Unpin(int index)
    {
        if (index < 0 || index >= _pins.Count) return;
        _pins.RemoveAt(index);
        _focusIndex = _pins.Count == 0
            ? -1
            : System.Math.Clamp(_focusIndex >= index ? _focusIndex - 1 : _focusIndex, 0, _pins.Count - 1);
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

    private void CycleFocus()
    {
        if (_pins.Count == 0) return;
        _focusIndex = (_focusIndex + 1) % _pins.Count;
        InvalidateField();
    }

    private int NextFreeAccent()
    {
        int n = TacticalOverlayConfig.AccentPalette.Length;
        for (int a = 0; a < n; a++)
            if (!_pins.Any(p => p.Accent == a)) return a;
        return _pins.Count % n; // more pins than palette entries -> wrap
    }

    private void InvalidateField() => _fieldBuiltOnce = false;

    /// <summary>Chips row for the move panel (spec section 4). Called from the resolver's DrawInfoPanel,
    /// so it runs inside that ImGui window.</summary>
    public void DrawPanelSection()
    {
        if (_pins.Count == 0) return;

        ImGui.Separator();
        ImGui.TextDisabled("Pinned (Tab focus, Esc clears)");

        int unpinTarget = -1, focusTarget = -1;
        for (int i = 0; i < _pins.Count; i++)
        {
            PinnedTarget pin = _pins[i];
            (byte r, byte g, byte b) = TacticalOverlayConfig.AccentPalette[pin.Accent % TacticalOverlayConfig.AccentPalette.Length];
            bool focused = i == _focusIndex;

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(r / 255f, g / 255f, b / 255f, 1f));
            if (ImGui.Button($"{(focused ? "> " : "  ")}{pin.Unit.Name}##pinchip{i}"))
                focusTarget = i;
            ImGui.PopStyleColor();

            // Chip click focuses; clicking the already-focused chip unpins (spec section 4).
            if (focused && focusTarget == i) { focusTarget = -1; unpinTarget = i; }

            ImGui.SameLine();
            if (ImGui.SmallButton($"x##pinx{i}")) unpinTarget = i;
        }

        if (unpinTarget >= 0)      Unpin(unpinTarget);
        else if (focusTarget >= 0) { _focusIndex = focusTarget; InvalidateField(); }
    }

    // ---- Opportunity field: build ----------------------------------------------------------------

    private (IUnit? target, int accent, float alphaScale) ResolveFieldTarget(DefineMovementPathRequest req)
    {
        if (_pins.Count > 0 && _focusIndex >= 0 && _focusIndex < _pins.Count)
            return (_pins[_focusIndex].Unit, _pins[_focusIndex].Accent, 1f);

        if (_hoverCandidate != null && _hoverElapsed >= TacticalOverlayConfig.HoverPreviewDelaySeconds)
            return (_hoverCandidate, 0, TacticalOverlayConfig.PreviewAlphaScale);

        return (null, 0, 0f);
    }

    private void RebuildFieldIfNeeded(DefineMovementPathRequest req, IUnit target, int accent, float alphaScale)
    {
        IUnit movingUnit = req.UnitDataBinding.GetValue();
        float shooterRadius = _probe!.ModalBaseRadius(movingUnit);
        List<BandSpec> bands = BuildBands(req, movingUnit, target);

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
            _field!.Clear();
            _bandLabels = new List<BandLabel>();
            return;
        }

        var sw = System.Diagnostics.Stopwatch.StartNew();
        OpportunityFieldBuilder.Build(_bandMask!, targets, shooterRadius, bands);
        (byte r, byte g, byte b) accentCol =
            TacticalOverlayConfig.AccentPalette[accent % TacticalOverlayConfig.AccentPalette.Length];
        _field!.Compose(_bandMask!, accentCol, alphaScale);
        _bandLabels = BuildBandLabels(movingUnit, bands, accent);
        sw.Stop();
        if (sw.Elapsed.TotalMilliseconds > TacticalOverlayConfig.RebuildBudgetMs)
            _warn?.Invoke($"[overlay] field rebuild {sw.Elapsed.TotalMilliseconds:0}ms " +
                          $"(budget {TacticalOverlayConfig.RebuildBudgetMs:0}ms)");
    }

    // The moving unit's deduplicated effective weapon ranges vs the target, as nested bands: shortest
    // range gets the highest value (innermost), so max-blend keeps the best band at each texel.
    private List<BandSpec> BuildBands(DefineMovementPathRequest req, IUnit movingUnit, IUnit target)
    {
        var byRange = new Dictionary<float, SortedSet<string>>();
        foreach (IModel m in movingUnit.Models)
        {
            if (!m.GetIsAlive()) continue;
            foreach (Weapon w in m.Weapons)
            {
                if (w.RangeInches <= 0f) continue;
                float eff = EffectiveRange(req, w.Name, target.ID, w.RangeInches);
                if (!byRange.TryGetValue(eff, out SortedSet<string>? names))
                    byRange[eff] = names = new SortedSet<string>();
                names.Add(w.Name);
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
            string names = string.Join(" / ", byRange[range]);
            bands.Add(new BandSpec(range, value, $"{range:0.#}\" {names}"));
        }
        return bands;
    }

    private static float EffectiveRange(DefineMovementPathRequest req, string weaponName, UnitID targetId, float baseRange)
    {
        foreach (WeaponRangeOverride o in req.WeaponRangeOverrides)
            if (o.WeaponName == weaponName && o.EnemyUnitId.Equals(targetId))
                return o.EffectiveRangeInches;
        return baseRange;
    }

    // One label pill per band, on that band's outer boundary at the point nearest the moving unit's
    // centroid (plan decision D4 primary), so it sits on the side the mover approaches from.
    private List<BandLabel> BuildBandLabels(IUnit movingUnit, List<BandSpec> bands, int accent)
    {
        var labels = new List<BandLabel>();
        Float2 centroid = MovingCentroid(movingUnit);

        foreach (BandSpec band in bands)
        {
            List<List<Float2>> boundary =
                MarchingSquares.Extract(_bandMask!, band.Value, 1f / TacticalOverlayConfig.TexelsPerInch);
            Float2? best = null;
            float bestD = float.MaxValue;
            foreach (List<Float2> poly in boundary)
                foreach (Float2 v in poly)
                {
                    float dx = v.X - centroid.X, dz = v.Y - centroid.Y;
                    float d = dx * dx + dz * dz;
                    if (d < bestD) { bestD = d; best = v; }
                }
            if (best.HasValue) labels.Add(new BandLabel(best.Value, band.Label, accent));
        }
        return labels;
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
            Float2 c = MovingCentroid(movingUnit);
            Mix((long)(c.X * 2f)); Mix((long)(c.Y * 2f));
            return h;
        }
    }

    private bool IsEnemyOf(PlayerID refPlayer, IUnit unit)
    {
        ITeam? team = _tableState!.Teams.Objects.FirstOrDefault(t => t.IsPlayerOnTeam(refPlayer));
        return team != null ? !team.IsPlayerOnTeam(unit.PlayerID) : !unit.PlayerID.Equals(refPlayer);
    }

    private void UpdateHover(double dt, TableHitTester hitTester, DefineMovementPathRequest req)
    {
        IUnit? hovered = hitTester.HoveredUnit;
        // Preview is the pre-pin affordance: only when there are no pins and the hover is an enemy.
        bool eligible = hovered != null && _pins.Count == 0 && IsEnemyOf(req.TargetPlayerID, hovered);
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

    private void DrawBandLabels()
    {
        if (_bandLabels.Count == 0 || _moveResolver?.ActiveRequest == null) return;

        ImDrawListPtr dl = ImGui.GetBackgroundDrawList();
        uint textCol = ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 1f, 1f, 1f));
        foreach (BandLabel bl in _bandLabels)
        {
            (byte r, byte g, byte b) = TacticalOverlayConfig.AccentPalette[bl.Accent % TacticalOverlayConfig.AccentPalette.Length];
            uint bgCol = ImGui.ColorConvertFloat4ToU32(new Vector4(r / 255f, g / 255f, b / 255f, 0.88f));

            Vector2 at   = WorldToScreen(bl.World);
            Vector2 size = ImGui.CalcTextSize(bl.Text);
            var pad = new Vector2(5f, 2f);
            dl.AddRectFilled(at - size * 0.5f - pad, at + size * 0.5f + pad, bgCol, 3f);
            dl.AddText(at - size * 0.5f, textCol, bl.Text);
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
