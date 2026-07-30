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

// #197 P16 - the one-shot extra-attack primitive and its two corpus rules:
//   Takedown Strike (OPR eyMkgYDVrP7C, 5 refs) - "Once per game, when it's this model's turn to attack in
//     melee, it may make one attack at Quality 2+ with AP(2), Deadly(3), and Takedown."
//   Takedown Shot (OPR LPEKodkJ6xPS, 2 refs) - the same, "when this model shoots ... against the target".
// Both were dead no-definition until 2026-07-30. The mechanism is pinned engine-side by
// ExtraAttackRuleIntegrationTests; these pin the authored JSON, the corpus census the design leans on, the
// embedded book copies, and a real-book compile.
[TestFixture]
public class ExtraAttackShippedDataTests
{
    private const string Strike = "Takedown Strike";
    private const string Shot = "Takedown Shot";

    // Every bundled book that references one of the two rules.
    private static readonly string[] StrikeBooks =
    {
        "AlienHives", "ElvenJesters", "RatmenClans", "RebelGuerrillas", "SoulSnatcherCults",
    };

    private static readonly string[] ShotBooks = { "HumanInquisition", "RatmenClans" };

    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static BookFile LoadBook(string name) => JsonSerializer.Deserialize<BookFile>(
        File.ReadAllText(Path.Combine(BooksDirectory, name + BookFile.EXTENSION_WITH_PERIOD)),
        RuleJson.Options)!;

