using System;
using System.Collections.Generic;
using System.Linq;
using FDG;
using FDG.Data;
using FDG.Players;
using FDG.StageResolution.Requests;
using FdgRaylib.Rendering.Resolvers;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #286 — the Assign Wounds dialog highlighted the table model when you hovered its dialog ROW, but the
// reverse direction did nothing: hovering the actual figure on the canvas neither ringed it nor pointed
// at the matching row. GetHoverLabel is the seam the table hover arrives through (TableTooltipOverlay
// calls it once per frame, before the resolver's own Draw), so it is what has to record the hover.
//
// The ring and the row highlight themselves are ImGui draw calls, hand-verified; these pin the plumbing.
[TestFixture]
public class GuiAssignWoundsResolverTests
{
    [Test]
    public void HoveringAModelOnTheTable_RecordsItAsTheCanvasHover()
    {
        (GuiAssignWoundsResolver resolver, DataBinding<UnitData> unit) = PendingAssignment(modelCount: 3);
        ModelData second = unit.GetValue().ModelBindings[1].GetValue();

        string? label = resolver.GetHoverLabel(unit.GetValue(), second);

        Assert.That(resolver.CanvasHoveredModel, Is.SameAs(second),
            "hovering the figure must emphasise it - the ring and its dialog row both read this");
        Assert.That(label, Is.Not.Null.And.Contain("Weapons:"),
            "the hover still contributes its tooltip text");
    }

    [Test]
    public void HoveringAModelOfAnotherUnit_RecordsNothing()
    {
        (GuiAssignWoundsResolver resolver, DataBinding<UnitData> unit) = PendingAssignment(modelCount: 2);
        (_, DataBinding<UnitData> bystander) = PendingAssignment(modelCount: 1);
        ModelData outsider = bystander.GetValue().ModelBindings[0].GetValue();

        string? label = resolver.GetHoverLabel(bystander.GetValue(), outsider);

        Assert.That(label, Is.Null, "a model outside the unit taking wounds is not a wound target");
        Assert.That(resolver.CanvasHoveredModel, Is.Null,
            "and it must not light up a row in a dialog it has nothing to do with");
        Assert.That(unit, Is.Not.Null);
    }

    // Each hover replaces the last, so dragging the cursor across a unit emphasises exactly one model.
    [Test]
    public void TheLatestHoverWins()
    {
        (GuiAssignWoundsResolver resolver, DataBinding<UnitData> unit) = PendingAssignment(modelCount: 3);
        UnitData unitData = unit.GetValue();

        resolver.GetHoverLabel(unitData, unitData.ModelBindings[0].GetValue());
        resolver.GetHoverLabel(unitData, unitData.ModelBindings[2].GetValue());

        Assert.That(resolver.CanvasHoveredModel, Is.SameAs(unitData.ModelBindings[2].GetValue()));
    }

    // With no request in flight there is nothing to highlight - a stale hover must not survive the
    // dialog closing (the resolver is long-lived and reused for every wound assignment).
    [Test]
    public void WithNoPendingRequest_NoHoverIsRecorded()
    {
        var resolver = new GuiAssignWoundsResolver();
        DataBinding<UnitData> unit = MakeUnit(GameDataStore.GameDataStoreBuilder.GetDefault(), 2);

        Assert.That(resolver.GetHoverLabel(unit.GetValue(), unit.GetValue().ModelBindings[0].GetValue()),
            Is.Null);
        Assert.That(resolver.CanvasHoveredModel, Is.Null);
    }

    private static (GuiAssignWoundsResolver, DataBinding<UnitData>) PendingAssignment(int modelCount)
    {
        GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
        DataBinding<UnitData> unit = MakeUnit(store, modelCount);

        var resolver = new GuiAssignWoundsResolver();
        // Fire-and-forget: the task completes only when the player finishes assigning, which is exactly
        // the state under test (a dialog waiting for input).
        _ = resolver.Resolve(new AssignWoundsRequest(unit.GetValue().PlayerID, "Assign Wounds", unit,
            totalWoundsToAssign: 1f));
        return (resolver, unit);
    }

    private static DataBinding<UnitData> MakeUnit(IReadWriteableGameDataStore store, int modelCount)
    {
        var modelBindings = new List<DataBinding<ModelData>>();
        for (int i = 0; i < modelCount; i++)
        {
            var model = new ModelData(baseRadiusInches: 0.5f,
                weapons: new List<Weapon> { new Weapon("Rifle", 24f, 2, 0) },
                initialPosition: new Position(10f + i, 10f), gameDataStore: store);
            modelBindings.Add(store.GetDataBinding<ModelData>(store.Create(model)));
        }

        var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Warriors", quality: 4, defense: 4,
            modelBindings: modelBindings);
        return store.GetDataBinding<UnitData>(store.Create(unit));
    }
}
