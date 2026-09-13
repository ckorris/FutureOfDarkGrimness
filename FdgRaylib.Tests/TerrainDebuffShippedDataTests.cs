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
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using FDG.Utilities;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #197 P8 - the terrain-debuff family (14 corpus references). "Dangerous Terrain Debuff" is TWO rules
// sharing one name: four books defer it ("counts as being in Dangerous Terrain once, next time the
// effect would apply") and two make it immediate ("must immediately take a Dangerous Terrain test").
// The importer routes the minority variant to "Dangerous Terrain Debuff (Immediate)", the Darkborn
// mechanism, so a re-import stays correct.
//
// The mechanics live engine-side (TerrainDebuffRuleIntegrationTests). What is pinned here is the
// authored JSON and its agreement with the corpus, because every failure in this layer is silent:
//  - a deferred rule whose grant chain is broken applies nothing at all;
//  - a line-of-sight flag copied from the wrong sibling quietly changes who can be targeted;
//  - a book routed to the wrong variant plays a different rule than its own page prints.
[TestFixture]
public class TerrainDebuffShippedDataTests
{
    private const string DangerousDeferred = "Dangerous Terrain Debuff";
    private const string DangerousDeferredEffect = "Dangerous Terrain Debuff Effect";
    private const string DangerousImmediate = "Dangerous Terrain Debuff (Immediate)";
    private const string Difficult = "Difficult Terrain Debuff";
    private const string DifficultEffect = "Difficult Terrain Debuff Effect";

    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static IReadOnlyList<SpecialRuleDefinition> Supplement() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
            Path.Combine(BooksDirectory, "GdfRuleSupplement.json")));

    private static SpecialRuleDefinition Definition(string name) =>
        Supplement().Single(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

    // ---- The offer shape, against the corpus text ---------------------------------------------------

    // "Once per activation, before attacking, pick one enemy unit within 18in [in line of sight]".
    // The line-of-sight column is the interesting one: the deferred DANGEROUS variant requires it and the
    // other two do not, which is easy to lose by copying whichever sibling was nearest in the file.
    [TestCase(DangerousDeferred, true)]
    [TestCase(DangerousImmediate, false)]
    [TestCase(Difficult, false)]
    public void EachVariant_OffersOnceBeforeAttacking_AtTheCorpusRangeAndSight(
        string ruleName, bool needsLineOfSight)
    {
        ActivatedAbility ability = Definition(ruleName).Activated.Single();

        Assert.That(ability.TriggerHook, Is.EqualTo(EHookID.Activation_OnBeforeAttackAction),
            "'before attacking' is the pre-attack hook");
        Assert.That(ability.Cost, Is.InstanceOf<Cost.OncePerActivation>(), "'once per activation'");
        Assert.That(ability.TargetSelector!.RangeInches, Is.EqualTo(18f));
        Assert.That(ability.TargetSelector.MaxCount, Is.EqualTo(1), "'pick ONE enemy unit'");
        Assert.That(ability.TargetSelector.TargetAffinity, Is.EqualTo(ETargetAffinity.Foe));
        Assert.That(ability.TargetSelector.RequireLineOfSight, Is.EqualTo(needsLineOfSight),
            $"'{ruleName}' " + (needsLineOfSight ? "says 'in line of sight'" : "does not say 'in line of sight'"));
    }

    // ---- The two arms are actually different ---------------------------------------------------------

    [Test]
    public void TheDeferredArm_GrantsAOneShotCountsAsRule()
    {
        Assert.That(Definition(DangerousDeferred).Activated.Single().Effect,
            Is.EqualTo(new Effect.AddRule(DangerousDeferredEffect, ELifetime.NextTrigger)),
            "'counts as being in Dangerous Terrain ONCE (next time the effect would apply)'");
        Assert.That(Definition(Difficult).Activated.Single().Effect,
            Is.EqualTo(new Effect.AddRule(DifficultEffect, ELifetime.NextTrigger)));
    }

    [TestCase(DangerousDeferredEffect, ECountAsTerrain.Dangerous)]
    [TestCase(DifficultEffect, ECountAsTerrain.Difficult)]
    public void TheGrantedRule_RidesTheVictimsNextMove(string effectName, ECountAsTerrain terrain)
    {
        HookEntry entry = Definition(effectName).Passive.Single();

        Assert.That(entry.HookID, Is.EqualTo(EHookID.Movement_OnMoveThroughTerrain),
            "read throughout the victim's move and spent by ExecuteMoveStage when it resolves");
        Assert.That(entry.Seat, Is.EqualTo(ERuleSeat.Actor), "the victim is the one moving");
        Assert.That(entry.Effect, Is.EqualTo(new Effect.CountAsInTerrain(terrain)));
    }

    [Test]
    public void TheImmediateArm_TestsOnTheSpot_RatherThanArmingAMove()
    {
        // The distinction the whole slice exists for. Authored as an AddRule of a counts-as rule instead,
        // this variant would do nothing at all to a victim that simply holds still.
        Assert.That(Definition(DangerousImmediate).Activated.Single().Effect,
            Is.InstanceOf<Effect.DangerousTerrainTest>(),
            "'must IMMEDIATELY take a Dangerous Terrain test'");
        Assert.That(Definition(DangerousImmediate).Passive, Is.Empty);
    }

    // ---- The grant survives to the move it is meant to spoil, and no further --------------------------

    // The failure this guards is the one DeferredDebuffCompositionTests names for Speed Debuff: a
    // NextTrigger grant consumed by a read-only projection would evaporate before the move it targets.
    // Here the read side is MovementRuleQueries (three separate call sites) and the spend is
    // ExecuteMoveStage's ConsumeOneShotGrants - so this drives the real authored JSON through both.
    [TestCase(DangerousDeferred, DangerousDeferredEffect, ECountAsTerrain.Dangerous)]
    [TestCase(Difficult, DifficultEffect, ECountAsTerrain.Difficult)]
    public void TheGrant_SurvivesUntilTheVictimMoves_AndIsSpentExactlyOnce(
        string debuffName, string effectName, ECountAsTerrain terrain)
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        RuleResolver resolver = CoreRuleCatalog.CreateResolver();
        resolver.Register(Definition(effectName));
        var evaluator = new RuleEvaluator(new ProbabilisticDiceRoller(), ruleResolver: resolver);

        IUnit caster = BuildUnit(store, "Caster");
        IUnit victim = BuildUnit(store, "Victim");

        var operations = new List<RuleOperation>();
        Definition(debuffName).Activated.Single().Effect.Apply(
            new RuleInvocation(null, caster, Array.Empty<RuleArgument>(), victim), operations);
        OperationApplier.ApplyTokenOperations(operations);

        bool Counts() => MovementRuleQueries.CountsAsInTerrain(victim, evaluator, terrain);

        Assert.That(Counts(), Is.True, "the victim counts as being in the terrain for its next move");
        Assert.That(Counts(), Is.True,
            "...and a second read must not spend it - the move budget, the path validator and the " +
            "dangerous-terrain roll all query before the move resolves");

        evaluator.ConsumeOneShotGrants(new MoveThroughTerrainContext(victim),
            (victim, ERuleSeat.Actor));

        Assert.That(Counts(), Is.False, "'once' - the move resolved, so the grant is spent");
    }

    // ---- The corpus, book by book ---------------------------------------------------------------------

    private record DebuffSite(string Book, string Unit, string Rule);

    private static IEnumerable<DebuffSite> DebuffSites()
    {
        string[] tracked = { DangerousDeferred, DangerousImmediate, Difficult };

        foreach (string path in ShippedBooks.GdfPaths()
                     .OrderBy(p => p))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            string bookName = Path.GetFileNameWithoutExtension(path);

            foreach (RosterUnit unit in book.Units)
            {
                foreach (string name in RuleNamesOn(unit))
                {
                    if (tracked.Contains(name, StringComparer.OrdinalIgnoreCase))
                        yield return new DebuffSite(bookName, unit.Name, name);
                }
            }
        }
    }

    // Every site army load reads a unit-scoped rule from: the unit's own rules and starting wargear, and
    // the same two inside every upgrade option. All 14 corpus references are wargear ("Summoned Tendrils",
    // "Curse of Plague", "Rad-Glow"), but the walk covers the other sites so a re-import that moves one
    // still counts.
    private static IEnumerable<string> RuleNamesOn(RosterUnit unit)
    {
        foreach (SpecialRuleEntry rule in unit.Rules) yield return NameOf(rule);
        foreach (ItemEntry item in unit.Items)
            foreach (SpecialRuleEntry rule in item.Rules) yield return NameOf(rule);

        foreach (UpgradeSection section in unit.Sections)
            foreach (UpgradeOption option in section.Options)
            {
                foreach (SpecialRuleEntry rule in option.RulesGained) yield return NameOf(rule);
                foreach (ItemEntry item in option.ItemsGained)
                    foreach (SpecialRuleEntry rule in item.Rules) yield return NameOf(rule);
            }
    }

    private static string NameOf(SpecialRuleEntry rule) =>
        ArmyListRuleResolution.DescribeRuleEntry(rule).lookupName;

    [Test]
    public void EveryTerrainDebuffReference_ResolvesAgainstItsOwnBook()
    {
        List<DebuffSite> sites = DebuffSites().ToList();

        Assert.That(sites.Count, Is.EqualTo(14),
            "the audit's 14 references (11 Dangerous + 3 Difficult) - a change here means the corpus " +
            "moved, not the engine");

        var unresolved = new List<string>();
        foreach (string path in ShippedBooks.GdfPaths())
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            string bookName = Path.GetFileNameWithoutExtension(path);
            if (!sites.Any(s => s.Book == bookName)) continue;

            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            foreach (SpecialRuleDefinition definition in book.RuleDefinitions)
                resolver.RegisterOrReplace(definition);

            foreach (DebuffSite site in sites.Where(s => s.Book == bookName))
            {
                if (!resolver.TryResolve(site.Rule, out ResolvedRule resolved)
                    || resolved.Definition.Activated.Count == 0)
                {
                    unresolved.Add($"{site.Book}: {site.Unit} ({site.Rule})");
                }
            }
        }

        Assert.That(unresolved, Is.Empty,
            "references whose own book carries no usable definition: " + string.Join(", ", unresolved));
    }

    // The disambiguation itself. Lust Disciples and War Disciples print "must immediately take a Dangerous
    // Terrain test"; the other four print "counts as being in Dangerous Terrain once". Routing a book to
    // the wrong variant makes it play a rule its own page does not describe - and both variants resolve
    // cleanly, so nothing else in the suite would notice.
    [Test]
    public void OnlyTheTwoImmediateArmies_CarryTheImmediateVariant()
    {
        string[] immediateArmies = { "LustDisciples", "WarDisciples" };

        List<DebuffSite> immediate = DebuffSites().Where(s => s.Rule == DangerousImmediate).ToList();
        List<DebuffSite> deferred = DebuffSites().Where(s => s.Rule == DangerousDeferred).ToList();

        Assert.That(immediate.Select(s => s.Book).Distinct().OrderBy(b => b),
            Is.EqualTo(immediateArmies.OrderBy(b => b)).AsCollection);
        Assert.That(immediate.Count, Is.EqualTo(2));
        Assert.That(deferred.Count, Is.EqualTo(9));
        Assert.That(deferred.Select(s => s.Book), Has.No.AnyOf(immediateArmies),
            "a book gets one variant or the other, never both");
    }

    // (The importer half of that routing - what keeps it true across a re-import, since the bundled books
    // were patched by hand - is pinned engine-side by OprBookImporterTests, where Disambiguate lives.)

    private static IUnit BuildUnit(GameDataStore store, string name)
    {
        var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
            initialPosition: new Position(), gameDataStore: store);
        DataBinding<ModelData> binding = store.GetDataBinding<ModelData>(store.Create(model));
        return new UnitData(new PlayerID(Guid.NewGuid()), name, quality: 4, defense: 4,
            modelBindings: new List<DataBinding<ModelData>> { binding });
    }
}
