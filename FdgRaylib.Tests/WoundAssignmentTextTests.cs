using System;
using System.Collections.Generic;
using FDG;
using FDG.Data;
using FDG.Players;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #401 - the one wording both wound-assignment fronts (ImGui panel, CLI prompt) use once the queue
// carries Deadly clumps. A plain pool keeps the old line; a clump queue says what the next click does,
// because "x / y assigned" can never reach y when a clump's excess is lost at its model.
[TestFixture]
public class WoundAssignmentTextTests
{
    [Test]
    public void PlainPool_KeepsTheAssignedOverTotalLine()
    {
        var results = new AssignWoundsResults(MakeUnit(3, 3), totalWoundsToAssign: 4f);

        Assert.That(WoundAssignmentText.Progress(results), Is.EqualTo("0 / 4 wounds assigned"));
        Assert.That(WoundAssignmentText.ClickHint(results), Is.EqualTo("Click to assign wounds (fills this model)"));
        Assert.That(WoundAssignmentText.Explanation(results), Is.Empty);
    }

    [Test]
    public void ClumpQueue_ReportsLandedLostAndWhatTheNextClickPlaces()
    {
        DataBinding<UnitData> unit = MakeUnit(3, 3);
        unit.GetValue().ModelBindings[2].GetValue().DealWounds(2); // the pre-assignment eats clump 1 here
        var results = new AssignWoundsResults(unit, new[] { WoundPacket.Clump(3f), WoundPacket.Clump(3f) });

        Assert.That(WoundAssignmentText.Progress(results),
            Is.EqualTo("1 landed, 2 lost (Deadly: no carry-over)\nNext: clump 2 of 2 - 3 wounds to ONE model"));
        Assert.That(WoundAssignmentText.ClickHint(results),
            Is.EqualTo("Click to land this clump here (3 wounds - what does not fit is lost)"));

        results.AutoFill();
        Assert.That(WoundAssignmentText.Next(results), Is.EqualTo("All wounds placed."));
    }

    [Test]
    public void MixedQueue_NamesThePlainTail()
    {
        var results = new AssignWoundsResults(MakeUnit(2, 3),
            new[] { WoundPacket.Clump(3f), WoundPacket.Unconfined(2f) });
        results.TryAddWounds(results.PendingWounds[0].Model);

        Assert.That(WoundAssignmentText.Next(results), Is.EqualTo("Next: 2 wounds, spread as needed (2 of 2)"));
    }

    // Chris, second GUI pass: a Deadly volley into a unit WITHOUT Tough showed the clump machinery for
    // nothing - every clump kills exactly one 1-wound model, so the choice is the plain one. The dialog
    // falls back to the plain wording, with the total being what will land (2), not the carry (6).
    [Test]
    public void ClumpsIntoOneWoundModels_LookLikeAPlainVolley()
    {
        var results = new AssignWoundsResults(MakeUnit(5, 1), new[] { WoundPacket.Clump(3f), WoundPacket.Clump(3f) });

        Assert.That(WoundAssignmentText.ClumpModeMatters(results), Is.False);
        Assert.That(WoundAssignmentText.Progress(results), Is.EqualTo("0 / 2 wounds assigned"));
        Assert.That(WoundAssignmentText.Explanation(results), Is.Empty);
        Assert.That(WoundAssignmentText.ClickHint(results), Is.EqualTo("Click to assign wounds (fills this model)"));
        Assert.That(WoundAssignmentText.ModelEffect(results, results.PendingWounds[0]), Is.Empty);

        results.TryAddWounds(results.PendingWounds[0].Model);
        Assert.That(WoundAssignmentText.Progress(results), Is.EqualTo("1 / 2 wounds assigned"),
            "the total holds as clumps are placed - one kill each");
    }

    // ...but a Tough model anywhere in the unit (a joined hero among 1-wound grunts) keeps it on: the
    // clumps matter for that model.
    [Test]
    public void AToughModelInTheUnit_KeepsClumpMode()
    {
        DataBinding<UnitData> unit = MakeUnit(3, 1);
        unit.GetValue().ModelBindings[2].GetValue().SetMaxWounds(6);
        var results = new AssignWoundsResults(unit, new[] { WoundPacket.Clump(3f) });

        Assert.That(WoundAssignmentText.ClumpModeMatters(results), Is.True);
        Assert.That(WoundAssignmentText.Explanation(results), Does.StartWith("Deadly(3):"));
    }

    [Test]
    public void WoundNounAgrees()
    {
        Assert.That(WoundAssignmentText.Wounds(1f), Is.EqualTo("1 wound"));
        Assert.That(WoundAssignmentText.Wounds(3f), Is.EqualTo("3 wounds"));
        Assert.That(WoundAssignmentText.Wounds(2f / 3f), Is.EqualTo("0.67 wounds"));
    }

