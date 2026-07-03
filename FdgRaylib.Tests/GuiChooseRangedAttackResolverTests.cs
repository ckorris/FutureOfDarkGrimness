using System.Collections.Generic;
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
    [Test]
    public void NearestModel_SkipsDeadAndUnplacedCandidates()
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

        ModelData? nearest = GuiChooseRangedAttackResolver.NearestModel(shooter.GetValue(),
            new List<DataBinding<ModelData>> { dead, unplaced, alive });

        Assert.That(nearest, Is.SameAs(alive.GetValue()),
            "the line must aim at the nearest LIVING, placed model - never a corpse or the table origin");
    }

    [Test]
    public void NearestModel_AllCandidatesDead_ReturnsNull()
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
            initialPosition: new Position(3, 0), gameDataStore: store);
        var binding = store.GetDataBinding<ModelData>(store.Create(model));
        model.DealWounds(model.TotalWounds - model.WoundsDealt);

        var from = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
            initialPosition: new Position(1, 0), gameDataStore: store);
        store.Create(from);

        Assert.That(GuiChooseRangedAttackResolver.NearestModel(from,
            new List<DataBinding<ModelData>> { binding }), Is.Null);
    }
}
