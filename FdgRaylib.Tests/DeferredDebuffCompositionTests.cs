using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FDG;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Data;
using FDG.Utilities;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #197 P6 - the deferred buff/debuff family ("pick one unit, which gets X once, next time the effect
// would apply"): Morale / Defense / Speed / Piercing / Casting Debuff plus Speed Buff and Casting Buff.
//
// The same gap BoostRuleCompositionTests was written for applies here. --validate-rules checks structure
// and RuleFireLint proves an entry CAN fire, but neither proves the op is consumed at the seam the author
// had in mind, nor which SIDE of an attack it lands on. Two of these rules are a seat away from being the
// opposite of what the corpus says:
//
//   * "Piercing Debuff Effect" is Fortified's mechanism (reduceArmorPenetration) on the ACTOR seat. On the
//     Subject seat the identical entry would PROTECT the debuffed unit instead of blunting its attacks -
//     a bug no structural check can see.
//   * "Speed Debuff"/"Speed Buff" grant Slow/Fast at NextTrigger. If the grant were consumed by the
//     read-only movement budget projection instead of by ExecuteMoveStage it would evaporate before the
//     move it is meant to shorten.
//
// These drive the real shipped supplement through the real evaluator and the real sinks.
[TestFixture]
public class DeferredDebuffCompositionTests
{
    private static IReadOnlyList<SpecialRuleDefinition> Supplement() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Books", "GdfRuleSupplement.json")));

    private static SpecialRuleDefinition Definition(string name) =>
        Supplement().Single(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>A live resolver + evaluator over the real supplement, plus the core catalog for the
    /// rules the supplement GRANTS (Slow, Fast) - the same shape BoostRuleCompositionTests uses.</summary>
    private sealed class Harness
    {
        private readonly GameDataStore _store = GameDataStore.GameDataStoreBuilder.GetDefault();
        private readonly RuleResolver _resolver = CoreRuleCatalog.CreateResolver();
        private readonly RuleEvaluator _evaluator;

        public Harness(params string[] supplementRuleNames)
        {
            var byName = Supplement().ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);
            foreach (string name in supplementRuleNames) _resolver.Register(byName[name]);
            _evaluator = new RuleEvaluator(new ProbabilisticDiceRoller(), ruleResolver: _resolver);
        }

        public IUnit BuildUnit(string name, params string[] ruleNames)
        {
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                initialPosition: new Position(), gameDataStore: _store);
            DataBinding<ModelData> modelBinding = _store.GetDataBinding<ModelData>(_store.Create(model));

            var unit = new UnitData(new PlayerID(Guid.NewGuid()), name, quality: 4, defense: 4,
                modelBindings: new List<DataBinding<ModelData>> { modelBinding });
            foreach (string ruleName in ruleNames) unit.AttachRuleDefinition(_resolver.Resolve(ruleName));
            return unit;
        }

