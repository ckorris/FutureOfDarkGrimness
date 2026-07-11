using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #197 slice 0 — the regression guard for the scope-mismatch bug: 157 references to rules the engine
// fully implements never attached, because ArmyListRuleResolution refuses a definition whose declared
// ERuleScope differs from the site naming it, and the imported corpus disagreed with the catalog.
//
// A weapon-scoped rule named at UNIT level is no longer a mismatch: wargear is a rule-bundle folded into
// the unit's rules, and both attachment paths now re-home it (ListCompiler onto the targeted weapon,
// army-load onto every weapon otherwise). The surviving mismatch is a UNIT-scoped rule named on a WEAPON,
// which has nowhere to go and is still dropped with a warning — so that is what this asserts.
//
// Without this, the next OPR import silently reintroduces the bug: the books are regenerated data, and a
// rule quietly doing nothing looks exactly like a rule working.
[TestFixture]
public class BookRuleScopeTests
{
    // A rule the corpus names on weapons that the catalog still defines at unit scope. Each entry is a
    // known engine gap with a reason, not a tolerance for misauthored data — a stale entry (the rule
    // starts resolving cleanly) fails the fixture just as a new mismatch does.
    private static readonly IReadOnlyDictionary<string, string> Allowlist = new Dictionary<string, string>
    {
        ["Strafing"] = "the source rule is weapon-scoped - the mid-move attack is made with the weapon " +
            "carrying the rule, and that weapon may be used no other way - so all 12 corpus references are " +
            "correctly on bomb weapons. The catalog's Strafing is a unit-scoped approximation (3 hits, no " +
            "weapon restriction) whose fly-over passive rides Movement_OnMoveThroughEnemy, a hook that " +
            "never reads weapon rules. Re-scoping it needs a mid-move attack-with-this-weapon primitive " +
            "plus a once-per-activation weapon-use restriction - its own #197 slice.",
    };

    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static IEnumerable<TestCaseData> Books() =>
        Directory.EnumerateFiles(BooksDirectory, "*" + BookFile.EXTENSION_WITH_PERIOD)
            .OrderBy(path => path)
            .Select(path => new TestCaseData(path).SetArgDisplayNames(Path.GetFileNameWithoutExtension(path)));

    /// <summary>Where a rule name sits in a book, and the scope that site attaches at.</summary>
    private record Reference(string Site, string Name, ERuleScope AttachesAt);

    [TestCaseSource(nameof(Books))]
    public void NoUnitScopedRuleIsNamedOnAWeapon(string bookPath)
    {
        BookFile book = LoadBook(bookPath);
        IRuleResolver resolver = ResolverFor(book);

        List<string> mismatches = new();

        foreach (Reference reference in ReferencesIn(book))
        {
            if (reference.AttachesAt != ERuleScope.Weapon) continue;

            // Unresolvable names are the other half of the audit (#196's data authoring), not this test's
            // business — a name with no definition anywhere has no scope to disagree about.
            if (!resolver.TryResolve(reference.Name, out ResolvedRule resolved)) continue;
            if (resolved.Definition.Scope == ERuleScope.Weapon) continue;

            if (Allowlist.ContainsKey(resolved.Definition.Name)) continue;

            mismatches.Add($"{reference.Site}: '{reference.Name}' resolves to a " +
                $"{resolved.Definition.Scope}-scoped definition and will be dropped at army load.");
        }

        Assert.That(mismatches, Is.Empty,
            $"{Path.GetFileName(bookPath)} names unit-scoped rules on weapons:{Environment.NewLine}  " +
            string.Join(Environment.NewLine + "  ", mismatches));
    }

    [Test]
    public void AllowlistedRulesAreStillMismatchedSomewhere()
    {
        var stillMismatched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (string bookPath in Directory.EnumerateFiles(BooksDirectory, "*" + BookFile.EXTENSION_WITH_PERIOD))
        {
            BookFile book = LoadBook(bookPath);
            IRuleResolver resolver = ResolverFor(book);

            foreach (Reference reference in ReferencesIn(book))
            {
                if (reference.AttachesAt != ERuleScope.Weapon) continue;
                if (!resolver.TryResolve(reference.Name, out ResolvedRule resolved)) continue;
                if (resolved.Definition.Scope != ERuleScope.Weapon) stillMismatched.Add(resolved.Definition.Name);
            }
        }

        List<string> stale = Allowlist.Keys.Where(name => !stillMismatched.Contains(name)).ToList();
        Assert.That(stale, Is.Empty,
            "These rules are allowlisted as known scope gaps but now resolve cleanly - delete their " +
            "allowlist entries: " + string.Join(", ", stale));
    }

