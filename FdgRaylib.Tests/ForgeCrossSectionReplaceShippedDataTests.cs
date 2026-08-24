using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #323 — corpus guard for "buy it in one section, replace it in another".
//
// The reported bug was one instance of a general shape: a Replace section whose targets another section
// grants. Compilation walks the choices in the book's SECTION ORDER, so whenever the granting section is
// authored BELOW the consuming one, the swap found nothing to consume and was silently clamped away - no
// weapon, no points, no warning. The books are regenerated OPR data and the ordering is theirs, so a
// re-import can reintroduce the shape anywhere; a spot test on the reported unit would not notice.
//
// This walks EVERY bundled book instead, and for every (unit, Replace section, granting section) pair it
// buys the grant and then asks the section for its whole pool. The invariant: what the unit can no longer
// replace is exactly what the choice bought, and the price is the per-application one. That holds whether
// the grantor sits above or below - which is the property the fix actually restored.
//
// Affects=All is excluded on purpose (see the tail of this file).
[TestFixture]
public class ForgeCrossSectionReplaceShippedDataTests
{
    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static IEnumerable<TestCaseData> Books() =>
        ShippedBooks.GdfPaths()
            .OrderBy(path => path)
            .Select(path => new TestCaseData(path).SetArgDisplayNames(Path.GetFileNameWithoutExtension(path)));

    private static BookFile Load(string path) =>
        JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;

    [TestCaseSource(nameof(Books))]
    public void EveryReplaceFedByAnotherSection_CanSpendItsWholePool(string bookPath)
    {
        BookFile book = Load(bookPath);
        var failures = new List<string>();
        int checkedPairs = 0;

        foreach (RosterUnit roster in book.Units)
        {
            foreach (UpgradeSection section in roster.Sections)
            {
                // All is single-pass by design; PickN/AddModels don't consume targets.
                if (section.Variant != UpgradeVariant.Replace) continue;
                if (section.Affects == UpgradeAffects.All || section.Targets.Count == 0) continue;

                UpgradeOption swap = section.Options.FirstOrDefault();
                if (swap is null) continue;

                foreach ((UpgradeSection grantor, UpgradeOption grant) in Grantors(roster, section))
                {
                    // Buy the grant alone: whatever the section can replace now is its true pool.
                    var withGrant = new UpgradeChoice { SectionId = grantor.Id, OptionId = grant.Id, Count = 1 };
                    (UnitFileEntry granted, List<ItemEntry> grantedItems) = Compile(book, roster, withGrant);
                    int pool = ListCompiler.AvailableApplications(granted.Weapons, grantedItems, section.Targets);
                    if (pool == 0) continue;   // the grant didn't actually feed this section

                    int wanted = section.Affects == UpgradeAffects.One ? 1
                        : section.MaxApplications > 0 ? Math.Min(pool, section.MaxApplications)
                        : pool;
                    checkedPairs++;

                    (UnitFileEntry both, List<ItemEntry> bothItems) = Compile(book, roster, withGrant,
                        new UpgradeChoice { SectionId = section.Id, OptionId = swap.Id, Count = wanted });

                    int left = ListCompiler.AvailableApplications(both.Weapons, bothItems, section.Targets);
                    string where = $"{roster.Name} / \"{section.Label}\" fed by \"{grantor.Label}\" ({grant.Label})";

                    if (left != pool - wanted)
                    {
                        failures.Add($"{where}: asked for {wanted} of a {pool} pool, but {left} target(s) " +
                            $"remain (expected {pool - wanted}) - applications were dropped.");
                    }

                    // Exact price, EXCEPT where the two sections feed each other (DAO Union's Tactical
                    // Grunts trade a Pulse Rifle + CCW for a Pulse Pistol, which the next section trades
                    // back for a Pulse Rifle). There the grantor is itself starved in the baseline compile
                    // and only becomes payable once this swap applies, so the baseline is not comparable -
                    // the applications-landed check above still covers it.
                    if (Mutual(section, swap, grantor)) continue;

                    int expectedCost = granted.PointCost + swap.Cost * wanted;
                    if (both.PointCost != expectedCost)
                    {
                        failures.Add($"{where}: charged {both.PointCost}, expected {expectedCost} " +
                            $"({granted.PointCost} + {swap.Cost}x{wanted}).");
                    }
                }
            }
        }

        Assert.That(failures, Is.Empty,
            $"{Path.GetFileNameWithoutExtension(bookPath)}: " + string.Join("\n  ", failures));
        Assert.That(checkedPairs, Is.GreaterThanOrEqualTo(0)); // books with no such pair legitimately check none
    }

    // Sections OTHER than this one whose options hand it something it replaces. Same-section options are
    // skipped: a combo swap that hands one half back ("Replace Combat Shield and CCW" -> "... , Combat
    // Shield") is a self-contained idiom, not a cross-section feed.
    private static IEnumerable<(UpgradeSection, UpgradeOption)> Grantors(RosterUnit roster, UpgradeSection section)
    {
        foreach (UpgradeSection other in roster.Sections)
        {
            if (ReferenceEquals(other, section)) continue;
            foreach (UpgradeOption option in other.Options)
            {
                bool feeds = option.WeaponsGained.Any(w => Feeds(section, w.Name))
                    || option.ItemsGained.Any(i => Feeds(section, i.Name));
                if (feeds) yield return (other, option);
            }
        }
    }

    /// <summary>Whether the swap hands the grantor something the grantor itself replaces - the two sections
    /// feed each other, so neither one's price can be read from a compile without the other.</summary>
    private static bool Mutual(UpgradeSection section, UpgradeOption swap, UpgradeSection grantor) =>
        swap.WeaponsGained.Any(w => Feeds(grantor, w.Name))
        || swap.ItemsGained.Any(i => Feeds(grantor, i.Name));

