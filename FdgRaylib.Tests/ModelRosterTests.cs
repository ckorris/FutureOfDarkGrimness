using FDG;
using FdgRaylib.Rendering.Resolvers;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #326 — single-model moves had no representation of the unit anywhere but the table: #295 moved "switch
// model" onto a click on the model itself, and a hover highlight that only appears once the cursor is
// already on a base is not an affordance a player finds on their own (one didn't). The movement panel now
// carries a roster of the unit's models with each one's distance travelled.
//
// The drawing is ImGui (hand-verified). What lives here is the part that fails SILENTLY: the vertical
// budget — a roster that eats the footer pushes Done off the bottom of the panel and the move becomes
// uncommittable — plus the wrap arithmetic behind the Up/Down / Tab cycle and the per-row numbers.
[TestFixture]
public class ModelRosterTests
{
    private const float Spacing = 8f;
    private const float LineH   = 16f;
    private const float FrameH  = 24f;

    // A panel with room to spare, so RosterHeight is deciding on row count rather than on space.
    private const float RoomyPanel = 2000f;

    [Test]
    public void FooterHeight_CountsTheWholeButtonStackAndTheHintBlock()
    {
        float footer = ModelRoster.FooterHeight(Spacing, LineH, FrameH, allowCancel: false);

        float buttons = ResolverPanelLayout.OptionRowHeight(LineH)          // Done
                      + ResolverPanelLayout.ActionRowHeight(LineH) * 2f;    // Skip/Auto row + Clear
        float controls = FrameH * 3f;                                       // mode button + two checkboxes
        float hints    = LineH * ModelRoster.HintLines;

        Assert.That(footer, Is.GreaterThan(buttons + controls + hints),
            "every row below the roster, plus the gaps between them, has to be paid for");
    }

    [Test]
    public void FooterHeight_PaysForBackOnlyWhenItIsOffered()
    {
        float withoutBack = ModelRoster.FooterHeight(Spacing, LineH, FrameH, allowCancel: false);
        float withBack    = ModelRoster.FooterHeight(Spacing, LineH, FrameH, allowCancel: true);

        Assert.That(withBack - withoutBack,
            Is.EqualTo(ResolverPanelLayout.ActionRowHeight(LineH) + Spacing).Within(0.001f),
            "a player-chosen move offers Back; a mandatory one has no back-destination and does not");
    }

    [Test]
    public void RosterHeight_ShowsEveryRowOfASmallUnitWithoutPadding()
    {
        float h = ModelRoster.RosterHeight(RoomyPanel, footerHeight: 100f, LineH, rowCount: 3);

        Assert.That(h, Is.EqualTo(ModelRoster.RowHeight(LineH) * 3f).Within(0.001f),
            "a three-model unit gets exactly three rows - no dead space under the last one");
    }

    [Test]
    public void RosterHeight_StopsGrowingAtTheVisibleRowCapSoBigUnitsScroll()
    {
        float atCap = ModelRoster.RosterHeight(RoomyPanel, 100f, LineH, ModelRoster.MaxVisibleRows);
        float over  = ModelRoster.RosterHeight(RoomyPanel, 100f, LineH, ModelRoster.MaxVisibleRows + 7);

        Assert.That(over, Is.EqualTo(atCap).Within(0.001f),
            "a 12-model Tough unit scrolls its roster rather than growing it");
    }

    [Test]
    public void RosterHeight_YieldsToTheFooterWhenThePanelIsShort()
    {
        float footer = ModelRoster.FooterHeight(Spacing, LineH, FrameH, allowCancel: true);
        float tight  = footer + ModelRoster.RowHeight(LineH) * 3f;

        float h = ModelRoster.RosterHeight(tight, footer, LineH, rowCount: ModelRoster.MaxVisibleRows);

        Assert.That(h, Is.EqualTo(ModelRoster.RowHeight(LineH) * 3f).Within(0.001f),
            "the footer is costed first (#288) - the roster takes the remainder, never the other way round");
    }

    [Test]
    public void RosterHeight_NeverCollapsesBelowTheMinimumEvenOnAnImpossiblePanel()
    {
        float h = ModelRoster.RosterHeight(availableHeight: 10f, footerHeight: 400f, LineH, rowCount: 8);

        Assert.That(h, Is.EqualTo(ModelRoster.RowHeight(LineH) * ModelRoster.MinVisibleRows).Within(0.001f),
            "a negative remainder must still produce a scrollable child, not a zero-height one");
    }

    [Test]
    public void Cycle_WalksForwardAndWrapsPastTheLastModel()
    {
        Assert.That(ModelRoster.Cycle(current: 0, count: 4, delta: 1), Is.EqualTo(1));
        Assert.That(ModelRoster.Cycle(current: 3, count: 4, delta: 1), Is.EqualTo(0));
    }

    [Test]
    public void Cycle_WalksBackwardAndWrapsPastTheFirstModel()
    {
        Assert.That(ModelRoster.Cycle(current: 2, count: 4, delta: -1), Is.EqualTo(1));
        Assert.That(ModelRoster.Cycle(current: 0, count: 4, delta: -1), Is.EqualTo(3),
            "Shift+Tab off the top lands on the last model, not on nothing");
    }

