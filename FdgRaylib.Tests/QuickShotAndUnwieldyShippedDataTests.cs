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

// #197 P20 - the action-permission pair. Quick Shot ("may shoot after using Rush actions", 5 refs as the
// Aura, 4 as the Mark) and Unwieldy ("strikes last when charging", 3 refs as the Debuff).
//
// Neither BASE rule is referenced by any unit in the corpus: both exist only as the target of a grant -
// an aura, a mark, a one-shot debuff, or a spell. That is why --rule-coverage never counted them, and it
// is the failure mode these tests exist for: a broken grant chain leaves the wrapper resolving cleanly
// while the mechanic underneath is unreachable, and nothing in the coverage report would say so.
//
// The mechanics are pinned engine-side (QuickShotRuleIntegrationTests, UnwieldyRuleIntegrationTests).
[TestFixture]
public class QuickShotAndUnwieldyShippedDataTests
{
    private const string QuickShot = "Quick Shot";
    private const string QuickShotAura = "Quick Shot Aura";
    private const string QuickShotMark = "Quick Shot Mark";
    private const string Unwieldy = "Unwieldy in melee";
    private const string UnwieldyDebuff = "Unwieldy Debuff";

    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static IReadOnlyList<SpecialRuleDefinition> Supplement() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
            Path.Combine(BooksDirectory, "GdfRuleSupplement.json")));

    private static SpecialRuleDefinition Definition(string name) =>
        Supplement().Single(r => string.Equals(r.Name, name, StringComparison.OrdinalIgnoreCase));

    // ---- The base rules ------------------------------------------------------------------------------

    [Test]
    public void QuickShot_IsAShootPermissionAtTheActionChoiceHook()
    {
        HookEntry entry = Definition(QuickShot).Passive.Single();

        Assert.That(entry.HookID, Is.EqualTo(EHookID.Activation_OnActionChoice),
            "the hook ChooseActionStage fires while deciding what the unit may do");
        Assert.That(entry.Seat, Is.EqualTo(ERuleSeat.Actor));
        Assert.That(entry.Effect, Is.InstanceOf<Effect.ShootAfterRush>(),
            "'may shoot after using Rush actions' - a permission, not a bigger Advance");
    }

    [Test]
    public void Unwieldy_IsAStrikeLastAtTheCounterHook()
    {
        HookEntry entry = Definition(Unwieldy).Passive.Single();

        Assert.That(entry.HookID, Is.EqualTo(EHookID.Melee_OnCounterTrigger),
            "the hook DetermineStrikeOrderStage fires to decide who swings first");
        Assert.That(entry.Seat, Is.EqualTo(ERuleSeat.Actor),
            "Unwieldy is the CHARGER's failing; on the Subject seat it would be Counter, a buff for " +
            "the unit it is aimed at");
        Assert.That(entry.Effect, Is.InstanceOf<Effect.StrikeLast>());
    }

    // ---- The wrappers, and that their grants land somewhere ------------------------------------------

    [Test]
    public void TheQuickShotAura_ConfersTheBaseRule()
    {
        HookEntry entry = Definition(QuickShotAura).Passive.Single();

        Assert.That(entry.Effect, Is.EqualTo(new Effect.Aura(QuickShot)),
            "every Quick Shot reference in the corpus is the Aura - a broken link makes the rule " +
            "unreachable in play");
    }

    [Test]
    public void TheQuickShotMark_MarksTheBaseRule()
    {
        ActivatedAbility ability = Definition(QuickShotMark).Activated.Single();

        Assert.That(ability.TriggerHook, Is.EqualTo(EHookID.Activation_OnBeforeAttackAction));
        Assert.That(ability.Cost, Is.InstanceOf<Cost.OncePerActivation>());
        Assert.That(ability.TargetSelector!.TargetAffinity, Is.EqualTo(ETargetAffinity.Foe));
        Assert.That(ability.TargetSelector.RangeInches, Is.EqualTo(18f));
        Assert.That(ability.TargetSelector.RequireLineOfSight, Is.True, "'in line of sight'");
        Assert.That(ability.Effect, Is.EqualTo(new Effect.MarkTarget(QuickShot)),
            "'friendly units get Quick Shot AGAINST it once' - the permission rides the enemy, which " +
            "is what binds it to that target");
    }

    [Test]
    public void TheUnwieldyDebuff_GrantsTheBaseRuleOnce()
    {
        ActivatedAbility ability = Definition(UnwieldyDebuff).Activated.Single();

        Assert.That(ability.TriggerHook, Is.EqualTo(EHookID.Activation_OnBeforeAttackAction));
        Assert.That(ability.Cost, Is.InstanceOf<Cost.OncePerActivation>());
        Assert.That(ability.TargetSelector!.RangeInches, Is.EqualTo(18f));
        Assert.That(ability.TargetSelector.RequireLineOfSight, Is.True, "'in line of sight'");
        Assert.That(ability.Effect, Is.EqualTo(new Effect.AddRule(Unwieldy, ELifetime.NextTrigger)),
            "'gets Unwieldy in melee ONCE (next time the effect would apply)'");
    }

    // Every grant target must actually resolve, and to a definition with dispatch entries. A grant naming
    // a rule the registry does not carry warns once and does nothing - the whole point of the wrapper
    // vanishes, and the wrapper itself still lints clean.
    [TestCase(QuickShotAura)]
    [TestCase(QuickShotMark)]
    [TestCase(UnwieldyDebuff)]
    public void EveryWrappersGrantTarget_IsItselfAuthored(string wrapper)
    {
        // The granted name is READ OFF the wrapper rather than passed in, so a renamed grant fails here
        // too - that is exactly the break that leaves the wrapper linting clean and the mechanic dead.
        string? granted = GrantedRuleName(Definition(wrapper));

        Assert.That(granted, Is.Not.Null, $"'{wrapper}' must grant something");
        Assert.That(Supplement().Any(r => string.Equals(r.Name, granted, StringComparison.OrdinalIgnoreCase)),
            Is.True, $"'{wrapper}' grants '{granted}', which is not authored");
        Assert.That(Definition(granted!).Passive, Is.Not.Empty,
            $"'{granted}' must have dispatch entries, or the grant is a silent no-op");
    }

    /// <summary>The rule name a wrapper hands out, whichever grant shape it uses.</summary>
    private static string? GrantedRuleName(SpecialRuleDefinition definition)
    {
        IEnumerable<Effect> effects = definition.Passive.Select(entry => entry.Effect)
            .Concat(definition.Activated.Select(ability => ability.Effect));

        foreach (Effect effect in effects)
        {
            switch (effect)
            {
                case Effect.Aura aura: return aura.RuleName;
                case Effect.MarkTarget mark: return mark.RuleName;
                case Effect.AddRule add: return add.RuleName;
            }
        }
        return null;
    }

    // ---- The corpus, book by book ---------------------------------------------------------------------

    private record Site(string Book, string Unit, string Rule);

    private static IEnumerable<Site> Sites()
    {
        string[] tracked = { QuickShotAura, QuickShotMark, UnwieldyDebuff };

        foreach (string path in Directory.EnumerateFiles(BooksDirectory, "*" + BookFile.EXTENSION_WITH_PERIOD)
                     .OrderBy(p => p))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            string bookName = Path.GetFileNameWithoutExtension(path);

            foreach (RosterUnit unit in book.Units)
                foreach (string name in RuleNamesOn(unit))
                    if (tracked.Contains(name, StringComparer.OrdinalIgnoreCase))
                        yield return new Site(bookName, unit.Name, name);
        }
    }

    private static IEnumerable<string> RuleNamesOn(RosterUnit unit)
    {
        foreach (SpecialRuleEntry rule in unit.Rules) yield return NameOf(rule);
        foreach (ItemEntry item in unit.Items)
            foreach (SpecialRuleEntry rule in item.Rules) yield return NameOf(rule);

        foreach (UpgradeSection section in unit.Sections)
            foreach (UpgradeOption option in section.Options)
            {
                foreach (SpecialRuleEntry rule in option.RulesGained) yield return NameOf(rule);
                foreach (ItemEntry item in option.ItemsGained)
                    foreach (SpecialRuleEntry rule in item.Rules) yield return NameOf(rule);
            }
    }

    private static string NameOf(SpecialRuleEntry rule) =>
        ArmyListRuleResolution.DescribeRuleEntry(rule).lookupName;

    [Test]
    public void EveryReference_ResolvesAgainstItsOwnBook_AlongWithWhatItGrants()
    {
        List<Site> sites = Sites().ToList();

        Assert.That(sites.Count(s => s.Rule == QuickShotAura), Is.EqualTo(5));
        Assert.That(sites.Count(s => s.Rule == QuickShotMark), Is.EqualTo(4));
        Assert.That(sites.Count(s => s.Rule == UnwieldyDebuff), Is.EqualTo(3),
            "the audit's 12 P20 references - a change here means the corpus moved, not the engine");

        var problems = new List<string>();
        foreach (string bookName in sites.Select(s => s.Book).Distinct())
        {
            RuleResolver resolver = ResolverFor(bookName);
            foreach (Site site in sites.Where(s => s.Book == bookName))
            {
                if (!resolver.TryResolve(site.Rule, out ResolvedRule wrapper))
                {
                    problems.Add($"{site.Book}: {site.Unit} - '{site.Rule}' has no definition");
                    continue;
                }

                // Read the grant target off the book's OWN copy of the wrapper, not off the supplement:
                // this is the shipped chain, and a book embedded before a rename would break here.
                string? granted = GrantedRuleName(wrapper.Definition);
                if (granted == null
                    || !resolver.TryResolve(granted, out ResolvedRule target)
                    || target.Definition.Passive.Count == 0)
                {
                    problems.Add($"{site.Book}: '{site.Rule}' grants '{granted}', which its book does not carry");
                }
            }
        }

        Assert.That(problems, Is.Empty, string.Join("; ", problems));
    }

    // The three books whose Combat Ecstasy spell marks "Quick Shot", and High Elf Fleets' Creator of
    // Illusions, which grants "Unwieldy in melee". These are SPELL grants, so --rule-coverage (which walks
    // unit/item/weapon/upgrade sites only) never reported them - they were dangling names that resolved to
    // nothing, and the spell silently did nothing when cast.
    [Test]
    public void EverySpellGrantingThesePermissions_HasTheDefinitionEmbedded()
    {
        var problems = new List<string>();

        foreach (string path in Directory.EnumerateFiles(BooksDirectory, "*" + BookFile.EXTENSION_WITH_PERIOD)
                     .OrderBy(p => p))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            string bookName = Path.GetFileNameWithoutExtension(path);
            RuleResolver? resolver = null;

            foreach (SpellDefinition spell in book.Spells)
            {
                string? granted = spell.Effect switch
                {
                    Effect.MarkTarget mark => mark.RuleName,
                    Effect.AddRule add => add.RuleName,
                    _ => null,
                };
                if (granted != QuickShot && granted != Unwieldy) continue;

                resolver ??= ResolverFor(bookName);
                if (!resolver.TryResolve(granted, out ResolvedRule resolved)
                    || resolved.Definition.Passive.Count == 0)
                {
                    problems.Add($"{bookName}: spell '{spell.Name}' grants '{granted}', which its book does not carry");
                }
            }
        }

        Assert.That(problems, Is.Empty, string.Join("; ", problems));
    }

    private static RuleResolver ResolverFor(string bookName)
    {
        BookFile book = JsonSerializer.Deserialize<BookFile>(
            File.ReadAllText(Path.Combine(BooksDirectory, bookName + BookFile.EXTENSION_WITH_PERIOD)),
            RuleJson.Options)!;

        RuleResolver resolver = CoreRuleCatalog.CreateResolver();
        foreach (SpecialRuleDefinition definition in book.RuleDefinitions)
            resolver.RegisterOrReplace(definition);
        return resolver;
    }
}
