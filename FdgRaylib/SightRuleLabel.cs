namespace FdgRaylib;

/// <summary>
/// Composes player-facing attribution text for a weapon's sight-affecting special rules — naming the rule
/// responsible for ignoring cover and/or line of sight (e.g. "Blast ignores cover",
/// "Indirect ignores line of sight"). Shared by the CLI + GUI shooting resolvers and the GUI movement
/// targeting overlay so the wording stays consistent. The rule names are the alias-aware display names
/// surfaced on the resolver requests (#052), so an army that renames Blast shows its own name.
/// </summary>
public static class SightRuleLabel
{
    /// <summary>
    /// The attribution phrase for the given cover- and LoS-ignore rule names (either may be null), without
    /// surrounding parentheses — or null if neither rule applies. When one rule ignores both (e.g. Takedown),
    /// it's named once: "Takedown ignores cover and line of sight".
    /// </summary>
    public static string? Describe(string? coverIgnoreRule, string? lineOfSightIgnoreRule)
    {
        if (coverIgnoreRule == null && lineOfSightIgnoreRule == null) return null;

        if (coverIgnoreRule != null && coverIgnoreRule == lineOfSightIgnoreRule)
            return $"{coverIgnoreRule} ignores cover and line of sight";

        if (coverIgnoreRule != null && lineOfSightIgnoreRule != null)
            return $"{coverIgnoreRule} ignores cover; {lineOfSightIgnoreRule} ignores line of sight";

        if (coverIgnoreRule != null)
            return $"{coverIgnoreRule} ignores cover";

        return $"{lineOfSightIgnoreRule} ignores line of sight";
    }

    /// <summary>
    /// The attribution phrase wrapped as " (…)" suitable for appending to a label, or "" when none applies.
    /// </summary>
    public static string Parenthetical(string? coverIgnoreRule, string? lineOfSightIgnoreRule)
    {
        string? text = Describe(coverIgnoreRule, lineOfSightIgnoreRule);
        return text == null ? string.Empty : $" ({text})";
    }
}