    private static SpecialRuleDefinition Supplement(string ruleName) =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
                Path.Combine(BooksDirectory, "GdfRuleSupplement.json")))
            .Single(r => r.Name == ruleName);

    // ---- The authored definitions ----------------------------------------------------------------------

    [TestCase(Strike, true)]
    [TestCase(Shot, false)]
    public void EachRule_IsAOncePerGameExtraAttackAtTheAttackWindow(string ruleName, bool melee)
    {
        SpecialRuleDefinition rule = Supplement(ruleName);

        Assert.That(rule.Passive, Is.Empty, "the whole rule is the activated ability - nothing fires passively");
        ActivatedAbility ability = rule.Activated.Single();

        Assert.That(ability.TriggerHook, Is.EqualTo(EHookID.Combat_OnAttackWindow),
            "the only hook ResolveExtraAttackStage gathers offers at; anywhere else is dead data");
        Assert.That(ability.Cost, Is.InstanceOf<Cost.OncePerGame>(), "'once per game'");

        // The combat-kind split is the ONLY difference between the two rules, and it is what keeps a melee
        // strike out of a shooting action and vice versa.
        if (melee)
        {
            Assert.That(ability.AvailableWhen, Is.InstanceOf<Condition.IsMelee>());
        }
        else
        {
            Assert.That(ability.AvailableWhen, Is.InstanceOf<Condition.Not>());
            Assert.That(((Condition.Not)ability.AvailableWhen).Inner, Is.InstanceOf<Condition.IsMelee>());
        }

        // Unit scope is exact here only because every carrier is a single-model unit - pinned below.
        Assert.That(rule.Scope, Is.EqualTo(ERuleScope.Unit));
        Assert.That(rule.EngineArgumentCount, Is.EqualTo(0));
    }

    // The profile IS the rule. Each rider is an existing weapon rule rather than new vocabulary: "at
    // Quality 2+" is Reliable (a QualityFloor folded by minimum), and Deadly(3) / Takedown are themselves.
    // A dropped name here is a silent partial rule, so all three are named explicitly.
    [TestCase(Strike)]
    [TestCase(Shot)]
    public void TheProfile_IsOneAttackAtApTwoCarryingReliableDeadlyAndTakedown(string ruleName)
    {
        var effect = Supplement(ruleName).Activated.Single().Effect as Effect.ExtraAttack;

        Assert.That(effect, Is.Not.Null, "the effect must be ExtraAttack - nothing else runs at this hook");
        Assert.That(effect!.Attacks, Is.EqualTo(1), "'one attack'");
        Assert.That(effect.ArmorPenetration, Is.EqualTo(2), "AP(2)");
        Assert.That(effect.WithRules, Is.EqualTo(new[] { "Reliable", "Deadly(3)", "Takedown" }),
            "Reliable = 'at Quality 2+'; the argument on Deadly(3) has to survive as text");
        Assert.That(effect.WeaponName, Is.EqualTo(ruleName),
            "the synthetic weapon is named for the rule, so the log and dice rows self-attribute");
    }

    // ---- The corpus census the design leans on ---------------------------------------------------------

    private record Site(string Book, string Unit, int MinModels, int MaxModels, bool FromItem);

    private static IEnumerable<Site> Sites(string ruleName)
    {
        foreach (string path in Directory.EnumerateFiles(BooksDirectory, "*" + BookFile.EXTENSION_WITH_PERIOD)
                     .OrderBy(p => p))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            string bookName = Path.GetFileNameWithoutExtension(path);

            foreach (RosterUnit unit in book.Units)
            {
                if (unit.Rules.Any(r => Names(r, ruleName)))
                {
                    yield return new Site(bookName, unit.Name, unit.MinModels, unit.MaxModels, false);
                }

                foreach (UpgradeSection section in unit.Sections)
                    foreach (UpgradeOption option in section.Options)
                    {
                        bool hit = option.RulesGained.Any(r => Names(r, ruleName))
                            || option.ItemsGained.Any(i => i.Rules.Any(r => Names(r, ruleName)));
                        if (hit)
                        {
                            yield return new Site(bookName, unit.Name, unit.MinModels, unit.MaxModels, true);
                        }
                    }
            }
        }
    }

    private static bool Names(SpecialRuleEntry entry, string ruleName) =>
        entry is SpecialRuleEntry_Core core && string.Equals(core.Name, ruleName, StringComparison.Ordinal);

    // The census that made unit scope safe. "Upgrade with one" grants the rule to ONE model, which would be
    // the per-model scoping problem Sergeant had - except every carrier is a 1-model hero, so unit scope and
    // model scope are the same thing. If a book update ever puts one of these on a multi-model unit, the
    // scoping has to be re-decided (a Unit-scoped rule would offer the ability once per unit, not per model).
    [TestCase(Strike, 5)]
    [TestCase(Shot, 2)]
    public void EveryCarrier_IsASingleModelUnit(string ruleName, int expectedRefs)
    {
        List<Site> sites = Sites(ruleName).ToList();

        Assert.That(sites.Count, Is.EqualTo(expectedRefs),
            $"the audit's {expectedRefs} references to {ruleName} - a change here means the corpus moved");

        foreach (Site site in sites)
        {
            Assert.That(site.MinModels, Is.EqualTo(1), $"{site.Book}/{site.Unit}");
            Assert.That(site.MaxModels, Is.EqualTo(1),
                $"{site.Book}/{site.Unit}: a multi-model carrier breaks the unit-scope reading");
        }
    }

    [Test]
    public void TheReferencingBooks_AreTheOnesThatEmbedTheDefinitions()
    {
        Assert.That(Sites(Strike).Select(s => s.Book).Distinct(), Is.EquivalentTo(StrikeBooks));
        Assert.That(Sites(Shot).Select(s => s.Book).Distinct(), Is.EquivalentTo(ShotBooks));
    }

    // The invariant BuildTargetListStage's lifted melee gate rests on (#197 P16): Takedown used to be
    // skipped for a swing, and the gate is gone so Takedown Strike can pick its victim. That is only a
    // no-change for existing data because no MELEE weapon in any bundled book carries Takedown - every
    // Takedown in the corpus is on a ranged profile. If that ever stops being true, the individual-model
    // pick starts appearing in ordinary close combat and the ruling needs revisiting.
    [Test]
    public void NoMeleeWeaponInAnyBook_CarriesTakedown()
    {
        var offenders = new List<string>();

        foreach (string path in Directory.EnumerateFiles(BooksDirectory, "*" + BookFile.EXTENSION_WITH_PERIOD)
                     .OrderBy(p => p))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            string bookName = Path.GetFileNameWithoutExtension(path);

            foreach (RosterUnit unit in book.Units)
            {
                foreach (WeaponFileEntry weapon in unit.Weapons)
                {
                    Record(bookName, unit.Name, weapon, offenders);
                }

                foreach (UpgradeSection section in unit.Sections)
                    foreach (UpgradeOption option in section.Options)
                        foreach (WeaponFileEntry weapon in option.WeaponsGained)
                        {
                            Record(bookName, unit.Name, weapon, offenders);
                        }
            }
        }

        Assert.That(offenders, Is.Empty,
            "a melee weapon carrying Takedown would newly get the individual-model pick in close combat");
    }

    private static void Record(string book, string unit, WeaponFileEntry weapon, List<string> offenders)
    {
        // Range 0 IS melee in this engine (IWeapon.IsMelee).
        if (weapon.RangeInches > 0f) return;
        if (weapon.SpecialRules.Any(r => Names(r, "Takedown")))
        {
            offenders.Add($"{book}/{unit}/{weapon.Name}");
        }
    }

    // ---- The embedded copies --------------------------------------------------------------------------

    [Test]
    public void TheEmbeddedBookCopies_CarryTheFullProfile()
    {
        foreach ((string ruleName, string[] books) in new[]
                 {
                     (Strike, StrikeBooks), (Shot, ShotBooks),
                 })
        {
            foreach (string bookName in books)
            {
                SpecialRuleDefinition embedded = LoadBook(bookName).RuleDefinitions
                    .Single(d => d.Name == ruleName);

                var effect = embedded.Activated.Single().Effect as Effect.ExtraAttack;
                Assert.That(effect, Is.Not.Null,
                    $"{bookName}/{ruleName}: the embedded copy lost its effect - re-run --apply-rules");
                Assert.That(effect!.WithRules, Is.EqualTo(new[] { "Reliable", "Deadly(3)", "Takedown" }),
                    $"{bookName}/{ruleName}: army load reads the BOOK's copy, so the riders must travel too");
                Assert.That(embedded.Activated.Single().Cost, Is.InstanceOf<Cost.OncePerGame>(), bookName);
            }
        }
    }

    // ---- End to end: a real book unit through the real compiler ----------------------------------------

    [Test]
    public void ClanHandler_BuyingASpyAssassinSniper_ResolvesTakedownShotOnTheUnit()
    {
        BookFile book = LoadBook("RatmenClans");
        RosterUnit handler = book.Units.Single(u => u.Name == "Clan Handler");
        UpgradeSection section = handler.Sections
            .Single(s => s.Options.Any(o => o.Label.Contains(Shot)));
        UpgradeOption option = section.Options.Single(o => o.Label.Contains(Shot));

        BuiltArmyFile army = ListCompiler.Compile(book, new BuilderList
        {
            Name = "Test", BookName = book.Name, PointsLimit = 500,
            Units =
            {
                new BuilderUnit
                {
                    RosterUnitId = handler.Id!,
                    Choices = { new UpgradeChoice { SectionId = section.Id!, OptionId = option.Id!, Count = 1 } },
                },
            },
        });

        UnitFileEntry unit = army.Units.Single();
        Assert.That(unit.SpecialRules.Select(r => r.PrintableName), Does.Contain(Shot),
            "the item's rule flattens onto the unit, which is exact for a 1-model hero");
    }
}
