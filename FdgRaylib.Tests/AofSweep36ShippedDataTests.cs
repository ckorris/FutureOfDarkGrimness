using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #435 - the OPR 3.6 AoF supplement sweep as SHIPPED. The mechanics are pinned engine-side (AofCommandGroup36- and
// AofSweep36RuleIntegrationTests, against hand-built copies in AofSweep36Definitions); this fixture pins that the authored
// JSON carries the 3.6.0 text verbatim for every name the sweep touched, that each re-authored definition is exactly the
// tested shape (or, for a name that is another rule's 3.6 twin, exactly that rule's entries), that the AoF file shadows
// no GDF definition of the same 3.6 text, and that the merged AoF universe (GDF first, AoF later-wins) resolves the
// command group to one definition for both systems.
[TestFixture]
public class AofSweep36ShippedDataTests
{
    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static List<SpecialRuleDefinition> Gdf() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(Path.Combine(BooksDirectory, "GdfRuleSupplement.json")));

    private static List<SpecialRuleDefinition> Aof() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(Path.Combine(BooksDirectory, "AofRuleSupplement.json")));

    // The universe an AoF book bakes against: GDF first, AoF later-wins (RuleSupplementSet).
    private static List<SpecialRuleDefinition> Merged() => FdgRaylib.Import.RuleSupplementSet.LoadMerged(new[]
    {
        Path.Combine(BooksDirectory, "GdfRuleSupplement.json"), Path.Combine(BooksDirectory, "AofRuleSupplement.json"),
    });

    private static SpecialRuleDefinition Shipped(string name) =>
        Merged().Single(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

    private static SpecialRuleDefinition AofShipped(string name) => Aof().Single(r => r.Name == name);

    private static SpecialRuleDefinition Catalog(string name) =>
        CoreRuleCatalog.All.Single(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

    // --- the 3.6.0 text, verbatim (ASCII-folded) -------------------------------------------------------------------

    private static IEnumerable<TestCaseData> Texts()
    {
        // AoF Core Rules v3.6.0, Command Groups (D2: the PDF's wording).
        yield return Case("Sergeant", "This model gets AP(+1) when attacking.");
        yield return Case("Banner", "This model and its unit get +1 to Morale Test rolls triggered by shooting or melee.");
        yield return Case("Musician", "This model and its unit move +1\" when using move actions.");
        // AoF 3.6.0 books.
        yield return Case("Great Sergeant", "This model gets AP(+2) when attacking.");
        yield return Case("Great Banner", "Once per activation, before attacking, pick one friendly unit within 12\", which gets +1 to Morale Test rolls triggered by shooting/melee once (next time the effect would apply).");
        yield return Case("Great Musician", "Once per activation, before attacking, pick one friendly unit within 12\", which moves +1\" when using move actions once (next time the effect would apply).");
        yield return Case("Ossified", "When units where most models have this rule take hits, those hits count as having AP(-1), to a min. of AP(0).");
        yield return Case("Warden", "When units where most models have this rule take hits, those hits count as having AP(-1), to a min. of AP(0).");
        yield return Case("Wave-Step", "When this unit is activated, you may move all models with this rule in it by up to D3\".");
        yield return Case("Ethereal", "Once per activation, before attacking, you may place this model anywhere fully within 6\" of its position. If you do, this model moves -6\" when using Advance, and -6\" when using Rush/Charge until the end of that activation.");
        yield return Case("Grounded Speed", "If most models in this unit are within 1\" of terrain when activated, this model may move +2\" on Advance, and +4\" on Rush/Charge during this activation.");
        // Behaviour already 3.6 (D1: the AoF file ends text-parity with the 3.6.0 books), and the new Piercing Charge Buff.
        yield return Case("Cursed Undead", "When a unit where most models have this rule takes wounds, roll one die for each. On a 6+ it is ignored.");
        yield return Case("Unmovable", "If a unit where most models have this rule is Shaken at the beginning of the round, roll one die. On a 4+, it stops being Shaken.");
        yield return Case("Vale Oath", "If a unit where most models have this rule is Shaken at the beginning of the round, roll one die. On a 4+, it stops being Shaken.");
        yield return Case("Buccaneer", "When shooting at enemies over 9\" away, this model gets +1 to hit rolls.");
        yield return Case("Grounded Protection", "If a unit where most models have this rule has most of them within 1\" of terrain, when they take wounds roll one die per wound, and on a 5+ it is ignored.");
        yield return Case("Grounded Protection Aura", "This model and its unit get Grounded Protection.");
        yield return Case("Reckless Piercing", "When a unit where most models have this rule is activated, you may roll one die. On a 2+ their weapons get AP(+1) when attacking until the end of the round, but on a 1 enemy units get AP(+1) on their weapons when attacking them instead.");
        yield return Case("Reckless Piercing Aura", "This model and its unit get Reckless Piercing.");
        yield return Case("Traversal", "May move through friendly and enemy units.");
        yield return Case("Bestial", "When attacking, targets must re-roll unmodified Defense results of 6 when blocking hits from this model's weapons.");
        yield return Case("Royal Warrior", "For each unmodified roll of 6 to hit when attacking, this model may roll +1 attack with that weapon. This rule doesn't apply to newly generated attacks.");
        yield return Case("Good Fighter", "This model gets +1 to hit rolls in melee.");
        yield return Case("Melee Evasion Buff", "Once per activation, before attacking, pick one friendly unit within 12\", which gets Melee Evasion once (next time the effect would apply).");
        yield return Case("Piercing Feat", "Once per game, when this unit attacks, you may use this rule so that the weapons of all models with this rule in it get AP(+1).");
        yield return Case("Precision Feat", "Once per game, when this unit attacks, you may use this rule so that all models with this rule in it get +1 to hit rolls.");
        yield return Case("Piercing Fighting Mark", "Once per activation, before attacking, pick one enemy unit within 18\" in line of sight, which friendly units gets AP(+1) on their weapons in melee against once (next time the effect would apply).");
        yield return Case("Rapid Charge Mark", "Once per activation, before attacking, pick one enemy unit within 18\" in line of sight, which friendly units gets Rapid Charge against once (next time the effect would apply).");
        yield return Case("Surge Mark", "Once per activation, before attacking, pick one enemy unit within 18\" in line of sight, which friendly units gets Surge on their weapons when attacking against once (next time the effect would apply).");
        yield return Case("Takedown when Shooting", "This model gets Takedown on its weapons when shooting.");
        yield return Case("Bloodthirsty Fighter", "For each unmodified roll of 1 that enemies roll when blocking hits from this model's weapons in melee, this model may roll +1 attack with that weapon. This rule doesn't apply to newly generated attacks.");
        yield return Case("Piercing Charge Buff", "Once per activation, before attacking, pick one friendly unit within 12\", which gets Piercing Charge once (next time the effect would apply).");
        // The GDF file's Piercing Fighter: only AoF Goblins defines it in 3.6.
        yield return Case("Piercing Fighter", "This model gets AP(+1) on its weapons in melee.");
    }

    private static TestCaseData Case(string name, string text) => new TestCaseData(name, text).SetArgDisplayNames(name);

    [TestCaseSource(nameof(Texts))]
    public void TheShippedDefinition_CarriesThe36Text(string name, string text)
    {
        Assert.That(Shipped(name).Description, Is.EqualTo(text), "the 3.6.0 text, verbatim");
        Assert.That(text.All(c => c < 128), Is.True, "game text is ASCII-only");
    }

    // --- the command group --------------------------------------------------------------------------------------

    [Test]
    public void Banner_HelpsOnlyTestsTriggeredByShootingOrMelee()
    {
        SpecialRuleDefinition banner = Shipped("Banner");
        Assert.That(banner.Scope, Is.EqualTo(ERuleScope.Unit), "'this model and its unit' - folds unit-wide");
        Assert.That(banner.Passive.Single(), Is.EqualTo(new HookEntry(EHookID.Morale_OnPreMoraleTest,
            new Condition.MoraleTestFromShootingOrMelee(), new Effect.RollModifier(ERollKind.Morale, 1),
            ELifetime.ThisActivation)));
    }

    [Test]
    public void Musician_IsPlusOneOnEachMoveAction()
    {
        SpecialRuleDefinition musician = Shipped("Musician");
        Assert.That(musician.Scope, Is.EqualTo(ERuleScope.Unit));
        Assert.That(musician.Passive, Is.EqualTo(new[] { EActionType.Advance, EActionType.Rush, EActionType.Charge }
            .Select(a => new HookEntry(EHookID.Movement_OnMoveActionDeclared, new Condition.ActionTypeIs(a),
                new Effect.MovementBonus(a, 1f), ELifetime.ThisActivation))));
    }

    [Test]
    public void GreatSergeant_IsSergeantsWeaponScopedApBody_AtTwo()
    {
        SpecialRuleDefinition great = Shipped("Great Sergeant");
        SpecialRuleDefinition sergeant = Shipped("Sergeant");
        Assert.That(great.Scope, Is.EqualTo(ERuleScope.Weapon), "routes ListCompiler's champion marking, as Sergeant");
        Assert.That(great.Passive.Single(), Is.EqualTo(sergeant.Passive.Single() with
        {
            Effect = new Effect.RollModifier(ERollKind.Save, -2),
        }));
    }

    [TestCase("Great Banner", "Banner")]
    [TestCase("Great Musician", "Musician")]
    public void TheGreatBuffs_GrantTheBaseRuleOnce(string name, string granted)
    {
        ActivatedAbility buff = Shipped(name).Activated.Single();
        Assert.That(buff.TriggerHook, Is.EqualTo(EHookID.Activation_OnBeforeAttackAction));
        Assert.That(buff.Cost, Is.InstanceOf<Cost.OncePerActivation>());
        Assert.That(buff.TargetSelector, Is.EqualTo(new TargetSelector(12f, 1, 1, ETargetAffinity.Friend,
            RequireLineOfSight: false)));
        Assert.That(buff.Effect, Is.EqualTo(new Effect.AddRule(granted, ELifetime.NextTrigger)));
    }

    // --- re-authored faction rules ------------------------------------------------------------------------------

    // A 3.6 AoF name that is another shipped rule's twin, entry for entry (the text is pinned above).
    [TestCase("Ossified", "Guardian")]
    [TestCase("Warden", "Guardian")]
    [TestCase("Wave-Step", "Wolfborn")]
    public void TheTwin_HasItsSiblingsEntries(string name, string sibling)
    {
        Assert.That(AofShipped(name).Passive, Is.EqualTo(Shipped(sibling).Passive));
        Assert.That(AofShipped(name).Scope, Is.EqualTo(Shipped(sibling).Scope));
    }

    [Test]
    public void OssifiedAndWarden_AreUnconditional_FortifiedsShape()
    {
        // 3.6 dropped the 3.5 "from over 9 inches away" gate (attackedFromOverInches).
        foreach (string name in new[] { "Ossified", "Warden" })
            Assert.That(AofShipped(name).Passive, Is.EqualTo(Catalog("Fortified").Passive), name);
    }

    [Test]
    public void WaveStep_IsAMove_NotAPlacement()
    {
        Assert.That(AofShipped("Wave-Step").Passive.Single(), Is.EqualTo(new HookEntry(EHookID.Activation_OnActivationStart,
            new Condition.MostModelsHaveThisRule(), new Effect.RepositionAtActivation(new DiceExpression.D3(), 0, Move: true),
            ELifetime.ThisActivation)));
    }

    [Test]
    public void Ethereal_MovesMinusSix_OnlyAfterItsTeleport()
    {
        SpecialRuleDefinition ethereal = AofShipped("Ethereal");
        var used = new TokenType("AbilityUsed:Ethereal");
        Assert.That(ethereal.Passive, Is.EqualTo(new[] { EActionType.Advance, EActionType.Rush, EActionType.Charge }
            .Select(a => new HookEntry(EHookID.Movement_OnMoveActionDeclared,
                new Condition.And(new Condition.ActionTypeIs(a), new Condition.TokenPresent(used)),
                new Effect.MovementBonus(a, -6f), ELifetime.ThisActivation))),
            "'if you do' - the ability's own once-per-activation marker (activation-end clear)");

        ActivatedAbility teleport = ethereal.Activated.Single();
        Assert.That(teleport.TriggerHook, Is.EqualTo(EHookID.Activation_OnActionChoice));
        Assert.That(teleport.Cost, Is.InstanceOf<Cost.OncePerActivation>());
        Assert.That(teleport.Effect, Is.InstanceOf<Effect.Teleport>(), "TeleportStage routes on the effect");
        Assert.That(teleport.AvailableWhen, Is.InstanceOf<Condition.MostModelsHaveThisRule>());
    }

    // Text only: the 3.5 shape already does what the 3.6 sentence says.
    [TestCase("Bestial", "Mischievous")]
    [TestCase("Buccaneer", "Precision Marksman")]
    [TestCase("Unmovable", "Steadfast")]
    [TestCase("Vale Oath", "Steadfast")]
    [TestCase("Royal Warrior", "Predator")]
    public void TheTextOnlyRule_IsItsTestedSiblingsShape(string name, string sibling)
    {
        // Royal Warrior shares Predator's extra-hit approximation of "may roll +1 attack" (#434 D7).
        Assert.That(AofShipped(name).Passive, Is.EqualTo(Shipped(sibling).Passive));
    }

    [Test]
    public void CursedUndead_IsTheCatalogProtected()
    {
        Assert.That(AofShipped("Cursed Undead").Passive, Is.EqualTo(Catalog("Protected").Passive));
    }

    [Test]
    public void TheCatalogBaneInMelee_CarriesThe36Text()
    {
        Assert.That(Catalog("Bane in Melee").Description, Is.EqualTo("This model gets Bane on its weapons in melee."));
    }

    // --- new names --------------------------------------------------------------------------------------------

    // The 3.6 AoF spell text grants "X on its weapons in melee" (#434 D5): each is its canonical catalog rule under the
    // 3.6 phrase. Melee hits are never a spell's, so no weapons-only gate is needed.
    [TestCase("AP(+1) on its weapons in melee", "AP(+1) in melee")]
    [TestCase("Bane on its weapons in melee", "Bane in melee")]
    [TestCase("Unstoppable on its weapons in melee", "Unstoppable in melee")]
    public void TheSpellPhraseAlias_IsTheCatalogRule(string alias, string canonical)
    {
        Assert.That(AofShipped(alias).Passive, Is.EqualTo(Catalog(canonical).Passive));
        Assert.That(AofShipped(alias).Scope, Is.EqualTo(Catalog(canonical).Scope));
    }

    [Test]
    public void SurgeOnTheirWeapons_IsSurge_WeaponsOnly()
    {
        // Chivalrous Kingdoms' Curse of the Huntress: "... which friendly units gets Surge on their weapons against once".
        // Surge's entries with #434 D2's weapons-only gate (the "Shred / Bane on their weapons when attacking" convention).
        SpecialRuleDefinition alias = AofShipped("Surge on their weapons");
        Assert.That(alias.Scope, Is.EqualTo(Catalog("Surge").Scope));
        Assert.That(alias.Passive, Is.EqualTo(Catalog("Surge").Passive.Select(e => e with
        {
            Condition = new Condition.And(e.Condition, new Condition.IsNotSpell()),
        })));
    }

    [Test]
    public void PiercingChargeBuff_IsThrustInMeleeBuffsShape_OverPiercingCharge()
    {
        ActivatedAbility buff = AofShipped("Piercing Charge Buff").Activated.Single();
        ActivatedAbility thrust = Shipped("Thrust in Melee Buff").Activated.Single();
        Assert.That(buff, Is.EqualTo(thrust with { Effect = new Effect.AddRule("Piercing Charge", ELifetime.NextTrigger) }));
    }

    // Every grant an AoF-file rule makes resolves in the universe an AoF book bakes against (+ the catalog).
    [Test]
    public void EveryAofGrant_ResolvesInTheMergedUniverse()
    {
        var names = new HashSet<string>(Merged().Select(r => r.Name).Concat(CoreRuleCatalog.All.Select(r => r.Name)),
            StringComparer.OrdinalIgnoreCase);
        foreach (SpecialRuleDefinition rule in Aof())
        {
            IEnumerable<string> granted = rule.Passive.Select(e => e.Effect).Concat(rule.Activated.Select(a => a.Effect))
                .Select(e => e switch
                {
                    Effect.Aura a => a.RuleName, Effect.AddRule r => r.RuleName, Effect.MarkTarget m => m.RuleName,
                    _ => null,
                })
                .OfType<string>();
            foreach (string target in granted)
                Assert.That(names, Does.Contain(target), $"'{rule.Name}' grants '{target}'");
        }
    }

    // --- per-book overrides --------------------------------------------------------------------------------------

    [Test]
    public void NoPerBookOverrideShips()
    {
        // Every 3.5 entry was moot in 3.6: Versatile Attack's helpers (no 3.6 AoF book names Versatile Attack; the four
        // Disciples books took Versatile Expert), Lust Disciples' Melee Slayer / Shatter (common text / not named) and
        // the Mind Control of Rift Daemons of Lust and Vampiric Undead (the common 3.6 text, once per round).
        string overrides = Path.Combine(BooksDirectory, "AofBookOverrides");
        Assert.That(Directory.Exists(overrides) ? Directory.EnumerateFiles(overrides, "*.json") : Array.Empty<string>(),
            Is.Empty);
    }

    [Test]
    public void TheFormerlyOverriddenMindControl_IsThe36OncePerRoundMove()
    {
        // Daemons of Lust / Vampiric Undead bake against the merged universe: the GDF file's 3.6 Mind Control.
        Effect effect = Shipped("Mind Control").Activated.Single().Effect;
        Assert.That(((Effect.TriggeredMove)((Effect.MoraleTestThen)effect).OnFailure).OncePerRoundKey,
            Is.EqualTo("Mind Control"));
    }

    [Test]
    public void TheAofFile_ShadowsNoGdfDefinition()
    {
        // 3.6 prints Difficult Terrain Debuff, Fortified Growth and Melee Slayer word for word in both games, so the AoF
        // copies left (the GDF file's #434 versions serve AoF books too). A future AoF-only divergence of a shared name
        // is the #375 C9 mechanism and belongs here as a named exception.
        HashSet<string> gdf = Gdf().Select(r => r.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        Assert.That(Aof().Select(r => r.Name).Where(gdf.Contains), Is.Empty);

        foreach (string name in new[] { "Difficult Terrain Debuff", "Fortified Growth", "Melee Slayer" })
            Assert.That(Shipped(name).Passive, Is.EqualTo(Gdf().Single(r => r.Name == name).Passive), name);
    }

    [Test]
    public void TheCommandGroup_IsOneDefinitionForBothSystems()
    {
        // Sergeant / Banner / Musician print the same 3.6 text in both games' references, so the GDF file's copy serves
        // AoF too (AoF merges GDF first) - an AoF copy would only shadow it.
        foreach (string name in new[] { "Sergeant", "Banner", "Musician" })
        {
            Assert.That(Aof().Select(r => r.Name), Has.None.EqualTo(name), name);
            SpecialRuleDefinition gdf = Gdf().Single(r => r.Name == name);
            Assert.That(Shipped(name).Passive, Is.EqualTo(gdf.Passive), name);
            Assert.That(Shipped(name).Description, Is.EqualTo(gdf.Description), name);
        }
    }
}
