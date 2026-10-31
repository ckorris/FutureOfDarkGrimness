using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #441 - the Army Forge, its hover glossary and its import summary against the SHIPPED 3.6 books. Everything here
// drives the ImGui-free seams (the screen's test-seam ctor, RuleTextFlow, RuleGlossary, ListValidator); the panes
// themselves are hand-verified (checklist in WorkItems/441-army-forge-ui-3-6.md).
[TestFixture]
public class ArmyForge36Tests
{
    private static BookFile Book(string fileName) =>
        JsonSerializer.Deserialize<BookFile>(
            File.ReadAllText(Path.Combine(ShippedBooks.Directory, fileName + BookFile.EXTENSION_WITH_PERIOD)),
            RuleJson.Options)!;

    private static List<BookFile> AllBooks() =>
        Directory.EnumerateFiles(ShippedBooks.Directory, "*" + BookFile.EXTENSION_WITH_PERIOD)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(p => JsonSerializer.Deserialize<BookFile>(File.ReadAllText(p), RuleJson.Options)!)
            .ToList();

    // ── Goal 2: the book roster ─────────────────────────────────────────────────────────────────────

    // The Forge's dropdown is the shipped library, filtered per system (#378). 3.6 renamed five books (OPR's
    // "Wormhole Daemons of X" / "Rift Daemons of X" -> "Daemons of X", "Saurian Starhost" -> "Saurian Starhosts"),
    // added Voidspace Stalkers, and lists 0-unit parent entries ("Wormhole Daemons", "Rift Daemons") the importer
    // never turns into books - so the dropdown must show 48 + 40 books, every one under its 3.6 name.
    [Test]
    public void TheRoster_Lists48GdfAnd40AofBooks_UnderTheir36Names()
    {
        var screen = new ArmyForgeScreen(AllBooks());

        Assert.That(screen.SystemIndex, Is.EqualTo(0), "Grimdark Future first");
        IReadOnlyList<string> gdf = screen.VisibleBookNames.ToList();
        screen.SwitchSystem(1);
        IReadOnlyList<string> aof = screen.VisibleBookNames.ToList();

        Assert.Multiple(() =>
        {
            Assert.That(gdf, Has.Count.EqualTo(48));
            Assert.That(aof, Has.Count.EqualTo(40));
            Assert.That(gdf, Is.Unique);
            Assert.That(aof, Is.Unique);
            Assert.That(gdf, Does.Contain("Voidspace Stalkers"), "new in 3.6");
            Assert.That(gdf, Does.Contain("Saurian Starhosts"));
            Assert.That(gdf, Is.SupersetOf(new[] { "Daemons of Change", "Daemons of Lust", "Daemons of Plague", "Daemons of War" }));
            Assert.That(aof, Is.SupersetOf(new[] { "Daemons of Change", "Daemons of Lust", "Daemons of Plague", "Daemons of War" }));
            foreach (string name in gdf.Concat(aof))
            {
                Assert.That(name, Does.Not.StartWith("Wormhole Daemons").And.Not.StartWith("Rift Daemons"),
                    "no 3.5 name and no 0-unit parent entry");
                Assert.That(name, Is.Not.EqualTo("Saurian Starhost"));
            }
        });
    }

    [Test]
    public void EveryShippedBook_IsStamped36_AndCarriesItsSystem()
    {
        foreach (BookFile book in AllBooks())
        {
            Assert.That(book.Version, Is.EqualTo("OPR v3.6.0"), book.Name);
            Assert.That(GameSystems.Normalize(book.GameSystem),
                Is.EqualTo(GameSystems.GrimdarkFuture).Or.EqualTo(GameSystems.AgeOfFantasy), book.Name);
            Assert.That(book.Units, Is.Not.Empty, $"{book.Name}: a parent entry is not a book");
        }
    }

