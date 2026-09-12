using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FDG.Calculator;
using FDG;

namespace FdgRaylib.Rendering.CombatCalc;

/// <summary>
/// #398: the presentation shape of a <see cref="CombatReport"/> - every string the results pane draws,
/// worked out here rather than inline in the ImGui code.
///
/// <para>This exists because the pane's problems were formatting problems, and formatting is testable
/// while ImGui drawing is not. Everything that used to be an interpolated string mid-draw - the number
/// format, the merged save buckets, the empty-tag-line suppression, the out-of-range wording - is a
/// pure function of the report and is pinned by <c>CombatReportViewTests</c>. The draw code becomes a
/// dumb painter of these fields.</para>
///
/// <para>It never computes combat arithmetic. Every number here is a <see cref="CombatReport"/> field
/// rendered to text, or a SUM of report fields that the report itself already split apart (the save
/// buckets); no probability, threshold or modifier is derived. That line matters: the whole point of
/// #397 is that the figures come from the real stage chain.</para>
/// </summary>
internal sealed record CombatReportView(
    string Headline,
    string HitsValue,
    string WoundsValue,
    string HealthValue,
    string PointsValue,
    string WoundBarText,
    float WoundFractionRemaining,
    IReadOnlyList<VolleyRowView> Rows,
    IReadOnlyList<string> Warnings,
    IReadOnlyList<string> Notes)
{
    internal const string HitsCaption = "Expected hits";
    internal const string WoundsCaption = "Expected wounds";
    internal const string HealthCaption = "Health removed";
    internal const string PointsCaption = "Points of damage";
    internal const string AttacksVerb = "attacks";

    /// <summary>
    /// Two decimals everywhere, including whole numbers. The pane is a column of figures the eye scans
    /// down and compares; "6" next to "2.78" breaks that alignment for no gain, and an expected value
    /// is a fraction by nature - showing "6" implies a certainty the number does not carry.
    /// </summary>
    internal static string Num(float value) => value.ToString("0.00", CultureInfo.InvariantCulture);

    /// <summary>Inches with no trailing noise: 24in, 7.5in.</summary>
    internal static string Inches(float value) => value.ToString("0.##", CultureInfo.InvariantCulture) + "in";

    /// <summary>A share of the defender's wounds, as a percentage: "18.6%". One decimal, because a
    /// single expected wound off a 30-wound monster is 3.3% and "3%" throws away the difference between
    /// that and half of it.</summary>
    internal static string Percent(float fraction) =>
        (fraction * 100f).ToString("0.#", CultureInfo.InvariantCulture) + "%";

    /// <summary>Points, trimmed: "30", "24.4".</summary>
    internal static string Points(float points) => points.ToString("0.#", CultureInfo.InvariantCulture);

    /// <summary>
    /// The share of the defender's wounds an attack removes. Damage in wounds does not say whether it
    /// mattered - two wounds is a squad wiped or a scratch on a monster - and this is the figure that
    /// does. A pure ratio of two numbers the report already carries.
    /// </summary>
    internal static float HealthShare(float wounds, float woundsBefore) =>
        woundsBefore <= 0f ? 0f : System.Math.Clamp(wounds / woundsBefore, 0f, 1f);

    /// <summary>
    /// The same share, priced: wounds taken off a 150-point, 10-wound unit at 2 wounds is 30 points of
    /// damage. It is what makes two loadouts comparable when their targets are not - the currency the
    /// whole game is costed in.
    /// </summary>
    internal static float PointsDealt(float wounds, float woundsBefore, int defenderPoints) =>
        HealthShare(wounds, woundsBefore) * defenderPoints;

    /// <param name="defenderPoints">What the defending unit costs. Not on the
    /// <see cref="CombatReport"/>: points are a list-building fact the screen already holds, and the
    /// report is about what the combat stages did.</param>
    internal static CombatReportView From(CombatReport report, int defenderPoints)
    {
        float before = report.DefenderWoundsBefore;
        float after = report.DefenderWoundsAfter;

        return new CombatReportView(
            Headline: $"{report.AttackerName} {AttacksVerb} {report.DefenderName}",
            HitsValue: Num(report.ExpectedHits),
            WoundsValue: Num(report.ExpectedWounds),
            HealthValue: Percent(HealthShare(report.ExpectedWounds, before)),
            PointsValue: Points(PointsDealt(report.ExpectedWounds, before, defenderPoints)),
            WoundBarText: $"{Num(after)} of {Num(before)} wounds remain",
            WoundFractionRemaining: before <= 0f ? 0f : System.Math.Clamp(after / before, 0f, 1f),
            // Ascending by reach, so the rows drop out of the table in the order the distance slider
            // walks past them, and the shortest-ranged weapon - the one that decides how close you have
            // to get - is the first thing read. OrderBy is stable, so equal reaches keep the engine's
            // profile order (and a melee report, where every reach is 0, is left exactly as it came).
            Rows: report.Volleys
                .OrderBy(volley => volley.EffectiveRangeInches)
                .Select(volley => VolleyRowView.From(volley, before, defenderPoints))
                .ToList(),
            Warnings: report.Warnings,
            Notes: report.Notes);
    }
}

