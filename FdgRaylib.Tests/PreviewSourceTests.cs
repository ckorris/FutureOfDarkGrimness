using System;
using System.Collections.Generic;
using System.Linq;
using FDG;
using FDG.Data;
using FDG.SaveLoad;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FdgRaylib.Rendering.Previews;
using FdgRaylib.Rendering.Resolvers;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #280 — the resolvers that opted into networked previews. Draw-side ghost capture is ImGui
// (hand-verified); these pin the BuildPreviewState seams that hold the payloads together across
// the wire. Movement family: roster identity and ORDER (ghost entries reference the base roster by
// index - a mismatch composes ghosts onto the wrong models), the BaseVersion stamp both slots must
// share, band codes, quantization, and the no-request -> null contract the publisher relies on.
// Marker family (objective/terrain placement): the zone-tree -> wire-primitive flattening and the
// same null contract (their ghosts exist only as Draw snapshots).
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
        Assert.That(PreviewQuantize.Inches(10.223f), Is.EqualTo(10.22f).Within(0.0001f));
        Assert.That(PreviewQuantize.Inches(10.228f), Is.EqualTo(10.23f).Within(0.0001f));
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
    public void Objective_SharesNothingWithoutRequestOrBeforeDraw()
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var resolver = new GuiPlaceObjectiveResolver(new TableState(store));
        Assert.That(resolver.BuildPreviewState(), Is.Null, "no pending request");

        _ = resolver.Resolve(new PlaceObjectiveRequest(new PlayerID(Guid.NewGuid()), "Objective",
            markerIndex: 1, totalMarkers: 3, new RectangularZone(0f, 48f, 12f, 36f),
            minSeparationInches: 9f));
        Assert.That(resolver.BuildPreviewState(), Is.Null,
            "the ghost exists only as a Draw snapshot - nothing to share before the first frame");
    }

    [Test]
    public void Terrain_SharesNothingWithoutRequestOrBeforeDraw()
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var resolver = new GuiPlaceOneTerrainResolver(new TableState(store));
        Assert.That(resolver.BuildPreviewState(), Is.Null, "no pending request");

        var pool = new List<TerrainPieceEntry>
        {
            new TerrainPieceEntry
            {
                TerrainType = ETerrainType.Cover,
                Shape = new RectangularZone(0f, 6f, 0f, 4f),
            },
        };
        _ = resolver.Resolve(new PlaceOneTerrainRequest(new PlayerID(Guid.NewGuid()), "Terrain",
            piecesPlaced: 0, totalPieces: 4, pool, tableWidthInches: 48f, tableHeightInches: 48f));
        Assert.That(resolver.BuildPreviewState(), Is.Null,
            "template selection has no canvas ghost - nothing to share before a Draw captures one");
    }

    [Test]
    public void MarkerFootprints_FlattenRotatedCompositeToWirePrimitives()
    {
        // A composite of a rect and a circle, rotated 90 degrees the way the terrain resolver does
        // it (around the template's own AABB center) - the leaf walk must yield the circle
        // translated (rotation-invariant) and the rect as a rotated quad.
        var inner = new CompositeZone(new List<IZone>
        {
            new RectangularZone(0f, 6f, 0f, 4f),
            new CircularZone(new Float2(8f, 2f), 1.5f),
        });
        Float2 pivot = inner.GetAABBCenter();
        IZone shape = TerrainTemplateUtilities.Rotate(inner, 90f);

        (IReadOnlyList<MarkerCircle> circles, IReadOnlyList<MarkerQuad> quads) =
            MarkerFootprints.Flatten(shape);

        Assert.That(circles, Has.Count.EqualTo(1));
        Assert.That(quads, Has.Count.EqualTo(1));
        Assert.That(quads[0].Corners, Has.Count.EqualTo(4), "wire quads are always four corners");
        Float2 expectedCircle = ZoneExtensions.RotateAround(new Float2(8f, 2f), pivot, 90f);
        Assert.That(circles[0].X, Is.EqualTo(PreviewQuantize.Inches(expectedCircle.X)).Within(0.0001f));
        Assert.That(circles[0].Z, Is.EqualTo(PreviewQuantize.Inches(expectedCircle.Y)).Within(0.0001f));
        Assert.That(circles[0].Radius, Is.EqualTo(1.5f).Within(0.0001f));

        Float2 expectedCorner = ZoneExtensions.RotateAround(new Float2(0f, 0f), pivot, 90f);
        Assert.That(quads[0].Corners[0].X, Is.EqualTo(PreviewQuantize.Inches(expectedCorner.X)).Within(0.0001f));
        Assert.That(quads[0].Corners[0].Z, Is.EqualTo(PreviewQuantize.Inches(expectedCorner.Y)).Within(0.0001f));
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
