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

    // ── #344: an outdated list is a different problem from an unimplemented rule ──────────────

    private static RuleDrop Outdated(string name, string owner = "unit 'Ripjawdactyl Riders'") =>
        new(name, owner, ERuleDropReason.OutdatedList,
            $"Skipping special rule '{name}' on {owner}: the current rulebook implements it, but this " +
            "saved list predates it and carries no definition for it - rebuild the list to pick it up.");

    [Test]
    public void Summarize_OutdatedDrops_GetTheirOwnLine_NotTheUnimplementedOne()
    {
        var lines = RuleLoadWarnings.Summarize(new List<RuleDrop>
        {
            Outdated("Heavy Impact(3)"),
            Outdated("Vengeance", "unit 'Royal Guard'"),
        });

        Assert.That(lines, Has.Count.EqualTo(1));
        Assert.That(lines[0], Does.StartWith(
            "2 special rules on the loaded armies predate the current rulebook and will do nothing " +
            "until the list is rebuilt: "));
        Assert.That(lines[0], Does.Contain("Heavy Impact(3), Vengeance"));
        Assert.That(lines[0], Does.Not.Contain("not implemented"),
            "these rules ARE implemented - claiming otherwise is the bug this reason exists to fix.");
    }

    [Test]
    public void Summarize_OutdatedDrops_AreNotCountedAsMisauthored()
    {
        var lines = RuleLoadWarnings.Summarize(new List<RuleDrop> { Outdated("Vengeance") });

        Assert.That(lines, Has.Count.EqualTo(1), "an outdated list is not a misauthored reference.");
        Assert.That(lines[0], Does.Not.Contain("misauthored"));
    }

    [Test]
    public void Summarize_BothKinds_ReportSeparately()
    {
        var lines = RuleLoadWarnings.Summarize(new List<RuleDrop>
        {
            Unimplemented("Wolfborn"),
            Outdated("Vengeance"),
            Misauthored("Stealth", ERuleDropReason.WrongScope),
        });

        Assert.That(lines, Has.Count.EqualTo(3));
        Assert.That(lines[0], Does.Contain("not implemented"));
        Assert.That(lines[1], Does.Contain("predates the current rulebook"));
        Assert.That(lines[2], Does.Contain("misauthored"));
    }

    [Test]
    public void SummarizeOutdated_SingularCopy_AndCallersSubject()
    {
        string? line = RuleLoadWarnings.SummarizeOutdated(
            new List<RuleDrop> { Outdated("Vengeance") }, "this list");

        Assert.That(line, Is.EqualTo("1 special rule on this list predates the current rulebook and " +
                                     "will do nothing until the list is rebuilt: Vengeance."));
    }

    [Test]
    public void SummarizeOutdated_NoOutdatedDrops_IsNull()
    {
        Assert.That(RuleLoadWarnings.SummarizeOutdated(
            new List<RuleDrop> { Unimplemented("Wolfborn") }, "this list"), Is.Null);
    }

    [Test]
    public void AllSummaryCopy_IsAsciiOnly()
    {
        // Game text is ASCII-only (font atlas bakes Basic Latin + Latin-1; beyond U+00FF renders '?').
        var drops = new List<RuleDrop>
        {
            Unimplemented("Wolfborn"),
            Unimplemented("Chrono Field"),
            Outdated("Vengeance"),
            Misauthored("Stealth", ERuleDropReason.WrongScope),
        };

        foreach (string line in RuleLoadWarnings.Summarize(drops)
                     .Append(RuleLoadWarnings.SummarizeUnimplemented(drops, "this list")!)
                     .Append(RuleLoadWarnings.SummarizeOutdated(drops, "this list")!))
        {
            Assert.That(line.All(c => c <= 0x7F), Is.True, $"non-ASCII character in: {line}");
        }
    }
}
