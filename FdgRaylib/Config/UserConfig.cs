using System.Text.Json;
using System.Text.Json.Serialization;
using FDG;
using FDG.Network;
using FDG.Network.Connection.Lobby;

namespace FdgRaylib.Config;

/// <summary>
/// Per-user preferences that outlive a single run (#310): the player name every lobby leads with, and
/// the host dialog + lobby settings the last hosted game was set up with.
///
/// <para>Stored as human-editable JSON in the OS per-user config directory - <c>%APPDATA%\FDG\config.json</c>
/// on Windows, <c>~/.config/fdg/config.json</c> elsewhere - so it survives downloading a fresh build
/// into a new folder. Override the directory with the <c>FDG_CONFIG_DIR</c> environment variable
/// (tests and portable installs). The file is written with defaults the first time it is read if it
/// doesn't exist yet; every read and write is best-effort, and a missing/unreadable/corrupt file just
/// means "defaults", never an error the player sees.</para>
/// </summary>
public sealed class UserConfig
{
    /// <summary>What a fresh install calls you, until you type something else into a host/join dialog.</summary>
    public const string DefaultPlayerName = "Newbie";

    public const string DefaultServerName = "The Table";

    /// <summary>The name this player joins and hosts under. Written back whenever you host or join.</summary>
    public string PlayerName { get; set; } = DefaultPlayerName;

    /// <summary>Host dialog: the server name shown to joiners.</summary>
    public string ServerName { get; set; } = DefaultServerName;

    /// <summary>Host dialog: the listen port (#189).</summary>
    public int Port { get; set; } = NetworkProtocol.DefaultPort;

    /// <summary>Host dialog: the "List publicly" tick (#271). The password is deliberately never stored.</summary>
    public bool ListPublicly { get; set; }

    /// <summary>Display: start in borderless-windowed fullscreen. F11 toggles it at runtime and writes the
    /// new choice back here, so it is remembered next launch. On by default on a fresh install.</summary>
    public bool StartFullscreen { get; set; } = true;

    /// <summary>The lobby settings panel, as the last hosted (non-resume) game left it.</summary>
    public HostGameSettings HostSettings { get; set; } = HostGameSettings.FromDefaults();

    // ── Store ──────────────────────────────────────────────────────────────────────────────

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        // Enums as names, so a hand-edit reads "BoltAction" rather than "1" and survives an enum
        // gaining a value in the middle (ETerrainPlacementMode already appended one to keep the wire
        // values stable).
        Converters = { new JsonStringEnumConverter() },
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    private static readonly object FileLock = new();
    private static UserConfig? _current;

    /// <summary>
    /// The live config. Loaded from disk on first touch; if no file exists yet, one is written with the
    /// defaults so an install always has a config to edit.
    /// </summary>
    public static UserConfig Current
    {
        get
        {
            lock (FileLock)
            {
                if (_current == null)
                {
                    _current = LoadFrom(FilePath);
                    if (!File.Exists(FilePath))
                        WriteTo(FilePath, _current);
                }
                return _current;
            }
        }
    }

    /// <summary>Full path of the config file (existing or not).</summary>
    public static string FilePath => Path.Combine(Directory, "config.json");

    /// <summary>The per-user config directory, honouring the <c>FDG_CONFIG_DIR</c> override.</summary>
    public static string Directory
    {
        get
        {
            string? overrideDir = Environment.GetEnvironmentVariable("FDG_CONFIG_DIR");
            if (!string.IsNullOrWhiteSpace(overrideDir))
                return overrideDir.Trim();

            // %APPDATA% on Windows; $XDG_CONFIG_HOME (defaulting to ~/.config) on Linux and macOS.
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            if (string.IsNullOrEmpty(appData))
                appData = AppContext.BaseDirectory; // no home directory (some sandboxes) - keep it local
            return Path.Combine(appData, OperatingSystem.IsWindows() ? "FDG" : "fdg");
        }
    }

    /// <summary>Writes <see cref="Current"/> back to disk. Best-effort: a failure is logged, never thrown.</summary>
    public static void Save()
    {
        lock (FileLock)
        {
            WriteTo(FilePath, Current);
        }
    }

    /// <summary>Makes sure the install has a config file, creating one with defaults if it doesn't (#310).</summary>
    public static void EnsureExists() => _ = Current;

    /// <summary>Drops the cached instance so the next <see cref="Current"/> re-reads the file (tests).</summary>
    public static void ResetCacheForTests()
    {
        lock (FileLock) _current = null;
    }

    /// <summary>Reads a config file, falling back to defaults when it is missing or unreadable.</summary>
    public static UserConfig LoadFrom(string path)
    {
        try
        {
            if (!File.Exists(path)) return new UserConfig();
            return JsonSerializer.Deserialize<UserConfig>(File.ReadAllText(path), Json) ?? new UserConfig();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            // A hand-edit that doesn't parse shouldn't stop the game starting - fall back to defaults
            // and say so, rather than silently rewriting the file the player was editing.
            Console.WriteLine($"Could not read {path} ({ex.Message}); using default settings.");
            return new UserConfig();
        }
    }

