using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #153 (P1) — the pure formatting seams behind the ArmyForge three-pane viewer. The ImGui layout itself is
// hand-verified in the running window; these pin the text.
[TestFixture]
public class ArmyForgeScreenTests
{
    [Test]
    public void BackgroundLibraryLoad_JoinsOnFirstUse()
    {
        // The public ctor parses the bundled book library on a worker task (it is ~0.5s of JSON, which
        // used to delay the window at startup); every entry point joins the load before touching state.
        var screen = new ArmyForgeScreen();
        Assert.That(screen.List.BookName, Is.Not.Empty);
        Assert.That(screen.Compile().Units, Is.Empty);
    }

    [Test]
    public void PointsHeader_ShowsTotalOverLimit()
    {
        Assert.That(ArmyForgeScreen.PointsHeader(271, 500), Is.EqualTo("271 / 500 pts"));
    }

    [Test]
    public void RosterStatLine_ShowsSizeQualityDefenseCost()
    {
        RosterUnit warriors = DemoBook.Build().Units.Single(u => u.Id == "warriors");
        Assert.That(ArmyForgeScreen.RosterStatLine(warriors),
            Is.EqualTo("Vanguard Warriors [5] - Qua 4+ Def 4+  (65 pts)"));
    }

    [Test]
    public void OptionSummary_AppendsCost_WhenNonZero()
    {
        var paid = new UpgradeOption { Label = "Plasma Rifle", Cost = 5 };
        var free = new UpgradeOption { Label = "Combat Blade", Cost = 0 };
        Assert.That(ArmyForgeScreen.OptionSummary(paid), Is.EqualTo("Plasma Rifle  (+5 pts)"));
        Assert.That(ArmyForgeScreen.OptionSummary(free), Is.EqualTo("Combat Blade"));
    }

    [Test]
    public void BaseSummary_RoundsInchesToAuthoredMm()
    {
        var circle = new BaseFileEntry { Shape = EBaseShapeKind.Circle, DiameterInches = 0.984252f };
        var rect = new BaseFileEntry
        {
            Shape = EBaseShapeKind.Rectangle, WidthInches = 0.984252f, HeightInches = 1.968504f,
        };
        Assert.That(ArmyForgeScreen.BaseSummary(circle), Is.EqualTo("25mm"));
        Assert.That(ArmyForgeScreen.BaseSummary(rect), Is.EqualTo("25 x 50mm"));
    }

    [Test]
    public void IsCaster_TrueForCasterRules_FalseOtherwise()
    {
        RosterUnit Make(params string[] rules) => new()
        {
            Rules = rules.Select(r => (SpecialRuleEntry)new SpecialRuleEntry_Core(r)).ToList(),
        };
        Assert.That(ArmyForgeScreen.IsCaster(Make("Caster Group", "Highborn")), Is.True);
        Assert.That(ArmyForgeScreen.IsCaster(new RosterUnit
        {
            Rules = { new SpecialRuleEntry_CoreNumeric("Caster", 2) },
        }), Is.True);
        Assert.That(ArmyForgeScreen.IsCaster(Make("Highborn", "Tough")), Is.False);
    }

    [Test]
    public void DemoBook_HasExpectedRosterAndUpgrades()
    {
        BookFile book = DemoBook.Build();
        Assert.That(book.Units.Select(u => u.Id), Is.EquivalentTo(new[] { "warriors", "gunners" }));
        RosterUnit warriors = book.Units.Single(u => u.Id == "warriors");
        Assert.That(warriors.Sections.Select(s => s.Variant),
            Does.Contain(UpgradeVariant.Replace).And.Contain(UpgradeVariant.AddModels).And.Contain(UpgradeVariant.Upgrade));
    }

    // ── P2: list building ───────────────────────────────────────────────────────────────────────────────

    [Test]
    public void AddToList_ThenCompile_SumsBasePoints()
    {
        var screen = new ArmyForgeScreen(DemoBook.Build());
        screen.AddToList("warriors");
        screen.AddToList("gunners");

        BuiltArmyFile army = screen.Compile();
        Assert.That(army.Units.Select(u => u.Name), Is.EqualTo(new[] { "Vanguard Warriors", "Heavy Gunners" }));
        Assert.That(army.TotalPoints, Is.EqualTo(185)); // 65 + 120 base, no options chosen yet
    }

    [Test]
    public void RemoveFromList_DropsThatUnit()
    {
        var screen = new ArmyForgeScreen(DemoBook.Build());
        screen.AddToList("warriors");
        screen.AddToList("gunners");
        screen.RemoveFromList(0);

        Assert.That(screen.Compile().Units.Single().Name, Is.EqualTo("Heavy Gunners"));
    }

    [Test]
    public void AddToList_UnknownRosterId_IsIgnored()
    {
        var screen = new ArmyForgeScreen(DemoBook.Build());
        screen.AddToList("does-not-exist");
        Assert.That(screen.List.Units, Is.Empty);
    }

    [Test]
    public void SaveLoadRoundTrip_RestoresEditableList()
    {
        var a = new ArmyForgeScreen(DemoBook.Build());
        a.AddToList("warriors");
        a.AddToList("gunners");

        // Exactly what Save writes (derived type → embed included).
        string json = JsonSerializer.Serialize(a.Compile(), RuleJson.Options);
        BuiltArmyFile loaded = JsonSerializer.Deserialize<BuiltArmyFile>(json, RuleJson.Options)!;

        var b = new ArmyForgeScreen(DemoBook.Build());
        Assert.That(b.AdoptLoaded(loaded), Is.True);
        Assert.That(b.List.Units.Select(u => u.RosterUnitId), Is.EqualTo(new[] { "warriors", "gunners" }));
        Assert.That(b.Compile().TotalPoints, Is.EqualTo(185));
    }