    // A loaded or imported list lands on its book in the dropdown even where the name exists in both systems
    // (3.6 gave GDF and AoF a "Daemons of War" each).
    [Test]
    public void AdoptLoaded_ADaemonsList_SelectsTheBookOfItsOwnSystem()
    {
        var screen = new ArmyForgeScreen(AllBooks());
        BookFile aofWar = Book("AoF-DaemonsofWar");
        BuiltArmyFile saved = new()
        {
            Book = aofWar,
            Selections = new BuilderList { BookName = aofWar.Name, PointsLimit = 750 },
        };

        Assert.That(screen.AdoptLoaded(saved), Is.True);
        Assert.That(screen.SystemIndex, Is.EqualTo(1), "Age of Fantasy");
        Assert.That(screen.VisibleBookNames, Has.Count.EqualTo(40));
        Assert.That(GameSystems.Normalize(screen.CurrentBook.GameSystem), Is.EqualTo(GameSystems.AgeOfFantasy));
    }
    // ── Goal 1: an Assault weapon on the card ───────────────────────────────────────────────────────

    // The shipped 3.6 Assault Brothers carry a Heavy Pistol (9", A1, AP(1), Assault) and a Hand Weapon. The Forge
    // prints the pistol ONCE, as the ranged line it is, with Assault a hoverable rule whose tooltip is the 3.6
    // sentence - never as a second melee line (#424). Its melee use is the rule's, so the card adds nothing else.
    [Test]
    public void AssaultBrothers_HeavyPistol_PrintsOnce_WithItsRange_AndAHoverableAssault()
    {
        BookFile book = Book("BattleBrothers");
        RosterUnit roster = book.Units.Single(u => u.Name == "Assault Brothers");
        var screen = new ArmyForgeScreen(book);
        screen.AddToList(roster.Id);

        UnitFileEntry unit = screen.Compile().Units.Single();
        List<string> lines = unit.Weapons.Select(w => RuleTextFlow.Flatten(RuleTextFlow.WeaponLine(w))).ToList();

        Assert.That(lines, Is.EqualTo(new[] { "5x Heavy Pistol (9\", A1, AP(1), Assault)", "5x Hand Weapon (A1)" }),
            "one line per weapon - the pistol is not repeated as a melee weapon");
        RuleTextFlow.RuleSegment assault = RuleTextFlow.WeaponLine(unit.Weapons[0]).Single(s => s.IsRule);
        Assert.That(assault.Text, Is.EqualTo("Assault"));
        Assert.That(RuleGlossary.Build(book).Tooltip(assault.Rule!),
            Is.EqualTo("Assault\nCounts as both a ranged weapon and a melee weapon."),
            "the hover is the 3.6 sentence from the book's embedded definition, underlined as documented");
    }

    // ── Goal 8 / #438 D3: the stepper with the pass-through combo bought ────────────────────────────

    // 3.6 Assault Brothers: "Replace any Heavy Pistol" sits ABOVE "Replace one Heavy Pistol and Hand Weapon -> Heavy
    // Pistol, Energy Hammer". The combo hands a pistol back, so the any-swap still reaches all five pistols (the
    // compiler yields, #438 D3) - the Forge stepper has to offer all five too, and the compile has to land both.
    [Test]
    public void AssaultBrothers_WithTheHammerComboBought_TheAnySwapStepperOffersAllFive()
    {
        BookFile book = Book("BattleBrothers");
        RosterUnit roster = book.Units.Single(u => u.Name == "Assault Brothers");
        UpgradeSection any = roster.Sections.Single(s => s.Label == "Replace any Heavy Pistol");
        UpgradeOption plasma = any.Options.Single();
        UpgradeSection combo = roster.Sections.First(s => s.Label == "Replace one Heavy Pistol and Hand Weapon");
        UpgradeOption hammer = combo.Options.Single(o => o.Label.Contains("Energy Hammer"));

        var screen = new ArmyForgeScreen(book);
        screen.AddToList(roster.Id);
        BuilderUnit bu = screen.List.Units[0];
        ArmyForgeScreen.SetChoice(bu, combo, hammer.Id, 1);

        Assert.That(StepperMaxFor(book, bu, any, plasma), Is.EqualTo(5), "the combo returns its pistol");

        ArmyForgeScreen.SetChoice(bu, any, plasma.Id, 5);
        Assert.That(StepperMaxFor(book, bu, any, plasma), Is.EqualTo(5), "still five with all five taken");

        UnitFileEntry compiled = screen.Compile().Units.Single();
        Assert.That(compiled.Weapons.Single(w => w.Name == "Plasma Pistol").Quantity, Is.EqualTo(5));
        Assert.That(compiled.Weapons.Single(w => w.Name == "Energy Hammer").Quantity, Is.EqualTo(1));
        Assert.That(compiled.Weapons.Any(w => w.Name == "Heavy Pistol"), Is.False);
        Assert.That(compiled.PointCost, Is.EqualTo(roster.BasePointCost + hammer.Cost + 5 * plasma.Cost));
    }

