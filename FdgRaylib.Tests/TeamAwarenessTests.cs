using System;
using System.Collections.Generic;
using FDG;
using FDG.Data;
using FdgRaylib.Placement;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #312 — one team-aware definition of "enemy" for the whole app. Every overlay that paints something
// hostile onto a unit asks this: the movement resolver's can-hit / can-charge indicators and fire lines,
// the tactical overlay's threat rings, and the enemy-spacing gates during placement and consolidation.
// A unit belonging to ANOTHER PLAYER ON YOUR TEAM must never be treated as a target - the preview would
// then promise a shot the engine refuses to offer.
[TestFixture]
public class TeamAwarenessTests
{
    [Test]
    public void AnotherPlayerOnMyTeam_IsNotAnEnemy()
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var me = new PlayerID(Guid.NewGuid());
        var teammate = new PlayerID(Guid.NewGuid());
        var foe = new PlayerID(Guid.NewGuid());
        store.Create(new TeamData(0, new List<PlayerID> { me, teammate }));
        store.Create(new TeamData(1, new List<PlayerID> { foe }));
        var tableState = new TableState(store);

        Assert.Multiple(() =>
        {
            Assert.That(TeamAwareness.IsEnemyUnit(tableState, me, Unit(store, teammate)), Is.False,
                "an ally's unit gets no can-hit / can-charge indicator");
            Assert.That(TeamAwareness.IsEnemyUnit(tableState, me, Unit(store, me)), Is.False,
                "nor does my own");
            Assert.That(TeamAwareness.IsEnemyUnit(tableState, me, Unit(store, foe)), Is.True,
                "the other team is still hostile");
        });
    }

    [Test]
    public void NoTeamsRegistered_FallsBackToAPlainPlayerComparison()
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var me = new PlayerID(Guid.NewGuid());
        var other = new PlayerID(Guid.NewGuid());
        var tableState = new TableState(store);

        Assert.Multiple(() =>
        {
            Assert.That(TeamAwareness.IsEnemyUnit(tableState, me, Unit(store, me)), Is.False);
            Assert.That(TeamAwareness.IsEnemyUnit(tableState, me, Unit(store, other)), Is.True,
                "with no teams at all - every solo / 1v1 path - a different player is an enemy");
        });
    }

    private static IUnit Unit(GameDataStore store, PlayerID owner)
    {
        var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
            initialPosition: new Position(5, 5), gameDataStore: store);
        var modelBinding = store.GetDataBinding<ModelData>(store.Create(model));
        var unit = new UnitData(owner, "U", quality: 4, defense: 4,
            modelBindings: new List<DataBinding<ModelData>> { modelBinding });
        return store.GetDataBinding<UnitData>(store.Create(unit)).GetValue();
    }
}
