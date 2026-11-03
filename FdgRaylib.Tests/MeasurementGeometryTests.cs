using System;
using FDG;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #251: the ruler overlay's edge-distance and snap geometry. The visual/interaction layer gets eyeballed
// in a GUI pass; this locks down the measurement itself -- specifically that a rectangular base measures
// by its true footprint rather than by IModel.BaseRadiusInches, which is its INSCRIBED radius (#149) and
// therefore wrong for anything elongated.
[TestFixture]
public class MeasurementGeometryTests
{
    private const float MmPerInch = 25.4f;

    // A 60x35mm bike base: 60mm along the facing (height), 35mm across (width).
    private static RectangleBase Bike() => new(35f / MmPerInch, 60f / MmPerInch);

    private static CircleBase Circle(float diameterMm) => new(diameterMm / MmPerInch / 2f);

    private static Position At(float xIn, float zIn) => new(xIn, 0f, zIn);

    // Facing +Z (forward) and +X (rotated 90 degrees).
    private static readonly Float2 FacingZ = new(0f, 1f);
    private static readonly Float2 FacingX = new(1f, 0f);

    [Test]
    public void TwoCircles_EdgeIsCentreDistanceMinusBothRadii()
    {
        // 32mm circles 4" apart, centre to centre. Each radius = 16mm.
        var a = Circle(32f);
        var b = Circle(32f);
        float? edge = MeasurementGeometry.EdgeDistanceInches(a, At(0f, 0f), FacingZ, b, At(0f, 4f), FacingZ);

        Assert.That(edge, Is.Not.Null);
        Assert.That(edge!.Value, Is.EqualTo(4f - 2f * (16f / MmPerInch)).Within(0.001f));
    }

    [Test]
    public void TwoBikes_NoseToNose_MeasureByLengthNotInscribedRadius()
    {
        // Both facing +Z, 4" apart along Z: they approach along their 60mm LONG axis, so each contributes
        // a 30mm half-extent. The old radius arithmetic used the inscribed 17.5mm instead.
        float? edge = MeasurementGeometry.EdgeDistanceInches(
            Bike(), At(0f, 0f), FacingZ,
            Bike(), At(0f, 4f), FacingZ);

        Assert.That(edge, Is.Not.Null);
        Assert.That(edge!.Value, Is.EqualTo(4f - 2f * (30f / MmPerInch)).Within(0.001f),
            "nose-to-nose bikes must measure from their 60mm length");

        // The defect this fixes, stated as a number: the old inscribed-radius math would have read
        // 4 - 2*(17.5/25.4) instead, overstating the gap by ~0.98" -- most of an inch, on a 12" charge.
        float oldReading = 4f - 2f * (17.5f / MmPerInch);
        Assert.That(oldReading - edge!.Value, Is.EqualTo(2f * (12.5f / MmPerInch)).Within(0.001f));
    }

    [Test]
    public void TwoBikes_SideBySide_MeasureByWidth()
    {
        // Both facing +Z but separated along X: now they approach across the 35mm width, so each
        // contributes 17.5mm -- here the inscribed radius happens to be right, which is exactly why the
        // old code looked correct in some orientations and not others.
        float? edge = MeasurementGeometry.EdgeDistanceInches(
            Bike(), At(0f, 0f), FacingZ,
            Bike(), At(4f, 0f), FacingZ);

        Assert.That(edge!.Value, Is.EqualTo(4f - 2f * (17.5f / MmPerInch)).Within(0.001f));
    }

    [Test]
    public void Facing_ChangesTheReading_ForTheSameCentres()
    {
        // Identical centres and shapes; only the facing differs. A rotated base presents a different
        // extent along the measured line, so the reading MUST differ -- the property the facing-blind
        // approximation could never express.
        float alongLength = MeasurementGeometry.EdgeDistanceInches(
            Bike(), At(0f, 0f), FacingZ, Bike(), At(0f, 4f), FacingZ)!.Value;
        float alongWidth = MeasurementGeometry.EdgeDistanceInches(
            Bike(), At(0f, 0f), FacingX, Bike(), At(0f, 4f), FacingX)!.Value;

        Assert.That(alongLength, Is.LessThan(alongWidth),
            "facing along the measured line presents the long axis, so the gap is smaller");
    }