    // The same purchase in the other order: five Plasma Pistols first, then the combo. The compiler lands both
    // whichever order the clicks came in; the Forge must not gray the combo out just because the any-swap holds
    // every pistol.
    [Test]
    public void AssaultBrothers_WithAllFivePistolsSwapped_TheHammerComboIsStillOffered()
    {
        BookFile book = Book("BattleBrothers");
        RosterUnit roster = book.Units.Single(u => u.Name == "Assault Brothers");
        UpgradeSection any = roster.Sections.Single(s => s.Label == "Replace any Heavy Pistol");
        UpgradeSection combo = roster.Sections.First(s => s.Label == "Replace one Heavy Pistol and Hand Weapon");
        UpgradeOption hammer = combo.Options.Single(o => o.Label.Contains("Energy Hammer"));

        var screen = new ArmyForgeScreen(book);
        screen.AddToList(roster.Id);
        BuilderUnit bu = screen.List.Units[0];
        ArmyForgeScreen.SetChoice(bu, any, any.Options.Single().Id, 5);

        UpgradeOption chainSword = combo.Options.Single(o => o.Label.StartsWith("Great Chain-Sword"));
        Assert.That(ArmyForgeScreen.AvailableExcludingSection(book, bu, combo), Is.Zero,
            "no pistol is left for an ordinary pick");
        Assert.That(ArmyForgeScreen.PassThroughPool(book, bu, roster, combo, hammer, excludeOwn: true), Is.GreaterThan(0),
            "but the hammer combo hands its pistol back, so its radio stays live (the swap above yields to it)");
        Assert.That(ArmyForgeScreen.PassThroughPool(book, bu, roster, combo, chainSword, excludeOwn: true), Is.Zero,
            "the chain-sword consumes the pistol for good - grayed out, as the compiler would drop it");

        ArmyForgeScreen.SetChoice(bu, combo, hammer.Id, 1);
        UnitFileEntry compiled = screen.Compile().Units.Single();
        Assert.That(compiled.Weapons.Single(w => w.Name == "Plasma Pistol").Quantity, Is.EqualTo(5));
        Assert.That(compiled.Weapons.Single(w => w.Name == "Energy Hammer").Quantity, Is.EqualTo(1));
    }

    private static int StepperMaxFor(BookFile book, BuilderUnit bu, UpgradeSection section, UpgradeOption option)
    {
        (UnitFileEntry unit, var items) = ListCompiler.CompileUnitDetailed(book, bu);
        RosterUnit roster = book.Units.Single(u => u.Id == bu.RosterUnitId);
        int own = ArmyForgeScreen.ChoiceCount(bu, section.Id, option.Id);
        int others = section.Options.Sum(o => ArmyForgeScreen.ChoiceCount(bu, section.Id, o.Id)) - own;
        int available = ArmyForgeScreen.YieldingAllSwapChosen(bu, roster, section)
            ? ArmyForgeScreen.ReplacePool(book, bu, roster, section, excludeOwn: false)
            : ListCompiler.AvailableApplications(unit.Weapons, items, section.Targets);
        return ArmyForgeScreen.StepperMax(section, roster, unit, available, own, others);
    }
    // ── Goals 3 + 4: Unique Hero and the force-org lines, per system ────────────────────────────────

