using System;
using System.Collections.Generic;
using FDG;
using FDG.Data;
using FDG.Players;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #401 - the one wording both wound-assignment fronts (ImGui panel, CLI prompt) use once the queue
// carries Deadly clumps. A plain pool keeps the old line; a clump queue says what the next click does,
// because "x / y assigned" can never reach y when a clump's excess is lost at its model.
[TestFixture]
public class WoundAssignmentTextTests
{
    [Test]
    public void PlainPool_KeepsTheAssignedOverTotalLine()
    {
        var results = new AssignWoundsResults(MakeUnit(3, 3), totalWoundsToAssign: 4f);

        Assert.That(WoundAssignmentText.Progress(results), Is.EqualTo("0 / 4 wounds assigned"));
        Assert.That(WoundAssignmentText.ClickHint(results), Is.EqualTo("Click to assign wounds (fills this model)"));
    }

    [Test]
    public void ClumpQueue_ReportsLandedLostAndWhatTheNextClickPlaces()
    {
        DataBinding<UnitData> unit = MakeUnit(3, 3);
        unit.GetValue().ModelBindings[2].GetValue().DealWounds(2); // the pre-assignment eats clump 1 here
        var results = new AssignWoundsResults(unit, new[] { WoundPacket.Clump(3f), WoundPacket.Clump(3f) });

        Assert.That(WoundAssignmentText.Progress(results),
            Is.EqualTo("1 landed, 2 lost (Deadly: no carry-over)\nNext: clump 2 of 2 - 3 wounds to ONE model"));
        Assert.That(WoundAssignmentText.ClickHint(results),
            Is.EqualTo("Click to land this clump here (3 wounds - what does not fit is lost)"));

        results.AutoFill();
        Assert.That(WoundAssignmentText.Next(results), Is.EqualTo("All wounds placed."));
    }

    [Test]
    public void MixedQueue_NamesThePlainTail()
    {
        var results = new AssignWoundsResults(MakeUnit(2, 3),
            new[] { WoundPacket.Clump(3f), WoundPacket.Unconfined(2f) });
        results.TryAddWounds(results.PendingWounds[0].Model);

        Assert.That(WoundAssignmentText.Next(results), Is.EqualTo("Next: 2 plain wound(s) (2 of 2)"));
    }

    private static DataBinding<UnitData> MakeUnit(int modelCount, int woundsPerModel)
    {
        GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var modelBindings = new List<DataBinding<ModelData>>();
        for (int i = 0; i < modelCount; i++)
        {
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                initialPosition: new Position(10f + i, 10f), gameDataStore: store);
            model.SetMaxWounds(woundsPerModel);
            modelBindings.Add(store.GetDataBinding<ModelData>(store.Create(model)));
        }
        var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Brutes", quality: 4, defense: 4,
            modelBindings: modelBindings);
        return store.GetDataBinding<UnitData>(store.Create(unit));
    }
}