    [Test]
    public void OverlappingBases_ReadZero_NeverNegative()
    {
        float? edge = MeasurementGeometry.EdgeDistanceInches(
            Bike(), At(0f, 0f), FacingZ,
            Bike(), At(0f, 0.2f), FacingZ);

        Assert.That(edge!.Value, Is.EqualTo(0f).Within(0.0001f));
    }

    [Test]
    public void OneEndFree_MeasuresFromTheBaseSurfaceToThePoint()
    {
        // Bike at origin facing +Z; free point 4" away along Z. Only the bike's 30mm half-length counts.
        float? edge = MeasurementGeometry.EdgeDistanceInches(
            Bike(), At(0f, 0f), FacingZ,
            null, At(0f, 4f), default);

        Assert.That(edge, Is.Not.Null);
        Assert.That(edge!.Value, Is.EqualTo(4f - (30f / MmPerInch)).Within(0.001f));
    }

    [Test]
    public void OneEndFree_IsSymmetric_WhicheverEndHoldsTheModel()
    {
        float a = MeasurementGeometry.EdgeDistanceInches(
            Bike(), At(0f, 0f), FacingZ, null, At(0f, 4f), default)!.Value;
        float b = MeasurementGeometry.EdgeDistanceInches(
            null, At(0f, 4f), default, Bike(), At(0f, 0f), FacingZ)!.Value;

        Assert.That(a, Is.EqualTo(b).Within(0.0001f));
    }

    [Test]
    public void NeitherEndSnapped_HasNoEdgeReading()
    {
        Assert.That(MeasurementGeometry.EdgeDistanceInches(
            null, At(0f, 0f), default, null, At(0f, 4f), default), Is.Null,
            "with no model at either end the ruler has only a centre-to-centre reading");
    }

    [Test]
    public void SnapDistance_IsZeroInsideTheBase_AndReachesTheLongEnds()
    {
        var bike = Bike();
        Position centre = At(0f, 0f);

        // Dead centre: inside.
        Assert.That(MeasurementGeometry.SnapDistanceInches(bike, centre, FacingZ, At(0f, 0f)),
            Is.EqualTo(0f).Within(0.0001f));

        // 25mm up the long axis: still inside the 60mm-long base (half-length 30mm). This is the case the
        // old inscribed-radius test (17.5mm) rejected, making the ends of the base unclickable.
        Assert.That(MeasurementGeometry.SnapDistanceInches(bike, centre, FacingZ, At(0f, 25f / MmPerInch)),
            Is.EqualTo(0f).Within(0.0001f), "the long ends of the base must snap");

        // Just outside the long end: positive, and small.
        float justOut = MeasurementGeometry.SnapDistanceInches(bike, centre, FacingZ, At(0f, 35f / MmPerInch));
        Assert.That(justOut, Is.EqualTo(5f / MmPerInch).Within(0.001f));
    }

    [Test]
    public void SnapDistance_FollowsFacing()
    {
        var bike = Bike();
        Position centre = At(0f, 0f);
        Position probe = At(0f, 25f / MmPerInch); // 25mm along +Z

        // Facing +Z: the long axis points at the probe, so it is inside.
        Assert.That(MeasurementGeometry.SnapDistanceInches(bike, centre, FacingZ, probe),
            Is.EqualTo(0f).Within(0.0001f));

        // Facing +X: now only the 35mm width faces the probe (half-extent 17.5mm), so it is outside.
        Assert.That(MeasurementGeometry.SnapDistanceInches(bike, centre, FacingX, probe),
            Is.GreaterThan(0f), "rotating the base must move its surface");
    }
}
