using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FDG;
using FDG.ArmyBuilding;
using FDG.Data;
using FDG.GameModel;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.SaveLoad;
using FDG.Stages;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #422 - the supplement half of Core Rules v3.6.0's Counter split. 3.6 Counter only strikes first ("..., unless
// this unit is Fatigued"); its 3.5 impact facet became Brace(X) ("Enemies charging this unit get -X total Impact
// rolls, unless this unit is Fatigued"). The book grants that "count as" those rules follow them:
//   GDF + AoF  "Counter in Melee"      = "This model gets Counter on its weapons in melee."
//              "Counter in Melee Aura" = "This model and its unit get Counter on its weapons in melee."
//   GDF        "Counter in Melee Buff" = "Once per activation, before attacking, pick one friendly unit within
//                                         12", which gets Counter in Melee."
//   AoF        "Brace in Melee"        = "This model gets Brace(1) on its weapons in melee."
// and Heavy Impact ("Counts as having Impact(X) with hits that have AP(1)") inherits Impact's Fatigue gate. The
// engine side (catalog entries, stages) is pinned in the engine's Counter/Impact/Brace integration tests; these
// pin the DATA as authored, and drive two of the grants through the real army-load path and evaluator.
[TestFixture]
public class CounterBraceShippedDataTests
{
    private static readonly Condition NotFatigued = new Condition.Not(new Condition.CountsAsFatiguedInMelee());

    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static List<SpecialRuleDefinition> Load(string fileName) =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(Path.Combine(BooksDirectory, fileName)));

    private static SpecialRuleDefinition Gdf(string name) => Load("GdfRuleSupplement.json").Single(r => r.Name == name);

    private static SpecialRuleDefinition Aof(string name) => Load("AofRuleSupplement.json").Single(r => r.Name == name);

    [Test]
    public void CounterInMelee_IsCounterExactly_WithNoImpactFacet()
    {
        SpecialRuleDefinition counterInMelee = Gdf("Counter in Melee");
        HookEntry counter = CoreRuleCatalog.Counter.Passive.Single();

        Assert.That(counterInMelee.Scope, Is.EqualTo(ERuleScope.Weapon), "\"on its weapons\"");
        HookEntry entry = counterInMelee.Passive.Single();
        Assert.That(entry.HookID, Is.EqualTo(counter.HookID));
        Assert.That(entry.Seat, Is.EqualTo(counter.Seat));
        Assert.That(entry.Condition, Is.EqualTo(counter.Condition).And.EqualTo(NotFatigued),
            "Counter's own 3.6 Fatigue gate, read on the charged unit");
        Assert.That(entry.Effect, Is.EqualTo(counter.Effect));
        Assert.That(entry.Lifetime, Is.EqualTo(counter.Lifetime));
        Assert.That(counterInMelee.Passive.Any(e => e.HookID == EHookID.Melee_OnChargeContact), Is.False,
            "3.5's impact-dice reduction is Brace's now - Counter in Melee must not carry it.");
        Assert.That(counterInMelee.Description, Is.EqualTo("This model gets Counter on its weapons in melee."));
    }

    [Test]
    public void CounterInMelee_IsAGdfName_SoTheAofFileNoLongerRedefinesIt()
    {
        // GDF 3.6 books name it too (Daemons of Lust, Elven Jesters, Rebel Guerrillas, Voidspace Stalkers), and
        // GDF books bake against the GDF file alone; AoF merges GDF first, so one definition serves both.
        Assert.That(Load("AofRuleSupplement.json").Any(r => r.Name == "Counter in Melee"), Is.False);
    }

    [Test]
    public void CounterInMeleeAura_GrantsCounter_ToTheWholeUnit()
    {
        SpecialRuleDefinition aura = Gdf("Counter in Melee Aura");

        HookEntry entry = aura.Passive.Single();
        Assert.That(entry.HookID, Is.EqualTo(EHookID.Lifecycle_OnUnitCreated));
        Assert.That(entry.Effect, Is.EqualTo(new Effect.Aura("Counter")),
            "grants the core rule by its exact name - the resolver is case-sensitive");
        Assert.That(CoreRuleCatalog.CreateResolver().TryResolve("Counter", out _), Is.True);
        Assert.That(aura.Description, Is.EqualTo("This model and its unit get Counter on its weapons in melee."));
    }

    [Test]
    public void CounterInMeleeBuff_GrantsCounterInMelee_LikeTheOtherMeleeBuffs()
    {
        SpecialRuleDefinition buff = Gdf("Counter in Melee Buff");
        ActivatedAbility bane = Gdf("Bane in Melee Buff").Activated.Single();

        ActivatedAbility ability = buff.Activated.Single();
        Assert.That(buff.Passive, Is.Empty);
        Assert.That(ability.TriggerHook, Is.EqualTo(bane.TriggerHook), "once per activation, before attacking");
        Assert.That(ability.Cost, Is.EqualTo(bane.Cost));
        Assert.That(ability.TargetSelector, Is.EqualTo(bane.TargetSelector), "one friendly unit within 12\"");
        Assert.That(ability.Effect, Is.EqualTo(new Effect.AddRule("Counter in Melee", ELifetime.NextTrigger)),
            "until it is next charged (#422 decision: the sibling Buffs' 'once (next time the effect would apply)')");
        Assert.That(buff.Description, Does.Contain("which gets Counter in Melee"));
    }

    [Test]
    public void BraceInMelee_IsBraceOne_OnItsWeapons()
    {
        SpecialRuleDefinition braceInMelee = Aof("Brace in Melee");
        HookEntry brace = CoreRuleCatalog.Brace.Passive.Single();

        Assert.That(braceInMelee.Scope, Is.EqualTo(ERuleScope.Weapon));
        Assert.That(braceInMelee.EngineArgumentCount, Is.EqualTo(0), "the X is fixed at 1 - no argument");
        HookEntry entry = braceInMelee.Passive.Single();
        Assert.That(entry.HookID, Is.EqualTo(brace.HookID));
        Assert.That(entry.Seat, Is.EqualTo(brace.Seat));
        Assert.That(entry.Condition, Is.EqualTo(brace.Condition).And.EqualTo(NotFatigued));
        Assert.That(entry.Effect, Is.EqualTo(new Effect.CancelImpactDice(new ValueSource.Literal(1))));
        Assert.That(braceInMelee.Description, Is.EqualTo("This model gets Brace(1) on its weapons in melee."));
    }

    [Test]
    public void HeavyImpact_CarriesImpactsFatigueGate()
    {
        HookEntry entry = Gdf("Heavy Impact").Passive.Single();

        Assert.That(entry.Condition, Is.EqualTo(CoreRuleCatalog.Impact.Passive.Single().Condition)
            .And.EqualTo(NotFatigued), "\"Counts as having Impact(X)\" - so \"unless this unit is Fatigued\" too");
    }

    // The AoF item shape ("Defensive Stakes (Brace in Melee)"): the rule arrives at UNIT level and is re-homed onto
    // the unit's weapons at load. Three spearmen then cancel 3 of a charger's Impact(6) dice - and none while the
    // unit is Fatigued.
    [Test]
    public void BraceInMelee_ThroughArmyLoad_CancelsOneDiePerModel_UnlessFatigued()
    {
        UnitData stakes = LoadUnit(Aof("Brace in Melee"), modelCount: 3);
        Assert.That(stakes.Models.All(m => m.Weapons.Any(w => w.RuleDefinitions.Any(r => r.RequestedName == "Brace in Melee"))),
            Is.True, "precondition: the unit-level grant was re-homed onto every model's weapon");

        Assert.That(NetImpactDice(Charger(impact: 6), stakes), Is.EqualTo(3), "Impact(6) - Brace(1) x 3 models");

        FatigueUtilities.ApplyFatigued(stakes);
        Assert.That(NetImpactDice(Charger(impact: 6), stakes), Is.EqualTo(6), "a Fatigued unit cancels nothing");
    }

    [Test]
    public void CounterInMelee_ThroughArmyLoad_StrikesFirst_UnlessFatigued_AndCancelsNoDice()
    {
        UnitData counterUnit = LoadUnit(Gdf("Counter in Melee"), modelCount: 3);
        var evaluator = new RuleEvaluator(new ProbabilisticDiceRoller());
        UnitData charger = Charger(impact: 6);

        Assert.That(NetImpactDice(charger, counterUnit), Is.EqualTo(6), "Counter in Melee cancels no impact dice");
        Assert.That(StrikesFirst(evaluator, charger, counterUnit), Is.True);

        FatigueUtilities.ApplyFatigued(counterUnit);
        Assert.That(StrikesFirst(evaluator, charger, counterUnit), Is.False,
            "\"unless this unit is Fatigued\" - Counter in Melee is Counter");
    }

    // --- harness ---

    private static RuleParticipant[] DefenderParticipants(UnitData defender)
    {
        var participants = new List<RuleParticipant> { RuleParticipant.Subject(defender) };
        participants.AddRange(defender.GetMeleeWeapons().Select(w => RuleParticipant.Subject(defender, w)));
        return participants.ToArray();
    }

    private static int NetImpactDice(UnitData charger, UnitData defender)
    {
        var participants = new List<RuleParticipant> { RuleParticipant.Actor(charger) };
        participants.AddRange(DefenderParticipants(defender));
        var sink = new ImpactSink();
        sink.ApplyFrom(new RuleEvaluator(new ProbabilisticDiceRoller())
            .EvaluateAll(new ChargeContactContext(charger, defender), participants.ToArray()));
        return sink.TotalDice;
    }

    private static bool StrikesFirst(RuleEvaluator evaluator, UnitData charger, UnitData defender) =>
        evaluator.EvaluateAll(new CounterTriggerContext(charger, defender), DefenderParticipants(defender))
            .OfType<RuleOperation.StrikeFirst>().Any();

    /// <summary>The real launch path: the list embeds the shipped definition and names it at unit level.</summary>
    private static UnitData LoadUnit(SpecialRuleDefinition shipped, int modelCount)
    {
        var army = new ArmyListFile
        {
            Name = "422 shipped-data list",
            Faction = string.Empty,
            RuleDefinitions = new List<SpecialRuleDefinition> { shipped },
            Units =
            {
                new UnitFileEntry
                {
                    Name = "Spearmen", ModelCount = modelCount, Quality = 4, Defense = 4,
                    SpecialRules = { new SpecialRuleEntry_Core(shipped.Name) },
                    Weapons = { new WeaponFileEntry { Name = "Spear", Quantity = modelCount, Attacks = 1 } },
                },
            },
        };

        GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
        RuleResolver resolver = CoreRuleCatalog.CreateResolver();
        ArmyListRuleResolution.RegisterEmbeddedDefinitions(resolver, army);
        GameBootstrap.CreateArmy(new PlayerID(Guid.NewGuid()), army, store, resolver);
        return store.GetAllValues<UnitData>().Single();
    }

    private static UnitData Charger(int impact)
    {
        GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var model = new ModelData(baseRadiusInches: 0.75f, weapons: new List<Weapon> { new Weapon("Horns", 0f, 1, 0) },
            initialPosition: new Position(0, 0), gameDataStore: store);
        var unit = new UnitData(new PlayerID(Guid.NewGuid()), "Charger", quality: 4, defense: 4,
            modelBindings: new List<DataBinding<ModelData>> { store.GetDataBinding<ModelData>(store.Create(model)) });
        unit.AttachRuleDefinition(new ResolvedRule("Impact", CoreRuleCatalog.Impact,
            new RuleArgument[] { new RuleArgument.Int(impact) }));
        return unit;
    }
}
