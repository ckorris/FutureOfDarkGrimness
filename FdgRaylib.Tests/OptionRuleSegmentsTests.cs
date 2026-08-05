using System.Collections.Generic;
using System.Linq;
using FDG;
using FDG.StageResolution.Requests;
using FdgRaylib.Rendering;
using FdgRaylib.Rendering.Resolvers;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #336: the melee weapon menu used to print every rule's full text under every button (#298), which is
// not how the shoot panel, the Army Forge or the army list explain a rule - they underline the NAME where
// it already sits and hover the text. The engine now sends the rules structured, and this locates each
// name inside the finished option label so the button can get that same treatment. The drawing is ImGui
// (hand-verified); these pin the splitting and the wrapping, which is where it can silently go wrong.
[TestFixture]
public class OptionRuleSegmentsTests
{
    // Every test measures 10px per character, so widths are countable by hand.
    private static float Measure(string text) => text.Length * 10f;

    private static StringSelectionRequest.OptionRule Rule(string name, string? description = "does a thing")
        => new StringSelectionRequest.OptionRule(name, description);

    private static string Rebuild(IEnumerable<RuleHoverText.Segment> segments)
        => string.Concat(segments.Select(s => s.Text));

    // The label is the option's identity on the wire (#306) and is replied with verbatim, so whatever this
    // does to it must be reversible by concatenation. Everything else here depends on that.
    [Test]
    public void Segments_ConcatenateBackIntoTheLabel()
    {
        const string label = "2x Great Sword - A2, AP1, Rending, Deadly(3)";

        var segments = OptionRuleSegments.Build(label, new[] { Rule("Rending"), Rule("Deadly(3)") });

        Assert.That(Rebuild(segments), Is.EqualTo(label));
    }

    [Test]
    public void EachRuleName_BecomesItsOwnHoverableSegment()
    {
        const string label = "2x Great Sword - A2, AP1, Rending, Deadly(3)";

        var segments = OptionRuleSegments.Build(label,
            new[] { Rule("Rending", "Ignores armour."), Rule("Deadly(3)", "Multiplies wounds.") });

        var rules = segments.Where(s => s.IsRule).ToList();
        Assert.That(rules.Select(s => s.RuleName).ToArray(), Is.EqualTo(new[] { "Rending", "Deadly(3)" }));
        Assert.That(rules[0].Description, Is.EqualTo("Ignores armour."));
        Assert.That(rules.All(s => s.IsDocumented), Is.True);
    }

    // The shoot panel underlines an undocumented rule faintly and says so on hover, rather than hiding it.
    // IsDocumented is what drives that, so a null description must survive as a rule segment.
    [Test]
    public void UndocumentedRule_IsStillASegment_ButNotDocumented()
    {
        var segments = OptionRuleSegments.Build("1x Odd Blade - A1, AP0, Mysterious",
            new[] { Rule("Mysterious", null) });

        RuleHoverText.Segment rule = segments.Single(s => s.IsRule);
        Assert.That(rule.RuleName, Is.EqualTo("Mysterious"));
        Assert.That(rule.IsDocumented, Is.False);
    }

    // The reason the scan runs from the right: a weapon whose NAME contains a rule word would otherwise
    // have the name underlined instead of the rule at the end of the line.
    [Test]
    public void RuleNameAlsoAppearingInTheWeaponName_MatchesTheTrailingOne()
    {
        const string label = "2x Rending Blade - A2, AP0, Rending";

        var segments = OptionRuleSegments.Build(label, new[] { Rule("Rending") });

        Assert.That(Rebuild(segments), Is.EqualTo(label));
        Assert.That(segments[^1].IsRule, Is.True,
            "the rule is the LAST run of the label, not the first word of the weapon's name");
        Assert.That(segments.Count(s => s.IsRule), Is.EqualTo(1));
    }

    // Two rules that share a name prefix must not both land on the same run of text.
    [Test]
    public void RepeatedRuleName_TakesADistinctOccurrenceEachTime()
    {
        const string label = "2x Twin Blade - A2, AP0, Rending, Rending";

        var segments = OptionRuleSegments.Build(label, new[] { Rule("Rending"), Rule("Rending") });

        Assert.That(Rebuild(segments), Is.EqualTo(label));
        Assert.That(segments.Count(s => s.IsRule), Is.EqualTo(2));
    }

    // A name the engine sent that isn't actually in the label (it can't happen today - the same
    // GetWeaponNameAndStats builds both - but a wrong underline would be worse than a missing one).
    [Test]
    public void RuleNameMissingFromTheLabel_IsSkippedAndTheLabelSurvives()
    {
        const string label = "3x Blade - A2, AP0";

        var segments = OptionRuleSegments.Build(label, new[] { Rule("Rending") });

        Assert.That(Rebuild(segments), Is.EqualTo(label));
        Assert.That(segments.Any(s => s.IsRule), Is.False);
    }

    [Test]
    public void NoRules_YieldsTheWholeLabelAsOneLiteral()
    {
        var segments = OptionRuleSegments.Build("Advance", null);

        Assert.That(segments.Single().Text, Is.EqualTo("Advance"));
        Assert.That(segments.Single().IsRule, Is.False);
    }

