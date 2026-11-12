using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using FDG.SaveLoad;

namespace FdgRaylib.Cli;

/// <summary>
/// #196 slice 1 / SYS-5: reports every dead rule reference across a directory of `.fdgbook` files —
/// the measurement loop #196/#197 close against, and the "import reconciliation report" the audit asked
/// for so a re-import can't silently regress coverage without anyone noticing.
///
/// Mirrors army load's own resolution exactly (mirrors `ArmyListRuleResolution` / `GameBootstrap` /
/// `ListCompiler`, and is exercised the same way `FdgRaylib.Tests/BookRuleScopeTests.cs` does): a name
/// with no definition anywhere is "no-definition"; a `Unit`-scoped definition named on a weapon is
/// "scope-mismatch" (nowhere for it to attach); a `Weapon`-scoped definition named at unit level is NOT
/// a mismatch — #197 slice 0 re-homes those onto the unit's weapons, so it counts as attached.
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

            // #377 — spell references, previously invisible to this census: a damage spell's WithRules
            // names (argument-parsed, Weapon scope — ArmyListSpellResolution.ResolveWeaponRuleNames) and
            // the names spells grant as rules (raw, argument-less — RuleEvaluator.CollectGrantedRules
            // screens out argument-reading definitions because grants carry none). Both classified
            // through the same ResolveOrDescribeDrop ladder the load paths use.
            foreach (SpellDefinition spell in book.Spells)
            {
                foreach (string ruleName in SpellRuleReferences.WeaponRuleNames(spell.Effect))
                {
                    totalRefs++;
                    SpecialRuleEntry entry = SpecialRuleEntryParser.Parse(ruleName);
                    ArmyListRuleResolution.ResolveOrDescribeDrop(resolver, entry, ERuleScope.Weapon,
                        $"spell '{spell.Name}'", out RuleDrop? drop);
                    if (drop != null)
                    {
                        Record(byName, drop.Value.RuleName, ClassOf(drop.Value.Reason));
                    }
                }

                foreach (string ruleName in SpellRuleReferences.GrantedRuleNames(spell.Effect))
                {
                    totalRefs++;
                    ArmyListRuleResolution.ResolveOrDescribeDrop(resolver,
                        new SpecialRuleEntry_Core(ruleName), attachmentScope: null,
                        $"spell '{spell.Name}'", out RuleDrop? drop);
                    if (drop != null)
                    {
                        Record(byName, drop.Value.RuleName,
                            drop.Value.Reason == ERuleDropReason.MissingArgument
                                ? "grant-arity" : ClassOf(drop.Value.Reason));
                    }
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
        string breakdown = string.Join(", ", dead
            .GroupBy(kv => kv.Value.FailureClass)
            .OrderBy(g => g.Key)
            .Select(g => $"{g.Sum(kv => kv.Value.Refs)} {g.Key} across {g.Count()} names"));

        Console.WriteLine();
        Console.WriteLine($"Total references: {totalRefs}");
        Console.WriteLine($"Dead: {deadRefs}" + (dead.Count > 0 ? $" ({breakdown})" : ""));
    }

    private static string ClassOf(ERuleDropReason reason) => reason switch
    {
        ERuleDropReason.WrongScope => "scope-mismatch",
        ERuleDropReason.MissingArgument => "missing-argument",
        _ => "no-definition",
    };

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