    private static RosterUnit Roster(BookFile book, string name) => book.Units.Single(u => u.Name == name);

    private static RosterUnit FirstSquad(BookFile book) => book.Units.First(u =>
        u.MaxModels > 1 && !u.Rules.Any(r => r.PrintableName is "Hero" or "Unique Hero"));

    // 3.6 HDF Colonel Rixxon is a Unique Hero: a Hero for every purpose (the join control is offered, it counts
    // toward the Hero cap) and an Error on the second copy - the rule is absolute, unlike the advisory caps.
    [Test]
    public void GdfUniqueHero_JoinsLikeAHero_AndTheSecondCopyIsAnError_WithTheGdfHeroCapWording()
    {
        BookFile book = Book("HumanDefenseForce");
        RosterUnit rixxon = Roster(book, "Colonel Rixxon");
        var screen = new ArmyForgeScreen(book);
        screen.AddToList(FirstSquad(book).Id);
        screen.AddToList(rixxon.Id);
        screen.AddToList(rixxon.Id);
        screen.AddToList(Roster(book, "Company Leader").Id);
        Assert.That(screen.List.PointsLimit, Is.EqualTo(1000), "GDF starter size");

        List<UnitFileEntry> rows = ListCompiler.CompileRows(book, screen.List);
        Assert.That(ForceOrgValidator.IsHero(rows[1]), Is.True, "the join control is drawn for a Hero");
        Assert.That(screen.HostCandidates(1, rows), Does.Contain(0), "and offers the squad as a host");
        Assert.That(RuleGlossary.Build(book).Describe("Unique Hero"),
            Is.EqualTo("Counts as having Hero, and this model may only be taken once per army."),
            "the hover is the 3.6 catalog sentence, not the not-enforced marker");

        IReadOnlyList<ListIssue> issues = screen.Issues();
        Assert.That(issues.Where(i => i.Severity == ListIssueSeverity.Error).Select(i => (i.UnitIndex, i.Message)),
            Is.EqualTo(new[] { ((int?)2, "Colonel Rixxon: Unique Hero - the army may only include one copy.") }),
            "only the SECOND copy, on its own row");
        Assert.That(issues.Where(i => i.Severity == ListIssueSeverity.Warning).Select(i => i.Message),
            Does.Contain("Too many Heroes: 3 (max 2 at 1000 pts; 1 per 500)."));
        Assert.That(issues.Select(i => i.Message), Has.None.StartsWith("Unique Hero \""),
            "the advisory duplicate line is dropped where the Error already says it");
    }

