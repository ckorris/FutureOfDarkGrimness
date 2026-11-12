using System.Collections.Generic;
using FDG;

namespace FdgRaylib.Rendering.TacticalOverlay;

/// <summary>
/// One enemy model's contribution to the threat picture: a disc centre (world inches) and the two
/// already-inflated reach radii (base radii folded in by the caller). ShootRadius is 0 for a model
/// whose unit has no ranged weapon.
/// </summary>
internal readonly record struct ThreatDisc(float X, float Z, float ChargeRadius, float ShootRadius);

/// <summary>
/// Builds the aggregate threat frontiers (spec section 5): unions every qualifying enemy's charge- and
/// shoot-reach discs into two masks, then marches each into polylines -- charge drawn solid, shoot
/// dashed. Rebuilt only when a trigger fires (an enemy activates / loses models / becomes Shaken, a new
/// round, the reference changes), never per frame.
/// </summary>
internal sealed class ThreatFrontierCache
{
    private readonly FieldMask _chargeMask;
    private readonly FieldMask _shootMask;

    public List<List<Float2>> ChargePolylines { get; private set; } = new();
    public List<List<Float2>> ShootPolylines  { get; private set; } = new();

    public ThreatFrontierCache(int w, int h, float tpi)
    {
        _chargeMask = new FieldMask(w, h, tpi);
        _shootMask  = new FieldMask(w, h, tpi);
    }

    public void Rebuild(IReadOnlyList<ThreatDisc> discs, float simplifyEpsilonInches)
    {
        _chargeMask.Clear();
        _shootMask.Clear();

        foreach (ThreatDisc d in discs)
        {
            _chargeMask.RasterizeDiscMax(d.X, d.Z, d.ChargeRadius, 1);
            if (d.ShootRadius > 0f)
                _shootMask.RasterizeDiscMax(d.X, d.Z, d.ShootRadius, 1);
        }

        ChargePolylines = MarchingSquares.Extract(_chargeMask, 1, simplifyEpsilonInches);
        ShootPolylines  = MarchingSquares.Extract(_shootMask, 1, simplifyEpsilonInches);
    }

    public void Clear()
    {
        ChargePolylines = new List<List<Float2>>();
        ShootPolylines  = new List<List<Float2>>();
    }

    // Fidelity-sampler read hooks: the field's claim of "inside charge/shoot reach" at a world point.
    public bool SampleChargeInside(float xIn, float zIn) => _chargeMask.SampleAt(xIn, zIn) > 0;
    public bool SampleShootInside(float xIn, float zIn)  => _shootMask.SampleAt(xIn, zIn) > 0;
}
