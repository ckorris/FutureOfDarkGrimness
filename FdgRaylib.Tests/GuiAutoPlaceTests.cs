using System;
using System.Collections.Generic;
using FDG;
using FDG.Data;
using FDG.SaveLoad;
using FDG.StageResolution.Requests;
using FdgRaylib.Rendering.Resolvers;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #458: a mandatory placement (spillout, Scout, Ambush, Reinforcements, spawn) has no Back, and Done needs
// every model placed by clicks that each pass the full rule set - so a zone with no legal spot left the
// player stuck forever. Auto-place runs the engine's own placer, which falls back to its least-bad spot,
// fills the whole unit in, and lets Done commit it. The Done/ImGui wiring is hand-verified; these pin
// that the stuck case gets a complete placement and that its rule break is reported, not hidden.
[TestFixture]
public class GuiAutoPlaceTests
{
    private static (GuiPlaceObjectsResolver<ModelData> resolver, PlaceObjectsRequest<ModelData> request)
        Pending(IBoundedZone zone, int models)
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var owner = new PlayerID(Guid.NewGuid());
        var bindings = new List<DataBinding<ModelData>>();
        for (int i = 0; i < models; i++)
            bindings.Add(store.GetDataBinding<ModelData>(store.Create(
                new ModelData(0.5f, new List<Weapon>(), new Position(0f, 0f), store))));

        var resolver = new GuiPlaceObjectsResolver<ModelData>(new TableState(store), new FormationModeState());
        var request = new PlaceObjectsRequest<ModelData>(owner, "Spillout", zone, bindings);
        _ = resolver.Resolve(request);
        return (resolver, request);
    }

    [Test]
    public void ZoneWithNoLegalSpot_AutoPlacePlacesEveryModel_AndSaysWhatItBreaks()
    {
        // Five 1" bases into a 2"x2" zone: no click can place them all, so the panel used to be a dead end.
        var (resolver, request) = Pending(new RectangularZone(20f, 22f, 20f, 22f), models: 5);

        resolver.AutoPlace(request);

        Assert.That(resolver.Placed, Has.Count.EqualTo(5), "every model placed, so Done is available");
        Assert.That(resolver.IsAutoPlaced, Is.True);
        Assert.That(resolver.FirstPlacementProblem(request), Is.Not.Null,
            "a least-bad spot is reported as such, not passed off as legal");
    }

    [Test]
    public void RoomyZone_AutoPlaceFindsAFullyLegalSpot()
    {
        var (resolver, request) = Pending(new RectangularZone(0f, 48f, 0f, 12f), models: 5);

        resolver.AutoPlace(request);

        Assert.That(resolver.Placed, Has.Count.EqualTo(5));
        Assert.That(resolver.FirstPlacementProblem(request), Is.Null);
    }
}
