using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #197 P17 - the shipped Spawn data (14 refs: Alien Hives, Ratmen Clans, Robot Legions, Wormhole
// Daemons of Lust). The mechanism (Str argument, aux spec build/registration/placement, same-round
// adoption) is pinned by the engine's SpawnRuleIntegrationTests; these pin the DATA - the definition,
// the book refs carrying their TEXT argument (the importer used to flatten "Spawn(Spores [5])" to a
// bare name, which is how the rule shipped dead), and the Forge compile embedding the named specs.
[TestFixture]
public class SpawnShippedDataTests
{
    private const string RuleName = "Spawn";

    private static SpecialRuleDefinition Definition() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
                Path.Combine(AppContext.BaseDirectory, "Assets", "Books", "GdfRuleSupplement.json")))
            .Single(r => r.Name == RuleName);

    [Test]
    public void Spawn_IsAOncePerGameActivationStartAbility_WithATextArgument()
    {
        SpecialRuleDefinition definition = Definition();
        ActivatedAbility ability = definition.Activated.Single();

        Assert.That(ability.TriggerHook, Is.EqualTo(EHookID.Activation_OnActivationStart),
            "'when this model is activated'");
        Assert.That(ability.Cost, Is.InstanceOf<Cost.OncePerGame>());
        var effect = (Effect.SpawnUnit)ability.Effect;
        Assert.That(effect.RadiusInches, Is.EqualTo(6f).Within(0.001f), "'fully within 6\" of it'");
        Assert.That(definition.EngineArgumentCount, Is.EqualTo(1),
            "the X - which unit spawns - is the rule instance's argument");
    }

    [Test]
    public void EveryBookRef_CarriesItsTextArgument_AndTheDefinitionIsEmbedded()
    {
        string booksDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Books");
        List<string> problems = new List<string>();

        foreach (string path in Directory.GetFiles(booksDir, "*.fdgbook"))
        {
            string json = File.ReadAllText(path);
            if (!json.Contains($"\"name\": \"{RuleName}\"", StringComparison.Ordinal)) continue;

            BookFile book = JsonSerializer.Deserialize<BookFile>(json, RuleJson.Options)!;
            int text = 0, bare = 0;
            void Count(IEnumerable<SpecialRuleEntry> rules)
            {
                foreach (SpecialRuleEntry rule in rules)
                {
                    if (rule is SpecialRuleEntry_Text t && t.Name == RuleName) text++;
                    else if (rule is SpecialRuleEntry_Core c && c.Name == RuleName) bare++;
                }
            }

            foreach (RosterUnit unit in book.Units)
            {
                Count(unit.Rules);
                foreach (ItemEntry item in unit.Items) Count(item.Rules);
                foreach (UpgradeSection section in unit.Sections)
                    foreach (UpgradeOption option in section.Options)
                    {
                        Count(option.RulesGained);
                        foreach (ItemEntry item in option.ItemsGained) Count(item.Rules);
                    }
            }

            if (bare > 0)
            {
                problems.Add($"{Path.GetFileName(path)}: {bare} bare Spawn ref(s) with no text argument");
            }

            if (text > 0 && !book.RuleDefinitions.Any(d => d.Name == RuleName))
            {
                problems.Add($"{Path.GetFileName(path)}: references Spawn but embeds no definition");
            }
        }

        Assert.That(problems, Is.Empty, string.Join("; ", problems));
    }

    [Test]
    public void ForgeCompile_EmbedsTheNamedAuxSpec_FromTheRealBook()
    {
        BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Books", "AlienHives.fdgbook")),
            RuleJson.Options)!;
        RosterUnit artillery = book.Units.Single(u => u.Name == "Invasion Artillery Spore");

        BuiltArmyFile army = ListCompiler.Compile(book, new BuilderList
        {
            PointsLimit = 100000,
            Units = { new BuilderUnit { RosterUnitId = artillery.Id } },
        });

        Assert.That(army.AuxiliaryUnits, Is.Not.Null,
            "the unit's Spawn(Spores [5]) item compiles its target into the army");
        UnitFileEntry spores = army.AuxiliaryUnits!.Single(u => u.Id == "Spores [5]");
        Assert.That(spores.Name, Is.EqualTo("Spores"));
        Assert.That(spores.ModelCount, Is.EqualTo(5));
        Assert.That(spores.PointCost, Is.EqualTo(0));
    }
}
