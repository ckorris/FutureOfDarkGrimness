using System.Collections.Generic;
using System.Linq;
using FDG.ArmyBuilding;
using FDG.Calculator;
using FDG.SaveLoad;
using FdgRaylib.Rendering;
using FdgRaylib.Rendering.CombatCalc;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #397 - the state behind the Combat Calculator's three columns. The ImGui layout itself is
// hand-verified in the running window; these pin the parts that decide what it shows.
[TestFixture]
public class CombatCalculatorScreenTests
{
    // ---- one column's unit ---------------------------------------------------------------------

    [Test]
    public void ChoosingAUnit_BuildsItAtItsDefaultSizeAndPrice()
    {
        var side = new CalculatorSide();
        side.SetUnit(Book, "warriors");

        Assert.Multiple(() =>
        {
            Assert.That(side.HasUnit, Is.True);
            Assert.That(side.Detail().Unit.Name, Is.EqualTo("Vanguard Warriors"));
            Assert.That(side.Detail().Unit.ModelCount, Is.EqualTo(5), "the roster's default size");
            Assert.That(side.Points, Is.EqualTo(65));
        });
    }

    [Test]
    public void CombiningDoublesTheUnitAndItsPrice()
    {
        var side = new CalculatorSide();
        side.SetUnit(Book, "warriors");
        Assert.That(side.CanCombine, Is.True, "a multi-model non-Hero unit may combine");

        side.SetCombined(true);
        ArmyListFile compiled = side.Compile();

        Assert.Multiple(() =>
        {
            Assert.That(side.IsCombined, Is.True);
            Assert.That(compiled.Units, Has.Count.EqualTo(1), "the pair merges into one unit for play");
            Assert.That(compiled.Units[0].ModelCount, Is.EqualTo(10));
            Assert.That(side.Points, Is.EqualTo(130));
        });

        side.SetCombined(false);
        Assert.That(side.Compile().Units[0].ModelCount, Is.EqualTo(5), "and un-combining puts it back");
    }

    [Test]
    public void BuyingAnUpgrade_ChangesThePriceAndTheFingerprint()
    {
        var side = new CalculatorSide();
        side.SetUnit(Book, "warriors");
        string before = side.Fingerprint();
        int pointsBefore = side.Points;

        UpgradeSection section = side.Roster!.Sections.First(s => s.Id == "warriors-special");
        BuilderListEditing.ApplyChoice(side.List.Units[CalculatorSide.MainIndex], side.Mirror, section,
            section.Options[0].Id, 1);

        Assert.Multiple(() =>
        {
            Assert.That(side.Points, Is.GreaterThan(pointsBefore), "the upgrade is paid for");
            Assert.That(side.Fingerprint(), Is.Not.EqualTo(before), "so the numbers must be recomputed");
        });
    }

    [Test]
    public void SwitchingUnits_DiscardsThePreviousOnesUpgrades()
    {
        var side = new CalculatorSide();
        side.SetUnit(Book, "warriors");
        UpgradeSection section = side.Roster!.Sections.First(s => s.Id == "warriors-special");
        BuilderListEditing.ApplyChoice(side.List.Units[CalculatorSide.MainIndex], null, section,
            section.Options[0].Id, 1);

        side.SetUnit(Book, "gunners");

        Assert.Multiple(() =>
        {
            Assert.That(side.Detail().Unit.Name, Is.EqualTo("Heavy Gunners"));
            Assert.That(side.List.Units, Has.Count.EqualTo(1));
            Assert.That(side.List.Units[0].Choices, Is.Empty, "no leftovers from the last unit");
        });
    }

    // ---- the picker ----------------------------------------------------------------------------

    [Test]
    public void ThePickerStartsOpen_SoAFreshScreenAsksForAUnit()
    {
        var picker = new UnitPicker();
        Assert.Multiple(() =>
        {
            Assert.That(picker.IsOpen, Is.True);
            Assert.That(picker.Level, Is.EqualTo(UnitPicker.ELevel.Armies));
            Assert.That(picker.Army, Is.Null);
        });
    }

    [Test]
    public void ReopeningReturnsToTheArmyAlreadyChosen_NotTheArmyList()
    {
        // The point of the whole two-level design: picking a second unit from the same army must not
        // make you find the army again.
        var picker = new UnitPicker();
        picker.ChooseArmy(Book);
        picker.Close();

        picker.Open();

        Assert.Multiple(() =>
        {
            Assert.That(picker.Level, Is.EqualTo(UnitPicker.ELevel.Units));
            Assert.That(picker.Army, Is.SameAs(Book));
        });

        picker.BackToArmies();
        Assert.That(picker.Level, Is.EqualTo(UnitPicker.ELevel.Armies), "and Back still reaches the armies");
    }