    // The end-to-end case behind the owner's ruling (2026-07-09): a weapon rule applies only when THAT
    // weapon fires. Sniper Drones buy a Drone Controller that upgrades their Pulse Rifles; they also carry
    // a melee Taser, which must not inherit Reliable's 2+ hit floor or Takedown's single-model targeting.
    [Test]
    public void SniperDrones_DroneController_UpgradesOnlyThePulseRifle()
    {
        BookFile book = LoadBook(Path.Combine(BooksDirectory, "DAOUnion" + BookFile.EXTENSION_WITH_PERIOD));
        RosterUnit drones = book.Units.Single(u => u.Name == "Sniper Drones");
        UpgradeSection section = drones.Sections.Single(s => s.Targets.Contains("Pulse Rifles"));

        var list = new BuilderList
        {
            Name = "Scope Test", BookName = book.Name, PointsLimit = 500,
            Units =
            {
                new BuilderUnit
                {
                    RosterUnitId = drones.Id,
                    Choices =
                    {
                        new UpgradeChoice
                        {
                            SectionId = section.Id, OptionId = section.Options.Single().Id, Count = 1,
                        },
                    },
                },
            },
        };

        UnitFileEntry compiled = ListCompiler.Compile(book, list).Units.Single();

        IEnumerable<string> RuleNames(IEnumerable<SpecialRuleEntry> rules) => rules.Select(r => r.PrintableName);

        Assert.That(RuleNames(compiled.Weapons.Single(w => w.Name == "Pulse Rifle").SpecialRules),
            Is.EquivalentTo(new[] { "Reliable", "Takedown" }));
        Assert.That(compiled.Weapons.Single(w => w.Name == "Taser").SpecialRules, Is.Empty,
            "The melee Taser is not the upgrade's target.");
        Assert.That(RuleNames(compiled.SpecialRules).Intersect(new[] { "Reliable", "Takedown" }), Is.Empty,
            "Rules placed on their target weapon do not also fold onto the unit.");
    }

    // #197 (Darkborn): OPR names two mechanically different rules bare "Darkborn" across armies, so the bare
    // name resolved to neither catalog variant and all 59 references were dead. The bundled books now name the
    // army's variant, and each resolves to the fully-built definition (its mechanics are proven by
    // RangeModifierRuleIntegrationTests). This is the end-to-end guard the reference-count fix rests on: a
    // regression to the bare name - or a re-import that reintroduces it without the importer's disambiguation -
    // would leave the rule silently doing nothing, which looks exactly like a rule working.
    [TestCase("DarkBrothers", "Darkborn (Defensive)")]
    [TestCase("DarkPrimeBrothers", "Darkborn (Offensive)")]
    public void Darkborn_ResolvesToTheArmyVariant(string bookFile, string expectedVariant)
    {
        BookFile book = LoadBook(Path.Combine(BooksDirectory, bookFile + BookFile.EXTENSION_WITH_PERIOD));
        IRuleResolver resolver = ResolverFor(book);

        List<Reference> darkbornRefs = ReferencesIn(book)
            .Where(r => r.Name.StartsWith("Darkborn", StringComparison.Ordinal)).ToList();

        Assert.That(darkbornRefs, Is.Not.Empty, $"{bookFile} should still reference Darkborn.");
        foreach (Reference reference in darkbornRefs)
        {
            Assert.That(reference.Name, Is.EqualTo(expectedVariant),
                $"{reference.Site}: bare 'Darkborn' resolves to nothing - it must name the army's variant.");
            Assert.That(resolver.TryResolve(reference.Name, out ResolvedRule resolved), Is.True,
                $"{reference.Site}: '{reference.Name}' must resolve to a definition.");
            Assert.That(resolved.Definition.Name, Is.EqualTo(expectedVariant));
        }
    }

    private static BookFile LoadBook(string path) =>
        JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;

    private static IRuleResolver ResolverFor(BookFile book)
    {
        RuleResolver resolver = CoreRuleCatalog.CreateResolver();
        foreach (SpecialRuleDefinition definition in book.RuleDefinitions) resolver.RegisterOrReplace(definition);
        return resolver;
    }

    /// <summary>Every rule name a book states, paired with the scope its attachment site resolves at.
    /// Mirrors what army load does: weapon profiles attach at Weapon scope; a unit's own rules and its
    /// wargear rule-bundles (which ListCompiler flattens into the unit) attach at Unit scope.</summary>
    private static IEnumerable<Reference> ReferencesIn(BookFile book)
    {
        foreach (RosterUnit unit in book.Units)
        {
            foreach (Reference reference in RulesOf(unit.Rules, $"{unit.Name}", ERuleScope.Unit))
                yield return reference;

            foreach (ItemEntry item in unit.Items)
                foreach (Reference reference in RulesOf(item.Rules, $"{unit.Name} / item '{item.Name}'", ERuleScope.Unit))
                    yield return reference;

            foreach (WeaponFileEntry weapon in unit.Weapons)
                foreach (Reference reference in RulesOf(weapon.SpecialRules, $"{unit.Name} / weapon '{weapon.Name}'", ERuleScope.Weapon))
                    yield return reference;

            foreach (UpgradeSection section in unit.Sections)
                foreach (UpgradeOption option in section.Options)
                {
                    string where = $"{unit.Name} / option '{option.Label}'";

                    foreach (Reference reference in RulesOf(option.RulesGained, where, ERuleScope.Unit))
                        yield return reference;

                    foreach (ItemEntry item in option.ItemsGained)
                        foreach (Reference reference in RulesOf(item.Rules, $"{where} / item '{item.Name}'", ERuleScope.Unit))
                            yield return reference;

                    foreach (WeaponFileEntry weapon in option.WeaponsGained)
                        foreach (Reference reference in RulesOf(weapon.SpecialRules, $"{where} / weapon '{weapon.Name}'", ERuleScope.Weapon))
                            yield return reference;
                }
        }
    }

    private static IEnumerable<Reference> RulesOf(IEnumerable<SpecialRuleEntry> rules, string site, ERuleScope scope) =>
        rules.Select(rule => new Reference(site, ArmyListRuleResolution.DescribeRuleEntry(rule).lookupName, scope));
}
