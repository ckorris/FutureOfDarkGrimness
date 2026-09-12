using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Rules.Serialization;
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

    private ArmyListFile? _saved;
    private readonly List<UnitFileEntry> _savedRows = new();

    internal bool HasUnit =>
        (Book is not null && List.Units.Count > MainIndex) || _savedRows.Count > 0;

    /// <summary>
    /// Whether this column's unit can be edited here. A book-backed unit can; one read out of a saved
    /// army that carries no book cannot - there are no upgrade options to offer, so it is shown as saved.
    /// </summary>
    internal bool IsEditable => Book is not null;

    /// <summary>A read-only column's units, hero first.</summary>
    internal IReadOnlyList<UnitFileEntry> SavedRows => _savedRows;

    /// <summary>Adopt a roster unit at its default size, discarding whatever was here before.</summary>
    internal void SetUnit(BookFile book, string rosterUnitId)
    {
        Book = book;
        Glossary = RuleGlossary.Build(book);
        List = new BuilderList { BookName = book.Name };
        Joined = null;
        _saved = null;
        _savedRows.Clear();
        BuilderListEditing.AddUnit(book, List, rosterUnitId);
    }

    /// <summary>
    /// Adopt a unit out of a saved army that has no book. It is copied rather than referenced, so
    /// editing one column can never disturb the file or the other column, and a hero the saved list
    /// already joins to this unit comes along with it - that pairing was the author's intent.
    /// </summary>
    internal void SetSavedUnit(ArmyListFile source, UnitFileEntry unit)
    {
        Book = null;
        List = new BuilderList();
        Joined = null;
        Glossary = RuleGlossary.Build(source.RuleDefinitions);
        _saved = source;

        _savedRows.Clear();
        UnitFileEntry? hero = source.Units.FirstOrDefault(other =>
            !ReferenceEquals(other, unit)
            && !string.IsNullOrEmpty(other.JoinsUnitId)
            && other.JoinsUnitId == unit.Id);

        if (hero is not null) _savedRows.Add(CloneEntry(hero));
        _savedRows.Add(CloneEntry(unit));
    }

    internal void Clear()
    {
        Book = null;
        Glossary = RuleGlossary.Empty;
        List = new BuilderList();
        Joined = null;
        _saved = null;
        _savedRows.Clear();
    }

    /// <summary>
    /// An independent copy of this column - what the "+" tab hands back (#398). The book, its glossary
    /// and the source file are SHARED: all three are read-only reference data, and a book is half a
    /// megabyte of parsed JSON. The <see cref="BuilderList"/> is deep-copied, because that is the one
    /// thing the upgrade editors mutate - sharing it would make the copy and the original the same unit
    /// wearing two tabs, and editing either would silently edit both.
    /// </summary>
    internal CalculatorSide Clone()
    {
        // Joined points INTO the list, so it is recovered by position in the copy rather than copied.
        int joined = Joined is null ? -1 : List.Units.IndexOf(Joined);

        var copy = new CalculatorSide
        {
            Book = Book,
            Glossary = Glossary,
            List = CloneList(List),
            _saved = _saved,
        };
        if (joined >= 0) copy.Joined = copy.List.Units[joined];
        copy._savedRows.AddRange(_savedRows.Select(CloneEntry));
        return copy;
    }

    /// <summary>The unit's name for a tab label, taken off the ROSTER rather than a compile: a tab is
    /// drawn every frame for every slot, and a compile per tab per frame to read one string back is a
    /// price with nothing to show for it.</summary>
    internal string UnitName =>
        Book is not null ? Roster?.Name ?? string.Empty
        : _savedRows.Count > 0 ? _savedRows[^1].Name : string.Empty;

    internal RosterUnit? Roster => Book is not null && HasUnit
        ? Book.Units.FirstOrDefault(unit => unit.Id == List.Units[MainIndex].RosterUnitId)
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
    internal bool MainIsHero => IsEditable && HasUnit && ForceOrgValidator.IsHero(Detail().Unit);

    /// <summary>
    /// Attach a second unit to this column: a Hero joining the main unit, or - when the main unit IS a
    /// Hero - the unit it joins. Army creation merges the pair into one fighting unit, exactly as a real
    /// army list does; a join the rules refuse is reported as a warning on the result, not swallowed.
    /// </summary>
    internal void SetJoin(string rosterUnitId)
    {
        if (!IsEditable || !HasUnit) return;

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

    internal bool CanCombine => IsEditable && HasUnit && BuilderListEditing.CanCombine(Book!, List, MainIndex);

    internal bool IsCombined => IsEditable && HasUnit && BuilderListEditing.IsCombined(List, MainIndex);

    internal void SetCombined(bool on)
    {
        if (IsEditable && HasUnit) BuilderListEditing.SetCombined(Book!, List, MainIndex, on);
    }

    /// <summary>The army the calculator fights with - the merged, fully-costed compilation.</summary>
    internal ArmyListFile Compile()
    {
        if (Book is not null) return ListCompiler.Compile(Book, List);
        if (_saved is null) return new ArmyListFile();

        // Carry the source's rule definitions, spells and effect-set defaults across: without them the
        // units would load with their rule names unresolved and quietly do nothing.
        var army = new ArmyListFile
        {
            Name = _saved.Name,
            Faction = _saved.Faction,
            GameSystem = _saved.GameSystem,
            RuleDefinitions = new List<FDG.Rules.Definitions.SpecialRuleDefinition>(_saved.RuleDefinitions),
            Spells = new List<FDG.Rules.Definitions.SpellDefinition>(_saved.Spells),
            DefaultRangedEffectSet = _saved.DefaultRangedEffectSet,
            DefaultMeleeEffectSet = _saved.DefaultMeleeEffectSet,
        };

        foreach (UnitFileEntry unit in _savedRows) army.Units.Add(CloneEntry(unit));
        return army;
    }

    internal int Points => Book is not null
        ? (HasUnit ? Compile().TotalPoints : 0)
        : _savedRows.Sum(unit => unit.PointCost);

    /// <summary>Changes here mean the numbers are stale. Cheap enough to ask every frame.</summary>
    internal string Fingerprint() => Book is not null
        ? $"{Book.Name}|{ArmyForgeScreen.ListFingerprint(List)}"
        : $"saved:{_saved?.Name ?? "-"}|{string.Join(",", _savedRows.Select(unit => unit.Name + unit.ModelCount))}";

    private static BuilderList CloneList(BuilderList list) =>
        JsonSerializer.Deserialize<BuilderList>(
            JsonSerializer.Serialize(list, RuleJson.Options), RuleJson.Options)!;

    /// <summary>A deep copy that KEEPS the id, so a saved hero-to-host link survives the copy.</summary>
    private static UnitFileEntry CloneEntry(UnitFileEntry unit) =>
        JsonSerializer.Deserialize<UnitFileEntry>(
            JsonSerializer.Serialize(unit, RuleJson.Options), RuleJson.Options)!;
}
