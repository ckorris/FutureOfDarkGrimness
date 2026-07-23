using System;
using System.Collections.Generic;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.SaveLoad;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #259 — the name -> description lookup behind the Army Forge's rule hover tooltips.
[TestFixture]
public class RuleGlossaryTests
{
    private static SpecialRuleDefinition Definition(string name, string description) =>
        new(name, Array.Empty<HookEntry>(), Array.Empty<ActivatedAbility>(), Description: description);

    private static BookFile BookWith(params SpecialRuleDefinition[] definitions) =>
        new() { Name = "Test Book", RuleDefinitions = new List<SpecialRuleDefinition>(definitions) };

    [Test]
    public void Describe_CoreRule_ReturnsTheCatalogsDescription()
    {
        RuleGlossary glossary = RuleGlossary.Build(BookWith());
        Assert.That(glossary.Describe("Stealth"), Is.EqualTo(CoreRuleCatalog.Stealth.Description));
        Assert.That(glossary.Describe("Stealth"), Is.Not.Empty);
    }

    [Test]
    public void Describe_BookDefinition_OverridesCoreByName()
    {
        RuleGlossary glossary = RuleGlossary.Build(BookWith(Definition("Stealth", "This book's own Stealth.")));
        Assert.That(glossary.Describe("Stealth"), Is.EqualTo("This book's own Stealth."));
    }

    [Test]
    public void Describe_FactionRuleFromTheBook_Resolves()
    {
        RuleGlossary glossary = RuleGlossary.Build(BookWith(Definition("Highborn", "Moves +2\" when Advancing.")));
        Assert.That(glossary.Describe("Highborn"), Is.EqualTo("Moves +2\" when Advancing."));
    }

    [Test]
    public void Describe_NumericEntry_LooksUpTheCanonicalNameNotThePrintableOne()
    {
        RuleGlossary glossary = RuleGlossary.Build(BookWith());
        var tough = new SpecialRuleEntry_CoreNumeric("Tough", 3);

        Assert.That(tough.PrintableName, Is.EqualTo("Tough(3)"));
        Assert.That(glossary.Describe("Tough(3)"), Is.Null, "the printable name is not a catalog key");
        Assert.That(glossary.Describe(tough), Is.EqualTo(CoreRuleCatalog.Tough.Description));
    }

    [Test]
    public void Describe_Alias_FallsThroughToTheRuleItRenames()
    {
        RuleGlossary glossary = RuleGlossary.Build(BookWith());
        var alias = new SpecialRuleEntry_Alias("Medical Training", new SpecialRuleEntry_Core("Regeneration"));

        Assert.That(glossary.Describe(alias), Is.EqualTo(CoreRuleCatalog.Regeneration.Description));
    }

    [Test]
    public void Describe_Alias_PrefersTheAliassOwnDefinitionWhenTheBookCarriesOne()
    {
        RuleGlossary glossary = RuleGlossary.Build(
            BookWith(Definition("Medical Training", "Battlefield surgery: as Regeneration, but fluffier.")));
        var alias = new SpecialRuleEntry_Alias("Medical Training", new SpecialRuleEntry_Core("Regeneration"));

        Assert.That(glossary.Describe(alias), Is.EqualTo("Battlefield surgery: as Regeneration, but fluffier."));
    }

    [Test]
    public void Describe_UnknownRule_ReturnsNull()
    {
        RuleGlossary glossary = RuleGlossary.Build(BookWith());
        Assert.That(glossary.Describe("Repel Ambushers"), Is.Null);
        Assert.That(glossary.Describe(new SpecialRuleEntry_Core("Repel Ambushers")), Is.Null);
    }

    [Test]
    public void Describe_DefinitionWithoutDescription_CountsAsUnknown()
    {
        RuleGlossary glossary = RuleGlossary.Build(BookWith(Definition("Silent Rule", "")));
        Assert.That(glossary.Describe("Silent Rule"), Is.Null);
    }

    // #260: the corpus is inconsistent about casing - the bundled books say "Bane in Melee" where the
    // catalog says "Bane in melee" - and RuleResolver has matched case-insensitively since #100, so these
    // rules DO fire in play. The glossary must describe them, not report them as inert.
    [Test]
    public void Describe_IsCaseInsensitive_LikeTheEnginesResolver()
    {
        RuleGlossary glossary = RuleGlossary.Build(BookWith());
        foreach (string bookSpelling in new[]
                 {
                     "Bane in Melee", "Rending in Melee", "Shred in Melee",
                     "Shred when Shooting", "Unstoppable in Melee",
                 })
            Assert.That(glossary.Describe(bookSpelling), Is.Not.Null, bookSpelling);
    }

    // The guard that keeps the two from drifting apart again: the glossary must be silent exactly when
    // army load would also fail to resolve the name, so a tooltip never contradicts what the engine does.
    [Test]
    public void Describe_IsSilentExactlyWhenTheResolverCannotResolve()
    {
        RuleResolver resolver = CoreRuleCatalog.CreateResolver();
        RuleGlossary glossary = RuleGlossary.Build(BookWith());

        foreach (string name in new[]
                 {
                     "Stealth", "stealth", "STEALTH", "Bane in melee", "Bane in Melee",
                     "Shred when Shooting", "Repel Ambushers", "Not A Rule At All",
                 })
            Assert.That(glossary.Describe(name) is not null, Is.EqualTo(resolver.TryResolve(name, out _)),
                $"glossary and resolver disagree about '{name}'");
    }

    [Test]
    public void Build_BookDefinition_OverridesCoreEvenWhenItsCasingDiffers()
    {
        // RegisterOrReplace overrides through the same case-insensitive dictionary at army load, so the
        // glossary must not end up describing the core rule while the engine runs the book's version.
        RuleGlossary glossary = RuleGlossary.Build(BookWith(Definition("Bane in Melee", "This book's own.")));
        Assert.That(glossary.Describe("Bane in melee"), Is.EqualTo("This book's own."));
    }

    [Test]
    public void Tooltip_LeadsWithThePrintableName_ThenTheDescription()
    {
        RuleGlossary glossary = RuleGlossary.Build(BookWith());
        string tooltip = glossary.Tooltip(new SpecialRuleEntry_CoreNumeric("Tough", 3));

        Assert.That(tooltip, Is.EqualTo($"Tough(3)\n{CoreRuleCatalog.Tough.Description}"));
    }

    [Test]
    public void Tooltip_UnknownRule_SaysItIsNotEnforced()
    {
        RuleGlossary glossary = RuleGlossary.Build(BookWith());
        Assert.That(glossary.Tooltip("Repel Ambushers"),
            Is.EqualTo($"Repel Ambushers\n{RuleGlossary.UnknownRuleText}"));
    }

    [Test]
    public void Empty_ResolvesNothing()
    {
        Assert.That(RuleGlossary.Empty.Describe("Stealth"), Is.Null);
    }

    // The bundled books are the real input: every faction rule they reference should either resolve in the
    // core catalog or ride along in the book's own definitions. This pins the one book we control by hand.
    [Test]
    public void Build_DemoBook_DescribesItsUnitsRules()
    {
        BookFile demo = DemoBook.Build();
        RuleGlossary glossary = RuleGlossary.Build(demo);

        foreach (RosterUnit unit in demo.Units)
            foreach (SpecialRuleEntry rule in unit.Rules)
                Assert.That(glossary.Describe(rule), Is.Not.Null, $"no description for '{rule.PrintableName}'");
    }
}
