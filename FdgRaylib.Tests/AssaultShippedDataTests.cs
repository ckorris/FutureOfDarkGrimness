using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FDG;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #424 - Core Rules v3.6.0 Assault ("Counts as both a ranged weapon and a melee weapon.") as SHIPPED in
// GdfRuleSupplement.json; the AoF supplement inherits the name, so one entry serves both games. The mechanics are
// pinned engine-side by AssaultRuleIntegrationTests against a hand-built copy (AssaultDefinitions); this fixture
// pins that the authored JSON is exactly that copy - entry for entry - and that the shipped definition actually
// confers both roles on a weapon through the capability seam, so the engine tests and the data cannot drift apart.
[TestFixture]
public class AssaultShippedDataTests
{
    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static List<SpecialRuleDefinition> Load(string fileName) =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(Path.Combine(BooksDirectory, fileName)));

    private static SpecialRuleDefinition ShippedAssault() => Load("GdfRuleSupplement.json").Single(r => r.Name == "Assault");

    [Test]
    public void TheShippedDefinition_GrantsBothRoles_AtTheCapabilityHook_WithThe36Text()
    {
        SpecialRuleDefinition assault = ShippedAssault();

        Assert.That(assault.Scope, Is.EqualTo(ERuleScope.Weapon), "printed on weapons");
        Assert.That(assault.EngineArgumentCount, Is.EqualTo(0));
        Assert.That(assault.Activated, Is.Empty);
        Assert.That(assault.Valence, Is.EqualTo(EValence.Positive));
        Assert.That(assault.Description, Is.EqualTo("Counts as both a ranged weapon and a melee weapon."),
            "the 3.6.0 book text, verbatim");
        Assert.That(assault.Passive, Is.EqualTo(new[]
        {
            new HookEntry(EHookID.Lifecycle_OnCapabilityQuery, new Condition.Always(), new Effect.EnableMeleeUse(),
                ELifetime.UntilEndOfGame),
            new HookEntry(EHookID.Lifecycle_OnCapabilityQuery, new Condition.Always(), new Effect.EnableRangedUse(),
                ELifetime.UntilEndOfGame),
        }), "a weapon ROLE grant through the capability convention - not a name-based marker");
    }

    [Test]
    public void TheShippedDefinition_MakesAHeavyPistol_BothRangedAndMelee()
    {
        var pistol = new Weapon("Heavy Pistol", rangeInches: 12f, attacks: 1, armorPenetration: 1);
        pistol.AttachRuleDefinition(new ResolvedRule("Assault", ShippedAssault()));

        Assert.That(pistol.IsRanged(), Is.True, "12\" shot");
        Assert.That(pistol.IsMelee(), Is.True, "and a melee weapon");
        Assert.That(WeaponRoleQueries.MeleeUseSource(pistol), Is.EqualTo("Assault"));
    }

    [Test]
    public void TheAofUniverse_ReadsTheSameAssault()
    {
        List<SpecialRuleDefinition> merged = FdgRaylib.Import.RuleSupplementSet.LoadMerged(new[]
        {
            Path.Combine(BooksDirectory, "GdfRuleSupplement.json"),
            Path.Combine(BooksDirectory, "AofRuleSupplement.json"),
        });

        SpecialRuleDefinition assault = merged.Single(r => r.Name == "Assault");
        Assert.That(assault.Passive, Is.EqualTo(ShippedAssault().Passive),
            "the AoF supplement does not redefine Assault - one entry serves both games");
    }
}
