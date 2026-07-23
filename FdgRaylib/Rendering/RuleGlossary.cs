using System;
using System.Collections.Generic;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.SaveLoad;

namespace FdgRaylib.Rendering;

// #259 — name -> player-facing description for every special rule a book can reference, so the Army Forge
// can explain a rule on hover the way the real Army Forge does.
//
// No new data is authored here: SpecialRuleDefinition already carries Description (#151, written for the
// granted-rule token hovers), every CoreRuleCatalog definition has one, and every bundled .fdgbook embeds
// the faction-specific subset of GdfRuleSupplement.json (BookRuleSupplement.Apply) with descriptions
// attached. This class is just the lookup the screens were missing.
public sealed class RuleGlossary
{
    /// <summary>What the tooltip says about a rule the engine will not resolve. Same fact the #241 import
    /// modal reports as "RULES NOT ENFORCED BY THE ENGINE (inert in play)", surfaced per rule.</summary>
    public const string UnknownRuleText =
        "No description available - this rule is not enforced by the engine and does nothing in play.";

    private readonly Dictionary<string, string> _byName;

    private RuleGlossary(Dictionary<string, string> byName) => _byName = byName;

    /// <summary>An empty glossary (every lookup misses). For screens with no book in hand.</summary>
    public static RuleGlossary Empty { get; } =
        new(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

    /// <summary>
    /// Core catalog first, then the book's embedded definitions overriding by name — the same
    /// core-then-embedded precedence army load uses when it registers rules (<c>RegisterOrReplace</c>),
    /// so what the tooltip describes is the definition that will actually fire in play.
    /// </summary>
    /// <remarks>
    /// Lookup is case-INSENSITIVE, matching <c>RuleResolver</c>'s own <c>OrdinalIgnoreCase</c> dictionary
    /// (#100). The corpus is not consistent about casing - the books say "Bane in Melee" where the catalog
    /// says "Bane in melee" - and the engine resolves those to the same rule, so the tooltip must describe
    /// it rather than claim the rule does nothing. Same comparer as the resolver means the glossary is
    /// silent exactly when army load would also fail to resolve the name.
    /// </remarks>
    public static RuleGlossary Build(BookFile? book)
    {
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (SpecialRuleDefinition definition in CoreRuleCatalog.All)
            byName[definition.Name] = definition.Description;
        if (book is not null)
            foreach (SpecialRuleDefinition definition in book.RuleDefinitions)
                byName[definition.Name] = definition.Description;
        return new RuleGlossary(byName);
    }

    /// <summary>The description for a canonical rule name, or null when the name resolves to nothing (or to
    /// a definition that was authored without a description).</summary>
    public string? Describe(string ruleName) =>
        _byName.TryGetValue(ruleName, out string? description) && !string.IsNullOrWhiteSpace(description)
            ? description
            : null;

    /// <summary>
    /// The description for a rule as an army/book file carries it. Looks up the CANONICAL name rather than
    /// the printable one, so "Tough(3)" finds Tough; an alias ("Medical Training (Regeneration)") falls
    /// through to the rule it renames, since that is what actually describes its behavior.
    /// </summary>
    public string? Describe(SpecialRuleEntry entry) => entry switch
    {
        SpecialRuleEntry_Core core => Describe(core.Name),
        SpecialRuleEntry_CoreNumeric numeric => Describe(numeric.Name),
        SpecialRuleEntry_Alias alias => Describe(alias.Name) ?? Describe(alias.AliasedRule),
        _ => Describe(entry.PrintableName),
    };

    /// <summary>Tooltip body for a rule: its description, or the inert-in-play note when it has none.
    /// Prefixed with the rule's own printable name so the tooltip stands alone when several rules sit on
    /// one line.</summary>
    public string Tooltip(SpecialRuleEntry entry) => Compose(entry.PrintableName, Describe(entry));

    /// <inheritdoc cref="Tooltip(SpecialRuleEntry)"/>
    public string Tooltip(string ruleName) => Compose(ruleName, Describe(ruleName));

    private static string Compose(string printableName, string? description) =>
        $"{printableName}\n{description ?? UnknownRuleText}";
}
