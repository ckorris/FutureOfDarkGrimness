using System.Collections.Generic;
using System.Linq;
using FDG;
using FDG.Data;
using FdgRaylib.Rendering.Resolvers;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #158 — the shooting-target chooser's canvas overlay must never point at corpses: a just-killed model
// is often the nearest candidate (you shot it last volley), and drawing the shooter line to its death
// position read as "shooting at a dead model". The ring/line loops are ImGui (hand-verified); this pins
// the shared nearest-candidate seam.
[TestFixture]
public class GuiChooseRangedAttackResolverTests
{
    // #312: the fire line must land on a model the shooter can SEE. Aiming at the nearest model outright
    // drew lines straight through blocking terrain while the volley resolved against a visible model.
    [Test]
    public void NearestVisibleModel_SkipsANearerModelBehindABlocker()
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        // Wall x 8..12, z 3..7 — the (1,12) -> (21,1) lane cuts it; (1,12) -> (25,12) is clear.
        var wall = new TerrainData(ETerrainType.Blocking, new RectangularZone(8, 12, 3, 7));
        store.Create(wall);

        DataBinding<ModelData> Make(Position pos)
        {
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                initialPosition: pos, gameDataStore: store);
            return store.GetDataBinding<ModelData>(store.Create(model));
        }

        var shooter = Make(new Position(1, 12));
        var blocked = Make(new Position(21, 1));   // nearer, but behind the wall
        var visible = Make(new Position(25, 12));  // farther, clear lane
        var blockers = new List<ITerrain> { wall };

        Assert.That(GuiChooseRangedAttackResolver.NearestVisibleModel(shooter.GetValue(),
                new List<DataBinding<ModelData>> { blocked, visible }, blockers),
            Is.SameAs(visible.GetValue()),
            "the line must point at the defender the shot could actually reach");

        Assert.That(GuiChooseRangedAttackResolver.NearestVisibleModel(shooter.GetValue(),
                new List<DataBinding<ModelData>> { blocked, visible }, blockers: null),
            Is.SameAs(blocked.GetValue()),
            "a weapon that ignores line of sight (Indirect/Takedown) still aims at the nearest model");
    }

    [Test]
    public void NearestVisibleModel_SkipsDeadAndUnplacedCandidates()
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();

        DataBinding<ModelData> Make(Position pos)
        {
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                initialPosition: pos, gameDataStore: store);
            return store.GetDataBinding<ModelData>(store.Create(model));
        }

        var shooter   = Make(new Position(1, 0));
        var dead      = Make(new Position(2, 0));   // nearest, but a corpse
        var unplaced  = Make(new Position(0, 0));   // never placed — sits at the origin
        var alive     = Make(new Position(10, 0));

        var deadModel = dead.GetValue();
        deadModel.DealWounds(deadModel.TotalWounds - deadModel.WoundsDealt);

        ModelData? nearest = GuiChooseRangedAttackResolver.NearestVisibleModel(shooter.GetValue(),
            new List<DataBinding<ModelData>> { dead, unplaced, alive }, blockers: null);

        Assert.That(nearest, Is.SameAs(alive.GetValue()),
            "the line must aim at the nearest LIVING, placed model - never a corpse or the table origin");
    }

    // ── #237: single-option pre-select seams ─────────────────────────────────
    // The pre-select must only ever fire when the choice is forced: exactly one fireable target for
    // the weapon (SoleFireableTargetIndex), and the first weapon that can actually shoot
    // (FirstFireableWeaponIndex). Guessing between real alternatives would silently aim the volley.

    private static FDG.StageResolution.Requests.ChooseRangedAttackRequest.WeaponTargetStats
        MakeTarget(GameDataStore store, bool inRange, string? unselectableReason = null)
    {
        var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
            initialPosition: new Position(5, 5), gameDataStore: store);
        var modelBinding = store.GetDataBinding<ModelData>(store.Create(model));
        var unit = new UnitData(new PlayerID(System.Guid.NewGuid()), "Target", quality: 4, defense: 4,
            modelBindings: new List<DataBinding<ModelData>> { modelBinding });
        var unitBinding = store.GetDataBinding<UnitData>(store.Create(unit));

        var canShoot = new HashSet<DataBinding<ModelData>>();
        if (inRange) canShoot.Add(modelBinding);
        return new FDG.StageResolution.Requests.ChooseRangedAttackRequest.WeaponTargetStats(
            unitBinding, canShoot, new HashSet<DataBinding<ModelData>>(),
            UnselectableReason: unselectableReason);
    }

    private static FDG.StageResolution.Requests.ChooseRangedAttackRequest.WeaponOption MakeWeapon(
        params FDG.StageResolution.Requests.ChooseRangedAttackRequest.WeaponTargetStats[] targets)
        => new(new Weapon("Rifle", rangeInches: 24f, attacks: 1, armorPenetration: 0), targets.ToList());

    [Test]
    public void SoleFireableTargetIndex_OneFireableAmongBlocked_ReturnsItsIndex()
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var wo = MakeWeapon(
            MakeTarget(store, inRange: false),                          // out of range
            MakeTarget(store, inRange: true, "Already targeted twice"), // rule-blocked
            MakeTarget(store, inRange: true));                          // the sole real option

        Assert.That(GuiChooseRangedAttackResolver.SoleFireableTargetIndex(wo), Is.EqualTo(2));
    }

    [Test]
    public void SoleFireableTargetIndex_TwoFireable_ReturnsMinusOne()
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var wo = MakeWeapon(MakeTarget(store, inRange: true), MakeTarget(store, inRange: true));

        Assert.That(GuiChooseRangedAttackResolver.SoleFireableTargetIndex(wo), Is.EqualTo(-1),
            "with a real choice to make, nothing may be pre-selected");
    }

    [Test]
    public void SoleFireableTargetIndex_NoneFireable_ReturnsMinusOne()
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var wo = MakeWeapon(MakeTarget(store, inRange: false), MakeTarget(store, inRange: true, "Blocked"));

        Assert.That(GuiChooseRangedAttackResolver.SoleFireableTargetIndex(wo), Is.EqualTo(-1));
    }

    [Test]
    public void FirstFireableWeaponIndex_SkipsWeaponsWithNoFireableTarget()
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var options = new List<FDG.StageResolution.Requests.ChooseRangedAttackRequest.WeaponOption>
        {
            MakeWeapon(MakeTarget(store, inRange: false)),
            MakeWeapon(MakeTarget(store, inRange: true)),
        };

        Assert.That(GuiChooseRangedAttackResolver.FirstFireableWeaponIndex(options), Is.EqualTo(1));
    }

    [Test]
    public void FirstFireableWeaponIndex_NoneFireable_FallsBackToZero()
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var options = new List<FDG.StageResolution.Requests.ChooseRangedAttackRequest.WeaponOption>
        {
            MakeWeapon(MakeTarget(store, inRange: false)),
            MakeWeapon(MakeTarget(store, inRange: false)),
        };

        Assert.That(GuiChooseRangedAttackResolver.FirstFireableWeaponIndex(options), Is.EqualTo(0),
            "the panel should still show the first weapon's rows when nothing can fire");
    }

    [Test]
    public void FirstFireableWeaponIndex_NoWeapons_ReturnsMinusOne()
    {
        Assert.That(GuiChooseRangedAttackResolver.FirstFireableWeaponIndex(
            new List<FDG.StageResolution.Requests.ChooseRangedAttackRequest.WeaponOption>()), Is.EqualTo(-1));
    }

    // ── #308: the target carries across a shoot action's weapons ─────────────
    // A volley is normally aimed at one unit, so the next weapon starts on the last one's target while
    // that stays legal. Ranked ABOVE #237's sole-target rule: the previous target is evidence of the
    // player's intent, a sole target only the absence of alternatives.

    [Test]
    public void PreferredTargetIndex_PicksThePreviousTarget_WhenStillFireable()
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var first  = MakeTarget(store, inRange: true);
        var second = MakeTarget(store, inRange: true);
        var wo = MakeWeapon(first, second);

        Assert.That(GuiChooseRangedAttackResolver.PreferredTargetIndex(wo, second.TargetUnit),
            Is.EqualTo(1), "the unit the last weapon fired at starts selected.");
    }

    [Test]
    public void PreferredTargetIndex_IgnoresThePreviousTarget_WhenThisWeaponCannotFireAtIt()
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var outOfRange = MakeTarget(store, inRange: false);   // the last weapon's target, out of this one's reach
        var reachable  = MakeTarget(store, inRange: true);
        var wo = MakeWeapon(outOfRange, reachable);

        Assert.That(GuiChooseRangedAttackResolver.PreferredTargetIndex(wo, outOfRange.TargetUnit),
            Is.EqualTo(1), "an unreachable previous target falls through to the sole-fireable rule.");
    }

    [Test]
    public void PreferredTargetIndex_BlockedPreviousTarget_DoesNotOverrideTheGate()
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var blocked   = MakeTarget(store, inRange: true, "Must fire Deadly weapons first.");
        var selectable = MakeTarget(store, inRange: true);
        var wo = MakeWeapon(blocked, selectable);

        Assert.That(GuiChooseRangedAttackResolver.PreferredTargetIndex(wo, blocked.TargetUnit),
            Is.EqualTo(1), "the pre-select is a hint, never a permission - a gated row stays unpicked.");
    }

    [Test]
    public void PreferredTargetIndex_NoPreviousTarget_FallsBackToTheSoleTargetRule()
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var wo = MakeWeapon(MakeTarget(store, inRange: false), MakeTarget(store, inRange: true));

        Assert.That(GuiChooseRangedAttackResolver.PreferredTargetIndex(wo, previousTarget: null),
            Is.EqualTo(1), "the first weapon of a shoot action has no previous target - #237 still applies.");
    }

    [Test]
    public void PreferredTargetIndex_NoPreviousTargetAndSeveralOptions_SelectsNothing()
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var wo = MakeWeapon(MakeTarget(store, inRange: true), MakeTarget(store, inRange: true));

        Assert.That(GuiChooseRangedAttackResolver.PreferredTargetIndex(wo, previousTarget: null),
            Is.EqualTo(-1), "with two real alternatives and no evidence, guessing would aim the volley.");
    }

    [Test]
    public void NearestVisibleModel_AllCandidatesDead_ReturnsNull()
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
            initialPosition: new Position(3, 0), gameDataStore: store);
        var binding = store.GetDataBinding<ModelData>(store.Create(model));
        model.DealWounds(model.TotalWounds - model.WoundsDealt);

        var from = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
            initialPosition: new Position(1, 0), gameDataStore: store);
        store.Create(from);

        Assert.That(GuiChooseRangedAttackResolver.NearestVisibleModel(from,
            new List<DataBinding<ModelData>> { binding }, blockers: null), Is.Null);
    }
}
