using FDG.ArmyBuilding;

namespace FdgRaylib.Rendering.CombatCalc;

/// <summary>
/// The two-level "choose a unit" browser for one column: pick an army, then a unit from it.
///
/// <para>
/// Re-opening lands on the UNITS of the army already in hand, because swapping between units of one
/// army is the common move and re-picking the army every time is the friction this avoids (#397 owner
/// request). Back steps out to the army list. The state is kept ImGui-free so it can be tested.
/// </para>
/// </summary>
internal sealed class UnitPicker
{
    internal enum ELevel
    {
        Armies,
        Units,
    }

    /// <summary>Which units the list should offer - a join only accepts one kind.</summary>
    internal enum ERoles
    {
        Any,
        HeroesOnly,
        HostsOnly,
    }

    internal ELevel Level { get; private set; } = ELevel.Armies;

    internal ERoles Roles { get; private set; } = ERoles.Any;

    /// <summary>True while picking a unit to JOIN the column's unit rather than to replace it.</summary>
    internal bool JoinMode { get; private set; }

    internal BookFile? Army { get; private set; }

    /// <summary>Both columns start open, so a fresh screen asks for both units at once.</summary>
    internal bool IsOpen { get; private set; } = true;

    internal string Filter = string.Empty;

    /// <summary>Open it: at the current army's units when there is one, else at the army list.</summary>
    internal void Open()
    {
        IsOpen = true;
        JoinMode = false;
        Roles = ERoles.Any;
        Level = Army is null ? ELevel.Armies : ELevel.Units;
        Filter = string.Empty;
    }

    /// <summary>
    /// Open it to pick a joining unit. Locked to <paramref name="army"/> and to one role: a hero joins
    /// a unit from its own army, so there is no army level to browse here.
    /// </summary>
    internal void OpenForJoin(BookFile army, ERoles roles)
    {
        IsOpen = true;
        JoinMode = true;
        Roles = roles;
        Army = army;
        Level = ELevel.Units;
        Filter = string.Empty;
    }

    internal void Close()
    {
        IsOpen = false;
        JoinMode = false;
        Roles = ERoles.Any;
    }

    internal void ChooseArmy(BookFile army)
    {
        Army = army;
        Level = ELevel.Units;
        Filter = string.Empty;
    }

    internal void BackToArmies()
    {
        Level = ELevel.Armies;
        Filter = string.Empty;
    }

    /// <summary>Case-insensitive substring match; an empty filter matches everything.</summary>
    internal static bool Matches(string text, string filter) =>
        filter.Length == 0 || text.Contains(filter, StringComparison.OrdinalIgnoreCase);

    internal static IEnumerable<BookFile> MatchingArmies(IEnumerable<BookFile> armies, string filter) =>
        armies.Where(army => Matches(army.Name, filter));

    internal static IEnumerable<RosterUnit> MatchingUnits(BookFile army, string filter) =>
        MatchingUnits(army, filter, ERoles.Any);

    /// <summary>
    /// The army's units that match the filter AND the wanted role. A Hero whose Tough exceeds the join
    /// cap is deliberately still listed: army creation refuses it and the result says so, which is more
    /// use than a unit that silently is not there.
    /// </summary>
    internal static IEnumerable<RosterUnit> MatchingUnits(BookFile army, string filter, ERoles roles) =>
        army.Units.Where(unit => Matches(unit.Name, filter) && roles switch
        {
            ERoles.HeroesOnly => CalculatorSide.IsHeroRoster(army, unit),
            ERoles.HostsOnly => CalculatorSide.IsHostRoster(army, unit),
            _ => true,
        });
}
