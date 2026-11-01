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

// #441 F6 (#438 D4 hand-off) - how a 3.6 mount/crew item and its bundled weapons show on the Forge card. The
// weapons stay in the card's weapon table, tagged with the item's name ("Explosive Spear (Beast Rider)"), and the
// bare item line goes when the item has no rules of its own; an item with rules keeps its line. Drives the
// ImGui-free row model (ForgeUnitDetail.GearRows) against the SHIPPED books; the table itself is hand-verified.
[TestFixture]
public class ForgeItemWeaponRowsTests
{
    private static BookFile Book(string fileName) =>
        JsonSerializer.Deserialize<BookFile>(
            File.ReadAllText(Path.Combine(ShippedBooks.Directory, fileName + BookFile.EXTENSION_WITH_PERIOD)),
            RuleJson.Options)!;

    // `optionId`: one upgrade option to take, by id (labels repeat - the Hunt Master has two "Upgrade with one").
    private static (UnitFileEntry Unit, List<ItemEntry> Items) Compiled(BookFile book, string unitName,
        string? optionId = null)
    {
        RosterUnit roster = book.Units.Single(u => u.Name == unitName);
        var screen = new ArmyForgeScreen(book);
        screen.AddToList(roster.Id);
        BuilderUnit bu = screen.List.Units[0];
        if (optionId is not null)
        {
            UpgradeSection section = roster.Sections.Single(s => s.Options.Any(o => o.Id == optionId));
            ArmyForgeScreen.SetChoice(bu, section, optionId, 1);
        }
        return ListCompiler.CompileUnitDetailed(book, bu);
    }

    // Jackals' Charger Beast: its only weapon is the Beast Rider's Explosive Spear (3.6 moved it inside the item,
    // #438 D4). The card shows the spear with the rider named, and no "1x Beast Rider" line - the item has no rules,
    // so the line would say nothing the tag does not.
    [Test]
    public void ChargerBeast_TheRidersSpear_IsTaggedWithTheRider_AndTheBareItemLineGoes()
    {
        (UnitFileEntry unit, List<ItemEntry> items) = Compiled(Book("Jackals"), "Charger Beast");
        Assert.That(items.Select(i => i.Name), Is.EqualTo(new[] { "Beast Rider" }), "the compiler keeps the item");
        Assert.That(items[0].Rules, Is.Empty);

        (IReadOnlyList<ForgeUnitDetail.WeaponRow> rows, IReadOnlyList<ItemEntry> lines) =
            ForgeUnitDetail.GearRows(unit, items);

        Assert.That(rows.Select(r => r.Cell), Is.EqualTo(new[] { "Explosive Spear (Beast Rider)" }));
        Assert.That(rows[0].SourceItem, Is.EqualTo("Beast Rider"));
        Assert.That(rows[0].Weapon, Is.SameAs(unit.Weapons[0]), "the stat cells still read off the compiled weapon");
        Assert.That(lines, Is.Empty, "no bare '1x Beast Rider' line");
    }

    // Jackals' Scavenger Beast: its own Stomp beside the Beast Gunner's Fumigator. Only the gunner's weapon is
    // tagged; the unit's own weapon has no tag.
    [Test]
    public void ScavengerBeast_OnlyTheGunnersWeapon_CarriesTheTag()
    {
        (UnitFileEntry unit, List<ItemEntry> items) = Compiled(Book("Jackals"), "Scavenger Beast");

        (IReadOnlyList<ForgeUnitDetail.WeaponRow> rows, IReadOnlyList<ItemEntry> lines) =
            ForgeUnitDetail.GearRows(unit, items);

        Assert.That(rows.Select(r => r.Cell), Is.EqualTo(new[] { "Stomp", "Fumigator (Beast Gunner)" }));
        Assert.That(rows[0].SourceItem, Is.Null);
        Assert.That(lines, Is.Empty);
    }