    // Chris: "some extra text explaining why we're in this clump mode". Names Deadly's X; brings up
    // Regeneration only when a clump actually shrank.
    [Test]
    public void Explanation_NamesDeadlyAndMentionsRegenerationOnlyWhenItBit()
    {
        var untouched = new AssignWoundsResults(MakeUnit(2, 3), new[] { WoundPacket.Clump(3f) });
        Assert.That(WoundAssignmentText.Explanation(untouched), Is.EqualTo(
            "Deadly(3): each failed save is a clump of 3 wounds that must all land on ONE model. " +
            "What that model cannot absorb is lost - it never carries to the next."));

        var shrunk = new AssignWoundsResults(MakeUnit(2, 3), new[] { WoundPacket.Clump(3f).WithWounds(1f) });
        Assert.That(WoundAssignmentText.Explanation(shrunk), Does.EndWith(
            "Regeneration was rolled per clump: a smaller clump is one that ignored some."));
    }

    // Chris: "show all the clumps that we will have to assign at the top in a list". One chip per
    // packet: placed ones say where they went and what was lost, the next says what it carries,
    // pending ones just their size.
    [Test]
    public void ChipLabels_PlacedNextAndPending()
    {
        DataBinding<UnitData> unit = MakeUnit(3, 3);
        unit.GetValue().ModelBindings[2].GetValue().DealWounds(2);
        var results = new AssignWoundsResults(unit,
            new[] { WoundPacket.Clump(3f), WoundPacket.Clump(3f).WithWounds(1f), WoundPacket.Clump(3f), WoundPacket.Clump(3f).WithWounds(2f) });
        int RowOf(PacketCommit commit) => results.PendingWounds.FindIndex(e => e.Model == commit.Model) + 1;

        Assert.That(WoundAssignmentText.ChipLabel(results, 0, RowOf), Is.EqualTo("1: 1 -> M3, 2 lost"));
        Assert.That(WoundAssignmentText.ChipLabel(results, 1, RowOf), Is.EqualTo("2: 1 wound"));
        Assert.That(WoundAssignmentText.ChipLabel(results, 2, RowOf), Is.EqualTo("3: 3"));
        Assert.That(WoundAssignmentText.ChipLabel(results, 3, RowOf), Is.EqualTo("4: 2"));

        Assert.That(WoundAssignmentText.ChipTooltip(results, 0, RowOf),
            Is.EqualTo("Clump 1: 3 wounds to land\n-> Model 3: 1 wound landed, 2 lost"));
        Assert.That(WoundAssignmentText.ChipTooltip(results, 1, RowOf),
            Is.EqualTo("Clump 2: 3 rolled, 2 ignored, 1 wound to land\nNext to place - click a model."));
    }

    // The per-model preview of the next clump - what makes the pick easy.
    [Test]
    public void ModelEffect_PreviewsTheNextClumpOnEachLegalModel()
    {
        // Two fresh Tough(3); clump 1 shrank to 1 wound and is landed on model 1 by hand, leaving it
        // mid-fill with 2 left. Clump 2 (3 wounds) is next: #024 forces it onto model 1, where 2 fit.
        var results = new AssignWoundsResults(MakeUnit(2, 3),
            new[] { WoundPacket.Clump(3f).WithWounds(1f), WoundPacket.Clump(3f) });
        results.TryAddWounds(results.PendingWounds[0].Model);

        Assert.That(WoundAssignmentText.ModelEffect(results, results.PendingWounds[0]), Is.EqualTo("takes 2, 1 lost - dies"));
        Assert.That(WoundAssignmentText.ModelEffect(results, results.PendingWounds[1]), Is.Empty,
            "a fresh model is not a legal target while another is mid-fill (#024)");

        var fresh = new AssignWoundsResults(MakeUnit(2, 3), new[] { WoundPacket.Clump(3f).WithWounds(1f) });
        Assert.That(WoundAssignmentText.ModelEffect(fresh, fresh.PendingWounds[0]), Is.EqualTo("takes 1 -> 2/3 left"));
    }

    private static DataBinding<UnitData> MakeUnit(int modelCount, int woundsPerModel)
    {
        GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var modelBindings = new List<DataBinding<ModelData>>();
        for (int i = 0; i < modelCount; i++)
        {
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                initialPosition: new Position(10f + i, 10f), gameDataStore: store);
            model.SetMaxWounds(woundsPerModel);
            modelBindings.Add(store.GetDataBinding<ModelData>(store.Create(model)));
        }
        var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Brutes", quality: 4, defense: 4,
            modelBindings: modelBindings);
        return store.GetDataBinding<UnitData>(store.Create(unit));
    }
}
