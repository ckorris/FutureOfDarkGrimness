using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FDG;
using FDG.ArmyBuilding;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FDG.Utilities;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #197 Versatile Defense (21 refs) - the app-side half, over the DATA as shipped rather than a hand-built
// stand-in. The engine's VersatileDefenseRuleIntegrationTests prove the mechanism; these prove the
// supplement authors it the way that mechanism expects, which --validate-rules and RuleFireLint cannot:
// the first checks structure, the second proves an entry CAN fire, neither checks what a player nets or
// which lifetime the grant carries.
//
// The two effects are Sturdy's and Changebound's bodies verbatim, so a regression here is most likely a
// wrong hook (a Hit delta emitted after the dice are rolled nets zero) or a dropped distance gate.
[TestFixture]
public class VersatileDefenseShippedDataTests
{
    private const string RuleName = "Versatile Defense";
    private const string AuraName = "Versatile Defense Aura";
    private const string GuardHelper = "Versatile Defense (Guard)";
    private const string EvasionHelper = "Versatile Defense (Evasion)";

    // Far enough to satisfy the ">9 inches" gate; near enough to fail it.
    private const float Far = 24f;
    private const float Near = 3f;

    private static IReadOnlyList<SpecialRuleDefinition> Supplement() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Books", "GdfRuleSupplement.json")));

    private static SpecialRuleDefinition Definition(string name) =>
        Supplement().Single(r => r.Name == name);

    [Test]
    public void TheRule_OffersBothEffects_AtBothOfItsTriggerHooks()
    {
        IReadOnlyList<ActivatedAbility> abilities = Definition(RuleName).Activated;

        Assert.That(abilities.Where(a => a.TriggerHook == EHookID.Deployment_OnUnitDeployed)
                .Select(a => a.Label),
            Is.EqualTo(new[] { "+1 to defense rolls", "-1 to enemy hit rolls" }),
            "'when a unit ... is deployed OR activated' - the deployment arm is half the rule.");
        Assert.That(abilities.Where(a => a.TriggerHook == EHookID.Activation_OnActivationStart)
                .Select(a => a.Label),
            Is.EqualTo(new[] { "+1 to defense rolls", "-1 to enemy hit rolls" }));
    }

    [Test]
    public void TheDeploymentArmIsFree_AndTheActivationArmIsGated()
    {
        IReadOnlyList<ActivatedAbility> abilities = Definition(RuleName).Activated;

        Assert.That(abilities.Where(a => a.TriggerHook == EHookID.Deployment_OnUnitDeployed)
                .Select(a => a.Cost), Has.All.InstanceOf<Cost.Free>(),
            "the once-per-X 'used' marker is keyed on the RULE name, so a gate paid at deployment would " +
            "still be shut at the unit's first activation - it must cost nothing here.");
        Assert.That(abilities.Where(a => a.TriggerHook == EHookID.Activation_OnActivationStart)
                .Select(a => a.Cost), Has.All.InstanceOf<Cost.OncePerActivation>(),
            "and one pick per activation, which is what makes taking one effect spend the other.");
    }

    [Test]
    public void EveryEffect_IsGrantedUntilTheUnitsNextActivation()
    {
        IReadOnlyList<Effect.AddRule> grants = Definition(RuleName).Activated
            .Select(a => a.Effect).OfType<Effect.AddRule>().ToList();

        Assert.That(grants, Has.Count.EqualTo(4), "all four abilities grant a helper rule.");
        Assert.That(grants.Select(g => g.Scope), Has.All.EqualTo(ELifetime.UntilNextActivation),
            "'this effect lasts until the units' next activation' - ThisActivation would die before the " +
            "opponent ever shoots, ThisRound at the wrong moment.");
        Assert.That(grants.Select(g => g.RuleName).Distinct(),
            Is.EquivalentTo(new[] { GuardHelper, EvasionHelper }));
    }

    [Test]
    public void TheChoice_IsGatedOnEveryModelHavingTheRule()
    {
        Assert.That(Definition(RuleName).Activated.Select(a => a.AvailableWhen),
            Has.All.InstanceOf<Condition.AllModelsHaveThisRule>(),
            "'a unit where ALL models have this rule' - the corpus puts the gate on the CHOICE here, " +
            "not on the effect, because a unit-held grant satisfies the effect's own gate vacuously.");
    }

    [Test]
    public void TheAura_ConfersTheRuleItself()
    {
        HookEntry entry = Definition(AuraName).Passive.Single();

        Assert.That(entry.HookID, Is.EqualTo(EHookID.Lifecycle_OnUnitCreated));
        Assert.That(entry.Effect, Is.InstanceOf<Effect.Aura>());
        Assert.That(((Effect.Aura)entry.Effect).RuleName, Is.EqualTo(RuleName),
            "every one of the 21 corpus references is the Aura form (the Icon of Havoc wargear option), " +
            "so a broken link here makes the whole rule unreachable in play.");
    }