    [Test]
    public void AdoptLoaded_PlainArmy_ReturnsFalse()
    {
        // A hand-authored .fdgarmy (no embedded book/selections) can't be catalog-edited.
        Assert.That(new ArmyForgeScreen(DemoBook.Build()).AdoptLoaded(new BuiltArmyFile()), Is.False);
    }

    // ── P3: interactive upgrade choices ─────────────────────────────────────────────────────────────────

    [Test]
    public void SetChoice_SingleSelectSection_IsMutuallyExclusive()
    {
        var unit = new BuilderUnit { RosterUnitId = "x" };
        var section = new UpgradeSection { Id = "s", MaxPicks = 1, Options = { new() { Id = "a" }, new() { Id = "b" } } };

        ArmyForgeScreen.SetChoice(unit, section, "a", 1);
        Assert.That(ArmyForgeScreen.IsChosen(unit, "s", "a"), Is.True);

        ArmyForgeScreen.SetChoice(unit, section, "b", 1);   // picking b clears a
        Assert.That(ArmyForgeScreen.IsChosen(unit, "s", "a"), Is.False);
        Assert.That(ArmyForgeScreen.IsChosen(unit, "s", "b"), Is.True);
        Assert.That(unit.Choices, Has.Count.EqualTo(1));

        ArmyForgeScreen.SetChoice(unit, section, "b", 0);   // unticking clears it
        Assert.That(unit.Choices, Is.Empty);
    }

    [Test]
    public void SetChoice_CountedSection_StoresCount()
    {
        var unit = new BuilderUnit();
        var section = new UpgradeSection { Id = "r", Variant = UpgradeVariant.AddModels, Options = { new() { Id = "add" } } };

        ArmyForgeScreen.SetChoice(unit, section, "add", 3);
        Assert.That(ArmyForgeScreen.ChoiceCount(unit, "r", "add"), Is.EqualTo(3));
    }

    [Test]
    public void UpgradeChoices_ThenCompile_MatchTheCompilersCost_Warriors()
    {
        var screen = new ArmyForgeScreen(DemoBook.Build());
        screen.AddToList("warriors");
        BuilderUnit bu = screen.List.Units[0];

        RosterUnit warriors = DemoBook.Build().Units.Single(u => u.Id == "warriors");
        ArmyForgeScreen.SetChoice(bu, warriors.Sections.Single(s => s.Id == "warriors-special"), "plasma", 1);
        ArmyForgeScreen.SetChoice(bu, warriors.Sections.Single(s => s.Id == "warriors-reinforce"), "add-warrior", 2);
        ArmyForgeScreen.SetChoice(bu, warriors.Sections.Single(s => s.Id == "warriors-banner"), "war-banner", 1);

        UnitFileEntry compiled = screen.Compile().Units.Single();
        Assert.That(compiled.ModelCount, Is.EqualTo(7));
        Assert.That(compiled.PointCost, Is.EqualTo(106)); // 65 + 5 + 13×2 + 10
    }

    [Test]
    public void UpgradeChoices_ReplaceAll_ThenCompile_Gunners()
    {
        var screen = new ArmyForgeScreen(DemoBook.Build());
        screen.AddToList("gunners");
        BuilderUnit bu = screen.List.Units[0];

        RosterUnit gunners = DemoBook.Build().Units.Single(u => u.Id == "gunners");
        ArmyForgeScreen.SetChoice(bu, gunners.Sections.Single(s => s.Id == "gunners-missiles"), "missile", 1);

        // #218: an "all" replace is a FLAT per-unit price, not per model — 120 + 15 once, however many
        // models are swapped. This test previously pinned the old 120 + 15×3 = 165 multiplication.
        Assert.That(screen.Compile().Units.Single().PointCost, Is.EqualTo(135));
    }

    [Test]
    public void AvailableExcludingSection_IgnoresOwnPick_SoRadiosCanSwitch()
    {
        // Hand-verify round 2: with a replace option picked, the compiled unit no longer has the target
        // weapon, so availability measured on the final state is 0 — which wrongly grayed out the section's
        // OTHER options (switching a mutually-exclusive pick implicitly returns the target to the pool).
        // The exclusion seam must report the pool as if this section had no pick.
        BookFile book = DemoBook.Build();
        var screen = new ArmyForgeScreen(book);
        screen.AddToList("gunners");
        BuilderUnit bu = screen.List.Units[0];
        UpgradeSection missiles = book.Units.Single(u => u.Id == "gunners").Sections
            .Single(s => s.Id == "gunners-missiles");

        ArmyForgeScreen.SetChoice(bu, missiles, "missile", 1); // all 3 Heavy Rifles replaced

        (UnitFileEntry compiled, var items) = ListCompiler.CompileUnitDetailed(book, bu);
        Assert.That(ListCompiler.AvailableApplications(compiled.Weapons, items, missiles.Targets), Is.Zero,
            "final compiled state has no Heavy Rifle left");
        Assert.That(ArmyForgeScreen.AvailableExcludingSection(book, bu, missiles), Is.EqualTo(3),
            "excluding the section's own pick, the full pool is switchable");
    }

