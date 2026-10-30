using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #378 — the census regression pin #375 promised: every unit/item/weapon/upgrade rule reference in
// every bundled book resolves against that book's own definitions (the exact walk `--rule-coverage`
// and army load perform). #196/#197 closed the GDF corpus and #375/#376 the AoF corpus to zero dead
// refs bar the allowlisted #381 gap; without this fixture, the next re-import or supplement edit that
// reintroduces a dead name looks exactly like a working rule (BookSpellCoverageTests pins the spell
// half; BookRuleScopeTests the scope-mismatch half).
[TestFixture]
public class BookRuleCensusTests
{
    // Known-dead names, each with a reason. A stale entry (the name starts resolving everywhere)
    // fails the fixture too - that is the reminder to delete it when the blocking item lands.
    private static readonly IReadOnlyDictionary<string, string> Allowlist = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
    {
        // Empty from #381 (Retreating Strike, the last dead AoF name) until #422. Add entries only for a
        // name that is KNOWINGLY dead, with the item that owns it.
        //
        // #422: OPR 3.6 dropped Counter-Attack and its Aura, so the core catalog no longer defines them, but
        // the bundled 3.5 books still name them (15 books, 36 references) until #438 re-bakes the corpus from
        // the 3.6 sources. A list naming them loads with the "not implemented" diagnostic. Remove both
        // entries once #438 lands - AllowlistedNamesAreStillDeadSomewhere fails until you do.
        ["Counter-Attack"] = "dropped by OPR 3.6 (#422); bundled 3.5 books name it until the #438 re-bake",
        ["Counter-Attack Aura"] = "dropped by OPR 3.6 (#422); bundled 3.5 books name it until the #438 re-bake",
        // #436: the rest of the 3.5 catalog rules 3.6 dropped, deleted on the same terms (CoreRuleCatalog; legacy
        // load pinned by Dropped35CatalogLegacyDataTests). Remove these with the #422 pair once #438 lands.
        ["Courage Aura"] = "dropped by OPR 3.6 (#436); bundled 3.5 books name it until the #438 re-bake",
        ["Crack"] = "dropped by OPR 3.6 (#436); bundled 3.5 books name it until the #438 re-bake",
        ["Strafing"] = "dropped by OPR 3.6 (#436); bundled 3.5 books name it until the #438 re-bake",
        ["Harassing Boost Aura"] = "dropped by OPR 3.6 (#436); bundled 3.5 books name it until the #438 re-bake",
        ["Guerrilla Boost Aura"] = "dropped by OPR 3.6 (#436); bundled 3.5 books name it until the #438 re-bake",
        ["Resistance"] = "dropped by OPR 3.6 (#436); bundled 3.5 books name it until the #438 re-bake",
        ["Resistance Aura"] = "dropped by OPR 3.6 (#436); bundled 3.5 books name it until the #438 re-bake",
        ["Piercing Assault"] = "dropped by OPR 3.6 (#436); bundled 3.5 books name it until the #438 re-bake",
        ["Piercing Assault Aura"] = "dropped by OPR 3.6 (#436); bundled 3.5 books name it until the #438 re-bake",
        ["Piercing Hunter Aura"] = "dropped by OPR 3.6 (#436); bundled 3.5 books name it until the #438 re-bake",
        ["Darkborn (Offensive)"] = "dropped by OPR 3.6 (#436); bundled 3.5 books name it until the #438 re-bake",
        ["Darkborn (Defensive)"] = "dropped by OPR 3.6 (#436); bundled 3.5 books name it until the #438 re-bake",
        ["Storm of Change"] = "dropped by OPR 3.6 (#436); bundled 3.5 books name it until the #438 re-bake",
        ["Storm of Lust"] = "dropped by OPR 3.6 (#436); bundled 3.5 books name it until the #438 re-bake",
        ["Storm of Plague"] = "dropped by OPR 3.6 (#436); bundled 3.5 books name it until the #438 re-bake",
        ["Storm of War"] = "dropped by OPR 3.6 (#436); bundled 3.5 books name it until the #438 re-bake",
    };

    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static IEnumerable<TestCaseData> Books() =>
        Directory.EnumerateFiles(BooksDirectory, "*" + BookFile.EXTENSION_WITH_PERIOD)
            .OrderBy(path => path)
            .Select(path => new TestCaseData(path).SetArgDisplayNames(Path.GetFileNameWithoutExtension(path)));

