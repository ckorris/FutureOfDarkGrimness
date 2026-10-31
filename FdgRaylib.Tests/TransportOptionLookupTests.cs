using FDG;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.StageResolution.Requests;
using FdgRaylib.Rendering.Resolvers;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #315: the pure lookups behind the activation picker's two-way transport hover — an embarked unit's
// row rings its transport, a canvas-hovered transport emphasises its occupants' rows. Both search the
// request's OWN option lists (the transport is always one of the acting player's units, so it is
// always listed); no table state involved.
[TestFixture]
public class TransportOptionLookupTests
{
    private GameDataStore _store = null!;
    private PlayerID _player;

    [SetUp]
    public void SetUp()
    {
        _store = GameDataStore.GameDataStoreBuilder.GetDefault();
        _player = new PlayerID(System.Guid.NewGuid());
    }

    [Test]
    public void FindTransportOf_EmbarkedUnit_FindsItsOwnRide()
    {
        var rhinoA = MakeUnit("Rhino");
        var rhinoB = MakeUnit("Rhino");   // same-named transports — the report's worst case
        var squadA = MakeUnit("Warriors");
        var squadB = MakeUnit("Warriors");
        TransportUtilities.Embark(squadA.GetValue(), rhinoA.GetValue());
        TransportUtilities.Embark(squadB.GetValue(), rhinoB.GetValue());
        var request = MakeRequest(valid: new[] { rhinoA, rhinoB, squadA, squadB });

        Assert.That(TransportOptionLookup.FindTransportOf(request, squadA.GetValue()),
            Is.SameAs(rhinoA.GetValue()),
            "each twin must resolve to its OWN transport, not the first one by name.");
        Assert.That(TransportOptionLookup.FindTransportOf(request, squadB.GetValue()),
            Is.SameAs(rhinoB.GetValue()));
    }

    [Test]
    public void FindTransportOf_OnTableUnit_ReturnsNull()
    {
        var rhino = MakeUnit("Rhino");
        var walker = MakeUnit("Warriors");
        var request = MakeRequest(valid: new[] { rhino, walker });

        Assert.That(TransportOptionLookup.FindTransportOf(request, walker.GetValue()), Is.Null);
    }

    [Test]
    public void FindTransportOf_TransportOnlyInInvalidOptions_IsStillFound()
    {
        // An already-activated transport is greyed out but still listed — the link must survive that.
        var rhino = MakeUnit("Rhino");
        var squad = MakeUnit("Warriors");
        TransportUtilities.Embark(squad.GetValue(), rhino.GetValue());
        var request = MakeRequest(valid: new[] { squad }, invalid: new[] { rhino });

        Assert.That(TransportOptionLookup.FindTransportOf(request, squad.GetValue()),
            Is.SameAs(rhino.GetValue()));
    }

    [Test]
    public void CargoOptionRefs_Transport_ListsValidAndInvalidOccupants()
    {
        var rhino = MakeUnit("Rhino");
        var aboardActive = MakeUnit("Warriors");
        var aboardSpent = MakeUnit("Gunners");
        var bystander = MakeUnit("Walkers");
        TransportUtilities.Embark(aboardActive.GetValue(), rhino.GetValue());
        TransportUtilities.Embark(aboardSpent.GetValue(), rhino.GetValue());
        var request = MakeRequest(valid: new[] { rhino, aboardActive, bystander },
            invalid: new[] { aboardSpent });

        var refs = TransportOptionLookup.CargoOptionRefs(request, rhino.GetValue());

        Assert.That(refs, Is.EquivalentTo(new[] { aboardActive.Reference, aboardSpent.Reference }),
            "occupants only — valid and invalid rows alike, never the bystander or the transport itself.");
    }

    [Test]
    public void CargoOptionRefs_NonTransport_IsEmpty()
    {
        var rhino = MakeUnit("Rhino");
        var squad = MakeUnit("Warriors");
        TransportUtilities.Embark(squad.GetValue(), rhino.GetValue());
        var request = MakeRequest(valid: new[] { rhino, squad });

        Assert.That(TransportOptionLookup.CargoOptionRefs(request, squad.GetValue()), Is.Empty);
    }

    private SelectionRequest<UnitData> MakeRequest(
        IReadOnlyList<DataBinding<UnitData>> valid,
        IReadOnlyList<DataBinding<UnitData>>? invalid = null)
    {
        var validOptions = valid
            .Select(u => new SelectionRequest<UnitData>.ValidOption(u, u.GetValue().Name))
            .ToList();
        var invalidOptions = (invalid ?? Array.Empty<DataBinding<UnitData>>())
            .Select(u => new SelectionRequest<UnitData>.InvalidOption(u, u.GetValue().Name, "Already activated."))
            .ToList();
        return new SelectionRequest<UnitData>(_player, "Choose Unit to Activate",
            validOptions, invalidOptions, allowCancel: false);
    }

    private DataBinding<UnitData> MakeUnit(string name)
    {
        var model = new ModelData(0.5f, new List<Weapon>(), new Position(0f, 0f), _store);
        var modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));

        var unit = new UnitData(_player, name, quality: 4, defense: 4,
            modelBindings: new List<DataBinding<ModelData>> { modelBinding });
        return _store.GetDataBinding<UnitData>(_store.Create(unit));
    }
}