/// <summary>One weapon's line in the results table, already in rules order: dice -> hit -> hits -> save
/// -> wounds.</summary>
internal sealed record VolleyRowView(
    IWeapon Weapon,
    int Copies,
    string CopiesPrefix,
    bool InRange,
    string OutOfRangeText,
    string Dice,
    string Hit,
    string Hits,
    string Save,
    string Wounds,
    string Health,
    string Points,
    IReadOnlyList<string> HitChips,
    IReadOnlyList<string> SaveChips,
    IReadOnlyList<SaveLineView> SaveLines,
    IReadOnlyList<string> Notes)
{
    internal static VolleyRowView From(VolleyReport volley, float defenderWounds, int defenderPoints)
    {
        IReadOnlyList<SaveLineView> saves = MergeSaves(volley.Saves);

        return new VolleyRowView(
            Weapon: volley.Weapon,
            Copies: volley.Copies,
            CopiesPrefix: volley.Copies > 1 ? $"{volley.Copies}x " : string.Empty,
            InRange: volley.InRange,
            OutOfRangeText: $"Out of range - reaches {CombatReportView.Inches(volley.EffectiveRangeInches)}",
            Dice: CombatReportView.Num(volley.AttackDice),
            Hit: $"{volley.HitRollNeeded}+",
            Hits: CombatReportView.Num(volley.ExpectedHits),
            Save: SaveSummary(saves),
            Wounds: CombatReportView.Num(volley.ExpectedWounds),
            Health: CombatReportView.Percent(
                CombatReportView.HealthShare(volley.ExpectedWounds, defenderWounds)),
            Points: CombatReportView.Points(
                CombatReportView.PointsDealt(volley.ExpectedWounds, defenderWounds, defenderPoints)),
            // An empty tag list draws nothing at all. The old pane emitted the line regardless, so a
            // volley with no save modifiers left a blank gap where the tags would have been.
            HitChips: volley.HitTags.Where(t => !string.IsNullOrWhiteSpace(t)).ToList(),
            SaveChips: volley.SaveTags.Where(t => !string.IsNullOrWhiteSpace(t)).ToList(),
            SaveLines: saves,
            Notes: volley.Notes);
    }

    /// <summary>
    /// Buckets that need the same roll are one line. The engine splits a volley whenever a per-hit rule
    /// peels hits off (Rending) or injects them (Furious), and those two cases look identical in the
    /// report but read very differently: Rending really is a second threshold, while Furious is extra
    /// hits saved at the SAME number. Showing "Save 5+ 1.5 hits" then "Save 5+ 0.5 hits [Ferocious]"
    /// asks the reader to add 1.5 and 0.5 themselves and invites them to think two different saves are
    /// being rolled. Merging is pure addition of numbers the report already produced.
    /// </summary>
    internal static IReadOnlyList<SaveLineView> MergeSaves(IReadOnlyList<SaveBucket> buckets)
    {
        var order = new List<int>();
        var hits = new Dictionary<int, float>();
        var sources = new Dictionary<int, List<string>>();

        foreach (SaveBucket bucket in buckets)
        {
            if (!hits.ContainsKey(bucket.SaveNeeded))
            {
                order.Add(bucket.SaveNeeded);
                hits[bucket.SaveNeeded] = 0f;
                sources[bucket.SaveNeeded] = new List<string>();
            }
            hits[bucket.SaveNeeded] += bucket.Hits;
            if (!string.IsNullOrWhiteSpace(bucket.Label) && !sources[bucket.SaveNeeded].Contains(bucket.Label))
                sources[bucket.SaveNeeded].Add(bucket.Label);
        }

        return order.Select(n => new SaveLineView(n, hits[n], sources[n])).ToList();
    }

    /// <summary>The save cell: one threshold, or every distinct one when a rule really did split them.</summary>
    private static string SaveSummary(IReadOnlyList<SaveLineView> saves) =>
        saves.Count == 0 ? "-" : string.Join(" / ", saves.Select(s => $"{s.SaveNeeded}+"));
}

/// <summary>One save threshold within a volley, with whatever rules contributed hits to it.</summary>
internal sealed record SaveLineView(int SaveNeeded, float Hits, IReadOnlyList<string> Sources)
{
    /// <summary>"1.50 hits" or "2.00 hits (Ferocious)" - the sources named, never bracket-jargon.</summary>
    internal string Describe() =>
        Sources.Count == 0
            ? $"{CombatReportView.Num(Hits)} hits"
            : $"{CombatReportView.Num(Hits)} hits ({string.Join(", ", Sources)})";
}