    // ── #323 Titan Lords double Heavy Hammer, against the SHIPPED book ─────────────────────────────────

    // Reported 2026-08-02 (friend's War Disciples list): on the War Errant Mini-Titan, trading the Titan
    // Shield for a second Heavy Hammer must leave BOTH hammers swappable. The book authors "Replace any
    // Heavy Hammer" ABOVE the shield section that grants the second one, which starved the second swap in
    // the compiler; this pins the whole path on the real bundled data - stepper bound and compiled result.
    [Test]
    public void WarErrantMiniTitan_ShieldTradedForASecondHammer_SwapsBothHammers()
    {
        BookFile book = JsonSerializer.Deserialize<BookFile>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "Books",
                "TitanLordsWarDisciples" + BookFile.EXTENSION_WITH_PERIOD)), RuleJson.Options)!;
        RosterUnit errant = book.Units.Single(u => u.Name == "War Errant Mini-Titan");
        UpgradeSection hammers = errant.Sections.Single(s => s.Label == "Replace any Heavy Hammer");
        UpgradeSection shield = errant.Sections.Single(s => s.Label == "Replace Titan Shield");
        UpgradeOption sword = hammers.Options.Single(o => o.Label.StartsWith("Heavy Sword"));

        var screen = new ArmyForgeScreen(book);
        screen.AddToList(errant.Id);
        BuilderUnit bu = screen.List.Units[0];

        ArmyForgeScreen.SetChoice(bu, shield, shield.Options.Single().Id, 1); // Titan Shield -> Heavy Hammer

        // With two hammers on the unit, the stepper must offer two swaps - both before and after the first.
        Assert.That(StepperMaxFor(book, bu, hammers, sword), Is.EqualTo(2), "two hammers, two swaps offered");
        ArmyForgeScreen.SetChoice(bu, hammers, sword.Id, 1);
        Assert.That(StepperMaxFor(book, bu, hammers, sword), Is.EqualTo(2), "the second swap is still offered");

        ArmyForgeScreen.SetChoice(bu, hammers, sword.Id, 2);
        UnitFileEntry compiled = screen.Compile().Units.Single();
        Assert.That(compiled.Weapons.Any(w => w.Name == "Heavy Hammer"), Is.False, "neither hammer is left behind");
        Assert.That(compiled.Weapons.Single(w => w.Name == "Heavy Sword").Quantity, Is.EqualTo(2));
        Assert.That(compiled.PointCost, Is.EqualTo(385), "295 base + 30 shield swap + 30x2 sword swaps");
    }

    // ── #324 an all-swap must not hide the specialist swap below it, against the SHIPPED book ──────────

    // DAO Union Tactical Grunts: 5 Pulse Rifles, "Replace all Pulse Rifles" (#1) above "Replace one Pulse
    // Rifle" (#2). Taking the all-swap used to eat the pool, so the compiler dropped the specialist AND the
    // Forge grayed it out. Both halves are checked here: the Forge still offers the swap, and the compile
    // honours it.
    [Test]
    public void TacticalGrunts_AllSwapLeavesTheSpecialistSwapAvailableAndCompiled()
    {
        BookFile book = JsonSerializer.Deserialize<BookFile>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "Books",
                "DAOUnion" + BookFile.EXTENSION_WITH_PERIOD)), RuleJson.Options)!;
        RosterUnit grunts = book.Units.Single(u => u.Name == "Tactical Grunts");
        UpgradeSection all = grunts.Sections.Single(s => s.Label == "Replace all Pulse Rifles");
        UpgradeSection one = grunts.Sections.Single(s => s.Label == "Replace one Pulse Rifle");
        UpgradeOption plasma = one.Options.Single(o => o.Label.StartsWith("Plasma Rifle"));

        var screen = new ArmyForgeScreen(book);
        screen.AddToList(grunts.Id);
        BuilderUnit bu = screen.List.Units[0];

        ArmyForgeScreen.SetChoice(bu, all, all.Options.Single().Id, 1);

        // The Forge gate: with every rifle swapped away by the all-swap, the specialist section must still
        // offer its pick - the compiler reserves a rifle for it.
        Assert.That(ArmyForgeScreen.AvailableExcludingSection(book, bu, one), Is.EqualTo(5),
            "the all-swap yields, so the specialist swap is still selectable");

        ArmyForgeScreen.SetChoice(bu, one, plasma.Id, 1);

        UnitFileEntry compiled = screen.Compile().Units.Single();
        Assert.That(compiled.Weapons.Single(w => w.Name == "Pulse Carbine").Quantity, Is.EqualTo(4));
        Assert.That(compiled.Weapons.Single(w => w.Name == "Plasma Rifle").Quantity, Is.EqualTo(1));
        Assert.That(compiled.Weapons.Any(w => w.Name == "Pulse Rifle"), Is.False);
        Assert.That(compiled.PointCost, Is.EqualTo(150), "115 base + 25 flat all-swap + 10 plasma");

        // And switching the specialist to another option still works with the all-swap in place.
        Assert.That(ArmyForgeScreen.AvailableExcludingSection(book, bu, one), Is.EqualTo(5));
    }

    // Dwarf Guilds Guardians: 5 Pistols + 5 Bashes, "Replace all Pistols and Bashes" -> CCW. The "Bashes"
    // target never matched the "Bash" weapon, so the swap left all five Bashes on the unit for free (#324).
    [Test]
    public void Guardians_ReplaceAllPistolsAndBashes_TakesTheBashesToo()
    {
        BookFile book = JsonSerializer.Deserialize<BookFile>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "Books",
                "DwarfGuilds" + BookFile.EXTENSION_WITH_PERIOD)), RuleJson.Options)!;
        RosterUnit guardians = book.Units.Single(u => u.Name == "Guardians");
        UpgradeSection swap = guardians.Sections.Single(s => s.Label == "Replace all Pistols and Bashes");

        var screen = new ArmyForgeScreen(book);
        screen.AddToList(guardians.Id);
        ArmyForgeScreen.SetChoice(screen.List.Units[0], swap, swap.Options.First().Id, 1);

        UnitFileEntry compiled = screen.Compile().Units.Single();
        Assert.That(compiled.Weapons.Any(w => w.Name == "Bash"), Is.False, "the Bashes are traded away");
        Assert.That(compiled.Weapons.Any(w => w.Name == "Pistol"), Is.False);
        Assert.That(compiled.Weapons.Single(w => w.Name == "CCW").Quantity, Is.EqualTo(5));
    }

    // ── #383 "Any model may replace one X" is one pick per MODEL, shared across the options ────────────

    // Reported 2026-08-22 (screenshots: Hive Warriors and Robot Snakes, both [3]): these sections rendered
    // as one-per-option checkboxes, so a 3-model unit could never take 3x Ravager Gun. Against the SHIPPED
    // book: the section is a counted stepper offering 3, the options spend one shared per-model budget,
    // and the compile carries the trio at 3x the option cost.
    [Test]
    public void HiveWarriors_AnyModelMayReplaceOneRazorClaws_TakesThreeOfTheSameGun()
    {
        BookFile book = JsonSerializer.Deserialize<BookFile>(
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Assets", "Books",
                "AlienHives" + BookFile.EXTENSION_WITH_PERIOD)), RuleJson.Options)!;
        RosterUnit warriors = book.Units.Single(u => u.Name == "Hive Warriors");
        UpgradeSection section = warriors.Sections.Single(
            s => s.Label == "Any model may replace one Razor Claws");
        UpgradeOption ravager = section.Options.Single(o => o.Label.StartsWith("Ravager Gun"));
        UpgradeOption spitter = section.Options.Single(o => o.Label.StartsWith("Spitter Gun"));

        Assert.That(section.PerModelBudget, Is.True, "the bundled book carries the #383 stamp");
        Assert.That(section.IsCounted, Is.True, "a stepper, not a checkbox");

        var screen = new ArmyForgeScreen(book);
        screen.AddToList(warriors.Id);
        BuilderUnit bu = screen.List.Units[0];

        Assert.That(StepperMaxFor(book, bu, section, ravager), Is.EqualTo(3),
            "3 models -> up to 3 applications of one option");

        ArmyForgeScreen.SetChoice(bu, section, ravager.Id, 3);
        Assert.That(StepperMaxFor(book, bu, section, spitter), Is.EqualTo(0),
            "the options share the per-model budget - 3 guns spend all of it");

        UnitFileEntry compiled = screen.Compile().Units.Single();
        Assert.That(compiled.Weapons.Single(w => w.Name == "Ravager Gun").Quantity, Is.EqualTo(3));
        Assert.That(compiled.PointCost, Is.EqualTo(warriors.BasePointCost + 3 * ravager.Cost),
            "each application is charged");
    }

    private static int StepperMaxFor(BookFile book, BuilderUnit bu, UpgradeSection section, UpgradeOption option)
    {
        (UnitFileEntry unit, var items) = ListCompiler.CompileUnitDetailed(book, bu);
        RosterUnit roster = book.Units.Single(u => u.Id == bu.RosterUnitId);
        int own = ArmyForgeScreen.ChoiceCount(bu, section.Id, option.Id);
        int others = section.Options.Sum(o => ArmyForgeScreen.ChoiceCount(bu, section.Id, o.Id)) - own;
        return ArmyForgeScreen.StepperMax(section, roster, unit,
            ListCompiler.AvailableApplications(unit.Weapons, items, section.Targets),
            own, others);
    }

    // ── #006 hero-join seams ────────────────────────────────────────────────────────────────────────────

    [Test]
    public void EnsureId_GeneratesOnce_ThenStable()
    {
        var bu = new BuilderUnit();
        string id = ArmyForgeScreen.EnsureId(bu);
        Assert.That(id, Is.Not.Empty);
        Assert.That(ArmyForgeScreen.EnsureId(bu), Is.EqualTo(id));
    }

    [Test]
    public void HostCandidates_ExcludesSelf()
    {
        var screen = new ArmyForgeScreen(DemoBook.Build());
        screen.AddToList("warriors");
        screen.AddToList("gunners");
        var hosts = screen.HostCandidates(0, screen.Compile().Units);
        Assert.That(hosts, Is.EqualTo(new[] { 1 }));
    }

    // ── #107 combined squads ────────────────────────────────────────────────────────────────────────────

    [Test]
    public void CombinedPair_SavesAsOneMergedUnit()
    {
        var screen = new ArmyForgeScreen(DemoBook.Build());
        screen.AddToList("warriors");
        screen.AddToList("warriors");
        var list = screen.Compile().Selections!;
        list.Units[1].CombinedWithId = ArmyForgeScreen.EnsureId(list.Units[0]);

        BuiltArmyFile army = screen.Compile();

        Assert.That(army.Units, Has.Count.EqualTo(1));
        Assert.That(army.Units[0].ModelCount, Is.EqualTo(10));
        Assert.That(army.Units[0].Name, Is.EqualTo("Vanguard Warriors"),
            "the merged pair keeps the plain unit name - no (Combined) suffix");
    }

    [Test]
    public void CombinedCheckbox_On_SpawnsLinkedCopy_MergingToOneUnit()
    {
        var screen = new ArmyForgeScreen(DemoBook.Build());
        screen.AddToList("warriors"); // 0
        Assert.That(screen.IsCombined(0), Is.False);

        screen.SetCombined(0, true);

        Assert.That(screen.List.Units, Has.Count.EqualTo(2), "checking Combined spawns a second copy");
        BuilderUnit copy = screen.List.Units[1];
        Assert.That(copy.RosterUnitId, Is.EqualTo("warriors"));
        Assert.That(copy.CombinedWithId, Is.EqualTo(screen.List.Units[0].Id).And.Not.Null,
            "the spawned copy links to the base");
        Assert.That(screen.CombinePartnerIndex(0), Is.EqualTo(1));
        Assert.That(screen.CombinePartnerIndex(1), Is.EqualTo(0), "either half resolves to the other");

        BuiltArmyFile army = screen.Compile();
        Assert.That(army.Units, Has.Count.EqualTo(1));
        Assert.That(army.Units[0].ModelCount, Is.EqualTo(10), "the pair merged into one 10-model unit");
    }

    [Test]
    public void CombinedCheckbox_Off_RemovesSpawnedCopy()
    {
        var screen = new ArmyForgeScreen(DemoBook.Build());
        screen.AddToList("warriors");
        screen.SetCombined(0, true);
        Assert.That(screen.List.Units, Has.Count.EqualTo(2));

        screen.SetCombined(0, false); // toggle off from the base

        Assert.That(screen.List.Units, Has.Count.EqualTo(1), "the spawned copy is removed");
        Assert.That(screen.List.Units[0].CombinedWithId, Is.Null);
        Assert.That(screen.IsCombined(0), Is.False);
    }

    [Test]
    public void RemovingOneHalf_UncombinesTheSurvivor_NoWarning()
    {
        var screen = new ArmyForgeScreen(DemoBook.Build());
        screen.AddToList("warriors"); // 0 base
        screen.SetCombined(0, true);  // 1 spawned copy links to 0

        screen.RemoveFromList(0);     // remove the BASE

        Assert.That(screen.List.Units, Has.Count.EqualTo(1));
        Assert.That(screen.List.Units[0].CombinedWithId, Is.Null, "the survivor's dangling link is cleared");
        Assert.That(screen.Issues(), Is.Empty, "no dangling-combine warning left behind");
    }

    [Test]
    public void SetCombined_IsNoOp_OnSingleModelHero()
    {
        var screen = new ArmyForgeScreen(HeroBook());
        screen.AddToList("hero");

        screen.SetCombined(0, true); // ineligible: a single-model Hero can't be combined

        Assert.That(screen.List.Units, Has.Count.EqualTo(1));
        Assert.That(screen.IsCombined(0), Is.False);
    }

    [Test]
    public void SetCombined_On_WhenAlreadyCombined_DoesNotSpawnAThird()
    {
        var screen = new ArmyForgeScreen(DemoBook.Build());
        screen.AddToList("warriors");
        screen.SetCombined(0, true);
        Assert.That(screen.List.Units, Has.Count.EqualTo(2));

        screen.SetCombined(0, true); // already a pair — no third copy

        Assert.That(screen.List.Units, Has.Count.EqualTo(2));
    }

    [Test]
    public void CombinedSpawn_SeedsWholeUnitUpgrade_AndPaysForBoth()
    {
        // gunners-missiles is Affects=All ("Replace all Heavy Rifles with Missile Launchers").
        UpgradeSection allSection = GunnersAllSection();

        var screen = new ArmyForgeScreen(DemoBook.Build());
        screen.AddToList("gunners");
        ArmyForgeScreen.SetChoice(screen.List.Units[0], allSection, "missile", 1); // upgrade the base first

        screen.SetCombined(0, true); // spawn — should seed the All-scope pick onto the copy

        Assert.That(ArmyForgeScreen.IsChosen(screen.List.Units[1], "gunners-missiles", "missile"), Is.True,
            "the whole-unit upgrade is mirrored onto the spawned copy");

        // "Pay for both": the merged unit costs exactly twice one upgraded copy.
        var reference = new ArmyForgeScreen(DemoBook.Build());
        reference.AddToList("gunners");
        ArmyForgeScreen.SetChoice(reference.List.Units[0], allSection, "missile", 1);
        int oneUpgraded = reference.Compile().Units[0].PointCost;

        Assert.That(screen.Compile().Units[0].PointCost, Is.EqualTo(2 * oneUpgraded));
    }

    [Test]
    public void ApplyChoice_MirrorsWholeUnitEdit_BothWays()
    {
        UpgradeSection allSection = GunnersAllSection();

        var screen = new ArmyForgeScreen(DemoBook.Build());
        screen.AddToList("gunners");
        screen.SetCombined(0, true);
        BuilderUnit a = screen.List.Units[0], b = screen.List.Units[1];

        ArmyForgeScreen.ApplyChoice(a, b, allSection, "missile", 1); // edit copy A
        Assert.That(ArmyForgeScreen.IsChosen(a, "gunners-missiles", "missile"), Is.True);
        Assert.That(ArmyForgeScreen.IsChosen(b, "gunners-missiles", "missile"), Is.True, "edit mirrored to partner");

        ArmyForgeScreen.ApplyChoice(a, b, allSection, string.Empty, 0); // clear on A
        Assert.That(ArmyForgeScreen.IsChosen(b, "gunners-missiles", "missile"), Is.False, "clearing mirrors too");
    }

    [Test]
    public void ApplyChoice_PerModelUpgrade_StaysIndependentPerCopy()
    {
        // warriors-special is Affects=One ("Upgrade one Rifle to a Plasma Rifle") - NOT shared.
        UpgradeSection oneSection = DemoBook.Build().Units.Single(u => u.Id == "warriors")
            .Sections.Single(s => s.Id == "warriors-special");

        var screen = new ArmyForgeScreen(DemoBook.Build());
        screen.AddToList("warriors");
        screen.SetCombined(0, true);
        BuilderUnit a = screen.List.Units[0], b = screen.List.Units[1];

        ArmyForgeScreen.ApplyChoice(a, b, oneSection, "plasma", 1);

        Assert.That(ArmyForgeScreen.IsChosen(a, "warriors-special", "plasma"), Is.True);
        Assert.That(ArmyForgeScreen.IsChosen(b, "warriors-special", "plasma"), Is.False,
            "per-model (Affects=One) upgrades stay independent per copy");
    }

    private static UpgradeSection GunnersAllSection() => DemoBook.Build().Units
        .Single(u => u.Id == "gunners").Sections.Single(s => s.Id == "gunners-missiles");

    // A single-model Hero: ineligible for combining (exercises the CanCombine multi-model + non-Hero gate).
    private static BookFile HeroBook() => new()
    {
        Name = "Hero Test",
        Units =
        {
            new RosterUnit
            {
                Id = "hero", Name = "Warlord",
                Quality = 3, Defense = 3, BaseModelCount = 1, MinModels = 1, MaxModels = 1, BasePointCost = 80,
                Rules = { new SpecialRuleEntry_Core("Hero") },
            },
        },
    };

    // ── P4: validation surfaced through the screen ──────────────────────────────────────────────────────

    [Test]
    public void Issues_EmptyForLegalList()
    {
        var screen = new ArmyForgeScreen(DemoBook.Build());
        screen.AddToList("warriors");
        screen.AddToList("gunners");
        Assert.That(screen.Issues(), Is.Empty);
    }

    [Test]
    public void Issues_FlagsOverMaxModels()
    {
        var screen = new ArmyForgeScreen(DemoBook.Build());
        screen.AddToList("warriors");
        BuilderUnit bu = screen.List.Units[0];
        RosterUnit warriors = DemoBook.Build().Units.Single(u => u.Id == "warriors");
        ArmyForgeScreen.SetChoice(bu, warriors.Sections.Single(s => s.Id == "warriors-reinforce"), "add-warrior", 10);

        Assert.That(screen.Issues().Any(i => i.Severity == ListIssueSeverity.Error), Is.True);
    }

    // ── #307: a rejected load must not be mistakable for a loaded one, and Save must not quietly write ───
    //          the screen's untouched startup default over a path the user picked for a real army.

    /// <summary>A plain (Army-Builder-shaped) army: no embedded selections/book, exactly the shape every
    /// tracked <c>armies/</c> list had before #357 retrofitted them, and the shape the Forge rejects.</summary>
    private static BuiltArmyFile PlainArmy() => new() { Selections = null, Book = null };

    [Test]
    public void TryAdopt_RejectsAPlainArmy_AndSaysTheScreenIsUnchanged()
    {
        var screen = new ArmyForgeScreen(DemoBook.Build());

        Assert.That(screen.TryAdopt(PlainArmy(), "3k - Eternal Dynasty.fdgarmy", null),
            Is.EqualTo(ELoadOutcome.Rejected));
        Assert.That(screen.Status.Kind, Is.EqualTo(EForgeStatusKind.Error));
        Assert.That(screen.Status.Text, Does.Contain("LOAD FAILED"));
    }

    [Test]
    public void LoadFailureMessage_NamesTheFile_AndWarnsWhatSaveWouldWrite()
    {
        string message = ArmyForgeScreen.LoadFailureMessage("3k - Eternal Dynasty.fdgarmy",
            ArmyForgeScreen.NoEmbeddedBookReason);

        Assert.That(message, Does.Contain("3k - Eternal Dynasty.fdgarmy"));
        Assert.That(message, Does.Contain("NOT loaded"));
        Assert.That(message, Does.Contain("Army Builder"));
        // The sentence the whole item exists for: the screen did not change, so Save writes the OLD list.
        Assert.That(message, Does.Contain("has not changed"));
        Assert.That(message, Does.Contain("Saving now would write"));
    }

    [Test]
    public void SaveGuard_FiresOnThePristineDefault_TheReportedDataLossPath()
    {
        // The report: launch the Forge, Load a plain army (rejected), press Save. The screen still holds the
        // startup default, and the old code wrote it out with no warning at all.
        var screen = new ArmyForgeScreen(DemoBook.Build());
        Assert.That(screen.PendingSaveGuard(), Is.EqualTo(ESaveGuard.EmptyList), "empty startup list");

        screen.TryAdopt(PlainArmy(), "3k - Eternal Dynasty.fdgarmy", null);

        Assert.That(screen.PendingSaveGuard(), Is.EqualTo(ESaveGuard.UnchangedAfterFailedLoad));
    }

    [Test]
    public void SaveGuard_FiresWhenARejectedLoadLeavesAnEarlierArmyOnScreen()
    {
        // The nastier variant: a real list IS on screen, so an empty-list check alone would miss it - Save
        // would write army A over the path of army B, the file the user just failed to open.
        var screen = new ArmyForgeScreen(DemoBook.Build());
        screen.AddToList("warriors");
        screen.AddToList("gunners");

        Assert.That(screen.PendingSaveGuard(), Is.EqualTo(ESaveGuard.None), "an edited list saves freely");

        screen.TryAdopt(PlainArmy(), "3k - Eternal Dynasty.fdgarmy", null);

        Assert.That(screen.PendingSaveGuard(), Is.EqualTo(ESaveGuard.UnchangedAfterFailedLoad));
    }

    [Test]
    public void SaveGuard_ClearsOnceTheUserEditsAfterTheFailure()
    {
        var screen = new ArmyForgeScreen(DemoBook.Build());
        screen.AddToList("warriors");
        screen.TryAdopt(PlainArmy(), "3k - Eternal Dynasty.fdgarmy", null);

        screen.AddToList("gunners"); // deliberate edit - the user now knows what is on screen

        Assert.That(screen.PendingSaveGuard(), Is.EqualTo(ESaveGuard.None));
    }

    [Test]
    public void SaveGuard_ClearsAfterASuccessfulLoad()
    {
        var screen = new ArmyForgeScreen(DemoBook.Build());
        screen.AddToList("warriors");
        screen.TryAdopt(PlainArmy(), "3k - Eternal Dynasty.fdgarmy", null);

        // A Forge-authored file (embedded selections + book) is adopted, so the screen holds what it says.
        Assert.That(screen.TryAdopt(screen.Compile(), "Warband.fdgarmy", null), Is.EqualTo(ELoadOutcome.Adopted));
        Assert.That(screen.Status.Kind, Is.EqualTo(EForgeStatusKind.Success));
        Assert.That(screen.PendingSaveGuard(), Is.EqualTo(ESaveGuard.None));
    }

    [Test]
    public void TryAdopt_ReportsReadFailuresInsteadOfReturningInSilence()
    {
        var screen = new ArmyForgeScreen(DemoBook.Build());

        Assert.That(screen.TryAdopt(null, "broken.fdgarmy", "That file could not be read:\n\nUnexpected token"),
            Is.EqualTo(ELoadOutcome.Rejected));
        Assert.That(screen.Status.Text, Does.Contain("broken.fdgarmy"));

        // A null deserialize (valid JSON, no army) gets its own reason rather than the no-book one.
        Assert.That(screen.TryAdopt(null, "empty.fdgarmy", null), Is.EqualTo(ELoadOutcome.Rejected));
    }

    [Test]
    public void SaveGuardMessage_NamesTheTargetFileAndWhatWouldBeWritten()
    {
        string stale = ArmyForgeScreen.SaveGuardMessage(ESaveGuard.UnchangedAfterFailedLoad,
            "3k - Eternal Dynasty.fdgarmy", "Alien Hives", 0);
        Assert.That(stale, Does.Contain("3k - Eternal Dynasty.fdgarmy"));
        Assert.That(stale, Does.Contain("did not take effect"));
        Assert.That(stale, Does.Contain("EMPTY Alien Hives list"));
        Assert.That(stale, Does.Contain("overwrite"));

        string empty = ArmyForgeScreen.SaveGuardMessage(ESaveGuard.EmptyList, "Warband.fdgarmy", "Alien Hives", 0);
        Assert.That(empty, Does.Contain("no units"));

        // Plural agreement on the non-empty (stale-content) wording.
        string one = ArmyForgeScreen.SaveGuardMessage(ESaveGuard.UnchangedAfterFailedLoad, "a.fdgarmy", "Book", 1);
        Assert.That(one, Does.Contain("(1 unit)"));
        string many = ArmyForgeScreen.SaveGuardMessage(ESaveGuard.UnchangedAfterFailedLoad, "a.fdgarmy", "Book", 3);
        Assert.That(many, Does.Contain("(3 units)"));
    }

    [Test]
    public void ListFingerprint_TracksListContent()
    {
        var a = new ArmyForgeScreen(DemoBook.Build());
        var b = new ArmyForgeScreen(DemoBook.Build());
        Assert.That(ArmyForgeScreen.ListFingerprint(a.List), Is.EqualTo(ArmyForgeScreen.ListFingerprint(b.List)));

        a.AddToList("warriors");
        Assert.That(ArmyForgeScreen.ListFingerprint(a.List), Is.Not.EqualTo(ArmyForgeScreen.ListFingerprint(b.List)));
    }

    // ── #356: an imported "Save As" army carries an editable session, and reopening it discloses drift ──

    /// <summary>The two halves of an imported file: Army Forge's verbatim units (what plays) and our
    /// reconstruction (what the Forge would rebuild). Here they disagree, as a real import can.</summary>
    private static BuiltArmyFile ImportedArmyWithDrift()
    {
        var screen = new ArmyForgeScreen(DemoBook.Build());
        screen.AddToList("warriors");
        screen.AddToList("gunners");
        BuiltArmyFile playable = screen.Compile();
        playable.UnattributedPoints = 25; // Army Forge priced upgrades it publishes no cost for (#219)

        return (BuiltArmyFile)ArmyForgeScreen.ImportedFileToWrite(
            playable, screen.List, DemoBook.Build());
    }

    [Test]
    public void ImportedFileToWrite_AttachesTheEditableSession_SoSaveAsIsNotADeadEnd()
    {
        var screen = new ArmyForgeScreen(DemoBook.Build());
        screen.AddToList("warriors");
        ArmyListFile playable = screen.Compile();

        ArmyListFile written = ArmyForgeScreen.ImportedFileToWrite(playable, screen.List, DemoBook.Build());

        Assert.That(written, Is.InstanceOf<BuiltArmyFile>());
        Assert.That(((BuiltArmyFile)written).Selections, Is.Not.Null);
        Assert.That(((BuiltArmyFile)written).Book, Is.Not.Null);

        // And it survives the trip to disk as the derived type, so it really does reopen.
        string json = JsonSerializer.Serialize(written, written.GetType(), RuleJson.Options);
        var back = JsonSerializer.Deserialize<BuiltArmyFile>(json, RuleJson.Options)!;
        Assert.That(new ArmyForgeScreen(DemoBook.Build()).AdoptLoaded(back), Is.True);
    }

    [Test]
    public void ImportedFileToWrite_StaysPlain_WhenNoBundledBookMatched()
    {
        var screen = new ArmyForgeScreen(DemoBook.Build());
        screen.AddToList("warriors");
        ArmyListFile playable = screen.Compile();

        Assert.That(ArmyForgeScreen.ImportedFileToWrite(playable, null, null), Is.SameAs(playable));
        Assert.That(ArmyForgeScreen.ImportedFileToWrite(playable, screen.List, null), Is.SameAs(playable));
    }

    [Test]
    public void Reopening_AForgeAuthoredFile_AdoptsSilently()
    {
        // Both halves came from one compile, so there is nothing to disclose - no modal, straight in.
        var screen = new ArmyForgeScreen(DemoBook.Build());
        screen.AddToList("warriors");

        var fresh = new ArmyForgeScreen(DemoBook.Build());
        Assert.That(fresh.TryAdopt(screen.Compile(), "Warband.fdgarmy", null), Is.EqualTo(ELoadOutcome.Adopted));
        Assert.That(fresh.List.Units, Has.Count.EqualTo(1));
    }

    [Test]
    public void Reopening_AnImportedFileThatWouldChange_AsksFirst_AndChangesNothingYet()
    {
        var screen = new ArmyForgeScreen(DemoBook.Build());
        screen.AddToList("gunners"); // the list already on screen

        Assert.That(screen.TryAdopt(ImportedArmyWithDrift(), "Imported.fdgarmy", null),
            Is.EqualTo(ELoadOutcome.NeedsDriftConfirm));

        // Nothing adopted yet - the screen still holds its own list until the user accepts.
        Assert.That(screen.List.Units.Select(u => u.RosterUnitId), Is.EqualTo(new[] { "gunners" }));
    }

    [Test]
    public void ReopenDriftMessage_ShowsWhatChanges_AndOnlyWhatChanges()
    {
        EditableSessionDrift drift = EditableSession.Measure(ImportedArmyWithDrift())!;
        string message = ArmyForgeScreen.ReopenDriftMessage("Imported.fdgarmy", drift);

        Assert.That(message, Does.Contain("Imported.fdgarmy"));
        Assert.That(message, Does.Contain("Points: "));
        Assert.That(message, Does.Contain("not touched until you save"));
        // Same units both ways here - only the price differs, so no unit lines are invented.
        Assert.That(message, Does.Not.Contain("Units: "));
        Assert.That(message, Does.Not.Contain("Dropped: "));
    }

    [Test]
    public void GuardAndFailureText_IsAsciiOnly()
    {
        // CLAUDE.md: the ImGui font atlas bakes Basic Latin + Latin-1 only; anything above U+00FF draws as '?'.
        var texts = new[]
        {
            ArmyForgeScreen.NoEmbeddedBookReason,
            ArmyForgeScreen.LoadFailedTitle,
            ArmyForgeScreen.SaveGuardTitle,
            ArmyForgeScreen.LoadFailureMessage("a.fdgarmy", ArmyForgeScreen.NoEmbeddedBookReason),
            ArmyForgeScreen.SaveGuardMessage(ESaveGuard.UnchangedAfterFailedLoad, "a.fdgarmy", "Book", 2),
            ArmyForgeScreen.SaveGuardMessage(ESaveGuard.EmptyList, "a.fdgarmy", "Book", 0),
            ArmyForgeScreen.ReopenDriftTitle,
            ArmyForgeScreen.ReopenDriftMessage("a.fdgarmy", EditableSession.Measure(ImportedArmyWithDrift())!),
        };
        foreach (string text in texts)
            Assert.That(text.All(c => c <= 0xFF), Is.True, $"non-Latin-1 character in: {text}");
    }
}
