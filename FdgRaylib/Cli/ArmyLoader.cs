using System.Text.Json;
using FDG.Rules.Serialization;
using FDG.SaveLoad;

namespace FdgRaylib.Cli;

public static class ArmyLoader
{
    public static ArmyListFile PromptForArmy(string playerLabel)
    {
        Console.WriteLine($"{playerLabel} - choose army:");
        Console.WriteLine("  [1] Load from .fdgarmy file");
        Console.WriteLine("  [2] Use built-in test army");

        while (true)
        {
            Console.Write("Choice: ");
            string? input = Console.ReadLine()?.Trim();
            if (input == null)
            {
                Console.WriteLine("(EOF - using built-in test army)");
                return MakeTestArmy(playerLabel);
            }
            if (input == "1") return LoadFromFile();
            if (input == "2") return MakeTestArmy(playerLabel);
            Console.WriteLine("Enter 1 or 2.");
        }
    }

    private static ArmyListFile LoadFromFile()
    {
        while (true)
        {
            Console.Write("Path to .fdgarmy file: ");
            string? path = Console.ReadLine()?.Trim().Trim('"');

            if (string.IsNullOrEmpty(path))
            {
                Console.WriteLine("No path entered.");
                continue;
            }

            if (!File.Exists(path))
            {
                Console.WriteLine($"File not found: {path}");
                continue;
            }

            try
            {
                string json = File.ReadAllText(path);
                var army = JsonSerializer.Deserialize<ArmyListFile>(json, RuleJson.Options);
                if (army == null) throw new InvalidOperationException("Deserialized army was null.");
                Console.WriteLine($"Loaded '{army.Name}' ({army.TotalPoints} pts, {army.Units.Count} units).");
                return army;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to load file: {ex.Message}");
            }
        }
    }

    private static ArmyListFile MakeTestArmy(string playerLabel)
    {
        return new ArmyListFile
        {
            Name = $"{playerLabel}'s Test Army",
            Faction = "Test",
            PointsLimit = 500,
            Units = new List<UnitFileEntry>
            {
                new UnitFileEntry
                {
                    Name = "Warriors",
                    ModelCount = 5,
                    Quality = 4,
                    Defense = 4,
                    PointCost = 150,
                    SpecialRules = new List<SpecialRuleEntry>
                    {
                        new SpecialRuleEntry_Core("Stealth"),
                        new SpecialRuleEntry_Core("Very Fast"),
                        new SpecialRuleEntry_Core("Vanguard"),
                        new SpecialRuleEntry_Core("Thrust"),
                        new SpecialRuleEntry_CoreNumeric("Impact", 2),
                        new SpecialRuleEntry_Core("Furious"),
                        new SpecialRuleEntry_Core("Strafing"),
                    },
                    Weapons = new List<WeaponFileEntry>
                    {
                        new WeaponFileEntry { Name = "Rifle", RangeInches = 24, Attacks = 1 },
                        new WeaponFileEntry { Name = "Blade", RangeInches = 0, Attacks = 2 }
                    }
                },
                new UnitFileEntry
                {
                    Name = "Heavy Gunners",
                    ModelCount = 3,
                    Quality = 4,
                    Defense = 4,
                    PointCost = 120,
                    SpecialRules = new List<SpecialRuleEntry>
                    {
                        new SpecialRuleEntry_Core("Scout"),
                        new SpecialRuleEntry_Core("Martial Prowess"),
                    },
                    Weapons = new List<WeaponFileEntry>
                    {
                        //#027: Surge/Blast/Counter are weapon rules — they live on the weapon entries now.
                        new WeaponFileEntry
                        {
                            Name = "Heavy Rifle", RangeInches = 36, Attacks = 1,
                            SpecialRules = new List<SpecialRuleEntry>
                            {
                                new SpecialRuleEntry_Core("Surge"),
                                new SpecialRuleEntry_CoreNumeric("Blast", 3),
                            }
                        },
                        new WeaponFileEntry
                        {
                            Name = "Fists", RangeInches = 0, Attacks = 1,
                            SpecialRules = new List<SpecialRuleEntry>
                            {
                                new SpecialRuleEntry_Core("Counter"),
                            }
                        }
                    }
                },
                new UnitFileEntry
                {
                    Name = "Infiltrators",
                    ModelCount = 2,
                    Quality = 4,
                    Defense = 4,
                    PointCost = 80,
                    SpecialRules = new List<SpecialRuleEntry>
                    {
                        new SpecialRuleEntry_Core("Ambush"),
                    },
                    Weapons = new List<WeaponFileEntry>
                    {
                        //#027: Takedown is a weapon rule — it lives on the weapon entry now.
                        new WeaponFileEntry
                        {
                            Name = "Rifle", RangeInches = 24, Attacks = 1,
                            SpecialRules = new List<SpecialRuleEntry>
                            {
                                new SpecialRuleEntry_Core("Takedown"),
                            }
                        }
                    }
                }
            }
        };
    }
}
