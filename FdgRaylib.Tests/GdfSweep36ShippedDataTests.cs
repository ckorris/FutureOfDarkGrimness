using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.ArmyBuilding;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #434 - the OPR 3.6 GDF supplement sweep as SHIPPED in GdfRuleSupplement.json. The mechanics of the shapes it added are
// pinned engine-side (GdfSweep36RuleIntegrationTests, against hand-built copies in GdfSweep36Definitions); this fixture
// pins that the authored JSON carries the 3.6.0 book text verbatim for every name the sweep touched, that each new or
// re-authored definition is exactly the tested shape (or, for a name that is another rule's 3.6 twin, exactly that
// rule's entries), that every Aura / Buff grants a name that resolves, and that the spell-phrase aliases (the names the
// 3.6 spell text produces, "X on its/their weapons ...") are their canonical rules entry for entry.
[TestFixture]
public class GdfSweep36ShippedDataTests
{
    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static List<SpecialRuleDefinition> Gdf() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(Path.Combine(BooksDirectory, "GdfRuleSupplement.json")));

    private static List<SpecialRuleDefinition> Aof() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(Path.Combine(BooksDirectory, "AofRuleSupplement.json")));

    private static SpecialRuleDefinition Shipped(string name) => Gdf().Single(r => r.Name == name);

    private static SpecialRuleDefinition Catalog(string name) =>
        CoreRuleCatalog.All.Single(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

    private static readonly Condition MostModels = new Condition.MostModelsHaveThisRule();
    private static readonly Condition NotSpell = new Condition.IsNotSpell();

    // --- the 3.6.0 text, verbatim (ASCII-folded) ---------------------------------------------------------------

    private static IEnumerable<TestCaseData> Texts()
    {
        yield return new TestCaseData("Battleborn", "If a unit where most models have this rule is Shaken at the beginning of the round, roll one die. On a 4+, it stops being Shaken.").SetArgDisplayNames("Battleborn");
        yield return new TestCaseData("Honor Code", "If a unit where most models have this rule is Shaken at the beginning of the round, roll one die. On a 4+, it stops being Shaken.").SetArgDisplayNames("Honor Code");
        yield return new TestCaseData("Steadfast", "If a unit where most models have this rule is Shaken at the beginning of the round, roll one die. On a 4+, it stops being Shaken.").SetArgDisplayNames("Steadfast");
        yield return new TestCaseData("Dangerous Terrain Debuff", "Once per activation, before attacking, pick one enemy unit within 18\" in line of sight, which counts as being in Dangerous terrain once (next time the effect would apply).").SetArgDisplayNames("Dangerous Terrain Debuff");
        yield return new TestCaseData("Defense Debuff", "Once per activation, before attacking, pick one enemy unit within 18\" in line of sight, which gets -1 to Defense rolls once (next time the effect would apply).").SetArgDisplayNames("Defense Debuff");
        yield return new TestCaseData("Defensive Frenzy", "If most models in this unit have this rule, place one token on this unit when it fully destroys an enemy unit, up to a max. of two tokens. For each token this unit gets +1 to Defense rolls until the end of the game.").SetArgDisplayNames("Defensive Frenzy");
        yield return new TestCaseData("Defensive Growth", "If most models in this unit have this rule, place one token on it at the beginning of each round if it's on the table and isn't Shaken, up to a max. of four tokens. For every two tokens, this unit gets +1 to Defense rolls.").SetArgDisplayNames("Defensive Growth");
        yield return new TestCaseData("Fatigue Debuff", "Once per activation, before attacking, pick one enemy unit within 18\" in line of sight, which must take a Morale Test. If failed, it becomes Fatigued.").SetArgDisplayNames("Fatigue Debuff");
        yield return new TestCaseData("Inquisitorial Agent", "Once per game, if all models in this unit have this rule, it may be activated even if it had already activated this round (stops being Fatigued when activated again). Only up to one third of the units in the army with this rule at the beginning of the game (rounding up) may use it in a single round.").SetArgDisplayNames("Inquisitorial Agent");
        yield return new TestCaseData("Melee Slayer", "When charging, this model's weapons get AP(+2) if most models in the target have Tough(3) or higher.").SetArgDisplayNames("Melee Slayer");
        yield return new TestCaseData("Morale Debuff", "Once per activation, before attacking, pick one enemy unit within 18\" in line of sight, which gets -1 to Morale Test rolls once (next time the effect would apply).").SetArgDisplayNames("Morale Debuff");
        yield return new TestCaseData("No Retreat", "When a unit where most models have this rule fails a Morale Test that causes it to be Shaken or Routed, the test counts as passed instead. Then, roll as many dice as the number of wounds it would take to fully destroy it, and for each result of 1-3 the unit takes one wound, which can't be ignored.").SetArgDisplayNames("No Retreat");
        yield return new TestCaseData("Piercing Debuff", "Once per activation, before attacking, pick one enemy unit within 18\" in line of sight, which loses AP(1) on its weapons when attacking once (next time the effect would apply).").SetArgDisplayNames("Piercing Debuff");
        yield return new TestCaseData("Piercing Frenzy", "Place one token on this unit when it fully destroys an enemy unit, up to a max. of two tokens. For each token all models with this rule in this unit get AP(+1) on their weapons when attacking until the end of the game.").SetArgDisplayNames("Piercing Frenzy");
        yield return new TestCaseData("Piercing Growth", "Place one token on this unit at the beginning of each round if it's on the table and isn't Shaken, up to a max. of four tokens. For every two tokens, models with this rule in this unit get AP(+1) on their weapons.").SetArgDisplayNames("Piercing Growth");
        yield return new TestCaseData("Precision Frenzy", "Place one token on this unit when it fully destroys an enemy unit, up to a max. of two tokens. For each token all models with this rule in this unit get +1 to hit rolls until the end of the game.").SetArgDisplayNames("Precision Frenzy");
        yield return new TestCaseData("Precision Growth", "Place one token on this unit at the beginning of each round if it's on the table and isn't Shaken, up to a max. of four tokens. For every two tokens, models with this rule in this unit get +1 to hit rolls.").SetArgDisplayNames("Precision Growth");
        yield return new TestCaseData("Piercing Spotter", "Once per activation, before attacking, pick one enemy unit within 30\" and in line of sight of this model and roll one die, on a 4+ place a token on it. When attacking, friendly units may remove tokens from their target before rolling to block to get +AP(X) on their weapons, where X is the number of removed tokens.").SetArgDisplayNames("Piercing Spotter");
        yield return new TestCaseData("Precision Spotter", "Once per activation, before attacking, pick one enemy unit within 30\" and in line of sight of this model and roll one die, on a 4+ place a token on it. Friendly units may remove tokens from their target before rolling to hit to get +X to hit rolls when attacking, where X is the number of removed tokens.").SetArgDisplayNames("Precision Spotter");
        yield return new TestCaseData("Plaguebound", "When a unit where most models have this rule takes wounds, roll one die for each. On a 6+ it is ignored.").SetArgDisplayNames("Plaguebound");
        yield return new TestCaseData("Self-Repair", "When a unit where most models have this rule takes wounds, roll one die for each. On a 6+ it is ignored.").SetArgDisplayNames("Self-Repair");
        yield return new TestCaseData("Rending Mark", "Once per activation, before attacking, pick one enemy unit within 18\" in line of sight, which friendly units gets Rending on their weapons against once (next time the effect would apply).").SetArgDisplayNames("Rending Mark");
        yield return new TestCaseData("Shred Mark", "Once per activation, before attacking, pick one enemy unit within 18\" in line of sight, which friendly units gets Shred on their weapons when attacking against once (next time the effect would apply).").SetArgDisplayNames("Shred Mark");
        yield return new TestCaseData("Unstoppable Mark", "Once per activation, before attacking, pick one enemy unit within 18\" in line of sight, which friendly units gets Unstoppable on their weapons against once (next time the effect would apply).").SetArgDisplayNames("Unstoppable Mark");
        yield return new TestCaseData("Rending when Shooting Aura", "This model and its unit get Rending on its weapons when shooting.").SetArgDisplayNames("Rending when Shooting Aura");
        yield return new TestCaseData("Targeting Visor", "When shooting at enemies over 9\" away, this model gets +1 to hit rolls.").SetArgDisplayNames("Targeting Visor");
        yield return new TestCaseData("Bane Mark", "Once per activation, before attacking, pick one enemy unit within 18\" in line of sight, which friendly units gets Bane on their weapons when attacking against once (next time the effect would apply).").SetArgDisplayNames("Bane Mark");
        yield return new TestCaseData("Bane in Melee Buff", "Once per activation, before attacking, pick one friendly unit within 12\", which gets Bane on its weapons in melee once (next time the effect would apply).").SetArgDisplayNames("Bane in Melee Buff");
        yield return new TestCaseData("Increased Shooting Range Buff", "Once per activation, before attacking, pick one friendly unit within 12\", which gets +6\" range on its weapons when shooting once (next time the effect would apply).").SetArgDisplayNames("Increased Shooting Range Buff");
        yield return new TestCaseData("Increased Shooting Range Mark", "Once per activation, before attacking, pick one enemy unit within 18\" in line of sight, which friendly units get +6\" range on their weapons when shooting against once (next time the effect would apply).").SetArgDisplayNames("Increased Shooting Range Mark");
        yield return new TestCaseData("Indirect Mark", "Once per activation, before attacking, pick one enemy unit within 18\" in line of sight, which friendly units gets Indirect on their weapons against once (next time the effect would apply).").SetArgDisplayNames("Indirect Mark");
        yield return new TestCaseData("Indirect when Shooting Aura", "This model and its unit get Indirect on its weapons when shooting.").SetArgDisplayNames("Indirect when Shooting Aura");
        yield return new TestCaseData("Piercing Fighter Aura", "This model and its unit get AP(+1) on its weapons in melee.").SetArgDisplayNames("Piercing Fighter Aura");
        yield return new TestCaseData("Piercing Shooter Aura", "This model and its unit get AP(+1) on its weapons when shooting.").SetArgDisplayNames("Piercing Shooter Aura");
        yield return new TestCaseData("Piercing Shooting Mark", "Once per activation, before attacking, pick one enemy unit within 18\" in line of sight, which friendly units gets AP(+1) on their weapons when shooting against once (next time the effect would apply).").SetArgDisplayNames("Piercing Shooting Mark");
        yield return new TestCaseData("Difficult Terrain Debuff", "Once per activation, before attacking, pick one enemy unit within 18\" in line of sight, which counts as being in Difficult terrain once (next time the effect would apply).").SetArgDisplayNames("Difficult Terrain Debuff");
        yield return new TestCaseData("Entrenched", "If most models in this unit have this rule, as long as they haven't moved during this round, enemy units shooting at them from over 12\" away get -1 to hit rolls.").SetArgDisplayNames("Entrenched");
        yield return new TestCaseData("Fortified Growth", "If most models in this unit have this rule, place one token on it at the beginning of each round if it's on the table and isn't Shaken, up to a max. of four tokens. For every two tokens, enemies attacking this unit get AP(-1) to a min. of AP(0).").SetArgDisplayNames("Fortified Growth");
        yield return new TestCaseData("Grounded Precision", "If most models in this unit are within 1\" of terrain, this model gets +1 to hit rolls when attacking.").SetArgDisplayNames("Grounded Precision");
        yield return new TestCaseData("Guardian", "When units where most models have this rule take hits, those hits count as having AP(-1), to a min. of AP(0).").SetArgDisplayNames("Guardian");
        yield return new TestCaseData("Primeborn", "When units where most models have this rule take hits, those hits count as having AP(-1), to a min. of AP(0).").SetArgDisplayNames("Primeborn");
        yield return new TestCaseData("Sergeant", "This model gets AP(+1) when attacking.").SetArgDisplayNames("Sergeant");
        yield return new TestCaseData("Shatter", "For each unmodified result of 6 to hit, those hits get AP(+3).").SetArgDisplayNames("Shatter");
        yield return new TestCaseData("Speed Feat", "Once per game, when this unit moves, you may use this rule so that all models with this rule in it move +2\" when using Advance actions and +4\" when using Rush/Charge actions.").SetArgDisplayNames("Speed Feat");
        yield return new TestCaseData("Speed Debuff", "Once per activation, before attacking, pick one enemy model within 18\" in line of sight, which moves -2\" when using Advance actions and -4\" when using Rush/Charge actions once (next time the effect would apply).").SetArgDisplayNames("Speed Debuff");
        yield return new TestCaseData("Thrust in Melee Aura", "This model and its unit get Thrust on its weapons in melee.").SetArgDisplayNames("Thrust in Melee Aura");
        yield return new TestCaseData("Thrust in Melee", "This model gets Thrust on its weapons in melee.").SetArgDisplayNames("Thrust in Melee");
        yield return new TestCaseData("Thrust in Melee Buff", "Once per activation, before attacking, pick one friendly unit within 12\", which gets Thrust in Melee once (next time the effect would apply).").SetArgDisplayNames("Thrust in Melee Buff");
        yield return new TestCaseData("Unwieldy Debuff", "Once per activation, before attacking, pick one enemy unit within 18\" in line of sight, which gets Unwieldy on its weapons in melee once (next time the effect would apply).").SetArgDisplayNames("Unwieldy Debuff");
        yield return new TestCaseData("Bravery Aura", "This model and its unit get +1 to Morale Test rolls.").SetArgDisplayNames("Bravery Aura");
        yield return new TestCaseData("Bravery Buff", "Once per activation, before attacking, pick one friendly unit within 12\", which gets +1 to Morale Test rolls once (next time the effect would apply).").SetArgDisplayNames("Bravery Buff");
        yield return new TestCaseData("Spell Resistance", "When a unit where most models have this rule takes wounds, roll one die for each. If the wound was from a spell, then it is ignored on a 2+. If the wound was not from a spell, then it is ignored on a 6+ instead.").SetArgDisplayNames("Spell Resistance");
        yield return new TestCaseData("Spell Resistance Aura", "This model and its unit get Spell Resistance.").SetArgDisplayNames("Spell Resistance Aura");
        yield return new TestCaseData("Dodge", "If most models in this unit have this rule, as long as they aren't Fatigued, enemy units get -1 to hit rolls in melee against them.").SetArgDisplayNames("Dodge");
        yield return new TestCaseData("Grounded Defense", "If a unit where most models have this rule has most of them within 1\" of terrain, they get +1 to Defense rolls.").SetArgDisplayNames("Grounded Defense");
        yield return new TestCaseData("Grounded Defense Aura", "This model and its unit get Grounded Defense.").SetArgDisplayNames("Grounded Defense Aura");
        yield return new TestCaseData("Grounded Evasion", "If a unit where most models have this rule has most of them within 1\" of terrain, enemies get -1 to hit rolls when attacking them.").SetArgDisplayNames("Grounded Evasion");
        yield return new TestCaseData("Piercing Charge", "This model gets AP(+1) on its weapons when charging.").SetArgDisplayNames("Piercing Charge");
        yield return new TestCaseData("Piercing Charge Aura", "This model and its unit get Piercing Charge.").SetArgDisplayNames("Piercing Charge Aura");
        yield return new TestCaseData("Piercing Marksman", "When shooting at enemies over 9\" away, this model's weapons get AP(+1).").SetArgDisplayNames("Piercing Marksman");
        yield return new TestCaseData("Piercing Marksman Aura", "This model and its unit get Piercing Marksman.").SetArgDisplayNames("Piercing Marksman Aura");
        yield return new TestCaseData("Precision Marksman", "When shooting at enemies over 9\" away, this model gets +1 to hit rolls.").SetArgDisplayNames("Precision Marksman");
        yield return new TestCaseData("Predator", "For each unmodified roll of 6 to hit when attacking, this model may roll +1 attack with that weapon. This rule doesn't apply to newly generated attacks.").SetArgDisplayNames("Predator");
        yield return new TestCaseData("Predator Aura", "This model and its unit get Predator.").SetArgDisplayNames("Predator Aura");
        yield return new TestCaseData("Rapid Rush Buff", "Once per activation, before attacking, pick one friendly unit within 12\", which gets Rapid Rush once (next time the effect would apply).").SetArgDisplayNames("Rapid Rush Buff");
        yield return new TestCaseData("Versatile Expert", "When this unit is activated, pick one effect: until the end of the activation all models with this rule in this unit either get AP(+1) when attacking, or get +1 to hit rolls when attacking.").SetArgDisplayNames("Versatile Expert");
        yield return new TestCaseData("Nimble", "When this unit is activated, you may move all models with this rule in it by up to D3\".").SetArgDisplayNames("Nimble");
        yield return new TestCaseData("Vicious", "When attacking, targets must re-roll unmodified Defense results of 6 when blocking hits from this model's weapons.").SetArgDisplayNames("Vicious");
        yield return new TestCaseData("Infiltrating Precision Strike", "Counts as having Infiltrate, and gets +1 to hit rolls in melee on the round in which it deploys via this rule.").SetArgDisplayNames("Infiltrating Precision Strike");
        yield return new TestCaseData("Bounding", "When this unit is activated, you may move all models with this rule in it by up to D3+1\".").SetArgDisplayNames("Bounding");
        yield return new TestCaseData("Dash", "Once per round, at the end of this unit's activation, you may move all models with this rule in it by up to D3+1\".").SetArgDisplayNames("Dash");
        yield return new TestCaseData("Rapid Blink", "When this unit is activated, you may move all models with this rule in it by up to D3\".").SetArgDisplayNames("Rapid Blink");
        yield return new TestCaseData("Wolfborn", "When this unit is activated, you may move all models with this rule in it by up to D3\".").SetArgDisplayNames("Wolfborn");
        yield return new TestCaseData("Ranged Slayer", "When using Hold actions and shooting, this model's weapons get AP(+2) if most models in the target have Tough(3) or higher.").SetArgDisplayNames("Ranged Slayer");
        yield return new TestCaseData("Takedown Shot", "Once per game, when this model shoots, it may make X extra attacks against the target at Quality 2+ with AP(2), Deadly(3), and Takedown.").SetArgDisplayNames("Takedown Shot");
        yield return new TestCaseData("Takedown Strike", "Once per game, when it's this model's turn to strike in melee, it may make X attacks at Quality 2+ with AP(2), Deadly(3), and Takedown.").SetArgDisplayNames("Takedown Strike");
        yield return new TestCaseData("Vengeance", "When this unit is fully destroyed, place as many Vengeance tokens on the unit that destroyed it as models with this rule in this unit at the beginning of the game. Friendly units get +X to hit rolls when attacking that unit, where X is the number of tokens on it.").SetArgDisplayNames("Vengeance");
        yield return new TestCaseData("Versatile Reach", "When this unit is activated, pick one effect: until the end of the activation, all models with this rule in this unit either get +4\" range when shooting or move +2\" when charging.").SetArgDisplayNames("Versatile Reach");
        yield return new TestCaseData("Regenerative Strength", "Place one token on this model when it ignores a wound. Once per round, when it's this model's turn to strike in melee, roll as many dice as tokens on it before attacking, unless this unit is Fatigued. For each 4+ the target takes one wound.").SetArgDisplayNames("Regenerative Strength");
        yield return new TestCaseData("Ravage Aura", "This model and its unit get Ravage(+1).").SetArgDisplayNames("Ravage Aura");
    }

    [TestCaseSource(nameof(Texts))]
    public void TheShippedDefinition_CarriesThe36Text(string name, string text)
    {
        Assert.That(Shipped(name).Description, Is.EqualTo(text), "the 3.6.0 book text, verbatim");
    }

    // --- the new / re-authored shapes ----------------------------------------------------------------------------

    // 3.6: "you may MOVE all models with this rule in it by up to D3(+1)"" - the move form of the activation reposition.
    [TestCase("Bounding", 1)]
    [TestCase("Rapid Blink", 0)]
    [TestCase("Wolfborn", 0)]
    [TestCase("Nimble", 0)]
    public void TheActivationRepositionFamily_IsAMove(string name, int plusInches)
    {
        Assert.That(Shipped(name).Passive.Single(), Is.EqualTo(new HookEntry(EHookID.Activation_OnActivationStart,
            MostModels, new Effect.RepositionAtActivation(new DiceExpression.D3(), plusInches, Move: true),
            ELifetime.ThisActivation)));
    }

    [Test]
    public void Dash_IsTheEndOfActivationMove()
    {
        ActivatedAbility dash = Shipped("Dash").Activated.Single();
        Assert.That(dash.TriggerHook, Is.EqualTo(EHookID.Activation_OnEndOfActivation));
        Assert.That(dash.Cost, Is.InstanceOf<Cost.OncePerRound>());
        Assert.That(dash.Effect, Is.EqualTo(new Effect.RepositionAtActivation(new DiceExpression.D3(), 1, Move: true)));
    }

    [Test]
    public void SpellResistance_IsTheTwoArmWoundIgnore_WithNoRangeGate()
    {
        Assert.That(Shipped("Spell Resistance").Passive, Is.EqualTo(new[]
        {
            new HookEntry(EHookID.Shooting_OnSaveRollComplete, new Condition.And(new Condition.IsSpell(), MostModels),
                new Effect.IgnoreWoundOnRoll(2), ELifetime.ThisAttack, ERuleSeat.Subject),
            new HookEntry(EHookID.Shooting_OnSaveRollComplete, new Condition.And(NotSpell, MostModels),
                new Effect.IgnoreWoundOnRoll(6), ELifetime.ThisAttack, ERuleSeat.Subject),
        }));
    }

    [Test]
    public void Dodge_IsMeleeEvasion_GatedOnFatigue()
    {
        Assert.That(Shipped("Dodge").Passive.Single(), Is.EqualTo(new HookEntry(EHookID.Shooting_OnHitRollModifier,
            new Condition.And(new Condition.IsMelee(),
                new Condition.And(MostModels, new Condition.Not(new Condition.CountsAsFatiguedInMelee()))),
            new Effect.RollModifier(ERollKind.Hit, -1), ELifetime.ThisAttack, ERuleSeat.Subject)));
    }

    [Test]
    public void Entrenched_IsArtillerysDefensiveFacet()
    {
        HookEntry artilleryFacet = Catalog("Artillery").Passive.Single(e => e.Seat == ERuleSeat.Subject);
        Assert.That(Shipped("Entrenched").Passive.Single(), Is.EqualTo(artilleryFacet),
            "3.6 Entrenched and Artillery's second sentence are the same text");
    }

    [Test]
    public void FortifiedGrowth_TicksAtRoundStart_AndReducesApPerTwoTokens()
    {
        var marker = new TokenType("FortifiedGrowthMarker");
        SpecialRuleDefinition growth = Shipped("Fortified Growth");
        HookEntry defensiveClock = Shipped("Defensive Growth").Passive.Single(e => e.HookID == EHookID.Round_OnRoundStart);

        HookEntry clock = growth.Passive.Single(e => e.HookID == EHookID.Round_OnRoundStart);
        Assert.That(clock.Condition, Is.EqualTo(defensiveClock.Condition), "Defensive Growth's 'on the table and isn't Shaken'");
        Assert.That(clock.Effect, Is.EqualTo(new Effect.GrantToken(marker, new ValueSource.Literal(1),
            new TokenClearTrigger.ManualOnly(), MaxTotal: 4)));

        Assert.That(growth.Passive.Single(e => e.HookID == EHookID.Shooting_OnHitRollComplete), Is.EqualTo(
            new HookEntry(EHookID.Shooting_OnHitRollComplete,
                new Condition.And(MostModels, new Condition.TokenPresent(marker, MinCount: 2)),
                new Effect.TokenScaledReduceArmorPenetration(marker, PerMarkers: 2, MaxReduction: 2),
                ELifetime.ThisAttack, ERuleSeat.Subject)));
    }

    [TestCase("Guardian")]
    [TestCase("Primeborn")]
    public void GuardianAndPrimeborn_AreFortifiedsShape(string name)
    {
        Assert.That(Shipped(name).Passive, Is.EqualTo(Catalog("Fortified").Passive),
            "3.6: unconditional - no 'shot or charged from over 9\"' gate");
    }

    [Test]
    public void Shatter_IsFracturesShape_AtApPlusThree()
    {
        SpecialRuleDefinition shatter = Shipped("Shatter");
        Assert.That(shatter.Scope, Is.EqualTo(ERuleScope.Weapon));
        Assert.That(shatter.Passive.Single(), Is.EqualTo(new HookEntry(EHookID.Shooting_OnHitRollComplete,
            new Condition.UnmodifiedRollEquals(6), new Effect.PerHitSaveModifier(6, -3), ELifetime.ThisAttack)));
    }

    [Test]
    public void RangedSlayer_IsAHoldShootingAp()
    {
        Assert.That(Shipped("Ranged Slayer").Passive.Single(), Is.EqualTo(new HookEntry(EHookID.Shooting_OnHitRollComplete,
            new Condition.And(new Condition.Not(new Condition.IsMelee()),
                new Condition.And(new Condition.Not(new Condition.AfterMoving()),
                    new Condition.And(NotSpell, new Condition.TargetMajorityHasTough(3)))),
            new Effect.RollModifier(ERollKind.Save, -2), ELifetime.ThisAttack)));
    }

    [Test]
    public void PiercingCharge_And_PiercingMarksman()
    {
        Assert.That(Shipped("Piercing Charge").Passive.Single(), Is.EqualTo(new HookEntry(EHookID.Shooting_OnHitRollComplete,
            new Condition.And(new Condition.IsMelee(), new Condition.IsCharging()),
            new Effect.RollModifier(ERollKind.Save, -1), ELifetime.ThisAttack)));
        Assert.That(Shipped("Piercing Marksman").Passive.Single(), Is.EqualTo(new HookEntry(EHookID.Shooting_OnHitRollComplete,
            new Condition.And(new Condition.Not(new Condition.IsMelee()),
                new Condition.And(NotSpell, new Condition.DistanceGreaterThan(9f))),
            new Effect.RollModifier(ERollKind.Save, -1), ELifetime.ThisAttack)));
    }

    // A 3.6 name that is another shipped rule's twin, entry for entry (the text is pinned above).
    [TestCase("Grounded Defense", "Grounded Reinforcement")]
    [TestCase("Grounded Evasion", "Grounded Stealth")]
    [TestCase("Precision Marksman", "Targeting Visor")]
    [TestCase("Predator", "Bloodborn")]
    [TestCase("Nimble", "Wolfborn")]
    [TestCase("Vicious", "Mischievous")]
    [TestCase("Unwieldy on its weapons in melee", "Unwieldy in melee")]
    public void TheTwin_HasItsSiblingsEntries(string name, string sibling)
    {
        Assert.That(Shipped(name).Passive, Is.EqualTo(Shipped(sibling).Passive));
        Assert.That(Shipped(name).Scope, Is.EqualTo(Shipped(sibling).Scope));
    }

    [Test]
    public void ThrustInMelee_IsTheCatalogThrust_AndTheAuraAndBuffGrantIt()
    {
        Assert.That(Shipped("Thrust in Melee").Passive, Is.EqualTo(Catalog("Thrust").Passive));
        Assert.That(Shipped("Thrust in Melee Aura").Passive.Single().Effect, Is.EqualTo(new Effect.Aura("Thrust in Melee")));
        Assert.That(Shipped("Thrust in Melee Buff").Activated.Single().Effect,
            Is.EqualTo(new Effect.AddRule("Thrust in Melee", ELifetime.NextTrigger)));
    }

    [Test]
    public void VersatileExpert_IsTheUngatedPick()
    {
        SpecialRuleDefinition pick = Shipped("Versatile Expert");
        Assert.That(pick.Passive, Is.Empty);
        Assert.That(pick.Activated.Select(a => (a.TriggerHook, a.Cost, a.Label, a.Effect)), Is.EqualTo(new[]
        {
            (EHookID.Activation_OnActivationStart, (Cost)new Cost.OncePerActivation(), "AP(+1)",
                (Effect)new Effect.AddRule("Versatile Expert (Piercing)", ELifetime.ThisActivation)),
            (EHookID.Activation_OnActivationStart, (Cost)new Cost.OncePerActivation(), "+1 to hit",
                (Effect)new Effect.AddRule("Versatile Expert (Precision)", ELifetime.ThisActivation)),
        }));
        Assert.That(Shipped("Versatile Expert (Piercing)").Passive.Single(), Is.EqualTo(new HookEntry(
            EHookID.Shooting_OnHitRollComplete, NotSpell, new Effect.RollModifier(ERollKind.Save, -1), ELifetime.ThisAttack)));
        Assert.That(Shipped("Versatile Expert (Precision)").Passive.Single(), Is.EqualTo(new HookEntry(
            EHookID.Shooting_OnHitRollModifier, new Condition.Always(), new Effect.RollModifier(ERollKind.Hit, 1),
            ELifetime.ThisAttack)));
    }

    [Test]
    public void InfiltratingPrecisionStrike_IsInfiltratePlusAnArrivalRoundMeleePlusOne()
    {
        SpecialRuleDefinition rule = Shipped("Infiltrating Precision Strike");
        Assert.That(rule.Passive[0], Is.EqualTo(Shipped("Infiltrate").Passive.Single()));
        Assert.That(rule.Passive[1], Is.EqualTo(new HookEntry(EHookID.Shooting_OnHitRollModifier,
            new Condition.And(new Condition.IsMelee(), new Condition.TokenPresent(TokenType.ArrivedFromReserve)),
            new Effect.RollModifier(ERollKind.Hit, 1), ELifetime.ThisAttack)));
    }

    [Test]
    public void TheBraveryPair_BothGivePlusOneToMoraleTestRolls()
    {
        Assert.That(Shipped("Bravery Aura").Passive.Single().Effect, Is.EqualTo(new Effect.Aura("Courage")),
            "the catalog's Courage is '+1 to this unit's morale test rolls'");
        Assert.That(Shipped("Bravery Buff").Activated.Single().Effect,
            Is.EqualTo(new Effect.StatModifier(ERollKind.Morale, 1, ELifetime.NextTrigger)),
            "Morale Debuff's mirror: a one-shot +1 token on the friendly unit");
    }

    [Test]
    public void RapidRushBuff_MovedFromTheAofFile()
    {
        Assert.That(Shipped("Rapid Rush Buff").Activated.Single().Effect,
            Is.EqualTo(new Effect.AddRule("Rapid Rush", ELifetime.NextTrigger)));
        Assert.That(Aof().Select(r => r.Name), Has.None.EqualTo("Rapid Rush Buff"),
            "AoF books merge the GDF file first, so one copy serves both");
    }

    [Test]
    public void AofDefenseBuff_IsAPlusOneDefenseToken_NotEntrenched()
    {
        // The 3.5 AoF Defense Buff granted "Entrenched", whose old GDF copy was a plain +1 Defense. 3.6 Entrenched is the
        // 12" shooting rule, so the Buff now places Defense Debuff's mirror token.
        SpecialRuleDefinition buff = Aof().Single(r => r.Name == "Defense Buff");
        Assert.That(buff.Activated.Single().Effect, Is.EqualTo(new Effect.StatModifier(ERollKind.Save, 1, ELifetime.NextTrigger)));
    }

    // --- Auras and Buffs resolve -------------------------------------------------------------------------------

    [TestCase("Bravery Aura")]
    [TestCase("Spell Resistance Aura")]
    [TestCase("Grounded Defense Aura")]
    [TestCase("Piercing Charge Aura")]
    [TestCase("Piercing Marksman Aura")]
    [TestCase("Predator Aura")]
    [TestCase("Thrust in Melee Aura")]
    [TestCase("Thrust in Melee Buff")]
    [TestCase("Rapid Rush Buff")]
    [TestCase("Unwieldy Debuff")]
    [TestCase("Versatile Expert")]
    public void EveryGrant_ResolvesInTheShippedUniverse(string name)
    {
        SpecialRuleDefinition rule = Shipped(name);
        IEnumerable<string> granted = rule.Passive.Select(e => e.Effect).Concat(rule.Activated.Select(a => a.Effect))
            .Select(e => e switch { Effect.Aura a => a.RuleName, Effect.AddRule r => r.RuleName, _ => null })
            .OfType<string>();
        var names = new HashSet<string>(Gdf().Select(r => r.Name).Concat(CoreRuleCatalog.All.Select(r => r.Name)),
            StringComparer.OrdinalIgnoreCase);

        foreach (string target in granted)
            Assert.That(names, Does.Contain(target), $"'{name}' grants '{target}'");
    }

    // --- spell-phrase aliases ----------------------------------------------------------------------------------

    // The 3.6 spell text says "gets Shred on its weapons when shooting" where 3.5 said "gets Shred when shooting"; the
    // importer keeps the phrase as the granted name. Each is authored as its canonical rule under the 3.6 phrase.
    [TestCase("Unstoppable on their weapons when shooting", "Unstoppable when shooting")]
    [TestCase("Shred on its weapons when shooting", "Shred when shooting")]
    [TestCase("AP(1) on their weapons when shooting", "AP(1) when shooting")]
    [TestCase("Rending on their weapons in melee", "Rending in melee")]
    [TestCase("+6\" range on its weapons when shooting", "+6\" range when shooting")]
    [TestCase("+6\" range on their weapons when shooting", "+6\" range when shooting")]
    [TestCase("Bane on their weapons when attacking", "Bane when attacking")]
    [TestCase("Shred on its weapons in melee", "Shred in melee")]
    [TestCase("Shred on their weapons when attacking", "Shred when attacking")]
    public void TheSpellPhraseAlias_IsTheCatalogRule(string alias, string canonical)
    {
        Assert.That(Shipped(alias).Passive, Is.EqualTo(Catalog(canonical).Passive));
        Assert.That(Shipped(alias).Scope, Is.EqualTo(Catalog(canonical).Scope));
    }

    [Test]
    public void IndirectOnTheirWeapons_IsTheSupplementsIndirectWhenShooting()
    {
        Assert.That(Shipped("Indirect on their weapons").Passive, Is.EqualTo(Shipped("Indirect when shooting").Passive));
        Assert.That(Shipped("Indirect on their weapons").Scope, Is.EqualTo(ERuleScope.Weapon));
    }
}
