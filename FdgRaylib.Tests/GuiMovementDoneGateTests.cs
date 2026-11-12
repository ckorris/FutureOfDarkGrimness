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

// #458: the movement panel's Done gate must agree with the engine's check of the reply (DefinePathStage /
// MovementExecutor.ValidateMove: lenient #159 coherency, every base at the facing the move leaves it at).
// Its old hand-mirrored check measured bases at their RESTING facing, strictly - the same drift that
// ended a v0.3.0 game from the consolidation panel (see GuiConsolidationDoneGateTests).
[TestFixture]
public class GuiMovementDoneGateTests
{
    private static DataBinding<ModelData> MakeModel(GameDataStore store, IBaseShape shape, Position pos)
    {
        var model = new ModelData(shape, new List<Weapon>(), pos, store);
        return store.GetDataBinding<ModelData>(store.Create(model));
    }

    private static (GuiDefineMovementResolver resolver, DefineMovementPathRequest request,
        DataBinding<ModelData> a, DataBinding<ModelData> b) TwoModelMove(IBaseShape shape, Position pa, Position pb)
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var owner = new PlayerID(Guid.NewGuid());
        var a = MakeModel(store, shape, pa);
        var b = MakeModel(store, shape, pb);
        var unit = store.GetDataBinding<UnitData>(store.Create(new UnitData(owner, "Unit", quality: 4, defense: 4,
            modelBindings: new List<DataBinding<ModelData>> { a, b })));
        var request = new DefineMovementPathRequest(owner, "Move", unit,
            maxAdvanceDistance: 6f, maxRushDistance: 12f, maxDistanceInches: 12f, mainActivationMove: true);
        return (new GuiDefineMovementResolver(new TableState(store), new FormationModeState()), request, a, b);
    }

    [Test]
    public void RectanglesTurnedByTravel_GateRejectsAtTheEndFacing()
    {
        // 4"x1" bases, long side along X at rest, 0.5" apart. Stepping both 1" along +X turns each to face
        // its travel (+X), so the long side now runs along Z and they end 3.5" apart.
        var shape = new RectangleBase(4f, 1f);
        var (resolver, request, a, b) = TwoModelMove(shape, new Position(10f, 10f), new Position(14.5f, 10f));
        var travel = new Float2(1f, 0f);

        var results = new List<ModelMoveEntry>
        {
            new(a, new List<Position> { new Position(11f, 10f) }, new List<Float2> { travel }),
            new(b, new List<Position> { new Position(15.5f, 10f) }, new List<Float2> { travel }),
        };
        float atRestingFacing = DistanceUtilities.GetBaseToBaseDistanceInches_3D(
            results[0].Positions[0], results[1].Positions[0], shape, a.GetValue().Facing, shape, b.GetValue().Facing);
        Assume.That(atRestingFacing, Is.LessThanOrEqualTo(1f), "the old resting-facing check would have passed this");

        bool valid = resolver.ValidateForDone(request, results, out var errors);

        Assert.That(valid, Is.False);
        Assert.That(errors.Select(e => e.ErrorReasonType),
            Has.Some.EqualTo(EErrorReasonType.TooFarFromAnyUnitModel));
    }

    [Test]
    public void AlreadyBrokenUnit_PartialReform_IsAllowed()
    {
        // #159: survivors start 2" apart; closing to 1.5" is legal for the engine (not worse), and the old
        // strict gate refused it.
        var (resolver, request, a, b) = TwoModelMove(new CircleBase(0.5f),
            new Position(10f, 10f), new Position(13f, 10f));

        var results = new List<ModelMoveEntry>
        {
            new(a, new List<Position>()),
            new(b, new List<Position> { new Position(12.5f, 10f) }),
        };

        bool valid = resolver.ValidateForDone(request, results, out var errors);

        Assert.That(valid, Is.True, string.Join(", ", errors.Select(e => e.ToString())));
    }
}
