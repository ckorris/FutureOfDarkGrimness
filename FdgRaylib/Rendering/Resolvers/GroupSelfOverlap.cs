using System;
using System.Collections.Generic;
using FDG;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// #399 - "would any two models of THIS unit end their step stacked on each other?", the one rule the
/// group-move preview never checked. <c>GuiDefineMovementResolver.GroupPositionBlocked</c> skips the
/// moving unit outright on the grounds that "my own cohesion governs my models' spacing", but cohesion
/// is a MAXIMUM-distance rule: two models 0" apart satisfy it perfectly. The engine's
/// <c>MovementUtilities.ValidateNoSelfOverlap</c> (#366) catches it at Done; nothing caught it while
/// placing, so the step committed and only the Done gate complained, pointing at a waypoint that was
/// already on the table.
///
/// <para>The case that surfaced it is specific to non-circular bases. A group step turns every phantom
/// to ONE shared heading (<c>GroupFormationUtilities.GroupHeading</c>), so dragging a rank of
/// rectangles sideways swings each base across its neighbours while their centres keep exactly the
/// spacing they had. Circles are rotation-invariant and never showed it - which is why it read as a
/// jetbike bug rather than a group-move bug.</para>
///
/// <para>Its own file, and taking poses rather than models, so the rule is testable without a table:
/// the same reason <c>ModelRoster</c> and <c>PlacementPanelLayout</c> are not methods on a resolver.</para>
/// </summary>
internal static class GroupSelfOverlap
{
    /// <summary>A base at a place and an attitude - the footprint a model actually presents.</summary>
    internal readonly record struct Pose(IBaseShape Shape, Position At, Float2 Facing);

    /// <summary>
    /// Flags every pair whose <paramref name="ends"/> footprints collide but whose
    /// <paramref name="starts"/> footprints did not, setting BOTH indices in
    /// <paramref name="blocked"/> - neither model of a pair is "the" offender, and one red base beside a
    /// legal-looking one reads as the wrong model being blamed.
    ///
    /// <para>"Not worsened", like the engine rule and like <c>ValidateEndsOnTable</c>: a pair that is
    /// ALREADY overlapping where it stands (an older save, a forced move, a past bug) must not be
    /// frozen in place by a validator that rejects every step it can make. Only a newly-stacked pair is
    /// illegal, which is why the start poses are measured rather than assumed clean.</para>
    ///
    /// <para>A null entry (a dead model) takes no part. Indices are shared across all three lists.</para>
    /// </summary>
    /// <returns>
    /// Where to anchor the one explanatory caption - midway between the first flagged pair, with the
    /// larger of their two radii - or null when nothing overlapped. Non-null is exactly "the step is
    /// illegal", so a caller needs no second flag to test.
    /// </returns>
    internal static (Position at, float radiusInches)? Mark(
        IReadOnlyList<Pose?> starts, IReadOnlyList<Pose?> ends, bool[] blocked)
    {
        (Position at, float radiusInches)? anchor = null;

        for (int i = 0; i < ends.Count; i++)
        {
            if (ends[i] is not { } endI || starts[i] is not { } startI) continue;

            for (int j = i + 1; j < ends.Count; j++)
            {
                if (ends[j] is not { } endJ || starts[j] is not { } startJ) continue;

                // Cheap reject on circumscribing circles before the exact footprint test: a base fits
                // inside that circle at ANY facing, so two clear circles cannot collide however they turn.
                float reach = endI.Shape.CircumscribedRadiusInches + endJ.Shape.CircumscribedRadiusInches;
                if (Position.GetDistance2D(endI.At, endJ.At) > reach) continue;

                // True oriented footprints (#150), because the facing is the whole point here: a
                // rectangular base that pivots into its neighbour overlaps without either centre having
                // moved an inch closer.
                if (!BaseShapeGeometry.AreColliding(endI.Shape, endI.At, endI.Facing,
                                                    endJ.Shape, endJ.At, endJ.Facing)) continue;

                if (BaseShapeGeometry.AreColliding(startI.Shape, startI.At, startI.Facing,
                                                   startJ.Shape, startJ.At, startJ.Facing)) continue;

                blocked[i] = true;
                blocked[j] = true;

                anchor ??= (new Position((endI.At.x + endJ.At.x) * 0.5f, (endI.At.z + endJ.At.z) * 0.5f),
                    MathF.Max(endI.Shape.CircumscribedRadiusInches, endJ.Shape.CircumscribedRadiusInches));
            }
        }

        return anchor;
    }
}