        public IReadOnlyList<RuleOperation> Evaluate(IUnit unit, ERuleSeat seat, IHookContext context) =>
            seat == ERuleSeat.Actor
                ? _evaluator.EvaluateAll(context, RuleParticipant.Actor(unit))
                : _evaluator.EvaluateAll(context, RuleParticipant.Subject(unit));
    }

    private static DiceResults Faces(params int[] faces)
    {
        var perSide = new float[6];
        foreach (int face in faces) perSide[face - 1] += 1f;
        return new DiceResults(perSide);
    }

    // ---- Piercing Debuff: the seat is the whole rule ------------------------------------------------

    [Test]
    public void PiercingDebuffEffect_BluntsTheBearersOwnAttacks_NotAttacksAgainstIt()
    {
        var harness = new Harness("Piercing Debuff Effect");
        IUnit debuffed = harness.BuildUnit("Debuffed", "Piercing Debuff Effect");
        IUnit other = harness.BuildUnit("Other");

        int AsAttacker() => harness
            .Evaluate(debuffed, ERuleSeat.Actor, new HitRollCompleteContext(debuffed, other, Faces(4)))
            .OfType<RuleOperation.ReduceArmorPenetration>().Sum(op => op.Amount);

        int AsDefender() => harness
            .Evaluate(debuffed, ERuleSeat.Subject, new HitRollCompleteContext(other, debuffed, Faces(4)))
            .OfType<RuleOperation.ReduceArmorPenetration>().Sum(op => op.Amount);

        Assert.That(AsAttacker(), Is.EqualTo(1),
            "the debuffed unit's own attacks lose 1 AP - this is the Actor seat, not Fortified's.");
        Assert.That(AsDefender(), Is.EqualTo(0),
            "on the Subject seat this same entry would be Fortified: a BUFF on the unit it is aimed at.");
    }

    [Test]
    public void PiercingDebuffEffect_AppliesInMeleeToo()
    {
        // "loses AP(+1) when attacking" - not "when shooting". The neighbouring "Corrode Weapons Effect"
        // in the same book gates on not(isMelee); this one deliberately does not.
        var harness = new Harness("Piercing Debuff Effect");
        IUnit debuffed = harness.BuildUnit("Debuffed", "Piercing Debuff Effect");
        IUnit other = harness.BuildUnit("Other");

        var melee = new HitRollCompleteContext(debuffed, other, Faces(4), IsMelee: true);
        int reduction = harness.Evaluate(debuffed, ERuleSeat.Actor, melee)
            .OfType<RuleOperation.ReduceArmorPenetration>().Sum(op => op.Amount);

        Assert.That(reduction, Is.EqualTo(1), "melee attacks lose the AP too.");
    }

    // ---- Speed Debuff / Speed Buff: the granted rule must net the corpus numbers --------------------

    [TestCase("Speed Debuff", "Slow", -2f, -4f)]
    [TestCase("Speed Buff", "Fast", +2f, +4f)]
    public void SpeedRules_GrantACoreRuleNettingTheCorpusDistances(
        string ruleName, string grantedRule, float advance, float rushAndCharge)
    {
        SpecialRuleDefinition definition = Definition(ruleName);
        Assert.That(definition.Activated.Single().Effect,
            Is.EqualTo(new Effect.AddRule(grantedRule, ELifetime.NextTrigger)),
            $"'{ruleName}' grants '{grantedRule}' for one move only.");

        var harness = new Harness(ruleName);
        IUnit target = harness.BuildUnit("Target", grantedRule);

        float Net(EActionType action)
        {
            var sink = new MovementModifierSink();
            sink.ApplyFrom(harness.Evaluate(target,
                ERuleSeat.Actor, new MoveActionDeclaredContext(target, action, BaseDistanceInches: 6f)));
            return sink.Net(action);
        }

        Assert.That(Net(EActionType.Advance), Is.EqualTo(advance));
        Assert.That(Net(EActionType.Rush), Is.EqualTo(rushAndCharge));
        Assert.That(Net(EActionType.Charge), Is.EqualTo(rushAndCharge));
    }

    // ---- The granted one-shot modifiers reach their carrier token types -----------------------------

    // Each of these grants a StatModifier the relevant roll stage reads back later. The roll kind decides
    // the carrier TokenType, so a wrong kind here is a modifier that silently never applies (Casting
    // Debuff is the one that needed a new carrier - ERollKind.Cast - built for this slice).
    [TestCase("Morale Debuff", ERollKind.Morale, -1)]
    [TestCase("Defense Debuff", ERollKind.Save, -1)]
    [TestCase("Casting Debuff", ERollKind.Cast, -1)]
    [TestCase("Casting Buff", ERollKind.Cast, +1)]
    public void StatModifierRules_CarryTheRightRollKindAndSign(string ruleName, ERollKind roll, int delta)
    {
        Effect effect = Definition(ruleName).Activated.Single().Effect;

        Assert.That(effect, Is.EqualTo(new Effect.StatModifier(roll, delta, ELifetime.NextTrigger)));
    }

    // Every roll kind must map to its OWN carrier token: two kinds sharing one would make a Defense Debuff
    // silently hinder casting as well (and be spent by whichever roll came first).
    [Test]
    public void EveryRollKind_HasItsOwnCarrierToken()
    {
        ERollKind[] kinds = Enum.GetValues<ERollKind>();
        TokenType[] carriers = kinds.Select(RollModifierTokens.TypeFor).ToArray();

        Assert.That(carriers, Is.Unique, "roll-kind carriers must not merge in the token container.");
        Assert.That(carriers, Has.Length.EqualTo(kinds.Length),
            "RollModifierTokens.TypeFor must map every roll kind - it throws on an unmapped one.");
    }

    // ---- The offer shape the corpus text describes --------------------------------------------------

    // "Once per activation, before attacking, pick one [enemy within 18in in line of sight / friendly
    // within 12in]". A wrong hook or a missing once-per-activation cost turns a one-shot debuff into a
    // repeatable one; a wrong affinity aims it at the wrong army.
    [TestCase("Morale Debuff", ETargetAffinity.Foe, 18f, true, null)]
    [TestCase("Defense Debuff", ETargetAffinity.Foe, 18f, true, null)]
    [TestCase("Speed Debuff", ETargetAffinity.Foe, 18f, true, null)]
    [TestCase("Piercing Debuff", ETargetAffinity.Foe, 18f, true, null)]
    [TestCase("Casting Debuff", ETargetAffinity.Foe, 18f, true, "Caster")]
    [TestCase("Speed Buff", ETargetAffinity.Friend, 12f, false, null)]
    [TestCase("Casting Buff", ETargetAffinity.Friend, 12f, false, "Caster")]
    public void DeferredModifierRules_OfferOnceBeforeAttacking_AtTheCorpusRangeAndAffinity(
        string ruleName, ETargetAffinity affinity, float rangeInches, bool needsLineOfSight,
        string? requiredRule)
    {
        ActivatedAbility ability = Definition(ruleName).Activated.Single();

        Assert.That(ability.TriggerHook, Is.EqualTo(EHookID.Activation_OnPreAttack),
            "'before attacking' is the pre-attack hook.");
        Assert.That(ability.Cost, Is.InstanceOf<Cost.OncePerActivation>());
        Assert.That(ability.TargetSelector!.TargetAffinity, Is.EqualTo(affinity));
        Assert.That(ability.TargetSelector.RangeInches, Is.EqualTo(rangeInches));
        Assert.That(ability.TargetSelector.RequireLineOfSight, Is.EqualTo(needsLineOfSight));
        Assert.That(ability.TargetSelector.MaxCount, Is.EqualTo(1), "'pick one unit'.");
        Assert.That(ability.TargetSelector.RequiredRule, Is.EqualTo(requiredRule),
            "the casting pair only sees targets that can actually cast.");
    }
}
