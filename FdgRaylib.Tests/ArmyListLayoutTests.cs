using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #329 — the army list overlay's pure layout/formatting core. The overlay itself is ImGui drawing;
// everything with a decision in it lives in ArmyListLayout so it can be pinned here: masonry column
// packing (cards keep army order within a column, columns stay near-even), local-player-first tab
// ordering, rule-line wrapping that never splits a hover target, and the weapon-table cell spellings
// that make a card read like the printed Army Forge list.
[TestFixture]
public class ArmyListLayoutTests
{
    // ── PackColumns ─────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void PackColumns_SingleColumn_TakesEverything()
    {
        var heights = new float[] { 30f, 50f, 20f };

        Assert.That(ArmyListLayout.PackColumns(heights, 1), Is.EqualTo(new[] { 0, 0, 0 }));
    }

    [Test]
    public void PackColumns_EqualHeights_AlternateLeftToRight()
    {
        // Ties go to the leftmost column, so equal cards deal out like a card hand.
        var heights = new float[] { 10f, 10f, 10f, 10f };

        Assert.That(ArmyListLayout.PackColumns(heights, 2), Is.EqualTo(new[] { 0, 1, 0, 1 }));
    }

    [Test]
    public void PackColumns_ATallFirstCard_SendsTheRestToTheOtherColumn()
    {
        // The Hive Lord's long card fills column 0; the grunts stack beside it instead of below it.
        var heights = new float[] { 100f, 10f, 10f, 10f };

        Assert.That(ArmyListLayout.PackColumns(heights, 2), Is.EqualTo(new[] { 0, 1, 1, 1 }));
    }

    [Test]
    public void PackColumns_KeepsArmyOrderWithinAColumn()
    {
        // 0 -> col0 (50). 1 -> col1 (30). 2 -> col1 again (30 < 50). 3 -> col0 (50 < 90). Cards 1
        // and 2 share column 1 in input order — assignment happens in input order, so a later unit
        // can never leapfrog an earlier one within its column.
        var heights = new float[] { 50f, 30f, 60f, 30f };
        int[] columns = ArmyListLayout.PackColumns(heights, 2);

        Assert.That(columns, Is.EqualTo(new[] { 0, 1, 1, 0 }));
    }

    // ── ColumnCount ─────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void ColumnCount_FloorsDivision_AndClamps()
    {
        Assert.That(ArmyListLayout.ColumnCount(900f, 420f), Is.EqualTo(2));
        Assert.That(ArmyListLayout.ColumnCount(400f, 420f), Is.EqualTo(1), "always at least one");
        Assert.That(ArmyListLayout.ColumnCount(5000f, 420f, maxColumns: 4), Is.EqualTo(4),
            "wide windows cap out instead of shrinking cards to strips");
        Assert.That(ArmyListLayout.ColumnCount(900f, 0f), Is.EqualTo(1), "degenerate card width");
    }

    // ── OrderTabs ───────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void OrderTabs_LocalPlayerFirst_RestKeepSlotOrder()
    {
        var slots = new[] { "A", "B", "C", "D" };

        Assert.That(ArmyListLayout.OrderTabs(slots, s => s == "C"),
            Is.EqualTo(new[] { "C", "A", "B", "D" }));
    }

    [Test]
    public void OrderTabs_NoLocals_IsUnchanged()
    {
        var slots = new[] { "A", "B", "C" };

        Assert.That(ArmyListLayout.OrderTabs(slots, _ => false), Is.EqualTo(slots));
        Assert.That(ArmyListLayout.OrderTabs(slots, _ => true), Is.EqualTo(slots),
            "all-local (hotseat) also keeps slot order");
    }

    // ── WrapSegments ────────────────────────────────────────────────────────────────────────────────

    // Width stub: 10px per character, so widths are easy to read off the string.
    private static float Measure(string s) => s.Length * 10f;

    private static RuleHoverText.Segment Rule(string name) => new(name, name, "desc");
    private static RuleHoverText.Segment Sep() => new(", ", null, null);

    [Test]
    public void WrapSegments_EverythingFits_OneLine()
    {
        var segments = new[] { Rule("Fear"), Sep(), Rule("Hero") };

        var lines = ArmyListLayout.WrapSegments(segments, Measure, 200f);

        Assert.That(lines, Has.Count.EqualTo(1));
        Assert.That(lines[0], Has.Count.EqualTo(3));
    }

    [Test]
    public void WrapSegments_BreaksBeforeARule_SeparatorStaysBehind()
    {
        // "AAAA, " = 60px; BBBB would end at 100 > 70, so the break lands before BBBB and the
        // separator stays glued to the line above — a line never opens with ", ".
        var segments = new[] { Rule("AAAA"), Sep(), Rule("BBBB") };

        var lines = ArmyListLayout.WrapSegments(segments, Measure, 70f);

        Assert.That(lines, Has.Count.EqualTo(2));
        Assert.That(lines[0][^1].Text, Is.EqualTo(", "));
        Assert.That(lines[1][0].Text, Is.EqualTo("BBBB"), "the rule name is never split");
    }

    [Test]
    public void WrapSegments_ARuleWiderThanTheLine_KeepsItsOwnLine()
    {
        var segments = new[] { Rule("Extremely-Long-Rule-Name") };

        var lines = ArmyListLayout.WrapSegments(segments, Measure, 50f);

        Assert.That(lines, Has.Count.EqualTo(1));
        Assert.That(lines[0], Has.Count.EqualTo(1));
    }

    [Test]
    public void WrapSegments_Empty_NoLines()
    {
        var lines = ArmyListLayout.WrapSegments(System.Array.Empty<RuleHoverText.Segment>(), Measure, 100f);

        Assert.That(lines, Is.Empty);
    }

    // ── Weapon cell spellings ───────────────────────────────────────────────────────────────────────

    [Test]
    public void WeaponCells_ReadLikeThePrintedList()
    {
        Assert.That(ArmyListLayout.RangeText(24f), Is.EqualTo("24\""));
        Assert.That(ArmyListLayout.RangeText(2.5f), Is.EqualTo("2.5\""));
        Assert.That(ArmyListLayout.RangeText(0f), Is.EqualTo("-"), "melee shows a dash under RNG");
        Assert.That(ArmyListLayout.AttacksText(6), Is.EqualTo("A6"));
        Assert.That(ArmyListLayout.ApText(4), Is.EqualTo("4"));
        Assert.That(ArmyListLayout.ApText(0), Is.EqualTo("-"));
        Assert.That(ArmyListLayout.CountedName(6, "Razor Claws"), Is.EqualTo("6x Razor Claws"));
        Assert.That(ArmyListLayout.CountedName(1, "Stomp"), Is.EqualTo("Stomp"),
            "a single weapon shows no count prefix");
    }
}
