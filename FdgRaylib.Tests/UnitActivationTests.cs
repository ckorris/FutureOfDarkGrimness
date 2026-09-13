using System;
using System.Collections.Generic;
using FDG;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// #329/#404 - "has this unit spent its activation this round", the one definition the canvas labels and
/// the army list overlay share.
///
/// <para>#404: the check is "missing from the round's unactivated pool", and a unit held off-table in
/// Ambush reserve is missing from that pool for the OPPOSITE reason - it cannot act until it arrives
/// (SingleRoundContext.SetUnactivatedUnits admits only units on the battlefield or embarked). So every
/// reserve unit read as Activated in the army list, in red, while it was in fact the one thing on the
/// roster that had done nothing at all.</para>
/// </summary>
[TestFixture]
public class UnitActivationTests
{
    private GameDataStore _store = null!;

    [SetUp]
    public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

    private UnitData MakeUnit(string name)
    {
        var models = new List<DataBinding<ModelData>>();
        var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
            initialPosition: new Position(0f, 0f), gameDataStore: _store);
        models.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));

        var unit = new UnitData(new PlayerID(Guid.NewGuid()), name, quality: 4, defense: 4,
            modelBindings: models);
        _store.Create(unit);
        return unit;
    }

    [Test]
    public void AUnitStillInThePoolHasNotActivated()
    {
        UnitData unit = MakeUnit("Warriors");
        var progress = new FakeProgress(round: 1, unactivated: new IUnit[] { unit });

        Assert.That(UnitActivation.HasActivated(progress, unit), Is.False);
    }

    [Test]
    public void AUnitMissingFromThePoolHasActivated()
    {
        UnitData unit = MakeUnit("Warriors");
        var progress = new FakeProgress(round: 1, unactivated: Array.Empty<IUnit>());

        Assert.That(UnitActivation.HasActivated(progress, unit), Is.True);
    }

    // The bug: same pool state as the test above, opposite meaning.
    [Test]
    public void AReserveUnitHasNotActivated_EvenThoughItIsMissingFromThePool()
    {
        UnitData unit = MakeUnit("Ambushers");
        ReserveRules.PlaceInReserve(unit);
        var progress = new FakeProgress(round: 1, unactivated: Array.Empty<IUnit>());

        Assert.That(UnitActivation.IsInReserve(unit), Is.True);
        Assert.That(UnitActivation.HasActivated(progress, unit), Is.False,
            "a unit that has not arrived cannot have spent its turn");
    }

    [Test]
    public void ArrivingFromReserveRestoresTheOrdinaryReading()
    {
        UnitData unit = MakeUnit("Ambushers");
        ReserveRules.PlaceInReserve(unit);
        ReserveRules.ClearReserve(unit);
        var progress = new FakeProgress(round: 1, unactivated: Array.Empty<IUnit>());

        Assert.That(UnitActivation.IsInReserve(unit), Is.False);
        Assert.That(UnitActivation.HasActivated(progress, unit), Is.True,
            "once it is on the table, absence from the pool means what it always meant");
    }

    [Test]
    public void NothingIsActivatedBeforeTheMainPhase()
    {
        UnitData unit = MakeUnit("Warriors");
        var progress = new FakeProgress(round: null, unactivated: Array.Empty<IUnit>());

        Assert.That(UnitActivation.HasActivated(progress, unit), Is.False);
    }

    [Test]
    public void TheUnitTakingItsTurnIsNotYetActivated()
    {
        UnitData unit = MakeUnit("Warriors");
        var progress = new FakeProgress(round: 1, unactivated: Array.Empty<IUnit>(), activating: unit);

        Assert.That(UnitActivation.HasActivated(progress, unit), Is.False);
    }

    private sealed class FakeProgress : IGameProgress
    {
        public FakeProgress(int? round, IReadOnlyList<IUnit> unactivated, IUnit? activating = null)
        {
            RoundCount = round;
            UnactivatedUnits = unactivated;
            ActivatingUnit = activating;
        }

        public int? RoundCount { get; }
        public int TotalRounds => 4;
        public IUnit? ActivatingUnit { get; }
        public IReadOnlyList<PlayerObjectiveScore> Scores => Array.Empty<PlayerObjectiveScore>();
        public IReadOnlyList<IUnit> UnactivatedUnits { get; }
    }
}