    [TestCaseSource(nameof(Books))]
    public void EveryRuleReferenceResolves(string bookPath)
    {
        BookFile book = LoadBook(bookPath);
        IRuleResolver resolver = ResolverFor(book);

        List<string> dead = new();
        foreach ((string site, string name) in ReferencesIn(book))
        {
            if (resolver.TryResolve(name, out _)) continue;
            if (Allowlist.ContainsKey(name)) continue;
            dead.Add($"{site}: '{name}'");
        }

        Assert.That(dead, Is.Empty,
            $"{Path.GetFileName(bookPath)} names rules with no definition anywhere - they will silently " +
            $"do nothing in play:{Environment.NewLine}  " + string.Join(Environment.NewLine + "  ", dead));
    }

    [Test]
    public void AllowlistedNamesAreStillDeadSomewhere()
    {
        var stillDead = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (TestCaseData bookCase in Books())
        {
            BookFile book = LoadBook((string)bookCase.Arguments[0]!);
            IRuleResolver resolver = ResolverFor(book);
            foreach ((_, string name) in ReferencesIn(book))
            {
                if (!resolver.TryResolve(name, out _)) stillDead.Add(name);
            }
        }

        foreach ((string name, string reason) in Allowlist)
        {
            Assert.That(stillDead, Does.Contain(name),
                $"allowlisted '{name}' ({reason}) now resolves everywhere - remove its stale entry.");
        }
    }

    private static BookFile LoadBook(string path) =>
        JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;

    private static IRuleResolver ResolverFor(BookFile book)
    {
        RuleResolver resolver = CoreRuleCatalog.CreateResolver();
        foreach (SpecialRuleDefinition definition in book.RuleDefinitions) resolver.RegisterOrReplace(definition);
        return resolver;
    }

    // The same attachment walk RuleCoverageReport / army load perform (scope classification lives in
    // BookRuleScopeTests; this fixture only asks "does a definition exist at all").
    private static IEnumerable<(string Site, string Name)> ReferencesIn(BookFile book)
    {
        foreach (RosterUnit unit in book.Units)
        {
            foreach (string name in NamesOf(unit.Rules))
                yield return ($"{unit.Name} (unit rule)", name);

            foreach (ItemEntry item in unit.Items)
                foreach (string name in NamesOf(item.Rules))
                    yield return ($"{unit.Name} / {item.Name}", name);

            foreach (WeaponFileEntry weapon in unit.Weapons)
                foreach (string name in NamesOf(weapon.SpecialRules))
                    yield return ($"{unit.Name} / {weapon.Name}", name);

            foreach (UpgradeSection section in unit.Sections)
                foreach (UpgradeOption option in section.Options)
                {
                    foreach (string name in NamesOf(option.RulesGained))
                        yield return ($"{unit.Name} / {option.Label}", name);

                    foreach (ItemEntry item in option.ItemsGained)
                        foreach (string name in NamesOf(item.Rules))
                            yield return ($"{unit.Name} / {option.Label} / {item.Name}", name);

                    foreach (WeaponFileEntry weapon in option.WeaponsGained)
                        foreach (string name in NamesOf(weapon.SpecialRules))
                            yield return ($"{unit.Name} / {option.Label} / {weapon.Name}", name);
                }
        }
    }

    private static IEnumerable<string> NamesOf(IEnumerable<SpecialRuleEntry> rules) =>
        rules.Select(rule => ArmyListRuleResolution.DescribeRuleEntry(rule).lookupName);
}