    // The same shapes in Age of Fantasy read AoF's numbers (1 Hero per 375 at a 750 starter list). The Army Builder
    // prints ForceOrgValidator's lines for the same army straight (no catalog behind it, so Unique Hero is a
    // warning there).
    [Test]
    public void AofUniqueHero_AndTheHeroCap_ReadAofNumbers_InTheForgeAndTheArmyBuilder()
    {
        BookFile book = Book("AoF-Dwarves");
        RosterUnit gamharl = Roster(book, "Gamharl");
        var screen = new ArmyForgeScreen(book);
        screen.AddToList(FirstSquad(book).Id);
        screen.AddToList(gamharl.Id);
        screen.AddToList(gamharl.Id);
        screen.AddToList(Roster(book, "Dwarf Champion").Id);
        Assert.That(screen.List.PointsLimit, Is.EqualTo(750), "AoF starter size");

        IReadOnlyList<ListIssue> issues = screen.Issues();
        Assert.That(issues.Single(i => i.Severity == ListIssueSeverity.Error).Message,
            Is.EqualTo("Gamharl: Unique Hero - the army may only include one copy."));
        Assert.That(issues.Select(i => i.Message), Does.Contain("Too many Heroes: 3 (max 2 at 750 pts; 1 per 375)."));

        IReadOnlyList<string> builderLines = ForceOrgValidator.Validate(screen.Compile());
        Assert.That(builderLines, Does.Contain("Too many Heroes: 3 (max 2 at 750 pts; 1 per 375)."));
        Assert.That(builderLines, Does.Contain("Unique Hero \"Gamharl\" taken 2 times (once per army)."));
        foreach (string line in builderLines.Concat(issues.Select(i => i.Message)))
            Assert.That(line.All(c => c < 0x80), Is.True, $"ASCII only: {line}");
    }

    // Every cap's wording per system, as the two screens print them (#430's numbers: GDF 500 / 1000 / 200 / 20,
    // AoF 375 / 750 / 150 / 15; 35% for both).
    [TestCase(GameSystems.GrimdarkFuture, 500, 1000, 200, 20)]
    [TestCase(GameSystems.AgeOfFantasy, 375, 750, 150, 15)]
    public void ForceOrgLines_NameTheSystemsNumbers(string system, int perHero, int perCopy, int perUnit, int perModel)
    {
        var army = new BuiltArmyFile { GameSystem = system, PointsLimit = 300 };
        for (int i = 0; i < 4; i++)
            army.Units.Add(new UnitFileEntry
            {
                Name = i < 3 ? "Squad" : "Boss", ModelCount = 10, PointCost = 120,
                SpecialRules = i < 3 ? new List<SpecialRuleEntry>() : new List<SpecialRuleEntry> { new SpecialRuleEntry_Core("Hero") },
            });
        army.Units.Add(new UnitFileEntry
        {
            Name = "Second Boss", ModelCount = 1, PointCost = 10,
            SpecialRules = new List<SpecialRuleEntry> { new SpecialRuleEntry_Core("Hero") },
        });

        string all = string.Join("\n", ForceOrgValidator.Validate(army));
        Assert.Multiple(() =>
        {
            Assert.That(all, Does.Contain($"; 1 per {perHero})."));
            Assert.That(all, Does.Contain($"; 1 + 1 per {perCopy})."));
            Assert.That(all, Does.Contain("; 35% of the army)."));
            Assert.That(all, Does.Contain($"; 1 per {perUnit})."));
            Assert.That(all, Does.Contain($"; 1 model or Tough per {perModel})."));
        });
    }
    // ── Goal 5: the hover glossary ──────────────────────────────────────────────────────────────────

    // Every rule name the Forge can print for a shipped book - unit rules, weapon rules, item rules, the weapons an
    // item carries (#438 D4), and everything an upgrade option grants - has a description in that book's glossary.
    // So no shipped rule underlines faintly with the #259 "not enforced" note; that marker stays for what the engine
    // cannot resolve (RuleGlossaryTests.Describe_IsSilentExactlyWhenTheResolverCannotResolve), which for the shipped
    // data is --rule-coverage's Dead 0.
    [Test]
    public void EveryRuleAShippedBookCanPrint_HasA36Description_InAscii()
    {
        var undocumented = new SortedSet<string>(StringComparer.Ordinal);
        var nonAscii = new SortedSet<string>(StringComparer.Ordinal);
        int checkedNames = 0;
        foreach (BookFile book in AllBooks())
        {
            RuleGlossary glossary = RuleGlossary.Build(book);
            foreach (SpecialRuleEntry rule in RulesPrintedBy(book))
            {
                checkedNames++;
                string? description = glossary.Describe(rule);
                if (description is null) undocumented.Add($"{book.Name}: {rule.PrintableName}");
                else if (glossary.Tooltip(rule).Any(c => c >= 0x80)) nonAscii.Add($"{book.Name}: {rule.PrintableName}");
            }
        }

        Assert.That(checkedNames, Is.GreaterThan(10000), "the walk reaches the corpus");
        Assert.That(undocumented, Is.Empty, "would hover as \"not enforced\":\n  " + string.Join("\n  ", undocumented));
        Assert.That(nonAscii, Is.Empty, "the ImGui atlas would print '?':\n  " + string.Join("\n  ", nonAscii));
    }

