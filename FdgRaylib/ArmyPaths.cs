namespace FdgRaylib;

/// <summary>
/// Where the .fdgarmy file dialogs open by default. Every army Save/Load in the app (lobby, Army Builder,
/// Army Forge, share-link import) starts in the "armies" folder rather than dumping the user in the
/// application root, which is full of DLLs and nothing they'd ever pick.
///
/// <para>Two candidates, in order: beside the executable (the shipped layout - <c>scripts/build-dist.sh</c>
/// copies the repo's <c>armies/</c> next to the binary) and under the working directory (a
/// <c>dotnet run</c> from the repo root, where the output folder is several levels down from
/// <c>armies/</c>). Neither existing falls back to "", i.e. the previous behavior.</para>
/// </summary>
public static class ArmyPaths
{
    /// <summary>Folder name looked for beside the executable and under the working directory.</summary>
    public const string DirectoryName = "armies";

    /// <summary>
    /// A default path for the TinyDialogs open/save dialogs, or "" when no armies folder was found.
    /// tinyfiledialogs reads a trailing separator as "open this directory with no preset file name", so
    /// the Save dialogs still get an empty name box the user types into.
    /// </summary>
    public static string DefaultDialogPath =>
        FolderPath is { } dir ? dir + Path.DirectorySeparatorChar : "";

    /// <summary>The armies folder, or null if neither candidate location exists.</summary>
    public static string? FolderPath => FindIn(AppContext.BaseDirectory, Directory.GetCurrentDirectory());

    /// <summary>First <paramref name="roots"/> entry with an armies subfolder, or null. Takes the roots as
    /// an argument so the search order is testable without moving the process working directory.</summary>
    internal static string? FindIn(params string[] roots)
    {
        foreach (string root in roots)
        {
            string candidate = Path.Combine(root, DirectoryName);
            if (Directory.Exists(candidate)) return candidate;
        }
        return null;
    }
}
