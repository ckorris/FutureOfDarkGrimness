namespace FdgRaylib.Rendering.TacticalOverlay;

/// <summary>
/// What the opportunity field is anchored on this frame — the single decision that used to be spread
/// across four places (<c>DrawField</c>'s move-request branch, the <c>GhostAnchoredField</c> mode flag,
/// <c>ResolveFieldTarget</c>'s pins/hover, and #230's placement fallback).
/// </summary>
internal enum FieldAnchorKind
{
    /// <summary>Nothing draws — the overlay is off, or nothing on screen can anchor a field.</summary>
    None,

    /// <summary>The hovered unit, at its live positions. Stationary, so the rebuild is signature-cached.</summary>
    Hover,

    /// <summary>The pending positions of a move or placement in progress. Moves every frame by design.</summary>
    Ghost,

    /// <summary>The pinned enemy — "where can I stand to shoot it", the original #162 picture.</summary>
    Target,
}

/// <summary>
/// The display-independent core of "who anchors the field this frame?". Exactly one anchor wins, which is
/// what makes the pictures mutually exclusive: two team-coloured washes over the same ground are
/// unreadable, so the contest is the feature, not just tidiness.
///
/// <para>Split out of <see cref="TacticalOverlayController"/> so the priority order is testable without
/// ImGui, a table, or a GPU — same split as <see cref="Rendering.ReachRingPlan"/> (#214). The controller
/// gathers the inputs and does the drawing.</para>
/// </summary>
internal static class FieldAnchorPlan
{
    /// <summary>
    /// The winning anchor, in priority order.
    /// <list type="number">
    /// <item>The overlay toggle (V) gates everything.</item>
    /// <item><paramref name="hoverAvailable"/> — inspecting a unit beats whatever else is on screen. This
    /// is what lets a player read any unit's reach mid-decision, or while idle waiting on an opponent,
    /// which nothing else offers. Callers must exclude the unit whose ghosts are live: its models still
    /// stand at their ORIGINAL positions, so anchoring there while the player aims ghosts somewhere else
    /// would answer a question nobody asked.</item>
    /// <item>A move job shows the pinned target's "where can I stand to shoot it" when something is
    /// pinned, and otherwise its own ghosts — "what can I hit from here". <b>Pinning is the gesture</b>,
    /// which is what retired the old <c>GhostAnchoredField</c> mode flag: the default (nothing pinned) is
    /// now the picture a player actually gets by moving, instead of a blank table until they found a
    /// checkbox in the Esc menu.</item>
    /// <item>A placement (#230) is always ghost-anchored: pins are scoped to a move job, so the
    /// target-anchored question has no meaning there.</item>
    /// </list>
    /// </summary>
    internal static FieldAnchorKind Resolve(
        bool showReach,
        bool hoverAvailable,
        bool moveJobActive,
        bool pinnedTargetAvailable,
        bool placementGhostsAvailable)
    {
        if (!showReach) return FieldAnchorKind.None;
        if (hoverAvailable) return FieldAnchorKind.Hover;

        if (moveJobActive)
            return pinnedTargetAvailable ? FieldAnchorKind.Target : FieldAnchorKind.Ghost;

        return placementGhostsAvailable ? FieldAnchorKind.Ghost : FieldAnchorKind.None;
    }
}
