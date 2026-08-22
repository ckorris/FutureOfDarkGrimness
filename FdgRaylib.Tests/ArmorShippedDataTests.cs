using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG;
using FDG.ArmyBuilding;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #197 Armor(X) - "Counts as having Defense X+ in place of the model's own Defense stat." #196 shipped a
// zero-hook marker so the name resolved and the description showed, but the mechanic was absent: every one
// of the 11 corpus sites is a paid upgrade (Heavy Armor, mounts, chariots) whose OTHER bundled rules
// (Tough, Fast, Strider) all worked, while the defense half silently did nothing. The 2026-07-29 slice
// authored the mechanic on Tough's creation seam - Lifecycle_OnUnitCreated -> setDefense(Arg(0)) - so
// UnitCreationRules writes the unit's Defense stat once and every save path (volleys, melee, impact,
// reflect, synthetic hits, the AI's CombatMath) reads it with no per-path folding. Owner-ruled: a literal
// SET, not a floor. The mechanism is pinned engine-side by ArmorRuleIntegrationTests; these pin the
// authored JSON, the embedded book copies, the corpus shape, and the numericValue -> argument flow.
[TestFixture]
public class ArmorShippedDataTests
{
    private const string RuleName = "Armor";

    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static SpecialRuleDefinition Armor() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
                Path.Combine(BooksDirectory, "GdfRuleSupplement.json")))
            .Single(r => r.Name == RuleName);

    // ---- The authored definition ----------------------------------------------------------------------

    [Test]
    public void Armor_IsACreationTimeDefenseSet_ReadingTheArgument()
    {
        SpecialRuleDefinition rule = Armor();
        HookEntry entry = rule.Passive.Single();

        Assert.That(entry.HookID, Is.EqualTo(EHookID.Lifecycle_OnUnitCreated),
            "authored at any other hook it would still validate, and UnitCreationRules would never fold it");
        Assert.That(entry.Condition, Is.InstanceOf<Condition.Always>());
        Assert.That(entry.Effect, Is.InstanceOf<Effect.SetDefense>());
        Assert.That(((Effect.SetDefense)entry.Effect).Defense, Is.InstanceOf<ValueSource.Arg>(),
            "the X in Armor(X) is the rule's argument - a literal here would freeze every site to one value");
        Assert.That(rule.EngineArgumentCount, Is.EqualTo(1));
        Assert.That(rule.Scope, Is.EqualTo(ERuleScope.Unit));
    }

    // ---- The corpus -----------------------------------------------------------------------------------

    private record Site(string Book, string Unit, int ArmorValue);

    private static IEnumerable<Site> Sites()
    {
        foreach (string path in Directory.EnumerateFiles(BooksDirectory, "*" + BookFile.EXTENSION_WITH_PERIOD)
                     .OrderBy(p => p))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            string bookName = Path.GetFileNameWithoutExtension(path);

            foreach (RosterUnit unit in book.Units)
                foreach (UpgradeSection section in unit.Sections)
                    foreach (UpgradeOption option in section.Options)
                        foreach (ItemEntry item in option.ItemsGained)
                            foreach (SpecialRuleEntry rule in item.Rules)
                            {
                                if (rule is SpecialRuleEntry_CoreNumeric numeric
                                    && string.Equals(numeric.Name, RuleName, StringComparison.Ordinal))
                                {
                                    yield return new Site(bookName, unit.Name, numeric.NumericValue);
                                }
                            }
        }
    }

    [Test]
    public void EveryReference_IsAnItemGrantAcrossSevenBooks()
    {
        List<Site> sites = Sites().ToList();

        Assert.That(sites.Count, Is.EqualTo(11),
            "the audit's 11 references - a change here means the corpus moved, not the engine");
        Assert.That(sites.Select(s => s.Book).Distinct(), Is.EquivalentTo(new[]
        {
            "DarkElfRaiders", "GoblinReclaimers", "HumanDefenseForce", "SaurianStarhost",
            "WormholeDaemonsofChange", "WormholeDaemonsofLust", "WormholeDaemonsofWar",
        }));
        Assert.That(sites.Select(s => s.ArmorValue), Is.All.InRange(2, 5),
            "every authored X is a plausible Defense value");
    }

    [Test]
    public void TheEmbeddedBookCopies_CarryTheMechanic()
    {
        // --apply-rules embeds the supplement's definition into each referencing book. A slice that edits
        // the supplement and forgets to re-embed ships a book whose Armor is still the #196 zero-hook
        // marker - name resolves, description shows, saves unchanged - and nothing else would notice.
        foreach (string bookName in Sites().Select(s => s.Book).Distinct())
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(
                File.ReadAllText(Path.Combine(BooksDirectory, bookName + BookFile.EXTENSION_WITH_PERIOD)),
                RuleJson.Options)!;

            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            foreach (SpecialRuleDefinition definition in book.RuleDefinitions)
            {
                resolver.RegisterOrReplace(definition);
            }

            Assert.That(resolver.TryResolve(RuleName, out ResolvedRule resolved), Is.True, bookName);
            Assert.That(resolved.Definition.Passive.Any(e => e.Effect is Effect.SetDefense), Is.True,
                $"{bookName}: the defense-set entry is missing - re-run --apply-rules");
        }
    }

    // ---- End to end: the shipped data through the real attach + creation path --------------------------

    [Test]
    public void HeavyArmor4_OnAShippedBookUnit_SetsDefenseTo4()
    {
        // The full flow a real army-load runs: the book's embedded definition registered on a resolver,
        // the item's coreNumeric entry resolved WITH its argument (numericValue 4 -> RuleArgument.Int(4)),
        // attached at unit scope, and UnitCreationRules folding the set onto the stat. HDF Veterans are
        // D5+ buying Heavy Armor (Armor(4)) for the whole unit.
        BookFile book = JsonSerializer.Deserialize<BookFile>(
            File.ReadAllText(Path.Combine(BooksDirectory, "HumanDefenseForce" + BookFile.EXTENSION_WITH_PERIOD)),
            RuleJson.Options)!;

        RuleResolver resolver = CoreRuleCatalog.CreateResolver();
        foreach (SpecialRuleDefinition definition in book.RuleDefinitions)
        {
            resolver.RegisterOrReplace(definition);
        }

        ResolvedRule? resolved = ArmyListRuleResolution.ResolveForScope(resolver,
            new SpecialRuleEntry_CoreNumeric(RuleName, 4), ERuleScope.Unit, "Veterans (test)");
        Assert.That(resolved, Is.Not.Null, "Armor(4) resolves at unit scope against the shipped book");

        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
            initialPosition: new Position(), gameDataStore: store);
        var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Veterans", quality: 4, defense: 5,
            modelBindings: new List<DataBinding<ModelData>>
            {
                store.GetDataBinding<ModelData>(store.Create(model)),
            });
        unit.AttachRuleDefinition(resolved!);

        UnitCreationRules.Apply(unit, new RuleEvaluator(new ProbabilisticDiceRoller()));

        Assert.That(unit.Defense, Is.EqualTo(4),
            "D5+ Veterans in shipped Heavy Armor save at 4+ - the paid upgrade finally does something");
    }
}
