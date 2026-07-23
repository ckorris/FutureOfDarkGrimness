using System;
using System.Collections.Generic;
using System.Linq;
using FDG.ArmyBuilding;
using FDG.SaveLoad;
using FdgRaylib.Rendering;
using NUnit.Framework;
using static FdgRaylib.Rendering.RuleTextFlow;

namespace FdgRaylib.Tests;

// #259 — the pure seams behind the Army Forge's underlined rule names: the segment builders (which must
// reproduce the exact lines they replaced) and the wrap layout. The ImGui drawing is hand-verified.
[TestFixture]
public class RuleTextFlowTests
{
    // A fixed-pitch stand-in for ImGui.CalcTextSize, so wrapping is exactly predictable in tests.
    private static float Measure(string text) => text.Length * 10f;

    private static SpecialRuleEntry Core(string name) => new SpecialRuleEntry_Core(name);

    private static WeaponFileEntry Weapon(string name, int quantity, int range, int attacks, int ap,
        params string[] rules) =>
        new()
        {
            Name = name, Quantity = quantity, RangeInches = range, Attacks = attacks, ArmorPenetration = ap,
            SpecialRules = rules.Select(Core).ToList(),
        };

    // ── Segment builders reproduce the strings they replaced ────────────────────────────────────────────

    [Test]
    public void WeaponLine_FlattensToTheSameTextAsWeaponSummary()
    {
        WeaponFileEntry[] weapons =
        {
            Weapon("Heavy Serrated Blade", 4, 24, 6, 4, "Reliable", "Rending"),
            Weapon("CCW", 1, 0, 2, 0),                       // melee, no AP, no rules
            Weapon("Shard Pistol", 2, 12, 1, 0, "Crack"),    // no AP, one rule
        };

        foreach (WeaponFileEntry weapon in weapons)
            Assert.That(Flatten(WeaponLine(weapon)), Is.EqualTo(ArmyBuilderScreen.WeaponSummary(weapon)));
    }

    [Test]
    public void ItemLine_FlattensToTheSameTextAsItemSummary()
    {
        var shield = new ItemEntry { Name = "Combat Shield", Quantity = 5, Rules = { Core("Shielded") } };
        var plain = new ItemEntry { Name = "Banner", Quantity = 1 };

        Assert.That(Flatten(ItemLine(shield)), Is.EqualTo(ArmyForgeScreen.ItemSummary(shield)));
        Assert.That(Flatten(ItemLine(plain)), Is.EqualTo(ArmyForgeScreen.ItemSummary(plain)));
    }

    [Test]
    public void RuleList_FlattensToTheCommaJoinedNames()
    {
        var rules = new List<SpecialRuleEntry>
        {
            Core("Hero"), new SpecialRuleEntry_CoreNumeric("Tough", 3), Core("Highborn"),
        };
        Assert.That(Flatten(RuleList(rules)), Is.EqualTo("Hero, Tough(3), Highborn"));
    }

    [Test]
    public void RuleList_MarksEveryRuleAndNoSeparator()
    {
        IReadOnlyList<RuleSegment> segments = RuleList(new[] { Core("Hero"), Core("Fearless") });

        Assert.That(segments.Where(s => s.IsRule).Select(s => s.Text), Is.EqualTo(new[] { "Hero", "Fearless" }));
        Assert.That(segments.Where(s => !s.IsRule).Select(s => s.Text), Is.EqualTo(new[] { ", " }));
    }

    [Test]
    public void WeaponLine_MarksOnlyTheRuleNames_NotTheStats()
    {
        IReadOnlyList<RuleSegment> segments = WeaponLine(Weapon("Blade", 4, 24, 6, 4, "Reliable", "Rending"));

        Assert.That(segments.Where(s => s.IsRule).Select(s => s.Text),
            Is.EqualTo(new[] { "Reliable", "Rending" }));
        // "AP(4)" and "A6" must stay plain - they are stats, not rules.
        Assert.That(segments.Any(s => s.IsRule && s.Text.Contains("AP")), Is.False);
    }

    // ── Free-text option-label scanning ─────────────────────────────────────────────────────────────────

    [Test]
    public void ScanLabel_ReassemblesTheLabelExactly()
    {
        const string label = "Energy Spear (A2, AP(4), Rending)  (+15 pts)";
        IReadOnlyList<RuleSegment> segments = ScanLabel(label, new[] { Core("Rending") });

        Assert.That(Flatten(segments), Is.EqualTo(label));
        Assert.That(segments.Single(s => s.IsRule).Text, Is.EqualTo("Rending"));
    }

    [Test]
    public void ScanLabel_RespectsWordBoundaries()
    {
        // "Shredder" must NOT light up just because the rule "Shred" is a prefix of it.
        IReadOnlyList<RuleSegment> segments =
            ScanLabel("Shredder Rifle (24\", A2, Shred)", new[] { Core("Shred") });

        Assert.That(segments.Count(s => s.IsRule), Is.EqualTo(1));
        Assert.That(segments.TakeWhile(s => !s.IsRule).Sum(s => s.Text.Length), Is.GreaterThan(10),
            "the match is the trailing rule, not the weapon name");
    }

