using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// The canvas hover tooltips (unit, terrain, and the unit-picker's stat block) paint on a translucent well
// instead of the near-opaque PopupBg the menus use, so the board underneath stays readable through a tall
// stat block.
// These guard the two ways that intent gets lost: someone restoring it to opaque, and someone thinning
// it far enough that the dark backing no longer carries the near-white body text.
[TestFixture]
public class TooltipTransparencyTests
{
    [Test]
    public void TheTooltipBackground_IsPartiallyTransparent()
    {
        Assert.That(ImGuiTheme.TooltipBg.W, Is.LessThan(1f), "opaque - nothing shows through");
        Assert.That(ImGuiTheme.TooltipBg.W, Is.GreaterThan(0.5f),
            "too thin for near-white text to hold contrast over a lit table");
    }

    // The tone must stay the theme's darkest well (ImGuiTheme.InkWell, what PopupBg is built from) so the
    // tooltip reads as the same surface as every other popup - just thinner - rather than a grey slab.
    [Test]
    public void TheTooltipBackground_KeepsTheThemePopupTone()
    {
        Assert.That(ImGuiTheme.TooltipBg.X, Is.EqualTo(ImGuiTheme.InkWell.X).Within(0.001f));
        Assert.That(ImGuiTheme.TooltipBg.Y, Is.EqualTo(ImGuiTheme.InkWell.Y).Within(0.001f));
        Assert.That(ImGuiTheme.TooltipBg.Z, Is.EqualTo(ImGuiTheme.InkWell.Z).Within(0.001f));
    }
}
