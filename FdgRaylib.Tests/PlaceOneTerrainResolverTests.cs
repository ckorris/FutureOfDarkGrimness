using FDG;
using FDG.Data;
using FDG.SaveLoad;
using FDG.StageResolution.Requests;
using FdgRaylib.Cli.Resolvers;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace FdgRaylib.Tests;

// #301 Alternating: Points - the CLI resolver must honor the request's budget: an unaffordable pick
// re-prompts instead of resolving, and the EOF fallback (piped/automated runs) never picks a piece
// the server-side check would bounce right back.
[TestFixture]
public class PlaceOneTerrainResolverTests
{
    private TextReader _originalIn = null!;
    private TextWriter _originalOut = null!;

    [SetUp]
    public void SetUp()
    {
        _originalIn = Console.In;
        _originalOut = Console.Out;
        Console.SetOut(new StringWriter()); // swallow the resolver's menu text
    }

    [TearDown]
    public void TearDown()
    {
        Console.SetIn(_originalIn);
        Console.SetOut(_originalOut);
    }

    private static List<TerrainPieceEntry> ExpensiveThenCheapPool() => new()
    {
        new TerrainPieceEntry
        {
            Name = "Big building", TerrainType = ETerrainType.Blocking,
            Shape = new RectangularZone(0f, 6f, 0f, 4f), Points = 3,
        },
        new TerrainPieceEntry
        {
            Name = "Fence", TerrainType = ETerrainType.Cover,
            Shape = new RectangularZone(0f, 3f, 0f, 1f), Points = 1,
        },
    };

    // 1 point left this turn, 1 left overall, a piece already placed - only the 1-cost fence is legal.
    private static TerrainPointsBudget OnePointLeftBudget() => new(
        allotmentTotal: 9, allotmentRemaining: 1, turnBudgetRemaining: 1,
        debtPaidThisTurn: 0, piecesPlacedThisTurn: 1);

    private static PlaceOneTerrainRequest PointsRequest() => new(
        new PlayerID(Guid.NewGuid()), "Terrain", piecesPlaced: 0, totalPieces: 0,
        ExpensiveThenCheapPool(), tableWidthInches: 48f, tableHeightInches: 48f,
        pointsBudget: OnePointLeftBudget());

    private static PlaceOneTerrainResolver NewResolver() =>
        new(new TableState(GameDataStore.GameDataStoreBuilder.GetDefault()));

    [Test]
    public async Task EofFallback_WithABudget_PicksAnAffordableTemplate()
    {
        Console.SetIn(new StringReader("")); // immediate EOF

        TerrainPlacementResult result = await NewResolver().Resolve(PointsRequest());

        Assert.That(result.TemplateIndex, Is.EqualTo(1),
            "the 3-cost building exceeds the 1 remaining point - the fallback must pick the fence.");
    }

    [Test]
    public async Task UnaffordablePick_RepromptsUntilALegalOne()
    {
        Console.SetIn(new StringReader("0 10,10\n1 10,10\n"));

        TerrainPlacementResult result = await NewResolver().Resolve(PointsRequest());

        Assert.That(result.TemplateIndex, Is.EqualTo(1),
            "the first (unaffordable) pick is refused with its reason; the second is accepted.");
        Assert.That(result.Center.X, Is.EqualTo(10f).Within(0.001f));
    }

    [Test]
    public async Task WithoutABudget_AnyPickResolvesAsBefore()
    {
        Console.SetIn(new StringReader("0 10,10\n"));
        var request = new PlaceOneTerrainRequest(
            new PlayerID(Guid.NewGuid()), "Terrain", piecesPlaced: 0, totalPieces: 4,
            ExpensiveThenCheapPool(), tableWidthInches: 48f, tableHeightInches: 48f);

        TerrainPlacementResult result = await NewResolver().Resolve(request);

        Assert.That(result.TemplateIndex, Is.EqualTo(0),
            "One Per mode has no budget - the 3-cost label is inert and every pick is legal.");
    }
}
