using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #197 Instinctive - Goblin Reclaimers v3.5.3, verbatim: "When this model is activated, if it is able to
// shoot/charge an enemy unit, then it must immediately attack the closest valid target and gets +1 to hit
// rolls for that attack." Dead no-definition (4 refs) until 2026-07-30.
//
// The compulsion machinery is rule-agnostic and pinned engine-side by
// CompelClosestTargetRuleIntegrationTests (capability, the activation-time obligation token, the menu
// restriction and both choosers' narrowing); nothing in the engine names Instinctive. These pin the
// authored JSON - the capability entry and the two rider entries with their obligation + combat-kind
// gates - the corpus census, the embedded book copy, and a carrier through the real compiler.
[TestFixture]
public class InstinctiveShippedDataTests
{
    private const string RuleName = "Instinctive";
    private const string Book = "GoblinReclaimers";

    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static BookFile LoadBook(string name) => JsonSerializer.Deserialize<BookFile>(
        File.ReadAllText(Path.Combine(BooksDirectory, name + BookFile.EXTENSION_WITH_PERIOD)),
        RuleJson.Options)!;

    private static SpecialRuleDefinition Supplement() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
                Path.Combine(BooksDirectory, "GdfRuleSupplement.json")))
            .Single(r => r.Name == RuleName);

    // ---- The authored definition ----------------------------------------------------------------------

    [Test]
    public void Instinctive_IsACompulsionPlusTwoRiders_AndNothingActivated()
    {
        SpecialRuleDefinition rule = Supplement();

        Assert.That(rule.Passive.Count, Is.EqualTo(3),
            "the capability answer plus the two +1 rider entries");
        Assert.That(rule.Activated, Is.Empty, "the compulsion is not optional - nothing to offer");
        Assert.That(rule.Scope, Is.EqualTo(ERuleScope.Unit));
        Assert.That(rule.EngineArgumentCount, Is.EqualTo(0));
    }

    [Test]
    public void TheCompulsion_IsACapabilityAnswer_GatedOnTheWholeUnit()
    {
        HookEntry entry = Supplement().Passive.Single(e => e.Effect is Effect.CompelClosestTarget);

        Assert.That(entry.HookID, Is.EqualTo(EHookID.Lifecycle_OnCapabilityQuery),
            "the capability ChooseActionStage reads when the unit activates, to decide whether to bind it");
        Assert.That(entry.Condition, Is.InstanceOf<Condition.AllModelsHaveThisRule>(),
            "#267: the compulsion binds the WHOLE unit's action, so a joined hero without it frees the unit");
        Assert.That(entry.Seat, Is.EqualTo(ERuleSeat.Actor));
    }

    // "+1 to hit rolls for THAT attack": the COMPELLED shot or charge swing only - never a strike-back,
    // and never an attack by a unit the rule did not bind when it activated.
    [Test]
    public void TheRiders_CoverTheShotAndTheChargeSwing_NotTheStrikeBack()
    {
        List<HookEntry> riders = Supplement().Passive
            .Where(e => e.Effect is Effect.RollModifier).ToList();

        Assert.That(riders.Count, Is.EqualTo(2));
        Assert.That(riders.All(r => r.HookID == EHookID.Shooting_OnHitRollModifier), Is.True,
            "both consume at the hit-modifier hook (shared by both combat kinds)");
        Assert.That(riders.All(r => ((Effect.RollModifier)r.Effect).RollKind == ERollKind.Hit
                                    && ((Effect.RollModifier)r.Effect).Delta == 1), Is.True);

        // Both gate on the OBLIGATION token (stamped only when the compulsion actually bound at
        // activation), then split by combat kind: NOT melee (the shot) / melee AND charging (the swing).
        // Flatten each condition tree and assert by the presence of the leaf kinds, so authoring style
        // changes (And nesting order) don't break the pin while the SEMANTICS stay pinned.
        static List<Condition> Flatten(Condition c) => c switch
        {
            Condition.And and => Flatten(and.Left).Concat(Flatten(and.Right)).ToList(),
            _ => new List<Condition> { c },
        };

        static bool GatesOnObligation(List<Condition> leaves) => leaves.Any(l =>
            l is Condition.TokenPresent token && token.TType == TokenType.CompelledToAttack);

        bool hasShotEntry = riders.Any(r =>
        {
            List<Condition> leaves = Flatten(r.Condition);
            return GatesOnObligation(leaves)
                && leaves.Any(l => l is Condition.Not not && not.Inner is Condition.IsMelee);
        });
        bool hasChargeEntry = riders.Any(r =>
        {
            List<Condition> leaves = Flatten(r.Condition);
            return GatesOnObligation(leaves)
                && leaves.Any(l => l is Condition.IsMelee)
                && leaves.Any(l => l is Condition.IsCharging);
        });

        Assert.That(hasShotEntry, Is.True, "the ranged rider: obligation gate + NOT melee");
        Assert.That(hasChargeEntry, Is.True,
            "the melee rider: obligation gate + melee + CHARGING - a strike-back must never get the +1");
        Assert.That(riders.All(r => GatesOnObligation(Flatten(r.Condition))), Is.True,
            "'+1 for THAT attack': a unit that merely HAS the rule, but was not compelled when it " +
            "activated, gets no bonus at all (owner clarification 2026-07-31)");
    }

    // ---- The corpus census -----------------------------------------------------------------------------

    private record Site(string Book, string Unit, string SectionAffects);

    private static IEnumerable<Site> Sites()
    {
        foreach (string path in ShippedBooks.GdfPaths()
                     .OrderBy(p => p))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            string bookName = Path.GetFileNameWithoutExtension(path);

            foreach (RosterUnit unit in book.Units)
            {
                foreach (UpgradeSection section in unit.Sections)
                {
                    bool hit = section.Options.Any(option =>
                        option.RulesGained.Any(Names)
                        || option.ItemsGained.Any(item => item.Rules.Any(Names)));
                    if (hit)
                    {
                        yield return new Site(bookName, unit.Name, section.Affects.ToString());
                    }
                }

                if (unit.Rules.Any(Names))
                {
                    yield return new Site(bookName, unit.Name, "native");
                }
            }
        }
    }

    private static bool Names(SpecialRuleEntry entry) =>
        entry is SpecialRuleEntry_Core core && string.Equals(core.Name, RuleName, StringComparison.Ordinal);

    [Test]
    public void EveryCarrier_IsAWholeSquadUpgrade()
    {
        List<Site> sites = Sites().ToList();

        Assert.That(sites.Count, Is.EqualTo(4), "the audit's 4 references - the Ramshackle Crew upgrades");
        Assert.That(sites.Select(s => s.Book).Distinct(), Is.EquivalentTo(new[] { Book }));
        Assert.That(sites.Select(s => s.Unit), Is.EquivalentTo(new[]
            { "Shooter Mob", "Storm Mob", "Freaks", "Scout Bikers" }));

        foreach (Site site in sites)
        {
            // The AllModelsHaveThisRule gate is load-bearing only while every carrier is an affects-All
            // section (the whole squad gains the rule, so the gate holds). A future partial upgrade would
            // make the gate silently turn the rule off for that unit - re-decide the scoping then.
            Assert.That(site.SectionAffects, Is.EqualTo("All"), $"{site.Book}/{site.Unit}");
        }
    }

    [Test]
    public void TheEmbeddedBookCopy_CarriesTheCompulsionAndBothRiders()
    {
        SpecialRuleDefinition embedded = LoadBook(Book).RuleDefinitions.Single(d => d.Name == RuleName);

        Assert.That(embedded.Passive.Count(e => e.Effect is Effect.CompelClosestTarget), Is.EqualTo(1),
            "army load reads the BOOK's copy - re-run --apply-rules if this fails");
        Assert.That(embedded.Passive.Count(e => e.Effect is Effect.RollModifier), Is.EqualTo(2));
    }

    // ---- End to end: a real book unit through the real compiler ----------------------------------------

    [Test]
    public void ShooterMob_BuyingRamshackleCrew_ResolvesInstinctiveOnTheUnit()
    {
        BookFile book = LoadBook(Book);
        RosterUnit mob = book.Units.Single(u => u.Name == "Shooter Mob");
        UpgradeSection section = mob.Sections
            .Single(s => s.Options.Any(o => o.Label.Contains("Ramshackle Crew")));
        UpgradeOption crew = section.Options.Single(o => o.Label.Contains("Ramshackle Crew"));

        BuiltArmyFile army = ListCompiler.Compile(book, new BuilderList
        {
            Name = "Test", BookName = book.Name, PointsLimit = 500,
            Units =
            {
                new BuilderUnit
                {
                    RosterUnitId = mob.Id!,
                    Choices = { new UpgradeChoice { SectionId = section.Id!, OptionId = crew.Id!, Count = 1 } },
                },
            },
        });

        Assert.That(army.Units.Single().SpecialRules.Select(r => r.PrintableName), Does.Contain(RuleName),
            "the affects-All item rule flattens onto the unit, satisfying the all-models gate");
    }
}
