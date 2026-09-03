using System.Text.Json;
using FDG.Rules.Serialization;
using FDG.SaveLoad;

namespace FdgLab;

/// <summary>
/// Loads armies for benchmark slots. A spec is either a path to a <c>.fdgarmy</c> file or the literal
/// <c>builtin</c> — the same two-unit-ish test army the headless CLI falls back to on EOF, so the lab
/// always has a zero-dependency army that is known to play headless.
/// </summary>
public static class Armies
{
    public const string BuiltinSpec = "builtin";
    public const string BuiltinBasicSpec = "builtin-basic";

    public static SlotSpec LoadSlot(string spec)
    {
        if (spec.Equals(BuiltinSpec, StringComparison.OrdinalIgnoreCase))
            return new SlotSpec("Builtin", Builtin("Builtin"));

        // The builtin army minus its Ambush unit: reserve arrival currently hits a timing race in the
        // engine (#198), so this is the army for verifying the HARNESS's own determinism. Keep using it
        // for that gate even after #198 is fixed - it isolates harness bugs from engine bugs.
        if (spec.Equals(BuiltinBasicSpec, StringComparison.OrdinalIgnoreCase))
        {
            ArmyListFile basic = Builtin("BuiltinBasic");
            basic.Units.RemoveAll(u => u.Name == "Infiltrators");
            return new SlotSpec("BuiltinBasic", basic);
        }

        string json = File.ReadAllText(spec);
        ArmyListFile army = JsonSerializer.Deserialize<ArmyListFile>(json, RuleJson.Options)
            ?? throw new InvalidOperationException($"Army deserialized to null: {spec}");
        string label = string.IsNullOrWhiteSpace(army.Name)
            ? Path.GetFileNameWithoutExtension(spec)
            : army.Name;
        return new SlotSpec(label, army);
    }

    /// <summary>
    /// Lab-side copy of the CLI's EOF fallback army (`FdgRaylib.Cli.ArmyLoader.MakeTestArmy`) — kept in
    /// sync by eye, duplicated because FdgLab does not reference FdgRaylib. Exercises a broad rule
    /// spread: Stealth/Very Fast/Vanguard/Thrust/Impact/Furious/Strafing, Scout/Martial Prowess,
    /// Surge/Blast/Counter/Takedown weapon rules, and an Ambush unit.
    /// </summary>
    public static ArmyListFile Builtin(string name) => new ArmyListFile
    {
        Name = name,
        Faction = "Test",
        PointsLimit = 500,
        Units = new List<UnitFileEntry>
        {
            new UnitFileEntry
            {
                Name = "Warriors", ModelCount = 5, Quality = 4, Defense = 4, PointCost = 150,
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
                    new WeaponFileEntry { Name = "Blade", RangeInches = 0, Attacks = 2 },
                },
            },
            new UnitFileEntry
            {
                Name = "Heavy Gunners", ModelCount = 3, Quality = 4, Defense = 4, PointCost = 120,
                SpecialRules = new List<SpecialRuleEntry>
                {
                    new SpecialRuleEntry_Core("Scout"),
                    new SpecialRuleEntry_Core("Martial Prowess"),
                },
                Weapons = new List<WeaponFileEntry>
                {
                    new WeaponFileEntry
                    {
                        Name = "Heavy Rifle", RangeInches = 36, Attacks = 1,
                        SpecialRules = new List<SpecialRuleEntry>
                        {
                            new SpecialRuleEntry_Core("Surge"),
                            new SpecialRuleEntry_CoreNumeric("Blast", 3),
                        },
                    },
                    new WeaponFileEntry
                    {
                        Name = "Fists", RangeInches = 0, Attacks = 1,
                        SpecialRules = new List<SpecialRuleEntry> { new SpecialRuleEntry_Core("Counter") },
                    },
                },
            },
            new UnitFileEntry
            {
                Name = "Infiltrators", ModelCount = 2, Quality = 4, Defense = 4, PointCost = 80,
                SpecialRules = new List<SpecialRuleEntry> { new SpecialRuleEntry_Core("Ambush") },
                Weapons = new List<WeaponFileEntry>
                {
                    new WeaponFileEntry
                    {
                        Name = "Rifle", RangeInches = 24, Attacks = 1,
                        SpecialRules = new List<SpecialRuleEntry> { new SpecialRuleEntry_Core("Takedown") },
                    },
                },
            },
        },
    };
}
