using System.Collections.Generic;
using FDG;
using FDG.Stages;

namespace FdgRaylib.Rendering.TacticalOverlay;

/// <summary>Eligibility of one (model, weapon, pin) shot: lit = in range with clear LoS, hatched = in
/// range but the shot goes into cover, dim = out of range or no LoS (spec section 4).</summary>
public enum PipState { Dim, Lit, Hatched }

/// <summary>
/// The narrow rules adapter (spec section 7). Every authoritative determination the overlay's
/// instruments make -- eligibility, counts, distances, reach -- flows through here, and this type holds
/// NO reference to the field grid or texture. That is the structural guarantee behind the invariant
/// (spec section 0): instruments call rules, never sample a texel.
///
/// It calls the engine's real functions: <see cref="DistanceUtilities"/> for base-edge measurement,
/// <see cref="FDG.Stages.LineOfSightUtilities"/> for LoS/cover, and <see cref="IUnit.GetMobility"/> for
/// movement distances. It never reimplements any rule.
/// </summary>
public sealed class RulesProbe
{
    private readonly ITableState _tableState;

    // #201: the launched game's cover-proximity-exceptions setting. Pips apply it (a pip may never
    // be wrong); BestSight deliberately does NOT - it feeds the field texture, whose polar shadow
    // maps cannot encode a two-endpoint rule, so the field keeps raw cover semantics and may paint
    // cover in spots where a proximity exception voids it. Recorded in WorkItems/201 + #162.
    private readonly bool _coverProximityExceptions;

    // #384: the launched game's see-through-allies LoS house rule; the blocker lists the pips and
    // field are built from must match the engine's, or the overlay paints shots the stage refuses.
    private readonly bool _seeThroughFriendlyUnits;

    public RulesProbe(ITableState tableState, bool coverProximityExceptions = true,
        bool seeThroughFriendlyUnits = false)
    {
        _tableState = tableState;
        _coverProximityExceptions = coverProximityExceptions;
        _seeThroughFriendlyUnits = seeThroughFriendlyUnits;
    }

    /// <summary>
    /// The charge- and shoot-reach DISTANCES (inches, before base-radius inflation) an enemy unit
    /// projects this round. Charge = the unit's charge mobility; shoot = advance mobility + its longest
    /// ranged weapon range (0 if it has no ranged weapon). The caller inflates by both base radii.
    ///
    /// Approximations (spec section 2, restated in the plan): v1 ignores difficult terrain, treating
    /// ground as open (A1); enemy distances use <see cref="IUnit.GetMobility"/> + raw weapon ranges
    /// rather than folding that enemy's own movement/range rule modifiers, since the client read path
    /// carries no RuleEvaluator (A2). The moving unit's own bands (opportunity field) ARE rule-true --
    /// they come from the request payload.
    /// </summary>
    public (float charge, float shoot) ThreatReach(IUnit unit)
    {
        unit.GetMobility(out float advance, out float charge);

        float maxRange = 0f;
        foreach (IModel m in unit.Models)
        {
            if (!m.GetIsAlive()) continue;
            foreach (Weapon w in m.Weapons)
                if (w.RangeInches > maxRange) maxRange = w.RangeInches;
        }

        float shoot = maxRange > 0f ? advance + maxRange : 0f;
        return (charge, shoot);
    }

    /// <summary>
    /// The terrain + model blockers a shot from <paramref name="movingUnit"/> at
    /// <paramref name="target"/> must clear, assembled exactly as the shooting stages do:
    /// <see cref="LineOfSightUtilities.BuildModelBlockers"/> under the game's #384 see-through-allies
    /// setting, concatenated with the live terrain snapshot. Pass to <see cref="BestSight"/>.
    /// </summary>
    public List<ITerrain> BuildBlockers(IUnit movingUnit, IUnit target)
    {
        var blockers = new List<ITerrain>(_tableState.Terrain.Objects);
        blockers.AddRange(LineOfSightUtilities.BuildModelBlockers(_tableState, movingUnit, target,
            _seeThroughFriendlyUnits));
        return blockers;
    }

