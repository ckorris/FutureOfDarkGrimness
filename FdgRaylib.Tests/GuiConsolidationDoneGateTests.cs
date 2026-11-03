using System;
using System.Collections.Generic;
using System.Linq;
using FDG;
using FDG.Data;
using FDG.SaveLoad;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FdgRaylib.Rendering.Resolvers;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #458: the consolidation Done gate must be the validator ConsolidateStage runs, not a hand-mirror of it.
// A v0.3.0 game ended with "Response to ConsolidateStage was invalid: Breaks cohesion" because the
// gate re-measured coherency itself at every base's RESTING facing (strictly), while the stage measures
// at the facing the move leaves each base at (#312) under the lenient not-worsened rule (#159). Circles
// can't tell the difference; a group-rotated unit of rectangular bases can.
[TestFixture]
public class GuiConsolidationDoneGateTests
{
    private static DataBinding<ModelData> MakeModel(GameDataStore store, IBaseShape shape, Position pos)
    {
        var model = new ModelData(shape, new List<Weapon>(), pos, store);
        return store.GetDataBinding<ModelData>(store.Create(model));
    }

    private static (TableState table, PlayerID owner, DataBinding<UnitData> unit,
        DataBinding<ModelData> a, DataBinding<ModelData> b) TwoModelUnit(IBaseShape shape, Position pa, Position pb)
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var owner = new PlayerID(Guid.NewGuid());
        var a = MakeModel(store, shape, pa);
        var b = MakeModel(store, shape, pb);
        var unit = new UnitData(owner, "Unit", quality: 4, defense: 4,
            modelBindings: new List<DataBinding<ModelData>> { a, b });
        return (new TableState(store), owner, store.GetDataBinding<UnitData>(store.Create(unit)), a, b);
    }

    private static ConsolidationMoveRequest Request(PlayerID owner, DataBinding<UnitData> unit) =>
        new(owner, "Consolidate", unit, maxDistanceInches: 3f, EConsolidationReason.Wipeout);

    [Test]
    public void RotatedRectangles_GateRejectsAtTheEndFacing_AsTheStageDoes()
    {
        // 4"x1" bases, long side along X at rest, 0.5" apart. Turning both 90 degrees in place leaves
        // them 3.5" apart - in coherency only if measured at the resting facing.
        var shape = new RectangleBase(4f, 1f);
        var (table, owner, unit, a, b) = TwoModelUnit(shape, new Position(10f, 10f), new Position(14.5f, 10f));
        var turned = new Float2(1f, 0f);

        float atRest = DistanceUtilities.GetBaseToBaseDistanceInches_3D(a.GetValue().Position, b.GetValue().Position,
            shape, a.GetValue().Facing, shape, b.GetValue().Facing);
        Assume.That(atRest, Is.LessThanOrEqualTo(1f), "the old resting-facing check would have passed this");

        var results = new List<ModelMoveEntry>
        {
            new(a, new List<Position> { a.GetValue().Position }, new List<Float2> { turned }),
            new(b, new List<Position> { b.GetValue().Position }, new List<Float2> { turned }),
        };

        bool valid = GuiConsolidationMoveResolver.ValidateForDone(table, Request(owner, unit), results, out var errors);

        Assert.That(valid, Is.False);
        Assert.That(errors.Select(e => e.ErrorReasonType),
            Has.Some.EqualTo(EErrorReasonType.TooFarFromAnyUnitModel));
    }

    [Test]
    public void AlreadyBrokenUnit_ReformThatStillFallsShort_IsAllowed()
    {
        // #159: survivors 2" apart after a casualty; closing to 1.5" doesn't restore coherency but doesn't
        // worsen it, so the stage accepts it - the old strict gate greyed Done out instead.
        var (table, owner, unit, a, b) = TwoModelUnit(new CircleBase(0.5f),
            new Position(10f, 10f), new Position(13f, 10f));

        var results = new List<ModelMoveEntry>
        {
            new(a, new List<Position>()),
            new(b, new List<Position> { new Position(12.5f, 10f) }),
        };

        bool valid = GuiConsolidationMoveResolver.ValidateForDone(table, Request(owner, unit), results, out var errors);

        Assert.That(valid, Is.True, string.Join(", ", errors.Select(e => e.ToString())));
    }
}
