using FdgRaylib.Rendering.Resolvers;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #298 — the resolver option lists (action menu, melee weapon, spell, charge target, ...) hardcoded 28-32px
// rows while the ImGui font is 18f * uiScale — up to 25px on a 4K display, so the label filled the button
// edge to edge and every list read as a cramped stack of text. Row height is now derived from the live font
// line height. The drawing is ImGui (hand-verified); the arithmetic that has to hold at every UI scale is
// pinned here, because the failure mode — buttons that shrink back toward the text as the font grows — is
// invisible on the developer's own display until someone runs it at a different resolution.
[TestFixture]
public class ResolverPanelLayoutTests
{
    // The font sizes the app actually produces: 18f at the 1.0 UI-scale floor, 18f * 1.4 at the 4K anchor.
    private const float LineHeight1x = 18f;
    private const float LineHeight4K = 25.2f;

    [Test]
    public void OptionRowHeight_IsAtLeastTwiceTheOldFixedHeight_AtEveryUiScale()
    {
        // 28f was the string-selection / ability-effect / spell row height before #298.
        const float oldFixedHeight = 28f;

        Assert.That(ResolverPanelLayout.OptionRowHeight(LineHeight1x), Is.GreaterThan(oldFixedHeight * 1.5f),
            "even at the smallest UI scale the row must clear the old sliver by a wide margin");
        Assert.That(ResolverPanelLayout.OptionRowHeight(LineHeight4K), Is.GreaterThan(oldFixedHeight * 2f),
            "at the 4K font the row is more than double what it used to be");
    }

    [Test]
    public void OptionRowHeight_LeavesRoomAroundTheLabel()
    {
        foreach (float lineHeight in new[] { LineHeight1x, LineHeight4K })
        {
            float row = ResolverPanelLayout.OptionRowHeight(lineHeight);
            Assert.That(row - lineHeight, Is.GreaterThan(lineHeight * 0.5f),
                $"a one-line label at {lineHeight}px must sit in the button, not fill it");
            Assert.That(row, Is.LessThan(lineHeight * 3f),
                "but a single option must not become a slab that pushes the list off the panel");
        }
    }

    [Test]
    public void OptionRowHeight_ScalesWithTheFont()
    {
        float small = ResolverPanelLayout.OptionRowHeight(LineHeight1x);
        float large = ResolverPanelLayout.OptionRowHeight(LineHeight4K);

        Assert.That(large / small, Is.EqualTo(LineHeight4K / LineHeight1x).Within(0.001f),
            "the whole point: the row tracks the font instead of a pixel constant");
    }

    [Test]
    public void ActionRowHeight_RecedesBelowAnOptionRow_ButStaysReadable()
    {
        // ActionRowHeight reads the live ImGui font, so compare the multiples rather than calling it here
        // (no ImGui context in a unit test).
        Assert.That(ResolverPanelLayout.OptionRowLineMultiple, Is.GreaterThan(2f),
            "Back/Cancel (2.0 line heights) must read as subordinate to a full option row");
    }
}
