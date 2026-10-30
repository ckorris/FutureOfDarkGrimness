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
using FDG.Rules.Foundation;
using FDG.SaveLoad;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #423 - Parry(X), new in Core Rules v3.6.0 ("When this unit takes hits in melee, roll X dice per model with this rule
// in it, up to as many dice as hits taken. For each 6+ one hit is ignored (defender picks which)."). The 3.6.0 books
// carry it as a WEAPON rule - AoF Hand Weapons (252 references across 35 books) and the four GDF Daemons books - so it
// is a CATALOG entry (the engine must read X and roll), not a supplement definition. These pin the DATA side: the
// catalog is the single definition (neither shipped supplement redefines it), the glossary / hover text is the 3.6
// sentence, and an army file's "Parry(1)" on a Hand Weapon - or named at unit level, re-homed onto the weapons - loads
// as the catalog rule with its argument. The mechanism is pinned engine-side by ParryRuleIntegrationTests.
[TestFixture]
public class ParryShippedDataTests
{
    private const string Sentence = "When this unit takes hits in melee, roll X dice per model with this rule in it, " +
        "up to as many dice as hits taken. For each 6+ one hit is ignored (defender picks which).";

    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static List<SpecialRuleDefinition> Load(string fileName) =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(Path.Combine(BooksDirectory, fileName)));

    private static IEnumerable<SpecialRuleDefinition> MergedSupplements() =>
        Load("GdfRuleSupplement.json").Concat(Load("AofRuleSupplement.json"));

    [Test]
    public void TheCatalog_IsTheSingleDefinition_NeitherSupplementRedefinesIt()
    {
        Assert.That(MergedSupplements().Any(r => string.Equals(r.Name, "Parry", StringComparison.OrdinalIgnoreCase)),
            Is.False, "Parry needs engine code to read X and roll, so it lives in CoreRuleCatalog; a supplement copy " +
            "would shadow it by name and fire nothing.");
        Assert.That(CoreRuleCatalog.All.Count(r => r.Name == "Parry"), Is.EqualTo(1));
        Assert.That(CoreRuleCatalog.Parry.EngineArgumentCount, Is.EqualTo(1), "Parry(X)");
        Assert.That(CoreRuleCatalog.Parry.Scope, Is.EqualTo(ERuleScope.Weapon), "the books put it on Hand Weapons");
        Assert.That(CoreRuleCatalog.Parry.Description, Is.EqualTo(Sentence));
    }

    [Test]
    public void TheGlossary_AndTheHoverTooltip_CarryThe36Sentence()
    {
        RuleGlossary glossary = RuleGlossary.Build(MergedSupplements());

        Assert.That(glossary.Describe("Parry"), Is.EqualTo(Sentence));
        Assert.That(glossary.Tooltip(new SpecialRuleEntry_CoreNumeric("Parry", 3)), Is.EqualTo($"Parry(3)\n{Sentence}"));
    }

    [Test]
    public void AHandWeaponsParryOne_LoadsAsTheCatalogRule_WithItsArgument()
    {
        UnitData elves = LoadUnit(modelCount: 5, weaponRules: new[] { new SpecialRuleEntry_CoreNumeric("Parry", 1) });

        foreach (IModel model in elves.Models)
        {
            ResolvedRule parry = model.Weapons.Single().RuleDefinitions.Single();
            Assert.That(parry.Definition, Is.SameAs(CoreRuleCatalog.Parry));
            Assert.That(parry.Arguments, Is.EqualTo(new RuleArgument[] { new RuleArgument.Int(1) }));
        }
        Assert.That(elves.RuleDefinitions.Any(r => r.Definition == CoreRuleCatalog.Parry), Is.False,
            "a weapon rule rides the weapon, not the unit list");
    }

    [Test]
    public void AUnitLevelParry_IsReHomedOntoEveryWeapon_LikeCounterAndBrace()
    {
        UnitData elves = LoadUnit(modelCount: 3, unitRules: new[] { new SpecialRuleEntry_CoreNumeric("Parry", 3) });

        Assert.That(elves.Models.All(m => m.Weapons.Single().RuleDefinitions.Any(r =>
                r.Definition == CoreRuleCatalog.Parry && r.Arguments.SequenceEqual(new RuleArgument[] { new RuleArgument.Int(3) }))),
            Is.True, "weapon scope: the mention moves onto the weapons, where the melee stage counts carriers");
        Assert.That(elves.RuleDefinitions.Any(r => r.Definition == CoreRuleCatalog.Parry), Is.False);
    }

    [Test]
    public void TheCatalogEntry_PassesTheFireLint()
    {
        Assert.That(RuleFireLint.Check(CoreRuleCatalog.Parry), Is.Empty);
        Assert.That(new RuleValidator().ValidateAuthoring(CoreRuleCatalog.Parry), Is.Empty);
    }

    private static UnitData LoadUnit(int modelCount, SpecialRuleEntry[]? weaponRules = null, SpecialRuleEntry[]? unitRules = null)
    {
        var weapon = new WeaponFileEntry { Name = "Hand Weapon", Quantity = modelCount, Attacks = 1 };
        foreach (SpecialRuleEntry rule in weaponRules ?? Array.Empty<SpecialRuleEntry>()) weapon.SpecialRules.Add(rule);

        var unit = new UnitFileEntry
        {
            Name = "Elves", ModelCount = modelCount, Quality = 4, Defense = 4,
            Weapons = { weapon },
        };
        foreach (SpecialRuleEntry rule in unitRules ?? Array.Empty<SpecialRuleEntry>()) unit.SpecialRules.Add(rule);

        var army = new ArmyListFile { Name = "423 shipped-data list", Faction = string.Empty, Units = { unit } };

        GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
        RuleResolver resolver = CoreRuleCatalog.CreateResolver();
        ArmyListRuleResolution.RegisterEmbeddedDefinitions(resolver, army);
        GameBootstrap.CreateArmy(new PlayerID(Guid.NewGuid()), army, store, resolver);
        return store.GetAllValues<UnitData>().Single();
    }
}