    // A greyed row's label continues with the reason it is greyed, and a reason can repeat a rule's own
    // name word for word - "Already used this game (Limited)." on a weapon whose rule IS Limited. Matching
    // from the right would underline the reason. The resolver keeps the reason out of the scan entirely.
    [Test]
    public void GreyedRow_UnderlinesTheRuleInTheOption_NotTheOneInTheReason()
    {
        const string option = "1x Demo Charge - A1, AP0, Limited";
        const string tail = " (Already used this game (Limited).)";

        var row = new GuiStringSelectionResolver.MenuRow(option, option + tail, null, -1);
        var request = new StringSelectionRequest(
            new PlayerID(System.Guid.NewGuid()), "Choose weapon:",
            new List<string>(),
            new List<StringSelectionRequest.InvalidOption>
                { new StringSelectionRequest.InvalidOption(option, "Already used this game (Limited).") },
            optionRules: new Dictionary<string, List<StringSelectionRequest.OptionRule>>
                { [option] = new() { Rule("Limited", "Once per game.") } });

        GuiStringSelectionResolver.AttachRuleSegments(row, request, option, tail);

        Assert.That(Rebuild(row.Segments!), Is.EqualTo(option + tail), "the row still reads the same");
        RuleHoverText.Segment rule = row.Segments!.Single(s => s.IsRule);
        Assert.That(Rebuild(row.Segments!.TakeWhile(s => !s.IsRule)).Length,
            Is.EqualTo(option.Length - "Limited".Length),
            "the underlined run is the one at the end of the OPTION, before the reason begins");
        Assert.That(rule.Description, Is.EqualTo("Once per game."));
    }

    // ── wrapping ────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void Wrap_PreservesEveryCharacterAcrossTheLines()
    {
        const string label = "2x Great Sword - A2, AP1, Rending, Deadly(3)";
        var segments = OptionRuleSegments.Build(label, new[] { Rule("Rending"), Rule("Deadly(3)") });

        var lines = OptionRuleSegments.Wrap(segments, Measure, 200f);

        Assert.That(lines.Count, Is.GreaterThan(1), "43 characters at 10px cannot fit in 200px");
        Assert.That(string.Concat(lines.SelectMany(l => l).Select(s => s.Text)), Is.EqualTo(label));
    }

    // A rule name split across two lines would be two half-underlined runs and two half hover targets.
    [Test]
    public void Wrap_NeverBreaksInsideARuleName()
    {
        var segments = OptionRuleSegments.Build("2x Great Sword - A2, AP1, Rending, Deadly(3)",
            new[] { Rule("Rending"), Rule("Deadly(3)") });

        var lines = OptionRuleSegments.Wrap(segments, Measure, 120f);

        foreach (var line in lines)
        {
            foreach (RuleHoverText.Segment segment in line.Where(s => s.IsRule))
                Assert.That(segment.Text, Is.EqualTo(segment.RuleName), "a rule name is drawn whole");
        }
        Assert.That(lines.SelectMany(l => l).Count(s => s.IsRule), Is.EqualTo(2),
            "and appears exactly once, not once per line it straddles");
    }

    // Adjacent literal chunks are merged per line so the drawn line measures as one string; a line of
    // eight one-word segments accumulates eight roundings and drifts away from what was measured.
    [Test]
    public void Wrap_MergesAdjacentLiteralsBackIntoOneSegmentPerLine()
    {
        var segments = OptionRuleSegments.Build("2x Great Sword - A2, AP1, Rending", new[] { Rule("Rending") });

        var lines = OptionRuleSegments.Wrap(segments, Measure, 1000f);

        Assert.That(lines.Count, Is.EqualTo(1));
        Assert.That(lines[0].Count, Is.EqualTo(2), "one literal head, then the rule");
        Assert.That(lines[0][0].Text, Is.EqualTo("2x Great Sword - A2, AP1, "));
        Assert.That(lines[0][1].RuleName, Is.EqualTo("Rending"));
    }

    [Test]
    public void Wrap_AlwaysReturnsAtLeastOneLine()
    {
        var lines = OptionRuleSegments.Wrap(OptionRuleSegments.Build("", null), Measure, 100f);

        Assert.That(lines, Has.Count.EqualTo(1));
    }

    // A word wider than the whole row keeps its own overlong line rather than being split mid-word -
    // ResolverText.Wrap's long-standing behaviour, which this had to match to replace it on these rows.
    [Test]
    public void Wrap_AnOverlongWordGetsItsOwnLine()
    {
        var segments = OptionRuleSegments.Build("2x Interminablyverbosename - A1, AP0", null);

        var lines = OptionRuleSegments.Wrap(segments, Measure, 60f);

        Assert.That(lines.Any(l => Measure(string.Concat(l.Select(s => s.Text))) > 60f), Is.True);
        Assert.That(string.Concat(lines.SelectMany(l => l).Select(s => s.Text)),
            Is.EqualTo("2x Interminablyverbosename - A1, AP0"));
    }
}
