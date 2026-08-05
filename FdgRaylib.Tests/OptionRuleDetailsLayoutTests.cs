using FdgRaylib.Rendering.Resolvers;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #333: the rule-details strip under the melee weapon list is costed out of the panel before the option
// list is, so a weapon with a wall of rules scrolls its own strip instead of squeezing the options it is
// there to be compared against. Sibling of ActionMenuLayoutTests - the arithmetic is testable, the
// drawing around it is hand-verified.
[TestFixture]
public class OptionRuleDetailsLayoutTests
{
    private const float Line = 20f;

    [Test]
    public void StripHeight_TakesItsShareOfARoomyPanel()
    {
        // 0.28 * 800 = 224, comfortably between the 5-line floor (100) and the 0.38 cap (304).
        Assert.That(OptionRuleDetailsLayout.StripHeight(800f, Line), Is.EqualTo(224f).Within(0.01f));
    }

    [Test]
    public void StripHeight_FlooredSoItStillSaysSomethingOnAShortPanel()
    {
        // 0.28 * 300 = 84, under the 5-line floor of 100 - and 100 is still inside the 114 cap.
        Assert.That(OptionRuleDetailsLayout.StripHeight(300f, Line),
            Is.EqualTo(Line * OptionRuleDetailsLayout.MinLines).Within(0.01f));
    }

    // The floor must never win big enough to leave the options with less room than the strip: on a panel
    // too short to honour both, the cap is the one that holds.
    [Test]
    public void StripHeight_CappedEvenWhenTheFloorWantsMore()
    {
        const float tiny = 100f;

        float height = OptionRuleDetailsLayout.StripHeight(tiny, Line);

        Assert.That(height, Is.EqualTo(tiny * OptionRuleDetailsLayout.MaxHeightFraction).Within(0.01f));
        Assert.That(height, Is.LessThan(Line * OptionRuleDetailsLayout.MinLines));
    }

    [Test]
    public void StripNeverTakesMoreOfThePanelThanTheOptionsKeep()
    {
        foreach (float panel in new[] { 100f, 240f, 400f, 800f, 1600f })
        {
            Assert.That(OptionRuleDetailsLayout.TotalHeight(panel, Line), Is.LessThan(panel * 0.5f),
                $"panel {panel}: the option list must keep the majority of the height");
        }
    }

    [Test]
    public void TotalHeight_IsTheStripPlusTheGapAboveIt()
    {
        Assert.That(OptionRuleDetailsLayout.TotalHeight(800f, Line),
            Is.EqualTo(OptionRuleDetailsLayout.StripHeight(800f, Line)
                       + OptionRuleDetailsLayout.GapAbove(Line)).Within(0.01f));
    }

    // #298's rule: every gap is a line-height multiple, so it stays padding on a 4K display instead of
    // collapsing to a hairline.
    [Test]
    public void GapAbove_ScalesWithTheFont()
    {
        Assert.That(OptionRuleDetailsLayout.GapAbove(40f),
            Is.EqualTo(OptionRuleDetailsLayout.GapAbove(20f) * 2f).Within(0.01f));
    }
}
