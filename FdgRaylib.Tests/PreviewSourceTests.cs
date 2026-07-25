using System;
using System.Collections.Generic;
using System.Linq;
using FDG;
using FDG.Data;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FdgRaylib.Rendering.Previews;
using FdgRaylib.Rendering.Resolvers;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #277 slice 3 — the movement-family resolvers that opted into networked previews. Draw-side ghost
// capture is ImGui (hand-verified); these pin the BuildPreviewState seams that hold the payloads
// together across the wire: roster identity and ORDER (ghost entries reference the base roster by
// index - a mismatch composes ghosts onto the wrong models), the BaseVersion stamp both slots must
// share, band codes, quantization, and the no-request -> null contract the publisher relies on.
[TestFixture]
public class PreviewSourceTests
{
    private static DataBinding<ModelData> MakeModel(GameDataStore store, Position pos)
    {
        var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
            initialPosition: pos, gameDataStore: store);
        return store.GetDataBinding<ModelData>(store.Create(model));
    }

    private static DataBinding<UnitData> MakeUnit(GameDataStore store, PlayerID owner,
        params DataBinding<ModelData>[] models)
    {
        var unit = new UnitData(owner, "Unit", quality: 4, defense: 4,
            modelBindings: models.ToList());
        return store.GetDataBinding<UnitData>(store.Create(unit));
    }

    private static (GhostPathBase basePayload, GhostPathGhosts ghostPayload) Slots(PreviewState state)
    {
        Assert.That(state.Slots.Select(s => s.Slot),
            Is.EqualTo(new[] { GhostPathSlots.Base, GhostPathSlots.Ghost }),
            "base must precede ghost so receivers cache the roster before ghosts reference it");
        return ((GhostPathBase)state.Slots[0].Payload, (GhostPathGhosts)state.Slots[1].Payload);
    }

    [Test]
    public void Quantize_RoundsToHundredthsOfAnInch()
    {
        Assert.That(GhostPathQuantize.Inches(10.223f), Is.EqualTo(10.22f).Within(0.0001f));
        Assert.That(GhostPathQuantize.Inches(10.228f), Is.EqualTo(10.23f).Within(0.0001f));
    }

    [Test]
    public void Consolidation_NoPendingRequest_SharesNothing()
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var resolver = new GuiConsolidationMoveResolver(new TableState(store), new FormationModeState());
        Assert.That(resolver.BuildPreviewState(), Is.Null);
    }

    [Test]
    public void Consolidation_PublishesRosterWithNeutralBandAndPairedVersions()
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var owner = new PlayerID(Guid.NewGuid());
        var m1 = MakeModel(store, new Position(10.2f, 8.4f));
        var m2 = MakeModel(store, new Position(11.4f, 8.4f));
        var unitBinding = MakeUnit(store, owner, m1, m2);

        var resolver = new GuiConsolidationMoveResolver(new TableState(store), new FormationModeState());
        _ = resolver.Resolve(new ConsolidationMoveRequest(owner, "Consolidate", unitBinding,
            maxDistanceInches: 3f, EConsolidationReason.Wipeout));

        PreviewState? state = resolver.BuildPreviewState();
        Assert.That(state, Is.Not.Null);
        Assert.That(state!.SourcePlayerID, Is.EqualTo(owner));

        var (basePayload, ghostPayload) = Slots(state);
        Assert.That(basePayload.Models.Select(m => m.ModelId),
            Is.EquivalentTo(new[] { m1.GetValue().ID.ID, m2.GetValue().ID.ID }));
        Assert.That(basePayload.Models.All(m => m.Waypoints.Count == 0),
            "no committed steps before any click");
        Assert.That(basePayload.Models.All(m => m.Band == GhostPathBands.Neutral),
            "consolidation has no advance/rush/charge semantics");
        Assert.That(ghostPayload.BaseVersion, Is.EqualTo(basePayload.BaseVersion),
            "both slots must carry the version stamp of the same build pass");
        Assert.That(ghostPayload.Ghosts, Is.Empty,
            "no Draw has captured a ghost snapshot for this request yet");
    }

    [Test]
    public void AircraftAdvance_PublishesGhostsAlongHeadingAtCurrentDistance()
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var owner = new PlayerID(Guid.NewGuid());
        var m1 = MakeModel(store, new Position(10.2f, 8.4f));
        var m2 = MakeModel(store, new Position(12.7f, 8.4f));
        var unitBinding = MakeUnit(store, owner, m1, m2);

        var resolver = new GuiAircraftAdvanceResolver();
        _ = resolver.Resolve(new AircraftAdvanceRequest(owner, "Aircraft", unitBinding,
            new Float2(0f, 1f), minDistanceInches: 30f, maxDistanceInches: 36f));

        PreviewState? state = resolver.BuildPreviewState();
        Assert.That(state, Is.Not.Null);
        var (basePayload, ghostPayload) = Slots(state!);

        Assert.That(basePayload.Models.Select(m => m.ModelId),
            Is.EqualTo(new[] { m1.GetValue().ID.ID, m2.GetValue().ID.ID }));
        Assert.That(basePayload.Models.All(m => m.Waypoints.Count == 0),
            "the forced move has no waypoints - the presenter's anchor line IS the approach");
        Assert.That(ghostPayload.BaseVersion, Is.EqualTo(basePayload.BaseVersion));

        // Before any Draw, the previewed distance is the request minimum (30").
        Assert.That(ghostPayload.Ghosts.Select(g => g.ModelIndex), Is.EqualTo(new[] { 0, 1 }),
            "ghost entries pair with the roster by index");
        Assert.That(ghostPayload.Ghosts[0].X, Is.EqualTo(10.2f).Within(0.0001f));
        Assert.That(ghostPayload.Ghosts[0].Z, Is.EqualTo(38.4f).Within(0.0001f));
        Assert.That(ghostPayload.Ghosts[1].X, Is.EqualTo(12.7f).Within(0.0001f));
        Assert.That(ghostPayload.Ghosts[1].Z, Is.EqualTo(38.4f).Within(0.0001f));
        Assert.That(ghostPayload.Ghosts.All(g => g.Band == GhostPathBands.Advance));
    }

    [Test]
    public void PlaceObjects_PublishesRosterInRequestOrderWithNoGhostsBeforeDraw()
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var owner = new PlayerID(Guid.NewGuid());
        // Deployment: both models still at the (0,0) unplaced sentinel.
        var m1 = MakeModel(store, new Position(0f, 0f));
        var m2 = MakeModel(store, new Position(0f, 0f));

        var resolver = new GuiPlaceObjectsResolver<ModelData>(new TableState(store), new FormationModeState());
        _ = resolver.Resolve(new PlaceObjectsRequest<ModelData>(owner, "Deploy",
            new RectangularZone(0f, 48f, 0f, 12f), new List<DataBinding<ModelData>> { m1, m2 }));

        PreviewState? state = resolver.BuildPreviewState();
        Assert.That(state, Is.Not.Null);
        Assert.That(state!.SourcePlayerID, Is.EqualTo(owner));

        var (basePayload, ghostPayload) = Slots(state);
        Assert.That(basePayload.Models.Select(m => m.ModelId),
            Is.EqualTo(new[] { m1.GetValue().ID.ID, m2.GetValue().ID.ID }),
            "roster order must be ModelsToPlace order - _placed[i] pairs with it");
        Assert.That(basePayload.Models.All(m => m.Waypoints.Count == 0),
            "nothing placed yet -> no committed waypoints");
        Assert.That(basePayload.Models.All(m => m.Band == GhostPathBands.Neutral));
        Assert.That(ghostPayload.BaseVersion, Is.EqualTo(basePayload.BaseVersion));
        Assert.That(ghostPayload.Ghosts, Is.Empty);
    }
}
