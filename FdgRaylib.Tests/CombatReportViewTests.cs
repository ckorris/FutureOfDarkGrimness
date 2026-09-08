using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using FDG;
using FDG.Calculator;
using FDG.Rules.Dispatch;
using FdgRaylib.Rendering.CombatCalc;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// #398: the results pane's formatting. The pane itself is ImGui and cannot be asserted, but every
/// string it draws is now produced by <see cref="CombatReportView"/>, which can be - so the things that
/// were wrong on screen (three number formats, a blank tag line, two "Save 5+" rows the reader had to
/// add up) are pinned here rather than re-checked by eye.
/// </summary>
[TestFixture]
public class CombatReportViewTests
{
    private sealed class FakeWeapon : IWeapon
    {
        public string Name { get; init; } = "Rifle";
        public float RangeInches { get; init; } = 24f;
        public int Attacks { get; init; } = 1;
        public int ArmorPenetration { get; init; }
        public IReadOnlyList<ResolvedRule> RuleDefinitions { get; init; } = new List<ResolvedRule>();
        public string? EffectKey => null;
    }

    private static VolleyReport Volley(
        float dice = 10f, int hitNeeded = 5, float hits = 3.33f, float wounds = 1.67f,
        IReadOnlyList<SaveBucket>? saves = null, IReadOnlyList<string>? hitTags = null,
        IReadOnlyList<string>? saveTags = null, bool inRange = true, float effectiveRange = 24f,
        int copies = 10) =>
        new(new FakeWeapon(), copies, inRange, effectiveRange, dice, hitNeeded,
            hitTags ?? new List<string>(), hits,
            saves ?? new List<SaveBucket> { new(4, hits, string.Empty) },
            saveTags ?? new List<string>(), wounds, new List<string>());

    private static CombatReport Report(params VolleyReport[] volleys) =>
        new(ECombatMode.Shooting, "Infantry Squad", "Hive Warriors", 9f, 7.33f,
            volleys.ToList(), 3.33f, 1.67f, new List<string>(), new List<string>());

    [Test]
    public void EveryNumberCarriesTwoDecimals_SoTheColumnScansAsAColumn()
    {
        CombatReportView view = CombatReportView.From(Report(Volley(dice: 6f, hits: 6f, wounds: 2.78f)));

        Assert.Multiple(() =>
        {
            Assert.That(view.Rows[0].Dice, Is.EqualTo("6.00"), "a whole number still shows its decimals");
            Assert.That(view.Rows[0].Wounds, Is.EqualTo("2.78"));
            Assert.That(view.HitsValue, Is.EqualTo("3.33"));
        });
    }

    [Test]
    public void HeadlineNamesTheDirectionOfTheAttackInWords()
    {
        CombatReportView view = CombatReportView.From(Report(Volley()));

        Assert.That(view.Headline, Is.EqualTo("Infantry Squad attacks Hive Warriors"));
    }

    [Test]
    public void BucketsNeedingTheSameRollBecomeOneLineWithTheirSourcesNamed()
    {
        // The screenshot's case: Furious adds hits saved at the SAME threshold, and the old pane drew
        // "Save 5+ 1.5 hits" then "Save 5+ 0.5 hits [Ferocious]" for the reader to add up.
        var view = VolleyRowView.From(Volley(saves: new List<SaveBucket>
        {
            new(5, 1.5f, string.Empty),
            new(5, 0.5f, "Ferocious"),
        }));

        Assert.Multiple(() =>
        {
            Assert.That(view.SaveLines, Has.Count.EqualTo(1), "one threshold, one line");
            Assert.That(view.SaveLines[0].Hits, Is.EqualTo(2.0f).Within(0.001f), "1.5 + 0.5 added for them");
            Assert.That(view.SaveLines[0].Describe(), Is.EqualTo("2.00 hits (Ferocious)"));
            Assert.That(view.Save, Is.EqualTo("5+"), "the summary cell shows the single roll");
        });
    }

