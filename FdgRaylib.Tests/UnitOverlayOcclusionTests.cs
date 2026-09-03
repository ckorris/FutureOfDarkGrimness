using FdgRaylib.Rendering;
using NUnit.Framework;
using Raylib_cs;

namespace FdgRaylib.Tests;

// #327 — unit name labels, token chips, transport badges and health bars are ImGui draw-list text, which
// renders ON TOP of the Raylib-drawn roll panels. Once panels started lingering (and stacking), labels
// over the middle of the table landed on them and both became unreadable. Anything that would collide
// with the stack yields while the stack is up.
[TestFixture]
public class UnitOverlayOcclusionTests
{
    // A panel strip across the bottom-centre of a 1920x1080 screen.
    private static readonly Rectangle Stack = new(660, 700, 600, 300);

    [Test]
    public void ALabelOverThePanel_Yields()
    {
        Assert.That(TableTooltipOverlay.IsOccluded(Stack, 800f, 800f, 120f, 16f), Is.True);
    }

    [Test]
    public void ALabelClearOfThePanel_Draws()
    {
        Assert.That(TableTooltipOverlay.IsOccluded(Stack, 200f, 300f, 120f, 16f), Is.False,
            "elsewhere on the table");
        Assert.That(TableTooltipOverlay.IsOccluded(Stack, 1400f, 800f, 120f, 16f), Is.False,
            "beside the stack, same height");
        Assert.That(TableTooltipOverlay.IsOccluded(Stack, 800f, 400f, 120f, 16f), Is.False,
            "above the stack");
    }

    [Test]
    public void ALabelJustTouchingTheEdge_Yields()
    {
        // Ending 2px short of the panel still reads as collision: text flush against a panel edge is as
        // hard to read as text on top of it.
        Assert.That(TableTooltipOverlay.IsOccluded(Stack, 540f, 800f, 118f, 16f), Is.True,
            "within the margin on the left edge");
        Assert.That(TableTooltipOverlay.IsOccluded(Stack, 800f, 682f, 120f, 16f), Is.True,
            "within the margin above the top edge");
    }

    [Test]
    public void AnEmptyStack_OccludesNothing()
    {
        // What the renderer passes when no roll is on screen.
        Assert.That(TableTooltipOverlay.IsOccluded(new Rectangle(0, 0, 0, 0), 800f, 800f, 120f, 16f),
            Is.False);
    }
}
