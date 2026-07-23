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

    // The engine's RuleResolver is case-sensitive, so a book name that differs only by case genuinely does
    // not resolve and is inert in play. The tooltip must report that, not paper over it with the right text.
    [Test]
    public void Describe_IsCaseSensitive_LikeTheEnginesResolver()
    {
        RuleGlossary glossary = RuleGlossary.Build(BookWith());
        Assert.That(glossary.Describe("Bane in melee"), Is.Not.Null, "the catalog's spelling");
        Assert.That(glossary.Describe("Bane in Melee"), Is.Null, "the books' spelling - inert in play");
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