    private static IEnumerable<SpecialRuleEntry> RulesPrintedBy(BookFile book)
    {
        IEnumerable<SpecialRuleEntry> Weapons(IEnumerable<WeaponFileEntry>? weapons) =>
            (weapons ?? Enumerable.Empty<WeaponFileEntry>()).SelectMany(w => w.SpecialRules);
        IEnumerable<SpecialRuleEntry> Items(IEnumerable<ItemEntry> items) =>
            items.SelectMany(i => i.Rules.Concat(Weapons(i.Weapons)));

        foreach (RosterUnit unit in book.Units)
        {
            foreach (SpecialRuleEntry r in unit.Rules.Concat(Weapons(unit.Weapons)).Concat(Items(unit.Items)))
                yield return r;
            foreach (UpgradeOption option in unit.Sections.SelectMany(s => s.Options))
                foreach (SpecialRuleEntry r in option.RulesGained.Concat(Weapons(option.WeaponsGained)).Concat(Items(option.ItemsGained)))
                    yield return r;
        }
    }

    // The tooltip carries the whole 3.6 sentence - nothing in the formatter clips the 2-4x longer texts (#426); the
    // wrap is ImGui's, at RuleHoverText.TooltipWrapEm, for every rule hover in the Forge and in game.
    [Test]
    public void TheLongest36Description_ReachesTheTooltipWhole()
    {
        (string Name, string Description) longest = AllBooks()
            .SelectMany(b => b.RuleDefinitions)
            .Concat(FDG.Rules.Dispatch.CoreRuleCatalog.All)
            .Where(d => !string.IsNullOrEmpty(d.Description))
            .Select(d => (d.Name, d.Description))
            .OrderByDescending(d => d.Description.Length)
            .First();

        BookFile owner = AllBooks().FirstOrDefault(b => b.RuleDefinitions.Any(d => d.Name == longest.Name)) ?? Book("BattleBrothers");
        Assert.That(longest.Description.Length, Is.GreaterThan(300), "3.6 texts are long - this is what the wrap is for");
        Assert.That(RuleGlossary.Build(owner).Tooltip(longest.Name), Is.EqualTo($"{longest.Name}\n{longest.Description}"));

        // Laid out at the shared wrap width with a half-em average glyph (the body font's), it wraps at word
        // boundaries into a readable block: every line inside the width, nothing lost, about a dozen lines.
        const float em = 18f;
        IReadOnlyList<RuleTextFlow.PlacedChunk> placed = RuleTextFlow.Layout(
            new[] { new RuleTextFlow.RuleSegment(longest.Description, null) },
            RuleHoverText.TooltipWrapEm * em, text => text.Length * em * 0.5f);
        Assert.That(string.Concat(placed.Select(c => c.Text)), Is.EqualTo(longest.Description), "nothing clipped");
        Assert.That(placed.Where(c => c.Text.Trim().Length > 0).All(c => c.X + c.Width <= RuleHoverText.TooltipWrapEm * em + em),
            Is.True, "no line runs past the wrap (a trailing space may hang)");
        Assert.That(placed[^1].Line + 1, Is.InRange(8, 20), "a tall-but-readable tooltip, not a one-line banner");
    }