    /// <summary>Writes a config file, creating the directory if needed. Best-effort.</summary>
    public static void WriteTo(string path, UserConfig config)
    {
        try
        {
            string? dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
            File.WriteAllText(path, JsonSerializer.Serialize(config, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"Could not write {path} ({ex.Message}); settings won't be remembered.");
        }
    }
}

/// <summary>
/// The lobby settings panel's host-owned values, as a saved preference (#310). Mirrors the editable
/// half of <see cref="GameSettings"/>.
///
/// <para>Two fields are deliberately absent. <see cref="GameSettings.TableBackground"/> is re-randomized
/// every time a lobby opens - remembering it would defeat the point - and <see cref="GameSettings.DiceSeed"/>
/// has no lobby control (it comes from a scenario file).</para>
/// </summary>
public sealed class HostGameSettings
{
    public int ArmyPoints { get; set; }
    public ETerrainPlacementMode TerrainPlacementMode { get; set; }
    public int TerrainPieceCount { get; set; }
    public int TerrainPointsTotal { get; set; }
    public int TerrainPointsPerTurn { get; set; }
    public string? TerrainLayoutPath { get; set; }
    public EObjectivePlacementMode ObjectivePlacementMode { get; set; }
    public ERandomnessType RandomnessType { get; set; }
    public ETurnStyle TurnStyle { get; set; }
    public EShootingMode ShootingMode { get; set; }
    public bool CoverProximityExceptions { get; set; }
    public bool SeeThroughFriendlyUnits { get; set; }
    public bool UnlimitedSplitFire { get; set; }

    /// <summary>The engine's own defaults - one source of truth for what a fresh config holds.</summary>
    public static HostGameSettings FromDefaults() => From(GameSettings.GetDefault());

    public static HostGameSettings From(GameSettings settings) => new()
    {
        ArmyPoints             = settings.ArmyPoints,
        TerrainPlacementMode   = settings.TerrainPlacementMode,
        TerrainPieceCount      = settings.TerrainPieceCount,
        TerrainPointsTotal     = settings.TerrainPointsTotal,
        TerrainPointsPerTurn   = settings.TerrainPointsPerTurn,
        TerrainLayoutPath      = settings.TerrainLayoutPath,
        ObjectivePlacementMode = settings.ObjectivePlacementMode,
        RandomnessType         = settings.RandomnessType,
        TurnStyle              = settings.TurnStyle,
        ShootingMode           = settings.ShootingMode,
        CoverProximityExceptions = settings.CoverProximityExceptionsEnabled,
        SeeThroughFriendlyUnits = settings.SeeThroughFriendlyUnits,
        UnlimitedSplitFire     = settings.UnlimitedSplitFire,
    };

    /// <summary>Snapshots what the lobby panel currently shows.</summary>
    public static HostGameSettings CaptureFrom(ILobbyViewModel viewModel) => new()
    {
        ArmyPoints             = viewModel.ArmyPoints,
        TerrainPlacementMode   = viewModel.TerrainPlacementMode,
        TerrainPieceCount      = viewModel.TerrainCount,
        TerrainPointsTotal     = viewModel.TerrainPointsTotal,
        TerrainPointsPerTurn   = viewModel.TerrainPointsPerTurn,
        TerrainLayoutPath      = viewModel.TerrainLayoutPath,
        ObjectivePlacementMode = viewModel.ObjectivePlacementMode,
        RandomnessType         = viewModel.RandomnessType,
        TurnStyle              = viewModel.TurnStyle,
        ShootingMode           = viewModel.ShootingMode,
        CoverProximityExceptions = viewModel.CoverProximityExceptions,
        SeeThroughFriendlyUnits = viewModel.SeeThroughFriendlyUnits,
        UnlimitedSplitFire     = viewModel.UnlimitedSplitFire,
    };

    /// <summary>
    /// Pushes these settings into a fresh host lobby. Host-only and never on a resume - a saved game
    /// launches from its own settings (see <see cref="GameSettings.WithResumeOverridesFrom"/>).
    /// </summary>
    public void ApplyTo(ILobbyViewModel viewModel)
    {
        if (!viewModel.HasHostPrivileges || viewModel.IsResumeMode) return;

        viewModel.SetArmyPoints(ArmyPoints);
        viewModel.SetTerrainPlacementMode(TerrainPlacementMode);
        viewModel.SetTerrainCount(TerrainPieceCount);
        viewModel.SetTerrainPointsTotal(TerrainPointsTotal);
        viewModel.SetTerrainPointsPerTurn(TerrainPointsPerTurn);
        viewModel.SetTerrainLayoutPath(TerrainLayoutPath);
        viewModel.SetObjectivePlacementMode(ObjectivePlacementMode);
        viewModel.SetRandomnessType(RandomnessType);
        viewModel.SetTurnStyle(TurnStyle);
        viewModel.SetShootingMode(ShootingMode);
        viewModel.SetCoverProximityExceptions(CoverProximityExceptions);
        viewModel.SetSeeThroughFriendlyUnits(SeeThroughFriendlyUnits);
        viewModel.SetUnlimitedSplitFire(UnlimitedSplitFire);
    }
}
