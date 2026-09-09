using FDG.ArmyBuilding;
using FDG.SaveLoad;

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

    internal ArmySource? Army { get; private set; }

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
    internal void OpenForJoin(ArmySource army, ERoles roles)
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

    internal void ChooseArmy(ArmySource army)
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

    internal static IEnumerable<ArmySource> MatchingArmies(IEnumerable<ArmySource> armies, string filter) =>
        armies.Where(army => Matches(army.Name, filter));

    /// <summary>One offerable unit: where it sits in its army, and how to show it.</summary>
    internal readonly record struct Entry(int Index, string Name, string StatLine);

    internal static IEnumerable<Entry> MatchingUnits(ArmySource army, string filter) =>
        MatchingUnits(army, filter, ERoles.Any);

    /// <summary>
    /// The army's units that match the filter AND the wanted role, whether it is a book's roster or a
    /// saved list's entries. A Hero whose Tough exceeds the join cap is deliberately still listed: army
    /// creation refuses it and the result says so by name, which is more use than a unit that silently
    /// is not there.
    /// </summary>
    internal static IEnumerable<Entry> MatchingUnits(ArmySource army, string filter, ERoles roles)
    {
        if (army.Book is { } book)
        {
            for (int i = 0; i < book.Units.Count; i++)
            {
                RosterUnit unit = book.Units[i];
                if (!Matches(unit.Name, filter) || !AllowsRoster(book, unit, roles)) continue;
                yield return new Entry(i, unit.Name, ArmyForgeScreen.RosterStatLine(unit));
            }
            yield break;
        }

        List<UnitFileEntry> saved = army.Saved?.Units ?? new List<UnitFileEntry>();
        for (int i = 0; i < saved.Count; i++)
        {
            UnitFileEntry unit = saved[i];
            if (!Matches(unit.Name, filter) || !AllowsSaved(unit, roles)) continue;
            yield return new Entry(i, unit.Name, ArmyBuilderScreen.UnitStatLine(unit));
        }
    }

    private static bool AllowsRoster(BookFile book, RosterUnit unit, ERoles roles) => roles switch
    {
        ERoles.HeroesOnly => CalculatorSide.IsHeroRoster(book, unit),
        ERoles.HostsOnly => CalculatorSide.IsHostRoster(book, unit),
        _ => true,
    };

    private static bool AllowsSaved(UnitFileEntry unit, ERoles roles) => roles switch
    {
        ERoles.HeroesOnly => ForceOrgValidator.IsHero(unit),
        ERoles.HostsOnly => unit.ModelCount > 1 && !ForceOrgValidator.IsHero(unit),
        _ => true,
    };
}
