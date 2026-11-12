using System;
using System.Collections.Generic;
using System.Linq;
using FDG;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #292 — the shoot panel showed a weapon's stats but not what its special rules DO. Rule names on the
// weapon subline are now their own underlined, hoverable runs (the #259 Army Forge treatment), and the
// Details pane spells them out. The drawing is ImGui (hand-verified); these pin the segmentation, which
// is where a bug would silently change what the player reads.
[TestFixture]
public class RuleHoverTextTests
{
    // The invariant that makes this safe to drop into a live panel: segmenting the line must not change
    // one character of it. Only the underlines are new.
    [Test]
    public void WeaponStatLine_ReassemblesTheExactLineThePanelPrintedBefore()
    {
        Weapon weapon = WeaponWith("Heavy Rifle", 30f, 3, 2,
            Rule("Rending", "Unmodified 6s to hit ignore armor."),
            Rule("Deadly(3)", "Each wound counts as 3."));

        string line = Flatten(RuleHoverText.WeaponStatLine(weapon));

        Assert.That(line, Is.EqualTo($"{weapon.RangeInches}\", A3 AP2, Rending, Deadly(3)"));
    }

    [Test]
    public void WeaponStatLine_WithNoRules_IsJustTheStats()
    {
        Weapon weapon = WeaponWith("Rifle", 24f, 1, 0);

        IReadOnlyList<RuleHoverText.Segment> segments = RuleHoverText.WeaponStatLine(weapon);

        Assert.That(Flatten(segments), Is.EqualTo($"{weapon.RangeInches}\", A1 AP0"));
        Assert.That(segments.Any(s => s.IsRule), Is.False, "no rules, nothing to underline");
    }

    // Only the rule NAMES are hoverable - the stats and the separators between them must stay plain, or
    // the underline would run through ", " and read as one long rule name.
    [Test]
    public void OnlyTheRuleNamesAreHoverableRuns()
    {
        Weapon weapon = WeaponWith("Blade", 0f, 2, 1,
            Rule("Rending", "Unmodified 6s to hit ignore armor."));

        var rules = RuleHoverText.WeaponStatLine(weapon).Where(s => s.IsRule).ToList();

        Assert.That(rules.Select(s => s.Text), Is.EqualTo(new[] { "Rending" }));
        Assert.That(rules[0].RuleName, Is.EqualTo("Rending"));
    }

    // Army load formats a parameterized rule's REQUESTED name ("Tough(2)"), and that is what every other
    // rule display shows - re-deriving it from the definition would print a bare "Tough".
    [Test]
    public void UsesTheResolvedRequestedName_SoArgumentsSurvive()
    {
        Weapon weapon = WeaponWith("Cannon", 36f, 1, 4,
            new ResolvedRule("Blast(3)", Definition("Blast", "Hits are multiplied, up to the target size.")));

        RuleHoverText.Segment rule = RuleHoverText.RuleSegments(weapon).Single();

        Assert.That(rule.RuleName, Is.EqualTo("Blast(3)"), "the printed name keeps its argument");
        Assert.That(rule.Description, Does.StartWith("Hits are multiplied"),
            "while the description still comes from the definition it resolved to");
    }

    [Test]
    public void Tooltip_LeadsWithTheRuleName_ThenItsDescription()
    {
        Weapon weapon = WeaponWith("Blade", 0f, 2, 1, Rule("Rending", "Unmodified 6s ignore armor."));

        string tooltip = RuleHoverText.Tooltip(RuleHoverText.RuleSegments(weapon).Single());

        Assert.That(tooltip, Is.EqualTo("Rending\nUnmodified 6s ignore armor."));
    }

    // A rule the engine will not resolve is still hoverable - and must say so, rather than showing a
    // blank tooltip that reads as "this does something I'm not telling you about" (#259's convention).
    [Test]
    public void AnUndocumentedRule_IsHoverableAndSaysItDoesNothing()
    {
        Weapon weapon = WeaponWith("Odd Gun", 18f, 1, 0, Rule("Repel Ambushers", ""));

        RuleHoverText.Segment rule = RuleHoverText.RuleSegments(weapon).Single();

        Assert.That(rule.IsDocumented, Is.False, "a faded underline, not a solid one");
        Assert.That(RuleHoverText.Tooltip(rule),
            Is.EqualTo($"Repel Ambushers\n{RuleHoverText.UnknownRuleText}"));
        Assert.That(RuleHoverText.UnknownRuleText, Does.Contain("does nothing in play"));
    }

    [Test]
    public void RuleSegments_KeepAttachmentOrder()
    {
        Weapon weapon = WeaponWith("Multitool", 12f, 4, 1,
            Rule("Rending", "a"), Rule("Reliable", "b"), Rule("Deadly(2)", "c"));

        Assert.That(RuleHoverText.RuleSegments(weapon).Select(s => s.RuleName),
            Is.EqualTo(new[] { "Rending", "Reliable", "Deadly(2)" }));
    }

    private static string Flatten(IReadOnlyList<RuleHoverText.Segment> segments) =>
        string.Concat(segments.Select(s => s.Text));

    private static SpecialRuleDefinition Definition(string name, string description) =>
        new(name, Array.Empty<HookEntry>(), Array.Empty<ActivatedAbility>(), Description: description);

    private static ResolvedRule Rule(string name, string description) =>
        new(name, Definition(name, description));

    private static Weapon WeaponWith(string name, float range, int attacks, int ap,
        params ResolvedRule[] rules)
    {
        var weapon = new Weapon(name, range, attacks, ap);
        foreach (ResolvedRule rule in rules) weapon.AttachRuleDefinition(rule);
        return weapon;
    }
}
