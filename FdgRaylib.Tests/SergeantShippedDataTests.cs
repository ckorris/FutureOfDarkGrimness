using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #197 Sergeant - OPR 8HWdOwMYcI0p: "When this model attacks, unmodified results of 6 to hit deal 1 extra
// hit (only the original hit counts as a 6 for special rules)." Dead no-definition (12 refs, the champion
// option of every Wormhole Daemons troop squad) until 2026-07-29: authored as a WEAPON-scoped Surge body,
// which is what routes it through ListCompiler's champion-marking - a weapon-scoped rule gained from a
// targets-less per-model section attaches to ONE copy of each weapon profile (after all Replaces) instead
// of folding unit-wide, and the marked copy rolls as its own volley. The mechanism is pinned engine-side by
// SergeantRuleIntegrationTests; these pin the authored JSON, the corpus shape the compiler gate relies on,
// the embedded book copies, and a real-book compile.
[TestFixture]
public class SergeantShippedDataTests
{
    private const string RuleName = "Sergeant";

    private static readonly string[] Books =
    {
        "WormholeDaemonsofChange", "WormholeDaemonsofLust", "WormholeDaemonsofPlague", "WormholeDaemonsofWar",
    };

    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static BookFile LoadBook(string name) => JsonSerializer.Deserialize<BookFile>(
        File.ReadAllText(Path.Combine(BooksDirectory, name + BookFile.EXTENSION_WITH_PERIOD)),
        RuleJson.Options)!;

    private static SpecialRuleDefinition Sergeant() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
                Path.Combine(BooksDirectory, "GdfRuleSupplement.json")))
            .Single(r => r.Name == RuleName);

    // ---- The authored definition ----------------------------------------------------------------------

    [Test]
    public void Sergeant_IsAWeaponScopedSurgeBody()
    {
        SpecialRuleDefinition rule = Sergeant();
        HookEntry entry = rule.Passive.Single();

        // The scope is load-bearing twice over: it routes ListCompiler's champion-marking (a Unit-scoped
        // Sergeant would fold unit-wide - the over-grant), and it is what lets the rule live on a weapon
        // copy at all.
        Assert.That(rule.Scope, Is.EqualTo(ERuleScope.Weapon));

        Assert.That(entry.HookID, Is.EqualTo(EHookID.Shooting_OnHitRollComplete),
            "the shared hit-complete hook - fires for shooting AND melee, as 'when this model attacks' asks");
        Assert.That(entry.Condition, Is.InstanceOf<Condition.UnmodifiedRollEquals>());
        Assert.That(entry.Effect, Is.InstanceOf<Effect.AddExtraHit>());
        Assert.That(((Effect.AddExtraHit)entry.Effect).OnRollValue, Is.EqualTo(6));
        Assert.That(rule.EngineArgumentCount, Is.EqualTo(0));
    }

    // ---- The corpus: the exact shape the compiler gate keys on -----------------------------------------

    private record Site(string Book, string Unit, UpgradeSection Section);

    private static IEnumerable<Site> Sites()
    {
        foreach (string path in Directory.EnumerateFiles(BooksDirectory, "*" + BookFile.EXTENSION_WITH_PERIOD)
                     .OrderBy(p => p))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            string bookName = Path.GetFileNameWithoutExtension(path);

            foreach (RosterUnit unit in book.Units)
                foreach (UpgradeSection section in unit.Sections)
                    foreach (UpgradeOption option in section.Options)
                        foreach (SpecialRuleEntry rule in option.RulesGained)
                            if (rule is SpecialRuleEntry_Core core
                                && string.Equals(core.Name, RuleName, StringComparison.Ordinal))
                                yield return new Site(bookName, unit.Name, section);
        }
    }

    [Test]
    public void EveryReference_IsATargetlessPerModelSection()
    {
        List<Site> sites = Sites().ToList();

        Assert.That(sites.Count, Is.EqualTo(12),
            "the audit's 12 references - a change here means the corpus moved, not the engine");
        Assert.That(sites.Select(s => s.Book).Distinct(), Is.EquivalentTo(Books));

        foreach (Site site in sites)
        {
            // The champion-marking gate: targets-less, not affects-All. If a book update ever moves a
            // Sergeant into a different section shape, the compiler routing must be re-audited.
            Assert.That(site.Section.Targets, Is.Empty, $"{site.Book}/{site.Unit}");
            Assert.That(site.Section.Affects, Is.Not.EqualTo(UpgradeAffects.All), $"{site.Book}/{site.Unit}");
        }
    }

    [Test]
    public void TheEmbeddedBookCopies_AreWeaponScoped()
    {
        // Scope travels with the embedded copy: ListCompiler reads the BOOK's definitions to decide the
        // champion routing, so an embedded Unit-scoped Sergeant would silently fall back to the over-grant.
        foreach (string bookName in Books)
        {
            SpecialRuleDefinition embedded = LoadBook(bookName).RuleDefinitions
                .Single(d => d.Name == RuleName);

            Assert.That(embedded.Scope, Is.EqualTo(ERuleScope.Weapon), bookName);
            Assert.That(embedded.Passive.Single().Effect, Is.InstanceOf<Effect.AddExtraHit>(),
                $"{bookName}: the embedded copy carries the extra-hit body - re-run --apply-rules");
        }
    }

    // ---- End to end: a real book unit through the real compiler ----------------------------------------

    [Test]
    public void BloodWarriors_BuyingASergeant_GetOneMarkedCopyPerWeaponProfile()
    {
        BookFile book = LoadBook("WormholeDaemonsofWar");
        RosterUnit warriors = book.Units.Single(u => u.Name == "Blood Warriors");
        UpgradeSection champions = warriors.Sections
            .Single(s => s.Options.Any(o => o.Label == "Sergeant") && s.Targets.Count == 0);
        UpgradeOption sergeant = champions.Options.Single(o => o.Label == "Sergeant");

        BuiltArmyFile army = ListCompiler.Compile(book, new BuilderList
        {
            Name = "Test", BookName = book.Name, PointsLimit = 500,
            Units =
            {
                new BuilderUnit
                {
                    RosterUnitId = warriors.Id!,
                    Choices =
                    {
                        new UpgradeChoice { SectionId = champions.Id!, OptionId = sergeant.Id!, Count = 1 },
                    },
                },
            },
        });

        UnitFileEntry unit = army.Units.Single();
        Assert.That(unit.SpecialRules.Select(r => r.PrintableName), Does.Not.Contain(RuleName),
            "the shipped book + shipped compiler must not fold the champion's rule unit-wide");

        List<WeaponFileEntry> marked = unit.Weapons
            .Where(w => w.SpecialRules.Any(r => r.PrintableName == RuleName)).ToList();
        List<string> baseProfiles = unit.Weapons.Except(marked).Select(w => w.Name).Distinct().ToList();

        Assert.That(marked, Has.Count.EqualTo(baseProfiles.Count),
            "one marked copy per weapon profile - the sergeant's own attacks, nothing else's");
        foreach (WeaponFileEntry copy in marked)
        {
            Assert.That(copy.Quantity, Is.EqualTo(1));
            Assert.That(copy.Name, Does.EndWith("(Sergeant)"),
                "unique names keep the shoot chooser's name-keyed pool valid, and the row self-explains");
        }

        Assert.That(unit.Weapons.Select(w => w.Name), Is.Unique,
            "a duplicate compiled name faults the ranged-attack chooser - found in the play probe");
    }
}
