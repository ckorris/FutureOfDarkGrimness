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
/// #354 — the engine's <see cref="ICurrentRulebook"/> over the bundled rulebook assets
/// (<c>Assets/Books</c>). Installed once at startup in every mode; army load uses it to fill in rule
/// definitions a saved list is too old to carry, and to tell an outdated list apart from a genuinely
/// unimplemented rule name.
/// </summary>
public sealed class BundledBookRulebook : ICurrentRulebook
{
    private static readonly SpecialRuleDefinition[] None = Array.Empty<SpecialRuleDefinition>();

    private readonly object _lock = new();

    // Per (faction, game system), cached including the empty result — a list whose faction matches no
    // bundled book must not re-walk the book files on every load. The system is part of the key because
    // four AoF faction names collide with GDF factions (#378); an absent system means GDF.
    private readonly Dictionary<string, IReadOnlyList<SpecialRuleDefinition>> _byFaction =
        new(StringComparer.OrdinalIgnoreCase);

    private HashSet<string>? _knownNames;

    /// <summary>Installs this source unless the host already installed one. Idempotent.</summary>
    public static void Install() => CurrentRulebook.Installed ??= new BundledBookRulebook();

    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    public IReadOnlyList<SpecialRuleDefinition> DefinitionsForFaction(string faction, string? gameSystem)
    {
        lock (_lock)
        {
            string key = $"{GameSystems.Normalize(gameSystem)}|{faction}";
            if (_byFaction.TryGetValue(key, out IReadOnlyList<SpecialRuleDefinition>? cached))
            {
                return cached;
            }

            IReadOnlyList<SpecialRuleDefinition> definitions = LoadFactionDefinitions(faction, gameSystem);
            _byFaction[key] = definitions;
            return definitions;
        }
    }

    /// <summary>
    /// Answered from the bundled supplement files (<see cref="RuleSupplementSet.BundledFileNames"/>)
    /// rather than by walking all the books: the supplements are the layer every book's definitions are
    /// stamped FROM, so their names cover every book definition except a handful of per-book
    /// "... Effect" helpers, and those are only ever granted by another rule - never named as a list
    /// rule entry, so they cannot reach this question. Small files instead of ~7 MB of book JSON, on a
    /// path that only runs when a reference failed to resolve.
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
    // Faction, but a hand-authored list may name the book instead — within the army's game system
    // (#378: absent means GDF on both sides, so pre-#378 armies keep finding their GDF books). Same
    // tolerance for a malformed book as the Forge screen and ArmyForgeShareService: skip it, never
    // fail the load.
    private static IReadOnlyList<SpecialRuleDefinition> LoadFactionDefinitions(string faction, string? gameSystem)
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
            if (!GameSystems.SameSystem(book.GameSystem, gameSystem)) continue;

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
        foreach (string fileName in RuleSupplementSet.BundledFileNames)
        {
            string path = Path.Combine(BooksDirectory, fileName);
            if (!File.Exists(path)) continue;

            try
            {
                foreach (SpecialRuleDefinition definition in BookRuleSupplement.LoadDefinitions(File.ReadAllText(path)))
                    names.Add(definition.Name);
            }
            catch
            {
                // A malformed supplement costs the outdated-vs-unimplemented distinction, nothing more.
            }
        }

        return names;
    }
}
