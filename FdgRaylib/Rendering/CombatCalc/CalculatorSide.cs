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

    internal bool HasUnit => Book is not null && List.Units.Count > MainIndex;

    /// <summary>Adopt a roster unit at its default size, discarding whatever was here before.</summary>
    internal void SetUnit(BookFile book, string rosterUnitId)
    {
        Book = book;
        Glossary = RuleGlossary.Build(book);
        List = new BuilderList { BookName = book.Name };
        BuilderListEditing.AddUnit(book, List, rosterUnitId);
    }

    internal void Clear()
    {
        Book = null;
        Glossary = RuleGlossary.Empty;
        List = new BuilderList();
    }

    /// <summary>Takes over the other side's contents - the two halves of a Swap.</summary>
    internal void AdoptFrom(CalculatorSide other)
    {
        Book = other.Book;
        Glossary = other.Glossary;
        List = other.List;
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