    [Test]
    public void Cycle_FromNoSelectionEntersTheListFromWhicheverEndYouCameFrom()
    {
        Assert.That(ModelRoster.Cycle(current: -1, count: 4, delta: 1), Is.EqualTo(0));
        Assert.That(ModelRoster.Cycle(current: -1, count: 4, delta: -1), Is.EqualTo(3));
    }

    [Test]
    public void Cycle_HandlesAnEmptyRosterAndAMultiStepJump()
    {
        Assert.That(ModelRoster.Cycle(current: 0, count: 0, delta: 1), Is.EqualTo(-1),
            "a wiped-out unit has nothing to select");
        Assert.That(ModelRoster.Cycle(current: 3, count: 4, delta: 3), Is.EqualTo(2),
            "key-repeat can deliver more than one step in a frame");
    }

    [Test]
    public void BuildRow_MarksAModelThatHasSpentItsAdvanceAsRushing()
    {
        var advancing = ModelRoster.BuildRow(1, movedInches: 5f, maxAdvanceInches: 6f,
            maxDistanceInches: 12f, cappedByTerrain: false);
        var rushing = ModelRoster.BuildRow(2, movedInches: 6f, maxAdvanceInches: 6f,
            maxDistanceInches: 12f, cappedByTerrain: false);

        Assert.That(advancing.InRush, Is.False, "still able to shoot after this move");
        Assert.That(rushing.InRush, Is.True,
            "exactly at the Advance allowance already costs the shot - the panel must not round it away");
    }

    [Test]
    public void BuildRow_SeparatesAnUntouchedModelFromOneThatHasBarelyMoved()
    {
        Assert.That(ModelRoster.BuildRow(1, 0f, 6f, 12f, false).Started, Is.False);
        Assert.That(ModelRoster.BuildRow(1, 0.01f, 6f, 12f, false).Started, Is.True,
            "the greyed / coloured split is the checklist of who has been dealt with");
    }

    [Test]
    public void BuildRow_ShowsTheDifficultTerrainCapAsTheRealMaximum()
    {
        var capped = ModelRoster.BuildRow(1, movedInches: 4f, maxAdvanceInches: 6f,
            maxDistanceInches: 12f, cappedByTerrain: true);

        Assert.That(capped.MaxInches,
            Is.EqualTo(GameWideConstants.DIFFICULT_TERRAIN_MOVE_CAP_INCHES).Within(0.001f),
            "#155: a committed path through difficult terrain is held to 6\" - the row must not promise 12\"");
    }

    [Test]
    public void BuildRow_LeavesABudgetAlreadyUnderTheCapAlone()
    {
        var slow = ModelRoster.BuildRow(1, 1f, 2f, maxDistanceInches: 4f, cappedByTerrain: true);

        Assert.That(slow.MaxInches, Is.EqualTo(4f).Within(0.001f),
            "the cap is a ceiling, not a floor - it must never raise a short move's maximum");
    }

    [Test]
    public void RowText_NumbersModelsAndMarksTheSelectionWithoutRelyingOnColour()
    {
        Assert.That(ModelRoster.RowNameText(3, selected: true), Does.StartWith(">"));
        Assert.That(ModelRoster.RowNameText(3, selected: false), Does.Not.StartWith(">"));
        Assert.That(ModelRoster.RowNameText(3, selected: false).Trim(), Is.EqualTo("Model 3"),
            "same 'Model N' vocabulary as the wound-assignment list");
    }

    [Test]
    public void RowText_ShowsDistanceAgainstThisModelsOwnMaximum()
    {
        var row = ModelRoster.BuildRow(1, movedInches: 4.237f, maxAdvanceInches: 6f,
            maxDistanceInches: 12f, cappedByTerrain: false);

        Assert.That(ModelRoster.RowDistanceText(row), Is.EqualTo("4.24\" / 12\""),
            "two decimals, matching the selected model's detail line - one number, one rendering");
    }

    [Test]
    public void FormatInches_DropsAPointlessDecimalButKeepsARealOne()
    {
        Assert.That(ModelRoster.FormatInches(12f), Is.EqualTo("12"));
        Assert.That(ModelRoster.FormatInches(11.98f), Is.EqualTo("12"));
        Assert.That(ModelRoster.FormatInches(4.5f), Is.EqualTo("4.5"));
    }

    [Test]
    public void RowsAreShorterThanOptionButtonsAndScaleWithTheFont()
    {
        Assert.That(ModelRoster.RowHeight(LineH), Is.LessThan(ResolverPanelLayout.OptionRowHeight(LineH)),
            "roster entries are list rows to scan, not buttons to aim at");
        Assert.That(ModelRoster.RowHeight(LineH * 2f), Is.EqualTo(ModelRoster.RowHeight(LineH) * 2f).Within(0.001f),
            "#298: a pixel constant is a hairline on a 4K display");
    }
}
