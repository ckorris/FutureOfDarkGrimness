using FDG.StageResolution.Requests;
using FdgRaylib.Rendering.Resolvers;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #311 — Pass ends an activation and cannot be taken back, but in the Choose Action menu it was just the
// last row of an ordinary option list: in a 2026-08-02 multiplayer game both players ended activations
// they still had actions left in by clicking it where it happened to sit under the cursor. It is now
// pinned to the bottom of the panel, separated from the real actions and raised off the bottom edge,
// behind a confirmation. The drawing is ImGui (hand-verified); the vertical budget that keeps the gaps
// real at every UI scale is pinned here — the failure mode is padding that collapses to a hairline as
// the font grows, which is invisible on the developer's own display.
[TestFixture]
public class ActionMenuLayoutTests
{
    // The font sizes the app actually produces: 18f at the 1.0 UI-scale floor, 18f * 1.4 at the 4K anchor.
    private const float LineHeight1x = 18f;
    private const float LineHeight4K = 25.2f;

    private const float PanelHeight = 400f;
    private const float ListTop = 60f;          // instructions + padding, measured by the resolver

    private static float PassRow(float lineHeight) => ResolverPanelLayout.OptionRowHeight(lineHeight);

    [Test]
    public void PassIndex_FindsThePassOption_AmongValidOptions()
    {
        string[] menu = { "Move", "Charge", "Shoot", "Pass" };

        Assert.That(ActionMenuLayout.PassIndex(menu), Is.EqualTo(3));
        Assert.That(ActionMenuLayout.PassIndex(new[] { "Move", "Charge" }), Is.EqualTo(-1),
            "a menu with no Pass (or any other string menu - weapons, spells) must not grow a footer");
    }

    [Test]
    public void PassIndex_FindsAGreyedOutPass_SoItKeepsItsPinnedSpot()
    {
        // #197 Instinctive and friends can make passing illegal; the row stays where it was, greyed.
        var invalid = new[]
        {
            new StringSelectionRequest.InvalidOption("Move", "Instinctive - must attack the closest target first."),
            new StringSelectionRequest.InvalidOption("Pass", "Instinctive - must attack the closest target first."),
        };

        Assert.That(ActionMenuLayout.PassIndex(invalid), Is.EqualTo(1));
        Assert.That(ActionMenuLayout.PassIndex(new[] { new StringSelectionRequest.InvalidOption("Move", "Moved.") }),
            Is.EqualTo(-1));
    }

    [Test]
    public void PassOption_MatchesTheEngineChoiceName()
    {
        // The client pins by NAME (as ResolverHotkeys pins its letters); if the engine ever renames the
        // choice, the footer would silently stop appearing rather than fail to compile.
        Assert.That(ActionMenuLayout.PassOption, Is.EqualTo(FDG.Stages.ChooseActionStage.PASS_CHOICE_NAME));
    }

    [Test]
    public void FooterHeight_ReservesTheGapsAroundPass_AtEveryUiScale()
    {
        foreach (float lineHeight in new[] { LineHeight1x, LineHeight4K })
        {
            float footer = ActionMenuLayout.FooterHeight(lineHeight, PassRow(lineHeight), allowCancel: false);

            Assert.That(footer - PassRow(lineHeight), Is.EqualTo(
                    ActionMenuLayout.GapAbove(lineHeight) + ActionMenuLayout.GapBelow(lineHeight)).Within(0.001f),
                "a Pass-only footer is exactly the row plus the gap above it and the gap below it");
            Assert.That(ActionMenuLayout.GapBelow(lineHeight), Is.GreaterThan(lineHeight * 0.5f),
                "the whole point of the gap below is that Pass is not flush against the panel edge");
            Assert.That(ActionMenuLayout.GapAbove(lineHeight), Is.GreaterThan(lineHeight * 0.5f),
                "and the gap above is what stops it reading as one more row of the action list");
        }
    }

    [Test]
    public void FooterHeight_CostsTheBackButton_WhenTheActivationIsCancellable()
    {
        float lineHeight = LineHeight1x;
        float without = ActionMenuLayout.FooterHeight(lineHeight, PassRow(lineHeight), allowCancel: false);
        float with    = ActionMenuLayout.FooterHeight(lineHeight, PassRow(lineHeight), allowCancel: true);

        Assert.That(with - without, Is.EqualTo(
                ResolverPanelLayout.ActionRowHeight(lineHeight) + ActionMenuLayout.GapBetween(lineHeight))
            .Within(0.001f), "#248's Back button is pinned below Pass, so the footer must pay for it");
    }

