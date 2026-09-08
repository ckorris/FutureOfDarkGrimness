using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Rules.Serialization;

namespace FdgRaylib.Rendering;

/// <summary>
/// The bundled faction books, parsed once per run and shared.
///
/// <para>
/// Reading the shipped library is about half a second of JSON across ~90 books, which is why the Army
/// Forge has always done it on a worker task. #397 added a second screen that wants the same books, and
/// paying that twice at startup for two copies of identical data is waste - so the parse lives here and
/// both screens await the same task. Callers get their own list, so adding or reordering entries on one
/// screen cannot disturb another; the <see cref="BookFile"/>s themselves are read-only in practice
/// (screens edit <see cref="BuilderList"/>s, never books).
/// </para>
/// </summary>
internal static class BookLibrary
{
    private static readonly Lazy<Task<List<BookFile>>> Shared =
        new(() => Task.Run(Parse), LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>Starts (or joins) the shared parse. Awaiting it twice never parses twice.</summary>
    internal static Task<List<BookFile>> LoadAsync() => Shared.Value;

    /// <summary>Joins the shared parse and hands back a private list over the shared books.</summary>
    internal static List<BookFile> Load() => new(Shared.Value.GetAwaiter().GetResult());

    // Every .fdgbook bundled under Assets/Books/ (the imported OPR snapshots). The hand-authored demo
    // book is deliberately NOT among them (it confused the list next to real factions); it remains only
    // as a fallback so a build with no bundled books still works.
    private static List<BookFile> Parse()
    {
        var books = new List<BookFile>();
        string directory = Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

        if (Directory.Exists(directory))
        {
            foreach (string path in Directory
                         .EnumerateFiles(directory, "*" + BookFile.EXTENSION_WITH_PERIOD)
                         .OrderBy(path => path))
            {
                try
                {
                    BookFile? book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options);
                    if (book is not null) books.Add(book);
                }
                catch { /* skip a malformed book rather than crash the screen */ }
            }
        }

        if (books.Count == 0) books.Add(DemoBook.Build());
        return books;
    }
}
