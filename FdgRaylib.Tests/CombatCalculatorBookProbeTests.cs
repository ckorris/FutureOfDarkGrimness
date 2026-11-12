using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Calculator;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using FdgRaylib.Rendering.CombatCalc;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// #397: point the Combat Calculator at EVERY unit in every bundled book and check it produces an
/// answer. The calculator builds a real game world and runs the real combat stages, so this is the
/// guard that the sandbox survives the whole shipped rule corpus - both game systems, every faction
/// rule, every weapon profile - and not just the tidy fixtures the unit tests use.
///
/// <para>
/// A green unit suite can still sit on a calculator that throws the moment it meets a real book
/// (reference `reference_headless_testing`); this is what stops that.
/// </para>
/// </summary>
[TestFixture]
public class CombatCalculatorBookProbeTests
{
    [Test]
    public void EveryUnitInEveryBundledBook_CanBeSimulated()
    {
        if (!Directory.Exists(ShippedBooks.Directory))
        {
            Assert.Ignore("No bundled books in this build.");
        }

        ArmyListFile target = Target();
        var failures = new List<string>();
        int probed = 0;

        foreach (string path in Directory
                     .EnumerateFiles(ShippedBooks.Directory, "*" + BookFile.EXTENSION_WITH_PERIOD)
                     .OrderBy(path => path))
        {
            BookFile? book;
            try { book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options); }
            catch { continue; } // a malformed book is a different item's problem

            if (book is null) continue;

            foreach (RosterUnit roster in book.Units)
            {
                var side = new CalculatorSide();
                side.SetUnit(book, roster.Id);
                ArmyListFile army = side.Compile();

                foreach (ECombatMode mode in new[] { ECombatMode.Shooting, ECombatMode.Melee })
                {
                    probed++;
                    try
                    {
                        CombatCalculator.Run(army, target, new CombatSituation(
                            Mode: mode,
                            DistanceInches: mode == ECombatMode.Shooting ? 12f : 0f,
                            AttackerCharging: mode == ECombatMode.Melee));
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{book.Name} / {roster.Name} ({mode}): {ex.GetType().Name}: {ex.Message}");
                    }
                }
            }
        }

        Assert.That(probed, Is.GreaterThan(100), "the bundled books should have produced plenty to probe");
        Assert.That(failures, Is.Empty,
            $"{failures.Count} of {probed} simulations threw:\n" + string.Join("\n", failures.Take(20)));
    }

    /// <summary>A plain, sturdy target: ten average models with nothing clever about them.</summary>
    private static ArmyListFile Target() => new()
    {
        Name = "Probe Target",
        Faction = "Probe",
        Units =
        {
            new UnitFileEntry
            {
                Name = "Targets", ModelCount = 10, Quality = 4, Defense = 4, PointCost = 100,
                Weapons = { new WeaponFileEntry { Name = "Rifle", Quantity = 10, RangeInches = 24, Attacks = 1 } },
            },
        },
    };
}
