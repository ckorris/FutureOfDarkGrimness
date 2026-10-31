using System.Numerics;

namespace FdgRaylib.Rendering.MenuBackground;

/// <summary>One army that can appear on the main menu's war table: its on-table abbreviation (the unit
/// label prefix), its full name, and its hologram colour (0..1 RGB).</summary>
internal sealed record MenuArmy(string Abbr, string Name, Vector3 Rgb);

/// <summary>
/// The armies the main menu's war table (#448) pits against each other, and which pairs may meet.
/// Main Grimdark Future armies only (no Brothers/Prime variants, no Disciples splits); abbreviations
/// and colours chosen by the owner on 2026-10-30.
///
/// <para>Pairing is hard-coded, not computed: two armies sharing any <see cref="ClashGroups"/> entry
/// look too alike to tell apart on the table, so they are never drawn together, and no army faces
/// itself. The source of truth is <c>docs/menu-background/army_pairings_check.py</c>, which also checks
/// that every pair the groups ALLOW is clearly distinguishable (delta-E) - edit both together; a test
/// fails if they drift apart.</para>
/// </summary>
internal static class MenuArmies
{
    private static MenuArmy A(string abbr, string name, string hex) => new(abbr, name, new Vector3(
        Convert.ToInt32(hex.Substring(1, 2), 16) / 255f,
        Convert.ToInt32(hex.Substring(3, 2), 16) / 255f,
        Convert.ToInt32(hex.Substring(5, 2), 16) / 255f));

    public static IReadOnlyList<MenuArmy> All { get; } = new[]
    {
        A("ALIEN", "Alien Hives", "#9C6BFF"),
        A("BAT-BR", "Battle Brothers", "#4F6BFF"),
        A("B-SIST", "Blessed Sisters", "#FF4D6D"),
        A("C-BR", "Custodian Brothers", "#F2E4BC"),
        A("DAO", "DAO Union", "#FF9F40"),
        A("D-ELF", "Dark Elf Raiders", "#E04DFF"),
        A("D-GUILD", "Dwarf Guilds", "#D09050"),
        A("ELF-J", "Elven Jesters", "#FF85C8"),
        A("E-DYN", "Eternal Dynasty", "#FFEB3B"),
        A("GOB", "Goblin Reclaimers", "#A6F58C"),
        A("HAVOC", "Havoc Brothers", "#E8352E"),
        A("H-ELF", "High Elf Fleets", "#8EC9FF"),
        A("HDF", "Human Defense Force", "#8CC63F"),
        A("H-INQ", "Human Inquisition", "#E6EAF2"),
        A("INFECT", "Infected Colonies", "#D4E157"),
        A("JAK", "Jackals", "#D8A878"),
        A("M-CULT", "Machine Cults", "#E0612A"),
        A("ORK", "Orc Marauders", "#3DDC5A"),
        A("RAT", "Ratmen Clans", "#FFB000"),
        A("REB-G", "Rebel Guerrillas", "#B5A66A"),
        A("ROBOT", "Robot Legions", "#8C4DFF"),
        A("S-STAR", "Saurian Starhost", "#2EE6A6"),
        A("S-CULT", "Soul-Snatcher Cults", "#C58CFF"),
        A("TITAN", "Titan Lords", "#FFD447"),
        A("DEMON", "Wormhole Daemons", "#FF2E88"),
    };

    /// <summary>Colour families: two armies in the same group are never on the table together. Groups
    /// overlap on purpose (a colour can sit between two families).</summary>
    public static IReadOnlyList<(string Name, IReadOnlyList<string> Members)> ClashGroups { get; } = new (string, IReadOnlyList<string>)[]
    {
        ("reds", new[] { "HAVOC", "B-SIST", "DEMON", "M-CULT" }),
        ("pinks/magentas", new[] { "DEMON", "ELF-J", "D-ELF", "B-SIST" }),
        ("purples/blues", new[] { "ALIEN", "ROBOT", "S-CULT", "BAT-BR" }),
        ("violets", new[] { "D-ELF", "S-CULT", "ALIEN", "ELF-J", "ROBOT" }),
        ("pale/whites", new[] { "H-INQ", "C-BR", "H-ELF" }),
        ("light blues", new[] { "H-ELF", "BAT-BR" }),
        ("yellows", new[] { "E-DYN", "TITAN", "INFECT", "RAT" }),
        ("oranges", new[] { "DAO", "RAT", "M-CULT", "D-GUILD", "JAK" }),
        ("golds", new[] { "TITAN", "DAO", "RAT", "D-GUILD" }),
        ("tans", new[] { "D-GUILD", "JAK", "REB-G", "C-BR" }),
        ("greens", new[] { "ORK", "HDF", "GOB", "INFECT", "S-STAR" }),
        ("drab greens", new[] { "HDF", "REB-G", "INFECT" }),
    };

    /// <summary>True when the two armies may share the table: different armies, no common clash group.</summary>
    public static bool CanFace(MenuArmy a, MenuArmy b) =>
        a.Abbr != b.Abbr && !ClashGroups.Any(g => g.Members.Contains(a.Abbr) && g.Members.Contains(b.Abbr));

    /// <summary>Every allowed pairing, each unordered pair once.</summary>
    public static IReadOnlyList<(MenuArmy A, MenuArmy B)> AllowedPairs { get; } = BuildAllowedPairs();

    private static IReadOnlyList<(MenuArmy, MenuArmy)> BuildAllowedPairs()
    {
        var pairs = new List<(MenuArmy, MenuArmy)>();
        for (int i = 0; i < All.Count; i++)
        for (int j = i + 1; j < All.Count; j++)
            if (CanFace(All[i], All[j])) pairs.Add((All[i], All[j]));
        return pairs;
    }

    /// <summary>A random allowed pairing, uniformly over pairs, in random near/far order.</summary>
    public static (MenuArmy Near, MenuArmy Far) PickPair(Random rng)
    {
        var (a, b) = AllowedPairs[rng.Next(AllowedPairs.Count)];
        return rng.Next(2) == 0 ? (a, b) : (b, a);
    }
}