    // An "Enhanced X" hover explains the rule it lifts: its description names X ("If most models in this unit have
    // Shadowborn, ..."). Three 3.6.0 texts name the MECHANIC instead ("If this model has Versatile Attack ... from
    // Versatile Attack") for Versatile Warrior / Watchborn / Vinci Tech - kept verbatim by #436 D3; two of them ship
    // in a book. Whether the hover should add the base rule's name is fork F7 in WorkItems/441. Pinned exactly, so a
    // re-bake that changes the set shows up here.
    [Test]
    public void EnhancedHovers_NameTheBaseRuleTheyLift_ExceptTheVersatileFamilyTexts()
    {
        var shipped = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (BookFile book in AllBooks())
            foreach (var d in book.RuleDefinitions.Where(d => d.Name.StartsWith("Enhanced ", StringComparison.Ordinal)))
                shipped[d.Name] = d.Description;

        var silent = shipped
            .Where(kv => !kv.Value.Contains(BaseOfEnhanced(kv.Key), StringComparison.OrdinalIgnoreCase))
            .Select(kv => kv.Key).OrderBy(n => n, StringComparer.Ordinal).ToList();

        Assert.That(shipped.Count, Is.GreaterThan(50), "the books embed the Enhanced family");
        Assert.That(silent, Is.EqualTo(new[] { "Enhanced Vinci Tech", "Enhanced Watchborn" }),
            "OPR's 3.6.0 text for these says \"Versatile Attack\" (F7); Enhanced Versatile Warrior is in no shipped book");
    }

    private static string BaseOfEnhanced(string name)
    {
        string rest = name["Enhanced ".Length..];
        foreach (string suffix in new[] { " Aura", " Buff", " Debuff" })
            if (rest.EndsWith(suffix, StringComparison.Ordinal)) return rest[..^suffix.Length];
        return rest;
    }
    // ── Goal 6: the import summary and the spell list ───────────────────────────────────────────────

    // #437's version gate refuses a list whose book is on another OPR release, with a remedy per direction; the
    // import popup prints the failure's message as is (red, wrapped), so both remedies reach the player.
    [TestCase("3.5.3", "That book has not been updated to this OPR version yet, so lists using it cannot be imported.")]
    [TestCase("3.7.0", "The game's data needs an update before this list can be imported.")]
    public void AVersionMismatch_ReachesTheImportPopup_WithItsRemedy(string version, string remedy)
    {
        var failed = System.Threading.Tasks.Task.FromException(new OprVersionMismatchException("Battle Brothers", version));

        string? text = ArmyForgeScreen.ImportErrorText(failed);

        Assert.That(text, Is.EqualTo($"Army book 'Battle Brothers' is at OPR version {version}; this game supports 3.6.x. {remedy}"));
        Assert.That(text!.All(c => c < 0x80), Is.True, "ASCII");
        Assert.That(ArmyForgeScreen.ImportErrorText(System.Threading.Tasks.Task.CompletedTask), Is.Null, "a result is no error");
    }

    // #262: a unit priced below Army Forge on an older list is a stale "Replace X" pick Army Forge still bills - the
    // reconciliation explains that under the deltas instead of leaving a bare number that reads as our bug.
    [Test]
    public void TheReconciliation_ExplainsAUnitPricedBelowArmyForge_AsAStaleReplacePick()
    {
        var under = new OprForgeSessionResult { Selections = new BuilderList() };
        under.UnitPointsDeltas.Add(("Retributors", 105, 125));
        var over = new OprForgeSessionResult { Selections = new BuilderList() };
        over.UnitPointsDeltas.Add(("Retributors", 130, 125));

        string? note = ArmyForgeScreen.StaleReplaceNote(under);
        Assert.That(note, Does.Contain("stale \"Replace X\" pick").And.Contain("Re-pick that unit's upgrades on Army Forge"));
        Assert.That(note!.All(c => c < 0x80), Is.True, "ASCII");
        Assert.That(ArmyForgeScreen.StaleReplaceNote(over), Is.Null, "only the under-priced direction is that quirk");
        Assert.That(ArmyForgeScreen.StaleReplaceNote(new OprForgeSessionResult { Selections = new BuilderList() }), Is.Null);
    }

