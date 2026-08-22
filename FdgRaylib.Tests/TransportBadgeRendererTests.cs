using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #096: transport occupancy indicator. Pure text formatting — the on-table "Carrying X/Y" badge and the
// hover cargo header/lines. Occupancy counting itself is TransportUtilities' job (engine-tested); this
// only pins the ASCII-safe wording the GUI renders.
[TestFixture]
public class TransportBadgeRendererTests
{
    [Test]
    public void FormatBadge_IsOccupiedOverCapacity()
    {
        Assert.That(TransportBadgeRenderer.FormatBadge(4, 6), Is.EqualTo("Carrying 4/6"));
        Assert.That(TransportBadgeRenderer.FormatBadge(0, 6), Is.EqualTo("Carrying 0/6"), "empty transport still shows capacity");
        Assert.That(TransportBadgeRenderer.FormatBadge(6, 6), Is.EqualTo("Carrying 6/6"), "full");
    }

    [Test]
    public void FormatAboardHeader_PluralizesAndHandlesEmpty()
    {
        Assert.That(TransportBadgeRenderer.FormatAboardHeader(0, 0, 6), Is.EqualTo("Empty (0/6 spaces)"));
        Assert.That(TransportBadgeRenderer.FormatAboardHeader(1, 1, 6), Is.EqualTo("1 unit aboard (1/6 spaces):"));
        Assert.That(TransportBadgeRenderer.FormatAboardHeader(2, 4, 6), Is.EqualTo("2 units aboard (4/6 spaces):"));
    }

    [Test]
    public void FormatOccupant_PluralizesSpaces()
    {
        Assert.That(TransportBadgeRenderer.FormatOccupant("Warriors", 1), Is.EqualTo("Warriors (1 space)"));
        Assert.That(TransportBadgeRenderer.FormatOccupant("Heavy Gunners", 3), Is.EqualTo("Heavy Gunners (3 spaces)"));
    }

    [Test]
    public void AllStringsAreAscii()
    {
        // Game text is ASCII-only (the ImGui atlas bakes only Basic Latin + Latin-1). Guard the wording.
        string[] samples =
        {
            TransportBadgeRenderer.FormatBadge(4, 6),
            TransportBadgeRenderer.FormatAboardHeader(0, 0, 6),
            TransportBadgeRenderer.FormatAboardHeader(2, 4, 6),
            TransportBadgeRenderer.FormatOccupant("Heavy Gunners", 3),
        };
        foreach (string s in samples)
            foreach (char c in s)
                Assert.That(c, Is.LessThanOrEqualTo((char)0x7F), $"non-ASCII char in \"{s}\"");
    }
}
