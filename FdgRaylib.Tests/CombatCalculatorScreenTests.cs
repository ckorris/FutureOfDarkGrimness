using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Calculator;
using FDG.Rules.Dispatch;
using FDG.Rules.Serialization;
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
        picker.ChooseArmy(ArmySource.FromBook(Book));
        picker.Close();

        picker.Open();

        Assert.Multiple(() =>
        {
            Assert.That(picker.Level, Is.EqualTo(UnitPicker.ELevel.Units));
            Assert.That(picker.Army!.Book, Is.SameAs(Book));
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
            Assert.That(UnitPicker.MatchingUnits(ArmySource.FromBook(Book), "gun").Select(e => e.Name),
                Is.EqualTo(new[] { "Heavy Gunners" }));
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
            Assert.That(screen.Situation.AttackerCharging, Is.True,
                "a unit only fights in melee because it charged, so the melee tab opens on the charge");
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
    public void TheEmptyStateNamesTheSideThatIsStillMissing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(CombatCalculatorScreen.EmptyStateHint(false, false),
                Is.EqualTo(CombatCalculatorScreen.NoUnitsHint));
            Assert.That(CombatCalculatorScreen.EmptyStateHint(true, false),
                Is.EqualTo(CombatCalculatorScreen.NoDefenderHint));
            Assert.That(CombatCalculatorScreen.EmptyStateHint(false, true),
                Is.EqualTo(CombatCalculatorScreen.NoAttackerHint));
        });
    }

    [Test]
    public void RangeTicksMarkEachDistinctWeaponReachInsideTheSlidersSpan()
    {
        var report = new CombatReport(ECombatMode.Shooting, "A", "B", 5f, 5f, new List<VolleyReport>
        {
            Volley(range: 24f),
            Volley(range: 18f),
            Volley(range: 24f),          // duplicate reach - one tick, not two
            Volley(range: 0f),           // melee weapon - nothing to mark
            Volley(range: 240f),         // beyond the slider - would draw off the end
        }, 0f, 0f, new List<string>(), new List<string>());

        Assert.That(CombatCalculatorScreen.RangeTicks(report), Is.EqualTo(new[] { 18f, 24f }));
    }

    private static VolleyReport Volley(float range) =>
        new(null!, 1, true, range, 1f, 4, new List<string>(), 0f,
            new List<SaveBucket>(), new List<string>(), 0f, new List<string>());

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
            CombatCalculatorScreen.AssumptionsLabel,
            CombatCalculatorScreen.AttackerBadge,
            CombatCalculatorScreen.DefenderBadge,
            CombatCalculatorScreen.NoAttackerHint,
            CombatCalculatorScreen.NoDefenderHint,
            CombatReportView.HitsCaption,
            CombatReportView.WoundsCaption,
            CombatReportView.AttacksVerb,
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


    // ---- hero joins ----------------------------------------------------------------------------

    [Test]
    public void JoiningAHero_SplitsTheColumnWithTheHeroOnTop()
    {
        BookFile force = HeroBook();
        var side = new CalculatorSide();
        side.SetUnit(force, "squad");

        side.SetJoin("captain");

        Assert.Multiple(() =>
        {
            Assert.That(side.Joined, Is.Not.Null);
            Assert.That(side.Rows().Select(row => side.DetailOf(row).Unit.Name),
                Is.EqualTo(new[] { "Captain", "Squad" }), "hero first, as the owner asked");
            Assert.That(side.Points, Is.EqualTo(110), "both halves are paid for");
        });
    }

    [Test]
    public void AJoinedHeroActuallyMergesIntoOneFightingUnit()
    {
        // The proof that the link is real: army creation folds the hero's model into the squad, so the
        // pair has six models' worth of wounds and there is nothing left over to warn about.
        BookFile force = HeroBook();
        var defender = new CalculatorSide();
        defender.SetUnit(force, "squad");
        defender.SetJoin("captain");

        var attacker = new CalculatorSide();
        attacker.SetUnit(force, "squad");

        CombatReport report = CombatCalculator.Run(attacker.Compile(), defender.Compile(),
            new CombatSituation(DistanceInches: 12f));

        Assert.Multiple(() =>
        {
            Assert.That(report.DefenderWoundsBefore, Is.EqualTo(6f).Within(0.001f), "5 troopers plus the hero");
            Assert.That(report.Warnings, Is.Empty, "a legal join produces no complaint");
            Assert.That(report.DefenderName, Is.EqualTo("Squad"), "the host is the unit that fights");
        });
    }

    [Test]
    public void AHeroAsTheMainUnit_JoinsTheOtherWayRound()
    {
        BookFile force = HeroBook();
        var side = new CalculatorSide();
        side.SetUnit(force, "captain");
        Assert.That(side.MainIsHero, Is.True);

        side.SetJoin("squad");

        Assert.Multiple(() =>
        {
            Assert.That(side.Rows().Select(row => side.DetailOf(row).Unit.Name),
                Is.EqualTo(new[] { "Captain", "Squad" }), "the hero is still on top");
            Assert.That(side.List.Units[CalculatorSide.MainIndex].JoinsUnitId, Is.Not.Null,
                "the hero carries the link, whichever side it was picked from");
        });
    }

    [Test]
    public void RemovingAJoinLeavesACleanSingleUnit()
    {
        BookFile force = HeroBook();
        var side = new CalculatorSide();
        side.SetUnit(force, "squad");
        side.SetJoin("captain");

        side.RemoveJoin();

        Assert.Multiple(() =>
        {
            Assert.That(side.Joined, Is.Null);
            Assert.That(side.List.Units, Has.Count.EqualTo(1));
            Assert.That(side.List.Units[0].JoinsUnitId, Is.Null, "no dangling link left behind");
            Assert.That(side.Points, Is.EqualTo(50));
        });
    }

    [Test]
    public void ChoosingANewUnitDropsAnyJoin()
    {
        BookFile force = HeroBook();
        var side = new CalculatorSide();
        side.SetUnit(force, "squad");
        side.SetJoin("captain");

        side.SetUnit(force, "squad");

        Assert.That(side.Joined, Is.Null);
        Assert.That(side.List.Units, Has.Count.EqualTo(1));
    }

    [Test]
    public void TheJoinPickerOffersOnlyTheRightKindOfUnit()
    {
        BookFile force = HeroBook();

        Assert.Multiple(() =>
        {
            ArmySource source = ArmySource.FromBook(force);
            Assert.That(UnitPicker.MatchingUnits(source, "", UnitPicker.ERoles.HeroesOnly).Select(e => e.Name),
                Is.EqualTo(new[] { "Captain" }));
            Assert.That(UnitPicker.MatchingUnits(source, "", UnitPicker.ERoles.HostsOnly).Select(e => e.Name),
                Is.EqualTo(new[] { "Squad" }));
            Assert.That(UnitPicker.MatchingUnits(source, "", UnitPicker.ERoles.Any).Count(), Is.EqualTo(2));
        });
    }

    [Test]
    public void OpeningTheJoinPickerStaysInsideTheUnitsOwnArmy()
    {
        var picker = new UnitPicker();
        BookFile force = HeroBook();

        ArmySource source = ArmySource.FromBook(force);
        picker.OpenForJoin(source, UnitPicker.ERoles.HeroesOnly);

        Assert.Multiple(() =>
        {
            Assert.That(picker.JoinMode, Is.True);
            Assert.That(picker.Level, Is.EqualTo(UnitPicker.ELevel.Units), "no army level to wander into");
            Assert.That(picker.Army, Is.SameAs(source));
            Assert.That(picker.Roles, Is.EqualTo(UnitPicker.ERoles.HeroesOnly));
        });

        picker.Close();
        Assert.That(picker.JoinMode, Is.False, "closing leaves join mode behind");
    }


    // ---- saved army lists ----------------------------------------------------------------------

    [Test]
    public void APlainSavedList_IsAdoptedReadOnly_ButStillCarriesItsRules()
    {
        ArmyListFile saved = PlainArmy();
        var side = new CalculatorSide();

        side.SetSavedUnit(saved, saved.Units[0]);
        ArmyListFile compiled = side.Compile();

        Assert.Multiple(() =>
        {
            Assert.That(side.HasUnit, Is.True);
            Assert.That(side.IsEditable, Is.False, "no book means no upgrades to offer");
            Assert.That(side.SavedRows.Select(unit => unit.Name), Is.EqualTo(new[] { "Veterans" }));
            Assert.That(side.Points, Is.EqualTo(90));
            // Without these the units would load with their rule names unresolved and quietly do nothing.
            Assert.That(compiled.RuleDefinitions, Is.Not.Empty, "the source's rule definitions come along");
            Assert.That(compiled.Faction, Is.EqualTo("Saved Faction"));
        });
    }

    [Test]
    public void ASavedUnitActuallyFights()
    {
        ArmyListFile saved = PlainArmy();
        var attacker = new CalculatorSide();
        var defender = new CalculatorSide();
        attacker.SetSavedUnit(saved, saved.Units[0]);
        defender.SetSavedUnit(saved, saved.Units[0]);

        CombatReport report = CombatCalculator.Run(attacker.Compile(), defender.Compile(),
            new CombatSituation(DistanceInches: 12f));

        Assert.Multiple(() =>
        {
            Assert.That(report.Volleys, Is.Not.Empty);
            Assert.That(report.ExpectedWounds, Is.GreaterThan(0f));
            Assert.That(report.Warnings, Is.Empty);
        });
    }

    [Test]
    public void ASavedUnitBringsTheHeroItsListAlreadyJoinedToIt()
    {
        ArmyListFile saved = PlainArmyWithJoinedHero();
        var side = new CalculatorSide();

        side.SetSavedUnit(saved, saved.Units.First(unit => unit.Name == "Veterans"));

        Assert.Multiple(() =>
        {
            Assert.That(side.SavedRows.Select(unit => unit.Name),
                Is.EqualTo(new[] { "Warlord", "Veterans" }), "the author's pairing, hero on top");
            Assert.That(side.Compile().Units, Has.Count.EqualTo(2), "both halves go to the calculator");
        });
    }

    [Test]
    public void AForgeBuiltListIsAdoptedAsEditable()
    {
        // The Forge embeds the book it built against, so its armies keep their upgrade options here.
        var list = new BuilderList { BookName = Book.Name };
        BuilderListEditing.AddUnit(Book, list, "warriors");
        ArmyListFile forgeBuilt = ListCompiler.Compile(Book, list);

        ArmySource source = ArmySource.FromSaved("/tmp/forge.fdgarmy", forgeBuilt);

        Assert.Multiple(() =>
        {
            Assert.That(source.IsEditable, Is.True);
            Assert.That(source.Book, Is.Not.Null);
            Assert.That(UnitPicker.MatchingUnits(source, "").Select(entry => entry.Name),
                Does.Contain("Vanguard Warriors"), "its units come from the embedded book");
        });
    }

    [Test]
    public void APlainListIsNotEditable()
    {
        ArmySource source = ArmySource.FromSaved("/tmp/plain.fdgarmy", PlainArmy());

        Assert.Multiple(() =>
        {
            Assert.That(source.IsEditable, Is.False);
            Assert.That(source.Book, Is.Null);
            Assert.That(source.UnitNames(), Is.EqualTo(new[] { "Veterans" }));
        });
    }

    [Test]
    public void LoadingAnArmyRemembersIt_AndLoadingItAgainDoesNotListItTwice()
    {
        string path = WriteTempArmy(PlainArmy());
        var screen = new CombatCalculatorScreen(new List<BookFile> { Book });

        Assert.That(screen.AdoptArmyFile(path), Is.True);
        Assert.That(screen.AdoptArmyFile(path), Is.True, "asking twice is not an error");

        Assert.Multiple(() =>
        {
            Assert.That(screen.LoadedArmies, Has.Count.EqualTo(1), "one entry, however often it is opened");
            Assert.That(screen.LoadedArmies[0].Name, Is.EqualTo("Saved Force"));
            Assert.That(screen.LoadError, Is.Null);
        });
    }

    [Test]
    public void AFileThatCannotBeReadSaysSoAndChangesNothing()
    {
        var screen = new CombatCalculatorScreen(new List<BookFile> { Book });

        bool ok = screen.AdoptArmyFile(Path.Combine(Path.GetTempPath(), "no-such-army-397.fdgarmy"));

        Assert.Multiple(() =>
        {
            Assert.That(ok, Is.False);
            Assert.That(screen.LoadedArmies, Is.Empty);
            Assert.That(screen.LoadError, Is.Not.Null.And.Contains("no longer exists"));
        });
    }

    [Test]
    public void GarbageInAnArmyFileIsReportedRatherThanThrown()
    {
        string path = Path.Combine(Path.GetTempPath(), $"broken-397-{Guid.NewGuid():N}.fdgarmy");
        File.WriteAllText(path, "{ this is not json");
        _temporaryFiles.Add(path);

        var screen = new CombatCalculatorScreen(new List<BookFile> { Book });

        Assert.That(screen.AdoptArmyFile(path), Is.False);
        Assert.That(screen.LoadError, Is.Not.Null.And.Contains("could not be read"));
    }

    private static ArmyListFile PlainArmy() => new()
    {
        Name = "Saved Force",
        Faction = "Saved Faction",
        Units =
        {
            new UnitFileEntry
            {
                Id = "vets", Name = "Veterans", ModelCount = 5, Quality = 4, Defense = 4, PointCost = 90,
                SpecialRules = { new SpecialRuleEntry_Core("Stealth") },
                Weapons = { new WeaponFileEntry { Name = "Rifle", Quantity = 5, RangeInches = 24, Attacks = 1 } },
            },
        },
        RuleDefinitions = { CoreRuleCatalog.Stealth },
    };

    private static ArmyListFile PlainArmyWithJoinedHero()
    {
        ArmyListFile army = PlainArmy();
        army.Units.Add(new UnitFileEntry
        {
            Id = "warlord", Name = "Warlord", ModelCount = 1, Quality = 3, Defense = 3, PointCost = 70,
            JoinsUnitId = "vets",
            SpecialRules = { new SpecialRuleEntry_Core("Hero") },
            Weapons = { new WeaponFileEntry { Name = "Blade", Quantity = 1, RangeInches = 0, Attacks = 3 } },
        });
        return army;
    }

    private string WriteTempArmy(ArmyListFile army)
    {
        string path = Path.Combine(Path.GetTempPath(), $"army-397-{Guid.NewGuid():N}.fdgarmy");
        File.WriteAllText(path, JsonSerializer.Serialize(army, RuleJson.Options));
        _temporaryFiles.Add(path);
        return path;
    }

    private readonly List<string> _temporaryFiles = new();

    [TearDown]
    public void RemoveTemporaryFiles()
    {
        foreach (string path in _temporaryFiles)
        {
            try { if (File.Exists(path)) File.Delete(path); } catch { /* a leftover temp file is harmless */ }
        }
        _temporaryFiles.Clear();
    }

    /// A tiny force with one squad and one Hero - DemoBook has no Hero to join.
    private static BookFile HeroBook() => new()
    {
        Name = "Test Force",
        Faction = "Test",
        Units =
        {
            new RosterUnit
            {
                Id = "squad", Name = "Squad", Quality = 4, Defense = 4,
                BaseModelCount = 5, MinModels = 5, MaxModels = 10, BasePointCost = 50,
                Weapons = { new WeaponFileEntry { Name = "Rifle", Quantity = 5, RangeInches = 24, Attacks = 1 } },
            },
            new RosterUnit
            {
                Id = "captain", Name = "Captain", Quality = 3, Defense = 3,
                BaseModelCount = 1, MinModels = 1, MaxModels = 1, BasePointCost = 60,
                Rules = { new SpecialRuleEntry_Core("Hero") },
                Weapons = { new WeaponFileEntry { Name = "Pistol", Quantity = 1, RangeInches = 12, Attacks = 2 } },
            },
        },
    };

    // One book per test: fresh state, but a stable identity within a test so reference checks mean
    // something.
    private BookFile Book = null!;

    [SetUp]
    public void BuildBook() => Book = DemoBook.Build();
}
