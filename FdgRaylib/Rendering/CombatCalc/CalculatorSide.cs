using FDG.ArmyBuilding;
using FDG.SaveLoad;

namespace FdgRaylib.Rendering.CombatCalc;

/// <summary>
/// One column of the Combat Calculator: the unit being tested, held as the same
/// <see cref="BuilderList"/> the Army Forge edits, so every upgrade rule, price and constraint is the
/// Forge's own (#397). The unit sits at index 0; combining it spawns its partner copy at index 1, which
/// the compiler merges back into one big unit exactly as it would in a real list.
/// <para>There is no points limit here - the point is to compare loadouts, not to build a legal army.</para>
/// </summary>
internal sealed class CalculatorSide
{
    /// <summary>The unit under test is always the first entry; a combined copy rides behind it.</summary>
    internal const int MainIndex = 0;

    internal BookFile? Book { get; private set; }

    internal RuleGlossary Glossary { get; private set; } = RuleGlossary.Empty;

    internal BuilderList List { get; private set; } = new();

    /// <summary>
    /// The hero joined to this unit, or the unit a hero has joined - the second row of a split column.
    /// Held by reference rather than by index because combining inserts a copy behind the main unit and
    /// would shift any index we remembered.
    /// </summary>
    internal BuilderUnit? Joined { get; private set; }

    internal bool HasUnit => Book is not null && List.Units.Count > MainIndex;

    /// <summary>Adopt a roster unit at its default size, discarding whatever was here before.</summary>
    internal void SetUnit(BookFile book, string rosterUnitId)
    {
        Book = book;
        Glossary = RuleGlossary.Build(book);
        List = new BuilderList { BookName = book.Name };
        Joined = null;
        BuilderListEditing.AddUnit(book, List, rosterUnitId);
    }

    internal void Clear()
    {
        Book = null;
        Glossary = RuleGlossary.Empty;
        List = new BuilderList();
        Joined = null;
    }

    /// <summary>Takes over the other side's contents - the two halves of a Swap.</summary>
    internal void AdoptFrom(CalculatorSide other)
    {
        Book = other.Book;
        Glossary = other.Glossary;
        List = other.List;
        Joined = other.Joined;
    }

    internal RosterUnit? Roster => HasUnit
        ? Book!.Units.FirstOrDefault(unit => unit.Id == List.Units[MainIndex].RosterUnitId)
        : null;

    /// <summary>The compiled unit plus its final wargear, for display and for upgrade availability.</summary>
    internal (UnitFileEntry Unit, List<ItemEntry> Items) Detail() =>
        ListCompiler.CompileUnitDetailed(Book!, List.Units[MainIndex]);

    /// <summary>The partner copy whose whole-unit upgrades mirror this one's, or null when not combined.</summary>
    internal BuilderUnit? Mirror
    {
        get
        {
            int partner = BuilderListEditing.CombinePartnerIndex(List, MainIndex);
            return partner >= 0 ? List.Units[partner] : null;
        }
    }

    /// <summary>Whether the main unit is a Hero, which decides which way round a join goes.</summary>
    internal bool MainIsHero => HasUnit && ForceOrgValidator.IsHero(Detail().Unit);

    /// <summary>
    /// Attach a second unit to this column: a Hero joining the main unit, or - when the main unit IS a
    /// Hero - the unit it joins. Army creation merges the pair into one fighting unit, exactly as a real
    /// army list does; a join the rules refuse is reported as a warning on the result, not swallowed.
    /// </summary>
    internal void SetJoin(string rosterUnitId)
    {
        if (!HasUnit) return;

        RemoveJoin();

        int added = BuilderListEditing.AddUnit(Book!, List, rosterUnitId);
        if (added < 0) return;

        BuilderUnit main = List.Units[MainIndex];
        BuilderUnit partner = List.Units[added];

        // The HERO carries the link, whichever of the two it is.
        if (MainIsHero) main.JoinsUnitId = BuilderListEditing.EnsureId(partner);
        else partner.JoinsUnitId = BuilderListEditing.EnsureId(main);

        Joined = partner;
    }

    internal void RemoveJoin()
    {
        if (Joined is null) return;

        List.Units[MainIndex].JoinsUnitId = null;
        int index = List.Units.IndexOf(Joined);
        if (index >= 0) BuilderListEditing.RemoveUnit(List, index);
        Joined = null;
    }

    /// <summary>The column's rows, hero first - the reading order the owner asked for.</summary>
    internal IReadOnlyList<BuilderUnit> Rows()
    {
        BuilderUnit main = List.Units[MainIndex];
        if (Joined is null) return new[] { main };
        return MainIsHero ? new[] { main, Joined } : new[] { Joined, main };
    }

    internal (UnitFileEntry Unit, List<ItemEntry> Items) DetailOf(BuilderUnit unit) =>
        ListCompiler.CompileUnitDetailed(Book!, unit);

    internal RosterUnit? RosterOf(BuilderUnit unit) =>
        Book?.Units.FirstOrDefault(roster => roster.Id == unit.RosterUnitId);

    /// <summary>Whether a roster entry would compile to a Hero - what the join picker filters on.</summary>
    internal static bool IsHeroRoster(BookFile book, RosterUnit roster) =>
        ForceOrgValidator.IsHero(CompileDefault(book, roster));

    /// <summary>A unit a Hero may join: multi-model and not itself a Hero.</summary>
    internal static bool IsHostRoster(BookFile book, RosterUnit roster)
    {
        UnitFileEntry unit = CompileDefault(book, roster);
        return unit.ModelCount > 1 && !ForceOrgValidator.IsHero(unit);
    }

    private static UnitFileEntry CompileDefault(BookFile book, RosterUnit roster) =>
        ListCompiler.CompileUnitDetailed(book,
            new BuilderUnit { RosterUnitId = roster.Id, ModelCount = roster.BaseModelCount }).Unit;

    internal bool CanCombine => HasUnit && BuilderListEditing.CanCombine(Book!, List, MainIndex);

    internal bool IsCombined => HasUnit && BuilderListEditing.IsCombined(List, MainIndex);

    internal void SetCombined(bool on)
    {
        if (HasUnit) BuilderListEditing.SetCombined(Book!, List, MainIndex, on);
    }

    /// <summary>The army the calculator fights with - the merged, fully-costed compilation.</summary>
    internal ArmyListFile Compile() => Book is null ? new ArmyListFile() : ListCompiler.Compile(Book, List);

    internal int Points => HasUnit ? Compile().TotalPoints : 0;

    /// <summary>Changes here mean the numbers are stale. Cheap enough to ask every frame.</summary>
    internal string Fingerprint() => $"{Book?.Name ?? "-"}|{ArmyForgeScreen.ListFingerprint(List)}";
}