    // An item WITH rules keeps its line, its weapons tagged all the same: AoF Beastmen's Waheni Hunt Master on a
    // Light Chariot (an upgrade-gained item: Tusks + Armor(4), Fast, Impact(4), Tough(6)).
    [Test]
    public void HuntMaster_OnALightChariot_TheChariotKeepsItsRuleLine_AndItsTusksAreTagged()
    {
        BookFile book = Book("AoF-Beastmen");
        UpgradeOption chariot = book.Units.Single(u => u.Name == "Waheni Hunt Master").Sections
            .SelectMany(s => s.Options).Single(o => o.Label.StartsWith("Light Chariot"));
        (UnitFileEntry unit, List<ItemEntry> items) = Compiled(book, "Waheni Hunt Master", chariot.Id);

        (IReadOnlyList<ForgeUnitDetail.WeaponRow> rows, IReadOnlyList<ItemEntry> lines) =
            ForgeUnitDetail.GearRows(unit, items);

        Assert.That(rows.Select(r => r.Cell), Is.EqualTo(new[] { "Dual Hand Weapons", "Tusks (Light Chariot)" }));
        Assert.That(lines.Select(l => RuleTextFlow.Flatten(RuleTextFlow.ItemLine(l))),
            Is.EqualTo(new[] { "1x Light Chariot (Armor(4), Fast, Impact(4), Tough(6))" }),
            "the line is what names the chariot's rules, so it stays");
    }