    /// <summary>
    /// The best sight a shooter at <paramref name="from"/> has to <paramref name="target"/>: Clear if any
    /// living target model is seen clearly, else Cover if one is seen through cover, else Blocking (no
    /// model visible). This is the engine's real <see cref="LineOfSightUtilities.EvaluateSightLine"/> per
    /// model -- the field's LoS/cover picture is built from it, so the picture matches the rules exactly
    /// (modulo cell quantization), and the pips call the same function. "Sees the unit = sees any model"
    /// (spec section 2).
    /// </summary>
    public ESightLineEffect BestSight(Position from, IUnit target, IReadOnlyList<ITerrain> blockers)
    {
        ESightLineEffect best = ESightLineEffect.Blocking; // worst; min() toward Clear below
        foreach (IModel m in target.Models)
        {
            if (!m.GetIsAlive()) continue;
            Position p = m.Position;
            if (p.x == 0f && p.z == 0f) continue;
            ESightLineEffect eff = LineOfSightUtilities.EvaluateSightLine(from, p, blockers);
            if (eff < best) best = eff;            // Clear(0) < Cover(1) < Blocking(2)
            if (best == ESightLineEffect.Clear) break;
        }
        return best;
    }

    /// <summary>
    /// The authoritative pip state for a shooter model standing at <paramref name="shooterPos"/> firing a
    /// weapon of effective range <paramref name="effRange"/> at <paramref name="target"/>. Uses the
    /// engine's real base-edge 3D distance and <see cref="LineOfSightUtilities.EvaluateSightLine"/> against
    /// the target's living models -- range to the unit is range to its nearest model (spec section 2), so
    /// this checks every living target model and reports the best shot available. This is a pip, and a pip
    /// may never be wrong: it never reads the field texture (spec section 0).
    /// </summary>
    public PipState EvaluatePip(IModel shooter, Position shooterPos, Float2 shooterFacing,
        IUnit target, float effRange, IReadOnlyList<ITerrain> blockers)
    {
        ESightLineEffect best = ESightLineEffect.Blocking;
        bool anyInRange = false;

        foreach (IModel tm in target.Models)
        {
            if (!tm.GetIsAlive()) continue;
            Position tp = tm.Position;
            if (tp.x == 0f && tp.z == 0f) continue;

            float d = DistanceUtilities.GetBaseToBaseDistanceInches_3D(
                shooterPos, tp, shooter.BaseShape, shooterFacing, tm.BaseShape, tm.Facing);
            if (d > effRange) continue;

            anyInRange = true;
            // #201: pips use the proximity-exception overload so "hatched" (cover) only shows where
            // the cover stage would actually grant the bonus from this (possibly ghost) position.
            ESightLineEffect eff = LineOfSightUtilities.EvaluateSightLine(shooterPos, tp, blockers,
                new CoverContext(shooterPos, shooter.BaseShape, shooterFacing, tp, tm.BaseShape, tm.Facing),
                _coverProximityExceptions);
            if (eff < best) best = eff;
            if (best == ESightLineEffect.Clear) break;
        }

        if (!anyInRange) return PipState.Dim;                    // out of range
        return best switch
        {
            ESightLineEffect.Clear => PipState.Lit,
            ESightLineEffect.Cover => PipState.Hatched,
            _                      => PipState.Dim,              // in range but no LoS
        };
    }

    /// <summary>The most common alive-model base radius in a unit, or a 28mm default for an empty unit.</summary>
    public float ModalBaseRadius(IUnit unit)
    {
        // Small units -> a tiny frequency count is cheaper and clearer than grouping.
        float best = TacticalOverlayConfig.DefaultReferenceRadiusInches;
        int bestCount = 0;
        var counts = new Dictionary<float, int>();
        foreach (IModel m in unit.Models)
        {
            if (!m.GetIsAlive()) continue;
            float r = m.BaseRadiusInches;
            int c = counts.TryGetValue(r, out int existing) ? existing + 1 : 1;
            counts[r] = c;
            if (c > bestCount) { bestCount = c; best = r; }
        }
        return best;
    }
}
