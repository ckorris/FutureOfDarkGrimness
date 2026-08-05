using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Serialization;
using FDG.SaveLoad;

namespace FdgRaylib.Import;

/// <summary>
/// #342 — the engine's <see cref="ICurrentRulebook"/> over the bundled rulebook assets
/// (<c>Assets/Books</c>). Installed once at startup in every mode; army load uses it to fill in rule
/// definitions a saved list is too old to carry, and to tell an outdated list apart from a genuinely
/// unimplemented rule name.
/// </summary>
public sealed class BundledBookRulebook : ICurrentRulebook
{
    private static readonly SpecialRuleDefinition[] None = Array.Empty<SpecialRuleDefinition>();

    private readonly object _lock = new();

    // Per faction, cached including the empty result — a list whose faction matches no bundled book
    // must not re-walk 47 files on every load.
    private readonly Dictionary<string, IReadOnlyList<SpecialRuleDefinition>> _byFaction =
        new(StringComparer.OrdinalIgnoreCase);

    private HashSet<string>? _knownNames;

    /// <summary>Installs this source unless the host already installed one. Idempotent.</summary>
    public static void Install() => CurrentRulebook.Installed ??= new BundledBookRulebook();

    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    public IReadOnlyList<SpecialRuleDefinition> DefinitionsForFaction(string faction)
    {
        lock (_lock)
        {
            if (_byFaction.TryGetValue(faction, out IReadOnlyList<SpecialRuleDefinition>? cached))
            {
                return cached;
            }

            IReadOnlyList<SpecialRuleDefinition> definitions = LoadFactionDefinitions(faction);
            _byFaction[faction] = definitions;
            return definitions;
        }
    }

    /// <summary>
    /// Answered from <c>GdfRuleSupplement.json</c> rather than by walking all 47 books: the supplement
    /// is the layer every book's definitions are stamped FROM, so its names cover every book definition
    /// except a handful of per-book "... Effect" helpers, and those are only ever granted by another
    /// rule - never named as a list rule entry, so they cannot reach this question. One small file
    /// instead of ~7 MB of book JSON, on a path that only runs when a reference failed to resolve.
    /// </summary>
    public bool Defines(string ruleName)
    {
        lock (_lock)
        {
            _knownNames ??= LoadSupplementNames();
            return _knownNames.Contains(ruleName);
        }
    }

    // Matches on the book's Faction or its Name — a compiled army's Faction is copied from the book's
    // Faction, but a hand-authored list may name the book instead. Same tolerance for a malformed book
    // as the Forge screen and ArmyForgeShareService: skip it, never fail the load.
    private static IReadOnlyList<SpecialRuleDefinition> LoadFactionDefinitions(string faction)
    {
        if (string.IsNullOrWhiteSpace(faction) || !Directory.Exists(BooksDirectory))
        {
            return None;
        }

        foreach (string path in Directory.EnumerateFiles(BooksDirectory, "*" + BookFile.EXTENSION_WITH_PERIOD)
                     .OrderBy(p => p, StringComparer.Ordinal))
        {
            BookFile? book;
            try
            {
                book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options);
            }
            catch
            {
                continue;
            }

            if (book == null) continue;

            if (string.Equals(book.Faction, faction, StringComparison.OrdinalIgnoreCase) ||
                string.Equals(book.Name, faction, StringComparison.OrdinalIgnoreCase))
            {
                return book.RuleDefinitions.Count == 0 ? None : book.RuleDefinitions;
            }
        }

        return None;
    }

    private static HashSet<string> LoadSupplementNames()
    {
        HashSet<string> names = new(StringComparer.OrdinalIgnoreCase);
        string path = Path.Combine(BooksDirectory, "GdfRuleSupplement.json");
        if (!File.Exists(path)) return names;

        try
        {
            foreach (SpecialRuleDefinition definition in BookRuleSupplement.LoadDefinitions(File.ReadAllText(path)))
                names.Add(definition.Name);
        }
        catch
        {
            // A malformed supplement costs the outdated-vs-unimplemented distinction, nothing more.
        }

        return names;
    }
}
