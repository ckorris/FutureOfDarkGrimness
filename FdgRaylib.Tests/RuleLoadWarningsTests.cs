using FDG.Rules.Dispatch;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #168: the aggregated "N rules are not implemented" copy shown in the game log at launch and in the
// army builder's validation pane. Pure formatting - the parity between what these summarize and what
// army load actually drops is pinned engine-side (ArmyRuleAuditParityTests).
[TestFixture]
public class RuleLoadWarningsTests
{
    private static RuleDrop Unimplemented(string name, string owner = "unit 'Berserkers'") =>
        new(name, owner, ERuleDropReason.Unimplemented, $"Skipping unimplemented special rule '{name}' on {owner}.");

    private static RuleDrop Misauthored(string name, ERuleDropReason reason) =>
        new(name, "unit 'Berserkers'", reason, $"Skipping special rule '{name}'.");

    [Test]
    public void Summarize_NoDrops_NoLines()
    {
        Assert.That(RuleLoadWarnings.Summarize(new List<RuleDrop>()), Is.Empty);
    }

    [Test]
    public void Summarize_AggregatesDistinctNames_AndCountsReferences()
    {
        var drops = new List<RuleDrop>
        {
            Unimplemented("Wolfborn"),
            Unimplemented("Wolfborn", "unit 'Cultists'"), // same rule, second unit: one name, two refs
            Unimplemented("Chrono Field"),
        };

        var lines = RuleLoadWarnings.Summarize(drops);

        Assert.That(lines, Has.Count.EqualTo(1));
        Assert.That(lines[0], Does.StartWith(
            "2 special rules on the loaded armies are not implemented and will do nothing: "));
        Assert.That(lines[0], Does.Contain("Chrono Field, Wolfborn"), "names are sorted and deduplicated");
        Assert.That(lines[0], Does.Contain("(3 references)"));
        Assert.That(lines[0], Does.Contain("Details in the Debug log."));
    }

    [Test]
    public void Summarize_SingleRule_SingularCopy_NoReferenceCount()
    {
        var lines = RuleLoadWarnings.Summarize(new List<RuleDrop> { Unimplemented("Wolfborn") });

        Assert.That(lines, Has.Count.EqualTo(1));
        Assert.That(lines[0], Does.StartWith("1 special rule on the loaded armies is not implemented"));
        Assert.That(lines[0], Does.Not.Contain("references"),
            "one name, one reference - the count adds nothing.");
    }

    [Test]
    public void Summarize_MisauthoredDrops_GetTheirOwnCountLine()
    {
        var drops = new List<RuleDrop>
        {
            Unimplemented("Wolfborn"),
            Misauthored("Stealth", ERuleDropReason.WrongScope),
            Misauthored("Tough", ERuleDropReason.MissingArgument),
        };

        var lines = RuleLoadWarnings.Summarize(drops);

        Assert.That(lines, Has.Count.EqualTo(2));
        Assert.That(lines[1], Does.StartWith("2 rule reference(s) were dropped as misauthored"));
    }

    [Test]
    public void Summarize_OnlyMisauthored_NoUnimplementedLine()
    {
        var lines = RuleLoadWarnings.Summarize(new List<RuleDrop>
        {
            Misauthored("Bane in melee", ERuleDropReason.NoWeaponsToAttach),
        });

        Assert.That(lines, Has.Count.EqualTo(1));
        Assert.That(lines[0], Does.Contain("misauthored"));
    }

    [Test]
    public void SummarizeUnimplemented_UsesTheCallersSubject()
    {
        string? line = RuleLoadWarnings.SummarizeUnimplemented(
            new List<RuleDrop> { Unimplemented("Wolfborn") }, "this list");

        Assert.That(line, Is.EqualTo(
            "1 special rule on this list is not implemented and will do nothing: Wolfborn."));
    }

    [Test]
    public void SummarizeUnimplemented_CaseInsensitiveNameDedup()
    {
        string? line = RuleLoadWarnings.SummarizeUnimplemented(new List<RuleDrop>
        {
            Unimplemented("Wolfborn"),
            Unimplemented("wolfborn", "unit 'Cultists'"),
        }, "this list");

        Assert.That(line, Does.StartWith("1 special rule"),
            "book casing differences must not double-report a rule.");
    }

    [Test]
    public void AllSummaryCopy_IsAsciiOnly()
    {
        // Game text is ASCII-only (font atlas bakes Basic Latin + Latin-1; beyond U+00FF renders '?').
        var drops = new List<RuleDrop>
        {
            Unimplemented("Wolfborn"),
            Unimplemented("Chrono Field"),
            Misauthored("Stealth", ERuleDropReason.WrongScope),
        };

        foreach (string line in RuleLoadWarnings.Summarize(drops)
                     .Append(RuleLoadWarnings.SummarizeUnimplemented(drops, "this list")!))
        {
            Assert.That(line.All(c => c <= 0x7F), Is.True, $"non-ASCII character in: {line}");
        }
    }
}