    // Every shipped item that bundles weapons (the 3.6 mounts and crews, base loadout or upgrade-gained) tags its
    // weapons as compiled, and loses its line exactly when it has no rules. No item vanishes from a card.
    [Test]
    public void EveryShippedItemWithWeapons_TagsThem_AndKeepsItsLineExactlyWhenItHasRules()
    {
        int checkedItems = 0;
        foreach (string path in Directory.EnumerateFiles(ShippedBooks.Directory, "*" + BookFile.EXTENSION_WITH_PERIOD))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            foreach (RosterUnit roster in book.Units)
            {
                var picks = new List<string?> { null };
                picks.AddRange(roster.Sections.SelectMany(s => s.Options)
                    .Where(o => o.ItemsGained.Any(i => i.Weapons is { Count: > 0 }))
                    .Select(o => o.Id));

                foreach (string? pick in picks)
                {
                    (UnitFileEntry unit, List<ItemEntry> items) = Compiled(book, roster.Name, pick);
                    if (!items.Any(i => i.Weapons is { Count: > 0 })) continue;

                    (IReadOnlyList<ForgeUnitDetail.WeaponRow> rows, IReadOnlyList<ItemEntry> lines) =
                        ForgeUnitDetail.GearRows(unit, items);
                    foreach (ItemEntry item in items.Where(i => i.Weapons is { Count: > 0 }))
                    {
                        checkedItems++;
                        string where = $"{book.Name} / {roster.Name} / {item.Name}";
                        Assert.That(rows.Any(r => r.SourceItem == item.Name), Is.True, $"{where}: a weapon carries the tag");
                        Assert.That(lines.Contains(item), Is.EqualTo(item.Rules.Count > 0), $"{where}: line iff rules");
                    }
                    Assert.That(rows.Sum(r => r.Quantity), Is.EqualTo(unit.Weapons.Sum(w => w.Quantity)),
                        $"{book.Name} / {roster.Name}: the rows account for every compiled copy");
                }
            }
        }
        Assert.That(checkedItems, Is.GreaterThan(12), "the #438 D4 sites and the upgrade-gained mounts");
    }

    // The split follows the compiler's merge key, not the name: a unit's own Sharp Claws and its mount's identical
    // Sharp Claws merge into one compiled entry and come apart as the untagged copy then the mount's; a same-named
    // weapon with a different profile is its own row and takes no tag.
    [Test]
    public void AnItemWeaponMergedWithTheUnitsOwn_SplitsOnTheProfile_UntaggedFirst()
    {
        var book = new BookFile
        {
            Name = "Synthetic",
            Units =
            {
                new RosterUnit
                {
                    Id = "u", Name = "Rider", Quality = 4, Defense = 4,
                    Weapons =
                    {
                        new WeaponFileEntry { Name = "Sharp Claws", Attacks = 3, Quantity = 2 }, // own + the mount's, merged
                        new WeaponFileEntry { Name = "Sharp Claws", Attacks = 4 },
                    },
                    Items =
                    {
                        new ItemEntry
                        {
                            Name = "Mount",
                            Weapons = new List<WeaponFileEntry> { new() { Name = "Sharp Claws", Attacks = 3 } },
                        },
                    },
                },
            },
        };
        var screen = new ArmyForgeScreen(book);
        screen.AddToList("u");
        (UnitFileEntry unit, List<ItemEntry> items) = ListCompiler.CompileUnitDetailed(book, screen.List.Units[0]);

        (IReadOnlyList<ForgeUnitDetail.WeaponRow> rows, IReadOnlyList<ItemEntry> lines) =
            ForgeUnitDetail.GearRows(unit, items);

        Assert.That(rows.Select(r => (r.Cell, r.Quantity, r.Weapon.Attacks)), Is.EqualTo(new[]
        {
            ("Sharp Claws", 1, 3), ("Sharp Claws (Mount)", 1, 3), ("Sharp Claws", 1, 4),
        }));
        Assert.That(lines, Is.Empty);
    }

    // A compiled list can hold the same profile twice un-merged (a roster's own entries are cloned as authored). One
    // mount copy tags ONE of them, never both.
    [Test]
    public void OneItemCopy_TagsOneRow_WhenTheProfileAppearsTwiceUnmerged()
    {
        var unit = new UnitFileEntry
        {
            Name = "Rider",
            Weapons =
            {
                new WeaponFileEntry { Name = "Sharp Claws", Attacks = 3 },
                new WeaponFileEntry { Name = "Sharp Claws", Attacks = 3 },
            },
        };
        var items = new List<ItemEntry>
        {
            new() { Name = "Mount", Weapons = new List<WeaponFileEntry> { new() { Name = "Sharp Claws", Attacks = 3 } } },
        };

        (IReadOnlyList<ForgeUnitDetail.WeaponRow> rows, _) = ForgeUnitDetail.GearRows(unit, items);

        Assert.That(rows.Select(r => r.Cell), Is.EqualTo(new[] { "Sharp Claws (Mount)", "Sharp Claws" }));
    }

    // An item whose weapons are nowhere in the compiled list (a Replace took the weapon by name and left the mount)
    // keeps its bare line - nothing the unit carries drops off the card. Two copies of one item tag ONE row.
    [Test]
    public void AnItemWhoseWeaponIsGone_KeepsItsLine_AndTwoCopiesTagOneRow()
    {
        var unit = new UnitFileEntry
        {
            Name = "Crew",
            Weapons = { new WeaponFileEntry { Name = "Fumigator", RangeInches = 12, Attacks = 1, Quantity = 3 } },
        };
        var items = new List<ItemEntry>
        {
            new() { Name = "Beast Gunner", Quantity = 2,
                Weapons = new List<WeaponFileEntry> { new() { Name = "Fumigator", RangeInches = 12, Attacks = 1 } } },
            new() { Name = "Beast Rider",
                Weapons = new List<WeaponFileEntry> { new() { Name = "Explosive Spear", Attacks = 2 } } },
        };

        (IReadOnlyList<ForgeUnitDetail.WeaponRow> rows, IReadOnlyList<ItemEntry> lines) =
            ForgeUnitDetail.GearRows(unit, items);

        Assert.That(rows.Select(r => r.Cell), Is.EqualTo(new[] { "Fumigator", "2x Fumigator (Beast Gunner)" }));
        Assert.That(lines.Select(l => l.Name), Is.EqualTo(new[] { "Beast Rider" }), "its spear is gone; the line stays");
    }
}
