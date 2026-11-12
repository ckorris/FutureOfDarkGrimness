using FDG;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FdgRaylib.Cli.Resolvers;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace FdgRaylib.Tests;

// #426 - the CLI placement resolver honours a request's enemy keep-out discs (Scout's "over 9in away from
// non-Scout enemy units", Vanguard's "non-Vanguard") the way the engine builds them: a typed spot inside one is
// refused with the STAGE's wording (no restated number), and the EOF auto-placer never lands in one.
[TestFixture]
public class PlaceObjectsResolverKeepOutTests
{
    private const string Note = "Too close to a non-Scout enemy unit - Scout units must be set up over 9\" away from them.";

    private TextReader _originalIn = null!;
    private TextWriter _originalOut = null!;
    private StringWriter _out = null!;

    [SetUp]
    public void SetUp()
    {
        _originalIn = Console.In;
        _originalOut = Console.Out;
        _out = new StringWriter();
        Console.SetOut(_out);
    }

    [TearDown]
    public void TearDown()
    {
        Console.SetIn(_originalIn);
        Console.SetOut(_originalOut);
    }

    // A Scout band (bottom zone extended to 18") with an enemy picket line standing inside it at z = 14: only a
    // strip under z ~ 5.5 is clear of the 9" discs, and the auto-placer's natural spot (the band's middle row)
    // is not in it.
    private static (PlaceObjectsRequest<ModelData> Request, ModelData Model) ScoutRequest()
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var model = new ModelData(0.5f, new List<Weapon>(), new Position(0f, 0f), store);
        var binding = store.GetDataBinding<ModelData>(store.Create(model));
        var discs = Enumerable.Range(0, 13).Select(i => new PlacementDisc(new Position(i * 6f, 14f), 9f)).ToList();
        var request = new PlaceObjectsRequest<ModelData>(new PlayerID(Guid.NewGuid()), "Place Scout Unit",
            new RectangularZone(0f, 72f, 0f, 18f), new List<DataBinding<ModelData>> { binding },
            enemyKeepOutDiscs: discs, enemyKeepOutNote: Note);
        return (request, model);
    }

    [Test]
    public async Task ATypedSpotInsideAKeepOut_IsRefusedWithTheStagesWording()
    {
        (PlaceObjectsRequest<ModelData> request, _) = ScoutRequest();
        Console.SetIn(new StringReader("36 12\n36 4\n"));

        var result = await new PlaceObjectsResolver<ModelData>().Resolve(request);

        Assert.That(_out.ToString(), Does.Contain("! " + Note), "the stage's reason, verbatim");
        Assert.That(_out.ToString(), Does.Not.Contain("Repel Ambushers"),
            "not the Ambush wording - this keep-out is Scout's");
        var placed = ((Selected<List<PlacedObjectEntry<ModelData>>>)result).Value.Single();
        Assert.That(placed.Position.z, Is.EqualTo(4f).Within(0.001f), "the legal second spot was taken");
    }

    [Test]
    public async Task EofAutoPlacement_StaysOutOfEveryKeepOut()
    {
        (PlaceObjectsRequest<ModelData> request, _) = ScoutRequest();
        Console.SetIn(new StringReader("")); // immediate EOF

        var result = await new PlaceObjectsResolver<ModelData>().Resolve(request);

        var placed = ((Selected<List<PlacedObjectEntry<ModelData>>>)result).Value.Single();
        Assert.That(PlacementDistanceRules.ViolatesEnemyDistance(request, placed.Position, new List<Position>()),
            Is.False, $"auto-placed at ({placed.Position.x:F1}, {placed.Position.z:F1}), outside every disc");
    }
}