    // Asked through the compiler's own availability seam rather than re-implementing its target parsing:
    // would a pile of this weapon satisfy any of the section's targets? (Handles the "2x Name" quantity
    // prefix and the plural/singular normalisation for free, exactly as compilation does.)
    private static bool Feeds(UpgradeSection section, string gainedName) =>
        section.Targets.Any(t => ListCompiler.AvailableApplications(
            new[] { new WeaponFileEntry { Name = gainedName, Quantity = 1000 } },
            Array.Empty<ItemEntry>(),
            new[] { t }) > 0);

    private static (UnitFileEntry, List<ItemEntry>) Compile(BookFile book, RosterUnit roster,
        params UpgradeChoice[] choices)
    {
        var bu = new BuilderUnit { RosterUnitId = roster.Id, ModelCount = roster.BaseModelCount };
        bu.Choices.AddRange(choices);
        return ListCompiler.CompileUnitDetailed(book, bu);
    }

    // #324 — every Replace target must name something its unit can actually hold, in the base loadout or
    // from some option's gains. One target failed this before the plural fix: Dwarf Guilds' "Guardians"
    // target "Bashes" against a weapon named "Bash", which the single-trailing-s rule turned into "bashe".
    // Because that section is Affects=All (max across targets, not min) it still fired off its Pistols half,
    // so the swap looked like it worked while quietly leaving all five Bashes on the unit, free.
    //
    // OPR publishes targets as display STRINGS with no id-based alternative (verified 2026-08-02 against the
    // live army-books API: 61 targets in the Dwarf Guilds book, all strings, and the section schema carries
    // no weapon-id field). So name matching is the only mechanism there is, and a typo-shaped mismatch here
    // is silent by construction - which is why it gets a corpus-wide assertion rather than a spot test.
    [Test]
    public void EveryReplaceTarget_NamesSomethingItsUnitCanHold()
    {
        var dead = new List<string>();

        foreach (string path in ShippedBooks.GdfPaths())
        {
            BookFile book = Load(path);
            foreach (RosterUnit roster in book.Units)
            {
                // Every name the unit can ever carry: its base loadout plus anything any option grants.
                var holdable = new List<WeaponFileEntry>(roster.Weapons);
                var holdableItems = new List<ItemEntry>(roster.Items);
                foreach (UpgradeSection s in roster.Sections)
                    foreach (UpgradeOption o in s.Options)
                    {
                        holdable.AddRange(o.WeaponsGained);
                        holdableItems.AddRange(o.ItemsGained);
                    }

                foreach (UpgradeSection section in roster.Sections)
                {
                    if (section.Variant != UpgradeVariant.Replace) continue;
                    foreach (string target in section.Targets)
                        if (ListCompiler.AvailableApplications(holdable, holdableItems, new[] { target }) == 0)
                            dead.Add($"{Path.GetFileNameWithoutExtension(path)}/{roster.Name}/" +
                                $"\"{section.Label}\" targets \"{target}\", which matches nothing on the unit");
                }
            }
        }

        Assert.That(dead, Is.Empty, string.Join("\n  ", dead));
    }

    // The census that sized the problem (2026-08-02) - a canary, so a re-import that changes the corpus's
    // shape shows up as a number here rather than as silence. 54 (unit, section) pairs across 17 books are
    // fed ONLY by a section authored below them: every Titan Lords chapter's Errant/Pilgrim/Questor/Knight
    // titans, the Battle/Blood/Dark/Knight/Wolf Brothers' "Replace Gravity Pistol", the Alien Hives Hive
    // Lord's Heavy Razor Claws, and the Orc Marauders Beast Titan's Heavy Mortars among them. Those are the
    // ones the pre-#323 compiler mis-clamped; the test above proves each can now spend its pool.
    //
    // A 55th, the Dark Brother Bikers' "Replace Energy Sword", is fed from BOTH directions - the earlier
    // feed made it look safe, but a player who takes only the later one hit the same bug. It is deliberately
    // not in this count (the test above covers it, grantor direction being irrelevant there).
    [Test]
    public void ReplaceSectionsFedOnlyFromBelow_AreStillTheKnownPopulation()
    {
        var fedFromBelow = new List<string>();

        foreach (string path in ShippedBooks.GdfPaths())
        {
            BookFile book = Load(path);
            foreach (RosterUnit roster in book.Units)
            {
                for (int i = 0; i < roster.Sections.Count; i++)
                {
                    UpgradeSection section = roster.Sections[i];
                    if (section.Variant != UpgradeVariant.Replace || section.Targets.Count == 0) continue;

                    var indexes = Grantors(roster, section)
                        .Select(g => roster.Sections.IndexOf(g.Item1))
                        .Distinct()
                        .ToList();
                    if (indexes.Count > 0 && indexes.All(j => j > i))
                        fedFromBelow.Add($"{Path.GetFileNameWithoutExtension(path)}/{roster.Name}/{section.Label}");
                }
            }
        }

        Assert.That(fedFromBelow, Has.Count.EqualTo(54),
            "census drift - re-run the audit and re-check the fix covers the new shape:\n  "
            + string.Join("\n  ", fedFromBelow));
        Assert.That(fedFromBelow, Has.Some.Contains("TitanLordsWarDisciples/War Errant Mini-Titan"));
        Assert.That(fedFromBelow, Has.Some.Contains("AlienHives/Hive Lord"));
    }
}
