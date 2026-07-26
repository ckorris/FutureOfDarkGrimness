using ImGuiNET;

namespace FdgRaylib.Rendering.TacticalOverlay;

/// <summary>
/// Every tunable for the tactical overlay in one place (spec section 7): raster density, colors,
/// opacities, interaction epsilons, hotkeys, and the rebuild budget. Nothing here is derived from
/// game rules -- these are purely presentation/interaction constants. Rules-authoritative numbers
/// (ranges, budgets, mobility) always come from the engine via <see cref="RulesProbe"/>.
/// </summary>
internal static class TacticalOverlayConfig
{
    // --- Field raster density ---------------------------------------------------------------------
    // Texels per inch for the world-space field grid/texture. Higher = crisper band/shadow edges at
    // more CPU per rebuild. Drop to 8 first if the rebuild budget is blown (see plan decision D1).
    public const float TexelsPerInch = 12f;

    // Default reference base radius (inches) when no moving/activating unit defines one -- a 28mm
    // circle. Field discs inflate by (reference radius + target radius); per-model rules calls carry
    // exact radii, this only affects the picture.
    public const float DefaultReferenceRadiusInches = 0.551f;

    // --- Threat frontiers -------------------------------------------------------------------------
    public static readonly (byte r, byte g, byte b) ThreatColor = (232, 72, 72); // dedicated red
    public const float ThreatContourAlpha       = 0.70f;
    public const float ThreatIsolatedAlpha      = 0.95f; // brightened unit under hover/idle isolation
    public const float ThreatDimmedAlpha        = 0.28f; // the aggregate while one unit is isolated
    public const float ThreatContourThicknessPx = 1.5f;
    public const float ThreatDashLengthPx       = 7f;    // shoot-reach frontier is dashed
    public const float ThreatDashGapPx          = 5f;

    // --- Opportunity field accents (pin order, spec section 4) ------------------------------------
    // Distinct from both players' identity colors, from the threat color, AND from the mat green: the
    // lead accent is a blue-dominant cyan (B > G) so a fill over the grass reads as a projected layer,
    // not a recolor of it (playtest feedback 2026-07-07). The two grammars only stay separate if the
    // un-overlaid board reads as ground.
    public static readonly (byte r, byte g, byte b)[] AccentPalette =
    {
        (0x2A, 0xC0, 0xDC), // cyan  (shifted off the mat green -- B dominant)
        (0xE0, 0xA6, 0x3C), // amber
        (0xC0, 0x5F, 0xA0), // magenta
        (0x6C, 0x8E, 0xE0), // cornflower (4th+ pin, in case more than three are pinned)
    };
    // The line is the information, the fill is atmosphere: bands read as concentric BOUNDARY rings over
    // a light, near-uniform wash -- not as three barely-different fills (playtest feedback 2026-07-07).
    public const float BandFillAlpha           = 0.14f; // base band fill -- deliberately light
    public const float BandInnerAlphaBoost      = 0.05f; // subtle inner-depth cue, NOT the band signal
    public const float BandFillAlphaMax         = 0.30f; // clamp so deep stacks stay a light wash (spec's ~30%)
    public const float BandBoundaryAlpha        = 0.85f; // the rings carry the band delineation
    // The FILL lerps toward white -> a luminous cool glow that lifts luminance (reads as a projected
    // layer, not a green tint) even at low alpha; the RINGS stay pure accent for identity.
    public const float BandFillWhiteMix         = 0.40f;
    public const float BandBoundaryThicknessPx = 1.5f;
    public const float HatchSpacingInches       = 0.6f; // cover crosshatch pitch (world-space, per direction)
    public const int   HatchLineWidthTexels     = 2;    // thin lines; crosshatch (both diagonals) so it reads
                                                        // uniformly regardless of the band edge's angle

    // --- Instruments ------------------------------------------------------------------------------
    public const float PipRadiusPx              = 3f;   // ~5-6px pip diameter along the ghost's lower edge
    public static readonly (byte r, byte g, byte b) GhostInsideThreatTint = (232, 72, 72);

    // --- Interaction (spec section 4) -------------------------------------------------------------
    // Hover dwell before a hovered unit's field appears. DISABLED (0) as of #247: the delay was meant to
    // stop the field flickering as the cursor transits units mid-decision, but in the hand it doesn't read
    // as deliberate restraint -- it reads as the picture being slow to bake, which is the one impression
    // this overlay cannot afford. Restore by raising this (0.150 was the original) if flicker turns out to
    // be the worse problem; the anti-flicker measure that survives is FieldAnchorPlan's rule that hovering
    // the unit whose ghosts are live does NOT steal the anchor.
    public const double HoverPreviewDelaySeconds = 0.0;
    public const float  SnapEpsilonInches        = 0.4f;  // drop within this of a boundary snaps
    public const float  SnapInsideMarginInches   = 0.05f; // band snap lands just inside
    public const float  MeasurementPromoteInches = 0.5f;  // draw the labeled measurement line within this

    // --- Perf -------------------------------------------------------------------------------------
    public const double RebuildBudgetMs = 30.0; // log a warning past this per rebuild

    // Angle buckets per PolarSightMap. Quantization error <= (pi/N)*distance: at 2048 that's ~0.05" at
    // 30" range -- under half a texel at 12 tpi. Power of two, but nothing relies on that.
    public const int PolarBuckets = 2048;

    // GPU field rasterizer (default, per user 2026-07-07) vs the CPU reference compositor. Runtime
    // toggle (toolbar "Field" button) so a driver problem is one click from the known-good CPU path;
    // flipping it invalidates the field cache. Auto-falls back to CPU when GPU init fails.
    public static bool UseGpuField = true;

    // (Retired #247) GhostAnchoredField chose between the ghost-anchored "what can I hit from here" field
    // and the pinned target's "where can I stand to shoot it". Default OFF meant a move job with nothing
    // pinned drew NO field at all until the player found the checkbox in the Esc menu -- reported as
    // "I can't see them during movement". The choice is now made by the gesture: pin an enemy for the
    // target-anchored picture, otherwise you get your own ghosts. See FieldAnchorPlan.

    // --- Hotkeys ----------------------------------------------------------------------------------
    // T is taken (dev token-reveal in TableTooltipOverlay); F is free. Alt is free.
    public const ImGuiKey ThreatToggleKey     = ImGuiKey.F;
    // #247: master reach toggle (the opportunity field, whatever it is anchored on). V was free across the
    // whole in-game key census (A/F/G/L/R/T, arrows, Enter, Backspace, Escape, Space, digits, F10).
    public const ImGuiKey ReachToggleKey      = ImGuiKey.V;
    public const ImGuiKey ClearPinsKey        = ImGuiKey.Escape;
    public const ImGuiKey FidelitySamplerKey  = ImGuiKey.F10; // debug (spec section 6)
}
