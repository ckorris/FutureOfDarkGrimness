using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using FDG.SaveLoad;

namespace FdgRaylib.Cli;

/// <summary>
/// #191 slice 1 / SYS-5: reports every dead rule reference across a directory of `.fdgbook` files —
/// the measurement loop #191/#192 close against, and the "import reconciliation report" the audit asked
/// for so a re-import can't silently regress coverage without anyone noticing.
///
/// Mirrors army load's own resolution exactly (mirrors `ArmyListRuleResolution` / `GameBootstrap` /
/// `ListCompiler`, and is exercised the same way `FdgRaylib.Tests/BookRuleScopeTests.cs` does): a name
/// with no definition anywhere is "no-definition"; a `Unit`-scoped definition named on a weapon is
/// "scope-mismatch" (nowhere for it to attach); a `Weapon`-scoped definition named at unit level is NOT
/// a mismatch — #192 slice 0 re-homes those onto the unit's weapons, so it counts as attached.
/// </summary>
public static class RuleCoverageReport
{
    private sealed record Reference(string Name, ERuleScope AttachesAt);

    private sealed class Tally
    {
        public int Refs;
        public string FailureClass = "";
    }

    public static void Run(string booksDirectory)
    {
        var byName = new Dictionary<string, Tally>(StringComparer.OrdinalIgnoreCase);
        int totalRefs = 0;

        foreach (string path in Directory.EnumerateFiles(booksDirectory, "*" + BookFile.EXTENSION_WITH_PERIOD)
                     .OrderBy(p => p))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            IRuleResolver resolver = ResolverFor(book);

            foreach (Reference reference in ReferencesIn(book))
            {
                totalRefs++;

                if (!resolver.TryResolve(reference.Name, out ResolvedRule resolved))
                {
                    Record(byName, reference.Name, "no-definition");
                    continue;
                }

                bool weaponRuleAtUnitLevel = reference.AttachesAt == ERuleScope.Unit
                    && resolved.Definition.Scope == ERuleScope.Weapon;
                bool mismatch = resolved.Definition.Scope != reference.AttachesAt && !weaponRuleAtUnitLevel;

                if (mismatch)
                {
                    Record(byName, resolved.Definition.Name, "scope-mismatch");
                }
            }
        }

        List<KeyValuePair<string, Tally>> dead = byName.OrderByDescending(kv => kv.Value.Refs)
            .ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).ToList();

        foreach ((string name, Tally tally) in dead)
        {
            Console.WriteLine($"  {tally.Refs,5}  {tally.FailureClass,-15}  {name}");
        }

        int deadRefs = dead.Sum(kv => kv.Value.Refs);
        int noDefRefs = dead.Where(kv => kv.Value.FailureClass == "no-definition").Sum(kv => kv.Value.Refs);
        int noDefNames = dead.Count(kv => kv.Value.FailureClass == "no-definition");
        int mismatchRefs = dead.Where(kv => kv.Value.FailureClass == "scope-mismatch").Sum(kv => kv.Value.Refs);
        int mismatchNames = dead.Count(kv => kv.Value.FailureClass == "scope-mismatch");

        Console.WriteLine();
        Console.WriteLine($"Total references: {totalRefs}");
        Console.WriteLine($"Dead: {deadRefs} ({noDefRefs} no-definition across {noDefNames} names, " +
            $"{mismatchRefs} scope-mismatch across {mismatchNames} names)");
    }

    private static void Record(Dictionary<string, Tally> byName, string name, string failureClass)
    {
        if (!byName.TryGetValue(name, out Tally? tally))
        {
            tally = new Tally { FailureClass = failureClass };
            byName[name] = tally;
        }

        tally.Refs++;
    }

    private static IRuleResolver ResolverFor(BookFile book)
    {
        RuleResolver resolver = CoreRuleCatalog.CreateResolver();
        foreach (SpecialRuleDefinition definition in book.RuleDefinitions) resolver.RegisterOrReplace(definition);
        return resolver;
    }

    /// <summary>Every rule name a book states, paired with the scope its attachment site resolves at.
    /// Matches what army load does: weapon profiles attach at Weapon scope; a unit's own rules and its
    /// wargear rule-bundles (which ListCompiler flattens into the unit) attach at Unit scope.</summary>
    private static IEnumerable<Reference> ReferencesIn(BookFile book)
    {
        foreach (RosterUnit unit in book.Units)
        {
            foreach (Reference reference in RulesOf(unit.Rules, ERuleScope.Unit))
                yield return reference;

            foreach (ItemEntry item in unit.Items)
                foreach (Reference reference in RulesOf(item.Rules, ERuleScope.Unit))
                    yield return reference;

            foreach (WeaponFileEntry weapon in unit.Weapons)
                foreach (Reference reference in RulesOf(weapon.SpecialRules, ERuleScope.Weapon))
                    yield return reference;

            foreach (UpgradeSection section in unit.Sections)
                foreach (UpgradeOption option in section.Options)
                {
                    foreach (Reference reference in RulesOf(option.RulesGained, ERuleScope.Unit))
                        yield return reference;

                    foreach (ItemEntry item in option.ItemsGained)
                        foreach (Reference reference in RulesOf(item.Rules, ERuleScope.Unit))
                            yield return reference;

                    foreach (WeaponFileEntry weapon in option.WeaponsGained)
                        foreach (Reference reference in RulesOf(weapon.SpecialRules, ERuleScope.Weapon))
                            yield return reference;
                }
        }
    }

    private static IEnumerable<Reference> RulesOf(IEnumerable<SpecialRuleEntry> rules, ERuleScope scope) =>
        rules.Select(rule => new Reference(ArmyListRuleResolution.DescribeRuleEntry(rule).lookupName, scope));
}
