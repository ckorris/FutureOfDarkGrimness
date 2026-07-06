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
    // Distinct from both players' identity colors and from the threat color.
    public static readonly (byte r, byte g, byte b)[] AccentPalette =
    {
        (0x2A, 0xB7, 0xA9), // teal
        (0xE0, 0xA6, 0x3C), // amber
        (0xC0, 0x5F, 0xA0), // magenta
        (0x6C, 0x8E, 0xE0), // cornflower (4th+ pin, in case more than three are pinned)
    };
    public const float BandFillAlpha           = 0.30f; // inner bands drawn slightly stronger
    public const float BandInnerAlphaBoost      = 0.10f; // added per band toward the target
    public const float BandBoundaryThicknessPx = 1.5f;
    public const float HatchSpacingInches       = 0.6f; // diagonal world-space cover hatch pitch
    public const float PreviewAlphaScale        = 0.5f; // hover-preview field: reduced opacity, no chip

    // --- Instruments ------------------------------------------------------------------------------
    public const float PipRadiusPx              = 3f;   // ~5-6px pip diameter along the ghost's lower edge
    public static readonly (byte r, byte g, byte b) GhostInsideThreatTint = (232, 72, 72);

    // --- Interaction (spec section 4) -------------------------------------------------------------
    public const double HoverPreviewDelaySeconds = 0.150;
    public const float  SnapEpsilonInches        = 0.4f;  // drop within this of a boundary snaps
    public const float  SnapInsideMarginInches   = 0.05f; // band snap lands just inside
    public const float  MeasurementPromoteInches = 0.5f;  // draw the labeled measurement line within this

    // --- Perf -------------------------------------------------------------------------------------
    public const double RebuildBudgetMs = 30.0; // log a warning past this per rebuild

    // --- Hotkeys ----------------------------------------------------------------------------------
    // T is taken (dev token-reveal in TableTooltipOverlay); F is free. Tab/Alt are free.
    public const ImGuiKey ThreatToggleKey     = ImGuiKey.F;
    public const ImGuiKey FocusCycleKey       = ImGuiKey.Tab;
    public const ImGuiKey ClearPinsKey        = ImGuiKey.Escape;
    public const ImGuiKey FidelitySamplerKey  = ImGuiKey.F10; // debug (spec section 6)
}