    [Test]
    public void ScanLabel_PrefersTheLongestMatchingName()
    {
        IReadOnlyList<RuleSegment> segments =
            ScanLabel("Gains Shred in melee", new[] { Core("Shred"), Core("Shred in melee") });

        Assert.That(segments.Single(s => s.IsRule).Text, Is.EqualTo("Shred in melee"));
    }

    [Test]
    public void ScanLabel_MatchesANumericRulesPrintableName()
    {
        IReadOnlyList<RuleSegment> segments =
            ScanLabel("Gains Tough(3)", new[] { (SpecialRuleEntry)new SpecialRuleEntry_CoreNumeric("Tough", 3) });

        Assert.That(segments.Single(s => s.IsRule).Text, Is.EqualTo("Tough(3)"));
    }

    [Test]
    public void ScanLabel_WithNoCandidates_IsOnePlainSegment()
    {
        IReadOnlyList<RuleSegment> segments = ScanLabel("Replace all Rifles", Array.Empty<SpecialRuleEntry>());

        Assert.That(segments.Count, Is.EqualTo(1));
        Assert.That(segments[0].IsRule, Is.False);
        Assert.That(segments[0].Text, Is.EqualTo("Replace all Rifles"));
    }

    [Test]
    public void OptionLabel_TakesCandidatesFromEveryThingTheOptionGrants()
    {
        var option = new UpgradeOption
        {
            Label = "Plasma Rifle (24\", A1, AP(4), Reliable) and a Combat Shield (Shielded), gains Fearless",
            RulesGained = { Core("Fearless") },
            WeaponsGained = { Weapon("Plasma Rifle", 1, 24, 1, 4, "Reliable") },
            ItemsGained = { new ItemEntry { Name = "Combat Shield", Rules = { Core("Shielded") } } },
        };

        IReadOnlyList<RuleSegment> segments = OptionLabel(option, option.Label);

        Assert.That(Flatten(segments), Is.EqualTo(option.Label));
        Assert.That(segments.Where(s => s.IsRule).Select(s => s.Text),
            Is.EquivalentTo(new[] { "Reliable", "Shielded", "Fearless" }));
    }

    // ── Wrap layout ─────────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void Layout_WrapsPlainTextAtWordBoundaries()
    {
        IReadOnlyList<PlacedChunk> placed =
            Layout(new[] { new RuleSegment("aaa bbb ccc", null) }, 45f, Measure);

        Assert.That(placed.Select(p => p.Line), Is.EqualTo(new[] { 0, 1, 2 }));
        Assert.That(placed.Select(p => p.X), Is.EqualTo(new[] { 0f, 0f, 0f }));
        Assert.That(string.Concat(placed.Select(p => p.Text)), Is.EqualTo("aaa bbb ccc"));
    }

    [Test]
    public void Layout_KeepsARuleNameWholeAcrossAWrap()
    {
        IReadOnlyList<PlacedChunk> placed = Layout(
            new[] { new RuleSegment("aa ", null), new RuleSegment("Rending", Core("Rending")) }, 50f, Measure);

        PlacedChunk rule = placed.Single(p => p.Rule is not null);
        Assert.That(rule.Text, Is.EqualTo("Rending"), "never split, even when it overflows the line");
        Assert.That(rule.Line, Is.EqualTo(1));
        Assert.That(rule.X, Is.EqualTo(0f));
    }

    [Test]
    public void Layout_DoesNotWrapWhatFits()
    {
        IReadOnlyList<PlacedChunk> placed =
            Layout(RuleList(new[] { Core("Hero"), Core("Fearless") }), 1000f, Measure);

        Assert.That(placed.All(p => p.Line == 0), Is.True);
        Assert.That(placed.Select(p => p.X), Is.EqualTo(new[] { 0f, 40f, 60f }));
    }

    [Test]
    public void Layout_AnOverlongChunkOverflowsRatherThanLoopingForever()
    {
        IReadOnlyList<PlacedChunk> placed =
            Layout(new[] { new RuleSegment("Interminable", null) }, 5f, Measure);

        Assert.That(placed.Count, Is.EqualTo(1));
        Assert.That(placed[0].Line, Is.EqualTo(0));
    }

    [Test]
    public void MeasureLines_CountsTheWrappedLines()
    {
        var segments = new[] { new RuleSegment("aaa bbb ccc", null) };

        Assert.That(MeasureLines(segments, 1000f, Measure), Is.EqualTo(1));
        Assert.That(MeasureLines(segments, 45f, Measure), Is.EqualTo(3));
    }

    [Test]
    public void MeasureLines_EmptyIsStillOneLine()
    {
        Assert.That(MeasureLines(Array.Empty<RuleSegment>(), 100f, Measure), Is.EqualTo(1));
    }
}