    [Test]
    public void GenuinelyDifferentThresholdsStayApart()
    {
        // Rending really is a second save. Merging those would be a lie, not a tidy-up.
        var view = VolleyRowView.From(Volley(saves: new List<SaveBucket>
        {
            new(5, 2f, string.Empty),
            new(6, 1f, "Rending"),
        }));

        Assert.Multiple(() =>
        {
            Assert.That(view.SaveLines, Has.Count.EqualTo(2));
            Assert.That(view.Save, Is.EqualTo("5+ / 6+"));
            Assert.That(view.SaveLines[1].Describe(), Is.EqualTo("1.00 hits (Rending)"));
        });
    }

    [Test]
    public void MergedBucketsKeepFirstAppearanceOrderAndDoNotRepeatASource()
    {
        var view = VolleyRowView.From(Volley(saves: new List<SaveBucket>
        {
            new(6, 1f, "Rending"),
            new(4, 2f, string.Empty),
            new(6, 0.5f, "Rending"),
        }));

        Assert.Multiple(() =>
        {
            Assert.That(view.SaveLines.Select(s => s.SaveNeeded), Is.EqualTo(new[] { 6, 4 }),
                "order follows the report, not a sort");
            Assert.That(view.SaveLines[0].Hits, Is.EqualTo(1.5f).Within(0.001f));
            Assert.That(view.SaveLines[0].Sources, Is.EqualTo(new[] { "Rending" }), "named once, not twice");
        });
    }

    [Test]
    public void EmptyTagsProduceNoChipsAtAll()
    {
        // The blank line in the screenshot: the pane drew the tag line whether or not there were tags.
        var view = VolleyRowView.From(Volley(
            hitTags: new List<string> { "Quality 4+", "  ", string.Empty },
            saveTags: new List<string>()));

        Assert.Multiple(() =>
        {
            Assert.That(view.HitChips, Is.EqualTo(new[] { "Quality 4+" }), "blank entries dropped");
            Assert.That(view.SaveChips, Is.Empty);
        });
    }

    [Test]
    public void OutOfRangeRowSaysHowFarTheWeaponActuallyReaches()
    {
        var view = VolleyRowView.From(Volley(inRange: false, effectiveRange: 36f));

        Assert.That(view.OutOfRangeText, Is.EqualTo("out of range - reaches 36in"));
    }

    [Test]
    public void WoundBarReportsTheRemainingFractionAndReadsAsWords()
    {
        CombatReportView view = CombatReportView.From(Report(Volley()));

        Assert.Multiple(() =>
        {
            Assert.That(view.WoundBarText, Is.EqualTo("7.33 of 9.00 wounds remain"));
            Assert.That(view.WoundFractionRemaining, Is.EqualTo(7.33f / 9f).Within(0.001f));
        });
    }

    [Test]
    public void ADefenderWipedOutLeavesAnEmptyBarRatherThanDividingByZero()
    {
        var report = new CombatReport(ECombatMode.Shooting, "A", "B", 0f, 0f,
            new List<VolleyReport>(), 0f, 0f, new List<string>(), new List<string>());

        Assert.That(CombatReportView.From(report).WoundFractionRemaining, Is.EqualTo(0f));
    }

    [Test]
    public void CopiesPrefixOnlyAppearsWhenThereIsMoreThanOne()
    {
        Assert.Multiple(() =>
        {
            Assert.That(VolleyRowView.From(Volley(copies: 10)).CopiesPrefix, Is.EqualTo("10x "));
            Assert.That(VolleyRowView.From(Volley(copies: 1)).CopiesPrefix, Is.Empty);
        });
    }

    [Test]
    public void NumbersDoNotFollowTheMachinesLocale()
    {
        // A comma decimal separator would put "2,78" on screen and break the ASCII-simple layout the
        // columns assume. Pinned because the app runs on whatever locale the player has.
        CultureInfo original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            Assert.That(CombatReportView.Num(2.78f), Is.EqualTo("2.78"));
            Assert.That(CombatReportView.Inches(7.5f), Is.EqualTo("7.5in"));
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }
}
