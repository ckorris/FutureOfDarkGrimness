using System;
using System.Collections.Generic;
using System.Numerics;
using FDG;
using FDG.Rules.Dispatch;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// #292 — in-game sibling of <see cref="RuleTextFlow"/>: rule names inside a stat line get their own
/// underline and their own hover tooltip, the way the Army Forge explains rules, but for the LIVE rules
/// on a weapon rather than the army-file entries the Forge edits.
///
/// <para>Two things differ from <see cref="RuleTextFlow"/>, which is why this is a sibling rather than an
/// overload of it. First, the rule type: in play a rule is a <see cref="ResolvedRule"/> that already
/// carries its own <see cref="FDG.Rules.Definitions.SpecialRuleDefinition.Description"/>, so there is no
/// <see cref="RuleGlossary"/> lookup to do — the description travels with the rule. Second, the drawing
/// mode: the shoot panel paints its weapon rows straight onto a draw list at computed offsets (the rows
/// are one invisible <c>Selectable</c> with text overlaid), so nothing here may touch the ImGui cursor or
/// reserve layout space, which is exactly what <see cref="RuleTextFlow.Draw"/> is built to do.</para>
///
/// <para>The VISUAL convention is deliberately identical to #259's: solid underline for a documented
/// rule, faded underline for one the engine will not resolve, and a wrapped "Name\ndescription" tooltip.
/// A player should not have to learn two vocabularies for the same idea.</para>
/// </summary>
public static class RuleHoverText
{
    /// <summary>What the tooltip says about a rule with no authored description. Mirrors
    /// <see cref="RuleGlossary.UnknownRuleText"/> — same fact, in-game phrasing.</summary>
    public const string UnknownRuleText =
        "No description available - this rule is not enforced by the engine and does nothing in play.";

    /// <summary>One run of a stat line: literal text, or a rule name that gets an underline and a
    /// tooltip. Concatenating every segment's <see cref="Text"/> reproduces the line verbatim.</summary>
    public readonly record struct Segment(string Text, string? RuleName, string? Description)
    {
        public bool IsRule => RuleName is not null;

        /// <summary>Whether the rule is documented — drives the underline's weight.</summary>
        public bool IsDocumented => !string.IsNullOrWhiteSpace(Description);
    }

    /// <summary>
    /// The shoot panel's compact weapon subline - <c>24", A6 AP1, Rending, Blast(3)</c> - with each rule
    /// name split out as its own hoverable segment. Byte-identical to what the panel printed before, so
    /// the only change on screen is the underlines.
    /// </summary>
    public static IReadOnlyList<Segment> WeaponStatLine(IWeapon weapon)
    {
        var segments = new List<Segment>
        {
            new($"{weapon.RangeInches}\", A{weapon.Attacks} AP{weapon.ArmorPenetration}", null, null),
        };
        foreach (Segment rule in RuleSegments(weapon))
        {
            segments.Add(new Segment(", ", null, null));
            segments.Add(rule);
        }
        return segments;
    }

    /// <summary>One segment per special rule on <paramref name="weapon"/>, in attachment order. The
    /// RESOLVED name is used (army load formats it "Tough(2)"), matching every other rule display.</summary>
    public static IReadOnlyList<Segment> RuleSegments(IWeapon weapon)
    {
        var segments = new List<Segment>();
        foreach (ResolvedRule rule in weapon.RuleDefinitions)
            segments.Add(new Segment(rule.RequestedName, rule.RequestedName, rule.Definition.Description));
        return segments;
    }

    /// <summary>The tooltip body for a rule: its name, then its description or the inert-in-play note.</summary>
    public static string Tooltip(Segment segment) =>
        $"{segment.RuleName}\n{(segment.IsDocumented ? segment.Description : UnknownRuleText)}";

    /// <summary>
    /// Draws <paramref name="segments"/> left to right starting at <paramref name="origin"/> (screen
    /// space) on <paramref name="drawList"/>, underlining each rule name, and returns the tooltip body
    /// for whichever rule the mouse is inside — or null. No wrapping, no cursor use, no layout space
    /// reserved: the caller owns the position entirely.
    /// </summary>
    /// <param name="ruleColor">Color for rule-name runs; pass the same value as
    /// <paramref name="textColor"/> to leave them looking like the rest of the line.</param>
    /// <param name="hoverEnabled">False suppresses hit-testing (e.g. the window isn't hovered, or another
    /// tooltip has already claimed the frame), so the underlines still draw but no tooltip is reported.</param>
    public static string? DrawInline(ImDrawListPtr drawList, Vector2 origin,
        IReadOnlyList<Segment> segments, uint textColor, uint ruleColor, bool hoverEnabled)
    {
        float lineHeight = ImGui.GetTextLineHeight();
        Vector2 mouse = ImGui.GetMousePos();
        string? tooltip = null;
        float x = 0f;

        foreach (Segment segment in segments)
        {
            float width = ImGui.CalcTextSize(segment.Text).X;
            Vector2 at = origin + new Vector2(x, 0f);
            drawList.AddText(at, segment.IsRule ? ruleColor : textColor, segment.Text);

            if (segment.IsRule)
            {
                // An unimplemented rule underlines faintly (#259): still hoverable - the tooltip says it
                // does nothing in play - but it must not advertise itself as documented.
                uint underline = segment.IsDocumented ? ruleColor : Fade(ruleColor, 0.45f);
                float baseline = at.Y + lineHeight - 1f;
                drawList.AddLine(new Vector2(at.X, baseline), new Vector2(at.X + width, baseline), underline);

                if (hoverEnabled &&
                    mouse.X >= at.X && mouse.X < at.X + width &&
                    mouse.Y >= at.Y && mouse.Y < at.Y + lineHeight)
                {
                    tooltip = Tooltip(segment);
                }
            }

            x += width;
        }

        return tooltip;
    }

    /// <summary>Raises a wrapped tooltip. Rule descriptions are full sentences; without a wrap they
    /// render as one very wide tooltip. Same wrap width as the Army Forge's.</summary>
    public static void ShowTooltip(string text)
    {
        ImGui.BeginTooltip();
        ImGui.PushTextWrapPos(ImGui.GetFontSize() * 28f);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.EndTooltip();
    }

    private static uint Fade(uint color, float alphaScale)
    {
        uint alpha = (color >> 24) & 0xFF;
        uint faded = (uint)Math.Clamp((int)MathF.Round(alpha * alphaScale), 0, 255);
        return (color & 0x00FFFFFF) | (faded << 24);
    }
}