    // After the 3.6 re-bake the import preview's "RULES NOT ENFORCED" list and the launch-time "N special rules ...
    // are not implemented" line (#168) are empty for a clean 3.6 list: every roster unit of every shipped book,
    // imported the way a share link is (units + the bundled book's definitions attached), resolves every rule.
    [Test]
    public void AClean36List_ReportsNoRuleAsNotEnforced_ForEveryShippedBook()
    {
        var inert = new List<string>();
        foreach (BookFile book in AllBooks())
        {
            var list = new BuilderList { BookName = book.Name, PointsLimit = 100000 };
            foreach (RosterUnit unit in book.Units) BuilderListEditing.AddUnit(book, list, unit.Id);
            BuiltArmyFile compiled = ListCompiler.Compile(book, list);

            var imported = new ArmyListFile { Name = "import", Faction = book.Faction, GameSystem = book.GameSystem };
            imported.Units.AddRange(compiled.Units);
            OprListImporter.AttachBookDefinitions(imported, book);

            foreach (string name in OprListImporter.UnresolvedRuleNames(imported)) inert.Add($"{book.Name}: {name}");
            string? line = RuleLoadWarnings.SummarizeUnimplemented(FDG.SaveLoad.ArmyRuleAudit.Audit(compiled).Drops, "this list");
            if (line is not null) inert.Add($"{book.Name}: {line}");
        }

        Assert.That(inert, Is.Empty, string.Join("\n", inert));
    }

    // AoF Shadow Stalkers ships five spells: OPR's 3.6.0 text for Combat Possession stops mid-sentence (#439 D1). The
    // Forge says so under the spell list rather than silently listing five; the table follows the shipped books.
    [Test]
    public void ABookShortOfSixSpells_SaysWhy_UnderItsSpellList_AndNoOtherBookDoes()
    {
        foreach (BookFile book in AllBooks())
        {
            IReadOnlyList<string> notes = ArmyForgeScreen.SpellDataNotes(book);
            if (book.Spells.Count == 6) Assert.That(notes, Is.Empty, book.Name);
            else Assert.That(notes, Has.Count.EqualTo(6 - book.Spells.Count), $"{book.Name}: {book.Spells.Count} spells, no note");
        }

        BookFile stalkers = Book("AoF-ShadowStalkers");
        Assert.That(stalkers.Spells, Has.Count.EqualTo(5));
        Assert.That(stalkers.Spells.Select(sp => sp.Name), Has.None.EqualTo("Combat Possession"));
        Assert.That(ArmyForgeScreen.SpellDataNotes(stalkers).Single(),
            Is.EqualTo("Combat Possession: text missing in OPR's 3.6.0 data, so it is not listed or castable."));
    }

    // The spell list prints SpellText's canonical line per spell (#439 left OPR's prose unstored - fork F3). Every
    // shipped line is ASCII and non-empty, and the new token-drain effect reads as a sentence.
    [Test]
    public void EveryShippedSpellLine_IsAscii_AndTheTokenDrainReadsWell()
    {
        foreach (BookFile book in AllBooks())
            foreach (var spell in book.Spells)
            {
                string line = FDG.Stages.SpellText.Describe(spell);
                Assert.That(line, Is.Not.Empty, $"{book.Name}: {spell.Name}");
                Assert.That(line.All(c => c < 0x80), Is.True, $"{book.Name}: {spell.Name}: {line}");
            }

        var rune = Book("AoF-SkyCityDwarves").Spells.Single(sp => sp.Name == "Aether-Cloud Rune");
        Assert.That(FDG.Stages.SpellText.Describe(rune), Does.StartWith("Loses 2 spell tokens - "));
    }
}
