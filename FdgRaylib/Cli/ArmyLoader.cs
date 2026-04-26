using FDG.SaveLoad;
using Newtonsoft.Json;

namespace FdgRaylib.Cli;

public static class ArmyLoader
{
    private static readonly JsonSerializerSettings _settings = new JsonSerializerSettings
    {
        TypeNameHandling = TypeNameHandling.Auto,
    };

    public static ArmyListFile PromptForArmy(string playerLabel)
    {
        Console.WriteLine($"{playerLabel} — choose army:");
        Console.WriteLine("  [1] Load from .fdgarmy file");
        Console.WriteLine("  [2] Use built-in test army");

        while (true)
        {
            Console.Write("Choice: ");
            string? input = Console.ReadLine()?.Trim();
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
                var army = JsonConvert.DeserializeObject<ArmyListFile>(json, _settings);
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
                    Weapons = new List<WeaponFileEntry>
                    {
                        new WeaponFileEntry { Name = "Rifle", RangeInches = 24, Attacks = 1 }
                    }
                },
                new UnitFileEntry
                {
                    Name = "Heavy Gunners",
                    ModelCount = 3,
                    Quality = 4,
                    Defense = 4,
                    PointCost = 120,
                    Weapons = new List<WeaponFileEntry>
                    {
                        new WeaponFileEntry { Name = "Heavy Rifle", RangeInches = 36, Attacks = 1 }
                    }
                }
            }
        };
    }
}
