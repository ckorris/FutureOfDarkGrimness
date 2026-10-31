using FDG.ArmyBuilding;
using FDG.SaveLoad;

namespace FdgRaylib.Rendering.CombatCalc;

/// <summary>
/// Somewhere the Combat Calculator can get a unit from: a bundled faction book, or an army saved to
/// disk (#397).
///
/// <para>
/// The distinction that matters is whether a <see cref="Book"/> came with it. A bundled book has one,
/// and so does an army the Army Forge built - the Forge embeds the book it was authored against - and
/// either can be EDITED here, upgrades and all. A hand-authored or imported list has no book, so there
/// are no upgrade options to offer and its units are shown as they were saved. Every list in the
/// shipped `armies/` folder is of that second kind today.
/// </para>
/// </summary>
internal sealed class ArmySource
{
    private ArmySource(string name, BookFile? book, ArmyListFile? saved, string? path)
    {
        Name = name;
        Book = book;
        Saved = saved;
        Path = path;
    }

    internal string Name { get; }

    /// <summary>The book to build against, or null when this army cannot be edited here.</summary>
    internal BookFile? Book { get; }

    /// <summary>The saved army, when this came off disk.</summary>
    internal ArmyListFile? Saved { get; }

    /// <summary>Where it was loaded from - the identity used to avoid listing one army twice.</summary>
    internal string? Path { get; }

    internal bool IsEditable => Book is not null;

    /// <summary>
    /// #398: the OPR game system this army belongs to, normalised (absent means Grimdark Future - the
    /// only system that existed before the field did). A bundled book carries it; a saved list carries
    /// it on the file.
    /// </summary>
    internal string GameSystem => GameSystems.Normalize(Book?.GameSystem ?? Saved?.GameSystem);

    internal static ArmySource FromBook(BookFile book) => new(book.Name, book, saved: null, path: null);

    /// <summary>
    /// Wraps a loaded file. A Forge-built army (one carrying both its selections and its book) is
    /// adopted as an editable book, so it behaves exactly like a bundled faction; anything else stays
    /// read-only.
    /// </summary>
    internal static ArmySource FromSaved(string path, ArmyListFile file)
    {
        bool forgeBuilt = file is BuiltArmyFile { Selections: not null, Book: not null };
        BookFile? book = forgeBuilt ? ((BuiltArmyFile)file).Book : null;
        string name = string.IsNullOrWhiteSpace(file.Name) ? System.IO.Path.GetFileName(path) : file.Name;
        return new ArmySource(name, book, file, path);
    }

    /// <summary>The unit names to list, whichever kind of army this is.</summary>
    internal IReadOnlyList<string> UnitNames() => Book is not null
        ? Book.Units.Select(unit => unit.Name).ToList()
        : Saved?.Units.Select(unit => unit.Name).ToList() ?? new List<string>();
}
