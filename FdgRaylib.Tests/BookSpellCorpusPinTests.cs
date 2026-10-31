using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #439 - the spell corpus of the bundled 3.6.0 books, pinned. Before this an unparsed spell was DROPPED at import with
// only a console warning and nothing counted spells, so a parser gap shipped as a book quietly missing a spell. Every
// OPR book carries exactly six spells (the 2026-10-30 snapshots: GDF 48 books / 288 spells, AoF 40 / 240); the one
// exception is a defect in OPR's own data, named below with its reason. The other pins are the per-book parity pass's
// invariants (WorkItems/439-spells-3-6.md): the "in line of sight" clause count and where it sits, the weapon-scope
// suffix never leaking into a granted name, the Smash / Pierce spells, and the one-shot Enhanced grants.
[TestFixture]
public class BookSpellCorpusPinTests
{
    private const int SpellsPerBook = 6;

    // Books whose OPR source carries a spell the importer cannot import, each with the spell and why. A stale entry
    // (OPR fixed the text and the re-bake now carries all six) fails BooksMissingASpell_AreExactlyTheKnownSourceDefects.
    private static readonly IReadOnlyDictionary<string, string> KnownSourceDefects = new Dictionary<string, string>
    {
        ["AoF-ShadowStalkers"] = "Combat Possession: the 3.6.0 text stops after its target clause (\"Pick up to two " +
            "friendly units within 12\\\", which\") and its generation block has no effect - nothing to parse (#439 D, owner review).",
    };

    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static IReadOnlyList<(string Name, BookFile Book)> Books() =>
        Directory.EnumerateFiles(BooksDirectory, "*" + BookFile.EXTENSION_WITH_PERIOD)
            .OrderBy(path => path, StringComparer.Ordinal)
            .Select(path => (Path.GetFileNameWithoutExtension(path),
                JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!))
            .ToList();

    private static IEnumerable<SpellDefinition> AllSpells() => Books().SelectMany(b => b.Book.Spells);

    [Test]
    public void BooksMissingASpell_AreExactlyTheKnownSourceDefects()
    {
        var short_ = Books().Where(b => b.Book.Spells.Count != SpellsPerBook)
            .ToDictionary(b => b.Name, b => b.Book.Spells.Count);

        Assert.That(short_.Keys, Is.EquivalentTo(KnownSourceDefects.Keys),
            "every bundled book carries its snapshot's six spells; a shortfall is an unparsed spell (fix the grammar)");
        foreach ((string name, int count) in short_)
        {
            Assert.That(count, Is.EqualTo(SpellsPerBook - 1), $"{name}: {KnownSourceDefects[name]}");
        }
    }

    [Test]
    public void TheCorpus_Is88Books_And527Spells()
    {
        IReadOnlyList<(string Name, BookFile Book)> books = Books();
        Assert.That(books, Has.Count.EqualTo(88));
        Assert.That(books.Count(b => b.Name.StartsWith("AoF-", StringComparison.Ordinal)), Is.EqualTo(40));
        Assert.That(books.Sum(b => b.Book.Spells.Count), Is.EqualTo(88 * SpellsPerBook - KnownSourceDefects.Count));
    }

    [Test]
    public void SpellNames_AreUniqueWithinEachBook()
    {
        foreach ((string name, BookFile book) in Books())
        {
            Assert.That(book.Spells.Select(s => s.Name), Is.Unique, name);
        }
    }

    // #429 counted the clause over the snapshots: 287 of 528 spells, every one in the opening target clause of an
    // ENEMY-targeting spell (Combat Possession, the one spell not imported, has none).
    [Test]
    public void LineOfSight_Is287Spells_AllOfThemEnemyTargeting()
    {
        List<SpellDefinition> sighted = AllSpells().Where(s => s.Target.RequireLineOfSight).ToList();
        Assert.That(sighted, Has.Count.EqualTo(287));
        Assert.That(sighted.Select(s => s.Target.TargetAffinity).Distinct(), Is.EqualTo(new[] { ETargetAffinity.Foe }));
    }

    // The weapon-scope suffix is a scope, not a name: "Shred on its weapons when shooting" grants "Shred when
    // shooting". A granted or marked name still carrying it means the parser regressed (the #434 clones are gone).
    [Test]
    public void NoGrantedName_CarriesTheWeaponScopeSuffix()
    {
        var leaked = AllSpells()
            .SelectMany(s => FDG.SaveLoad.SpellRuleReferences.GrantedRuleNames(s.Effect).Select(n => $"{s.Name}: {n}"))
            .Where(n => n.Contains(" on its weapons", StringComparison.OrdinalIgnoreCase)
                        || n.Contains(" on their weapons", StringComparison.OrdinalIgnoreCase))
            .ToList();
        Assert.That(leaked, Is.Empty);
    }

    // #431's hand-off: seventeen 3.6 damage spells name Smash or Pierce (their "over half of this weapon's range" facet
    // reads the spell's range since #439).
    [Test]
    public void SeventeenDamageSpells_NameSmashOrPierce()
    {
        int count = AllSpells().Count(s => s.Effect is Effect.DealHits hits
            && hits.WithRules.Any(r => r is "Smash" or "Pierce"));
        Assert.That(count, Is.EqualTo(17));
    }

    // #433's family: every "get Enhanced X once" spell grants it NextTrigger. 80 spells over 44 distinct names (GDF 42
    // spells / 29 names, AoF 38 / 29) - #433's hand-off said "45 spells", a miscount the #439 parity pass corrected.
    [Test]
    public void EnhancedGrants_Are80OneShotSpells_Over44Names()
    {
        List<Effect.AddRule> grants = AllSpells().Select(s => s.Effect).OfType<Effect.AddRule>()
            .Where(g => g.RuleName.StartsWith("Enhanced ", StringComparison.Ordinal)).ToList();
        Assert.That(grants, Has.Count.EqualTo(80));
        Assert.That(grants.Select(g => g.RuleName).Distinct().Count(), Is.EqualTo(44));
        Assert.That(grants.Select(g => g.Scope).Distinct(), Is.EqualTo(new[] { ELifetime.NextTrigger }));
    }

    // The 3.6 spell-token drain (Shifting Form x6, Burn the Heretic, Aether-Cloud Rune): skipped before #439.
    [Test]
    public void EightSpells_DrainTwoSpellTokens()
    {
        var drains = AllSpells().Where(s => s.Effect is Effect.ConsumeToken).ToList();
        Assert.That(drains, Has.Count.EqualTo(8));
        Assert.That(drains.Select(s => s.Effect).Distinct(),
            Is.EqualTo(new Effect[] { new Effect.ConsumeToken(TokenType.SpellTokens, 2) }));
        Assert.That(drains.Select(s => s.Name).Distinct(),
            Is.EquivalentTo(new[] { "Shifting Form", "Burn the Heretic", "Aether-Cloud Rune" }));
    }

    // "Units may only be moved once per round in this way" - both morale-move spells key the limit by their own name.
    [Test]
    public void MoraleMoveSpells_CarryTheirOwnOncePerRoundKey()
    {
        var moves = AllSpells()
            .Where(s => s.Effect is Effect.MoraleTestThen { OnFailure: Effect.TriggeredMove })
            .Select(s => (s.Name, ((Effect.TriggeredMove)((Effect.MoraleTestThen)s.Effect).OnFailure).OncePerRoundKey))
            .ToList();
        Assert.That(moves, Is.EquivalentTo(new[]
        {
            ("Deep Hypnosis", (string?)"Deep Hypnosis"),
            ("Seductive Invocation", (string?)"Seductive Invocation"),
        }));
    }
}