    [Test]
    public void FooterHeight_GrowsWithAWrappedReason_WhenPassIsGreyedOut()
    {
        float lineHeight = LineHeight1x;
        float oneLine   = ActionMenuLayout.FooterHeight(lineHeight, PassRow(lineHeight), allowCancel: false);
        float threeLine = ActionMenuLayout.FooterHeight(lineHeight, PassRow(lineHeight) + lineHeight * 2f,
            allowCancel: false);

        Assert.That(threeLine - oneLine, Is.EqualTo(lineHeight * 2f).Within(0.001f),
            "a greyed Pass carrying a wrapped reason takes the extra height out of the list, not out of the gaps");
    }

    [Test]
    public void FooterHeight_ScalesWithTheFont()
    {
        float small = ActionMenuLayout.FooterHeight(LineHeight1x, PassRow(LineHeight1x), allowCancel: true);
        float large = ActionMenuLayout.FooterHeight(LineHeight4K, PassRow(LineHeight4K), allowCancel: true);

        Assert.That(large / small, Is.EqualTo(LineHeight4K / LineHeight1x).Within(0.001f),
            "every part of the footer is a line-height multiple (#298), so the whole thing tracks the font");
    }

    [Test]
    public void ListHeight_TakesWhateverTheFooterLeaves()
    {
        float lineHeight = LineHeight1x;
        float footer = ActionMenuLayout.FooterHeight(lineHeight, PassRow(lineHeight), allowCancel: true);

        Assert.That(ActionMenuLayout.ListHeight(PanelHeight, ListTop, footer, lineHeight),
            Is.EqualTo(PanelHeight - ListTop - footer).Within(0.001f));
    }

    [Test]
    public void ListHeight_NeverCollapsesToNothing_OnAVeryShortPanel()
    {
        float lineHeight = LineHeight1x;
        float footer = ActionMenuLayout.FooterHeight(lineHeight, PassRow(lineHeight), allowCancel: true);

        // A panel shorter than its own footer: the list must still be a scrollable strip, not a
        // zero-height (or negative) child, which ImGui treats as "fill the parent".
        float listHeight = ActionMenuLayout.ListHeight(80f, ListTop, footer, lineHeight);

        Assert.That(listHeight, Is.EqualTo(lineHeight * ActionMenuLayout.MinListLines).Within(0.001f));
        Assert.That(listHeight, Is.GreaterThan(0f));
    }

    // The invariant the drawing code actually depends on: the resolver lays the footer out by walking down
    // from the bottom of the list, so its LAST row - Back when the activation is cancellable, Pass
    // otherwise - lands exactly GapBelow above the panel's bottom edge. If FooterHeight and that walk ever
    // disagree, the footer drifts off the panel (or floats in the middle of it).
    [Test]
    public void TheLastFooterRow_LandsExactlyOneBottomGapAboveThePanelEdge()
    {
        foreach (float lineHeight in new[] { LineHeight1x, LineHeight4K })
        {
            foreach (bool allowCancel in new[] { false, true })
            {
                float passRow = PassRow(lineHeight);
                float footer = ActionMenuLayout.FooterHeight(lineHeight, passRow, allowCancel);
                float listHeight = ActionMenuLayout.ListHeight(PanelHeight, ListTop, footer, lineHeight);

                // Mirrors GuiStringSelectionResolver.Draw's footer walk: Pass first, then Back under it.
                float y = ListTop + listHeight + ActionMenuLayout.GapAbove(lineHeight);
                float bottom = y + passRow;
                if (allowCancel)
                {
                    bottom += ActionMenuLayout.GapBetween(lineHeight)
                              + ResolverPanelLayout.ActionRowHeight(lineHeight);
                }

                Assert.That(PanelHeight - bottom, Is.EqualTo(ActionMenuLayout.GapBelow(lineHeight)).Within(0.001f),
                    $"lineHeight {lineHeight}, allowCancel {allowCancel}");
            }
        }
    }
}