    [Test]
    public void Guard_NetsPlusOneDefense_OnlyBeyondNineInches()
    {
        var harness = new Harness(GuardHelper);
        IUnit defender = harness.BuildUnit("P1", GuardHelper);
        IUnit attacker = harness.BuildUnit("P2");

        Assert.That(harness.NetSave(attacker, defender, Far), Is.EqualTo(1));
        Assert.That(harness.NetSave(attacker, defender, Near), Is.EqualTo(0),
            "'when shot or charged from over 9 inches away'.");
    }

    [Test]
    public void Evasion_NetsMinusOneToEnemyHitRolls_OnlyBeyondNineInches()
    {
        var harness = new Harness(EvasionHelper);
        IUnit defender = harness.BuildUnit("P1", EvasionHelper);
        IUnit attacker = harness.BuildUnit("P2");

        Assert.That(harness.NetHit(attacker, defender, Far), Is.EqualTo(-1));
        Assert.That(harness.NetHit(attacker, defender, Near), Is.EqualTo(0));
    }

    [Test]
    public void TheEffects_ReachTheUnit_WhenGrantedThroughTheRealRule()
    {
        // End to end over shipped data: the aura confers the rule, the rule's activation-start ability is
        // offered, and accepting it lands a grant the evaluator reads back into a live save-roll bonus.
        var harness = new Harness(AuraName, RuleName, GuardHelper, EvasionHelper);
        IUnit defender = harness.BuildUnit("P1", AuraName);
        IUnit attacker = harness.BuildUnit("P2");

        harness.Apply(harness.Evaluate(defender, ERuleSeat.Actor, new UnitCreatedContext(defender)));

        AbilityOffer guard = harness.Offers(new ActivationStartContext(defender))
            .Single(o => o.Ability.Label == "+1 to defense rolls");
        harness.Apply(harness.Accept(guard, defender));

        Assert.That(harness.NetSave(attacker, defender, Far), Is.EqualTo(1),
            "aura -> rule -> chosen effect -> a modifier the save stage actually reads.");
        Assert.That(harness.NetHit(attacker, defender, Far), Is.EqualTo(0),
            "and the effect NOT chosen confers nothing.");
    }

    /// <summary>Minimal stand-in for the engine's internal TestRuleHarness (the BoostRuleCompositionTests
    /// precedent): a live resolver + evaluator carrying the real shipped definitions, so what is asserted
    /// is the data as authored.</summary>
    private sealed class Harness
    {
        private readonly GameDataStore _store = GameDataStore.GameDataStoreBuilder.GetDefault();
        private readonly RuleResolver _resolver = new();
        private readonly RuleEvaluator _evaluator;

        public Harness(params string[] ruleNames)
        {
            var byName = Supplement().ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);
            foreach (string name in ruleNames) _resolver.Register(byName[name]);
            _evaluator = new RuleEvaluator(new ProbabilisticDiceRoller(), ruleResolver: _resolver);
        }

        public IUnit BuildUnit(string playerName, params string[] ruleNames)
        {
            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 2; i++)
            {
                var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                    initialPosition: new Position(), gameDataStore: _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(new PlayerID(Guid.NewGuid()), playerName, quality: 4, defense: 4,
                modelBindings: modelBindings);
            foreach (string name in ruleNames) unit.AttachRuleDefinition(_resolver.Resolve(name));
            return unit;
        }

        public IReadOnlyList<RuleOperation> Evaluate(IUnit unit, ERuleSeat seat, IHookContext context) =>
            seat == ERuleSeat.Actor
                ? _evaluator.EvaluateAll(context, RuleParticipant.Actor(unit))
                : _evaluator.EvaluateAll(context, RuleParticipant.Subject(unit));

        public IReadOnlyList<AbilityOffer> Offers(IHookContext context) => _evaluator.GatherOffers(context);

        public IReadOnlyList<RuleOperation> Accept(AbilityOffer offer, IUnit target) =>
            _evaluator.ResolveAbility(offer, new[] { target });

        public void Apply(IReadOnlyList<RuleOperation> operations) =>
            OperationApplier.ApplyTokenOperations(operations);

        public int NetSave(IUnit attacker, IUnit defender, float distanceInches)
        {
            var sink = new RollModifierSink();
            sink.ApplyFrom(Evaluate(defender, ERuleSeat.Subject,
                new HitRollCompleteContext(attacker, defender, Faces(4), distanceInches)));
            return sink.Net(ERollKind.Save);
        }

        public int NetHit(IUnit attacker, IUnit defender, float distanceInches)
        {
            var sink = new RollModifierSink();
            sink.ApplyFrom(Evaluate(defender, ERuleSeat.Subject,
                new HitRollModifierContext(attacker, defender, distanceInches)));
            return sink.Net(ERollKind.Hit);
        }

        private static DiceResults Faces(params int[] faces)
        {
            var perSide = new float[6];
            foreach (int face in faces) perSide[face - 1] += 1f;
            return new DiceResults(perSide);
        }
    }
}
