using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FDG.ArmyBuilding;

namespace FdgRaylib.Tests;

/// <summary>
/// #378: which bundled books a fixture should walk. The #196/#197-era ShippedData census fixtures pin
/// counts and populations that were closed against the 47-book GDF corpus, so they enumerate
/// <see cref="GdfPaths"/>; the AoF corpus carries its own census (#375) and is pinned by the
/// all-books fixtures (BookRuleCensusTests, BookSpellCoverageTests, BookRuleScopeTests). AoF bundles
/// are filename-prefixed "AoF-" by scripts/bake-aof-books.sh - the filename convention IS the
/// contract here, and the census fixtures verify the books' GameSystem field independently.
/// </summary>
internal static class ShippedBooks
{
    public static string Directory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    public static IEnumerable<string> GdfPaths() =>
        System.IO.Directory.EnumerateFiles(Directory, "*" + BookFile.EXTENSION_WITH_PERIOD)
            .Where(p => !Path.GetFileName(p).StartsWith("AoF-", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p);
}
