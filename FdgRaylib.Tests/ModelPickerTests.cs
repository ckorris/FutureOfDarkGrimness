using FDG;
using FDG.Data;
using FdgRaylib.Rendering.Resolvers;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// #295 — single-model movement/consolidation switch models by clicking the model directly (Space used to
/// cycle; Space now confirms), and the hover highlight that advertises it is painted from the SAME hit test
/// the click reads. These pin that hit test: what lights up under the cursor is what a click selects.
/// </summary>
[TestFixture]
public class ModelPickerTests
{
    private GameDataStore _store = null!;

    [SetUp]
    public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

    private ModelData Model(float x, float z, float radius = 0.5f) =>
        new(baseRadiusInches: radius, weapons: new List<Weapon>(), initialPosition: new Position(x, z),
            gameDataStore: _store);

    private ModelData Model(IBaseShape shape, float x, float z) =>
        new(shape, weapons: new List<Weapon>(), initialPosition: new Position(x, z), gameDataStore: _store);

    [Test]
    public void PointInsideABase_PicksThatModel()
    {
        var a = Model(10f, 10f);
        var b = Model(20f, 20f);

        Assert.That(ModelPicker.HitTest(new IModel[] { a, b }, 10.2f, 9.8f), Is.SameAs(a));
        Assert.That(ModelPicker.HitTest(new IModel[] { a, b }, 20f, 20f), Is.SameAs(b));
    }

    [Test]
    public void PointOnBareTable_PicksNothing()
    {
        var a = Model(10f, 10f);

        // 1" away from a 0.5"-radius base: outside the footprint, so a click there places a waypoint
        // instead of switching models. A near-miss must not "helpfully" snap to the nearest model.
        Assert.That(ModelPicker.HitTest(new IModel[] { a }, 11f, 10f), Is.Null);
    }

    [Test]
    public void OverlappingBases_PickTheNearerCentre()
    {
        // Two big bases whose footprints overlap; the cursor sits inside both, nearer to b.
        var a = Model(10f, 10f, radius: 2f);
        var b = Model(13f, 10f, radius: 2f);

        Assert.That(ModelPicker.HitTest(new IModel[] { a, b }, 12f, 10f), Is.SameAs(b));
        Assert.That(ModelPicker.HitTest(new IModel[] { b, a }, 12f, 10f), Is.SameAs(b),
            "the pick must not depend on iteration order");
        Assert.That(ModelPicker.HitTest(new IModel[] { a, b }, 11f, 10f), Is.SameAs(a));
    }

    [Test]
    public void RectangularBase_IsHitByItsFootprintNotItsBoundingCircle()
    {
        // A long, narrow vehicle: 1" wide x 6" long, default +Z facing. A point 2" out along Z is on the
        // model; the same distance out along X is off it -- a circle-radius test would claim both.
        var vehicle = Model(new RectangleBase(1f, 6f), 10f, 10f);

        Assert.That(ModelPicker.HitTest(new IModel[] { vehicle }, 10f, 12f), Is.SameAs(vehicle));
        Assert.That(ModelPicker.HitTest(new IModel[] { vehicle }, 12f, 10f), Is.Null);
    }

    [Test]
    public void EmptyUnit_PicksNothing()
    {
        Assert.That(ModelPicker.HitTest(System.Array.Empty<IModel>(), 10f, 10f), Is.Null);
    }
}