    [Test]
    public void TheFilterMatchesCaseInsensitivelyAndAnywhereInTheName()
    {
        Assert.Multiple(() =>
        {
            Assert.That(UnitPicker.Matches("Vanguard Warriors", "warri"), Is.True);
            Assert.That(UnitPicker.Matches("Vanguard Warriors", "VANGUARD"), Is.True);
            Assert.That(UnitPicker.Matches("Vanguard Warriors", ""), Is.True, "an empty filter hides nothing");
            Assert.That(UnitPicker.Matches("Vanguard Warriors", "gunners"), Is.False);
            Assert.That(UnitPicker.MatchingUnits(Book, "gun").Select(u => u.Id), Is.EqualTo(new[] { "gunners" }));
        });
    }

    // ---- the screen ----------------------------------------------------------------------------

    [Test]
    public void BothColumnsAskForAUnitWhenTheScreenOpens()
    {
        var screen = new CombatCalculatorScreen(new List<BookFile> { Book });

        Assert.Multiple(() =>
        {
            Assert.That(screen.AttackerPicker.IsOpen, Is.True);
            Assert.That(screen.DefenderPicker.IsOpen, Is.True);
            Assert.That(screen.Report, Is.Null, "nothing to work out until both sides are chosen");
            Assert.That(screen.Situation.Mode, Is.EqualTo(ECombatMode.Shooting), "shooting is the default");
        });
    }

    [Test]
    public void SwapExchangesTheTwoColumns()
    {
        var screen = new CombatCalculatorScreen(new List<BookFile> { Book });
        screen.Attacker.SetUnit(Book, "warriors");
        screen.Defender.SetUnit(Book, "gunners");

        screen.Swap();

        Assert.Multiple(() =>
        {
            Assert.That(screen.Attacker.Detail().Unit.Name, Is.EqualTo("Heavy Gunners"));
            Assert.That(screen.Defender.Detail().Unit.Name, Is.EqualTo("Vanguard Warriors"));
        });
    }

    [Test]
    public void TheTwoColumnsAreIndependent()
    {
        var screen = new CombatCalculatorScreen(new List<BookFile> { Book });
        screen.Attacker.SetUnit(Book, "warriors");
        screen.Defender.SetUnit(Book, "warriors");

        screen.Attacker.SetCombined(true);

        Assert.Multiple(() =>
        {
            Assert.That(screen.Attacker.Compile().Units[0].ModelCount, Is.EqualTo(10));
            Assert.That(screen.Defender.Compile().Units[0].ModelCount, Is.EqualTo(5),
                "combining one side must not touch the other");
        });
    }

    [Test]
    public void TwoDemoUnitsActuallyFight()
    {
        // The end-to-end shape: two Forge-built units go in, real numbers come out.
        var screen = new CombatCalculatorScreen(new List<BookFile> { Book });
        screen.Attacker.SetUnit(Book, "warriors");
        screen.Defender.SetUnit(Book, "gunners");

        CombatReport report = CombatCalculator.Run(screen.Attacker.Compile(), screen.Defender.Compile(),
            new CombatSituation(DistanceInches: 12f));

        Assert.Multiple(() =>
        {
            Assert.That(report.Volleys, Is.Not.Empty);
            Assert.That(report.ExpectedHits, Is.GreaterThan(0f));
            Assert.That(report.DefenderWoundsBefore, Is.GreaterThan(0f));
        });
    }

    [Test]
    public void TheScreensTextIsAsciiOnly()
    {
        // CLAUDE.md: the ImGui font atlas bakes Basic Latin + Latin-1 only; anything above U+00FF
        // renders as '?'. This caught a stray non-Latin character during development.
        var texts = new List<string>
        {
            CombatCalculatorScreen.Title,
            CombatCalculatorScreen.SwapLabel,
            CombatCalculatorScreen.ChooseUnitLabel,
            CombatCalculatorScreen.NoUnitsHint,
            CombatCalculatorScreen.ArmyPrompt,
            CombatCalculatorScreen.VariablesHeader,
            CombatCalculatorScreen.DistanceLabel,
            CombatCalculatorScreen.CoverLabel,
            CombatCalculatorScreen.MovedLabel,
            CombatCalculatorScreen.ChargingLabel,
            CombatCalculatorScreen.FatiguedLabel,
        };

        // The engine's own notes and warnings are shown verbatim, so they are held to the same rule.
        var side = new CalculatorSide();
        side.SetUnit(Book, "warriors");
        CombatReport shooting = CombatCalculator.Run(side.Compile(), side.Compile(), new CombatSituation());
        CombatReport melee = CombatCalculator.Run(side.Compile(), side.Compile(),
            new CombatSituation(Mode: ECombatMode.Melee));
        texts.AddRange(shooting.Notes);
        texts.AddRange(melee.Notes);
        texts.AddRange(shooting.Volleys.SelectMany(volley => volley.Notes));

        foreach (string text in texts)
            Assert.That(text.All(c => c <= 0xFF), Is.True, $"non-Latin-1 character in: {text}");
    }

    // One book per test: fresh state, but a stable identity within a test so reference checks mean
    // something.
    private BookFile Book = null!;

    [SetUp]
    public void BuildBook() => Book = DemoBook.Build();
}
