using FDG;
using FDG.Data;
using FdgRaylib.Rendering.Resolvers;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// #295 — single-model movement/consolidation switch models by clicking the model directly (Space used to
/// cycle; Space now confirms), and the hover highlight that advertises it is painted from the SAME hit test
/// the click reads. These pin that hit test: what lights up under the cursor is what a click selects.
/// #310 — the hit test now takes each model's POSE (planned ghost position + facing): a model with
/// committed waypoints is picked where its ghost stands, and its vacated start slot is bare table.
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

    // A model standing where it is (no plan): pose = its own position + facing.
    private static (IModel, Position, Float2) AtRest(IModel m) => (m, m.Position, m.Facing);

    // A model with a planned move: pose = the planned endpoint (+ optional end facing).
    private static (IModel, Position, Float2) PlannedAt(IModel m, float x, float z, Float2? facing = null) =>
        (m, new Position(x, z), facing ?? m.Facing);

    [Test]
    public void PointInsideABase_PicksThatModel()
    {
        var a = Model(10f, 10f);
        var b = Model(20f, 20f);

        Assert.That(ModelPicker.HitTest(new[] { AtRest(a), AtRest(b) }, 10.2f, 9.8f), Is.SameAs(a));
        Assert.That(ModelPicker.HitTest(new[] { AtRest(a), AtRest(b) }, 20f, 20f), Is.SameAs(b));
    }

    [Test]
    public void PointOnBareTable_PicksNothing()
    {
        var a = Model(10f, 10f);

        // 1" away from a 0.5"-radius base: outside the footprint, so a click there places a waypoint
        // instead of switching models. A near-miss must not "helpfully" snap to the nearest model.
        Assert.That(ModelPicker.HitTest(new[] { AtRest(a) }, 11f, 10f), Is.Null);
    }

    [Test]
    public void OverlappingBases_PickTheNearerCentre()
    {
        // Two big bases whose footprints overlap; the cursor sits inside both, nearer to b.
        var a = Model(10f, 10f, radius: 2f);
        var b = Model(13f, 10f, radius: 2f);

        Assert.That(ModelPicker.HitTest(new[] { AtRest(a), AtRest(b) }, 12f, 10f), Is.SameAs(b));
        Assert.That(ModelPicker.HitTest(new[] { AtRest(b), AtRest(a) }, 12f, 10f), Is.SameAs(b),
            "the pick must not depend on iteration order");
        Assert.That(ModelPicker.HitTest(new[] { AtRest(a), AtRest(b) }, 11f, 10f), Is.SameAs(a));
    }

    [Test]
    public void RectangularBase_IsHitByItsFootprintNotItsBoundingCircle()
    {
        // A long, narrow vehicle: 1" wide x 6" long, default +Z facing. A point 2" out along Z is on the
        // model; the same distance out along X is off it -- a circle-radius test would claim both.
        var vehicle = Model(new RectangleBase(1f, 6f), 10f, 10f);

        Assert.That(ModelPicker.HitTest(new[] { AtRest(vehicle) }, 10f, 12f), Is.SameAs(vehicle));
        Assert.That(ModelPicker.HitTest(new[] { AtRest(vehicle) }, 12f, 10f), Is.Null);
    }

    [Test]
    public void RotatedRectangularBase_IsHitAtItsFacing()
    {
        // #310: the same vehicle turned to face +X: its long axis now runs along X, so the point 2" out
        // along X is ON it and the point 2" out along Z is OFF it -- the reverse of the unrotated case.
        var vehicle = Model(new RectangleBase(1f, 6f), 10f, 10f);
        var facingX = new Float2(1f, 0f);

        Assert.That(ModelPicker.HitTest(new[] { (vehicle as IModel, new Position(10f, 10f), facingX) }, 12f, 10f),
            Is.SameAs(vehicle));
        Assert.That(ModelPicker.HitTest(new[] { (vehicle as IModel, new Position(10f, 10f), facingX) }, 10f, 12f),
            Is.Null);
    }

    [Test]
    public void PlannedModel_IsHitAtItsEndpoint_NotItsVacatedStart()
    {
        // #310: model a is planned to move from (10,10) to (16,10). Clicking its vacated start slot must
        // NOT pick it (that ground is free for another model's waypoint - the "6 of 10 moved" bug);
        // clicking where its ghost stands must pick it.
        var a = Model(10f, 10f);

        Assert.That(ModelPicker.HitTest(new[] { PlannedAt(a, 16f, 10f) }, 10f, 10f), Is.Null,
            "the vacated start slot must be bare table");
        Assert.That(ModelPicker.HitTest(new[] { PlannedAt(a, 16f, 10f) }, 16.2f, 9.8f), Is.SameAs(a),
            "the planned endpoint is the model's click hotspot");
    }

    [Test]
    public void EmptyUnit_PicksNothing()
    {
        Assert.That(ModelPicker.HitTest(System.Array.Empty<(IModel, Position, Float2)>(), 10f, 10f), Is.Null);
    }
}
