using FDG;
using FDG.Ai;
using FDG.ArmyBuilding;
using FDG.EngineInterface;
using FDG.Network.Connection.Lobby;
using FDG.Network.Messages;
using FDG.Players;
using FDG.SaveLoad;
using FdgRaylib.Config;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// #310: the per-user config file - the remembered player name and the last hosted game's settings.
/// </summary>
[TestFixture]
public class UserConfigTests
{
    private string _dir = "";

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "fdg-config-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    private string PathFor(string name) => Path.Combine(_dir, name);

    // ── Defaults ───────────────────────────────────────────────────────────────────────────

    [Test]
    public void MissingFile_ReadsAsDefaults()
    {
        UserConfig config = UserConfig.LoadFrom(PathFor("nope.json"));

        Assert.That(config.PlayerName, Is.EqualTo("Newbie"), "a fresh install plays as Newbie.");
        Assert.That(config.ServerName, Is.EqualTo("The Table"));
        Assert.That(config.Port, Is.EqualTo(FDG.Network.NetworkProtocol.DefaultPort));
        Assert.That(config.ListPublicly, Is.False);
    }

    [Test]
    public void DefaultHostSettings_MatchTheEngineDefaults()
    {
        HostGameSettings saved = new UserConfig().HostSettings;
        GameSettings engine = GameSettings.GetDefault();

        Assert.Multiple(() =>
        {
            Assert.That(saved.ArmyPoints, Is.EqualTo(engine.ArmyPoints));
            Assert.That(saved.TurnStyle, Is.EqualTo(engine.TurnStyle));
            Assert.That(saved.RandomnessType, Is.EqualTo(engine.RandomnessType));
            Assert.That(saved.TerrainPlacementMode, Is.EqualTo(engine.TerrainPlacementMode));
            Assert.That(saved.TerrainPieceCount, Is.EqualTo(engine.TerrainPieceCount));
            Assert.That(saved.TerrainPointsTotal, Is.EqualTo(engine.TerrainPointsTotal));
            Assert.That(saved.TerrainPointsPerTurn, Is.EqualTo(engine.TerrainPointsPerTurn));
            Assert.That(saved.ObjectivePlacementMode, Is.EqualTo(engine.ObjectivePlacementMode));
            Assert.That(saved.CoverProximityExceptions, Is.EqualTo(engine.CoverProximityExceptionsEnabled));
        });
    }

    // ── Round trip ─────────────────────────────────────────────────────────────────────────

    [Test]
    public void WrittenConfig_ReadsBackIdentically()
    {
        string path = PathFor("config.json");
        var written = new UserConfig
        {
            PlayerName = "Grimdark Gary",
            ServerName = "Gary's Table",
            Port = 7777,
            ListPublicly = true,
            HostSettings = new HostGameSettings
            {
                ArmyPoints = 1250,
                TurnStyle = ETurnStyle.BoltAction,
                RandomnessType = ERandomnessType.Probabilistic,
                TerrainPlacementMode = ETerrainPlacementMode.AlternatingPoints,
                TerrainPieceCount = 14,
                TerrainPointsTotal = 24,
                TerrainPointsPerTurn = 5,
                TerrainLayoutPath = "/tables/ruins.fdgterrain",
                ObjectivePlacementMode = EObjectivePlacementMode.PlayerPlaced,
                CoverProximityExceptions = false,
            },
        };

        UserConfig.WriteTo(path, written);
        UserConfig read = UserConfig.LoadFrom(path);

        Assert.Multiple(() =>
        {
            Assert.That(read.PlayerName, Is.EqualTo("Grimdark Gary"));
            Assert.That(read.ServerName, Is.EqualTo("Gary's Table"));
            Assert.That(read.Port, Is.EqualTo(7777));
            Assert.That(read.ListPublicly, Is.True);
            Assert.That(read.HostSettings.ArmyPoints, Is.EqualTo(1250));
            Assert.That(read.HostSettings.TurnStyle, Is.EqualTo(ETurnStyle.BoltAction));
            Assert.That(read.HostSettings.RandomnessType, Is.EqualTo(ERandomnessType.Probabilistic));
            Assert.That(read.HostSettings.TerrainPlacementMode, Is.EqualTo(ETerrainPlacementMode.AlternatingPoints));
            Assert.That(read.HostSettings.TerrainPieceCount, Is.EqualTo(14));
            Assert.That(read.HostSettings.TerrainPointsTotal, Is.EqualTo(24));
            Assert.That(read.HostSettings.TerrainPointsPerTurn, Is.EqualTo(5));
            Assert.That(read.HostSettings.TerrainLayoutPath, Is.EqualTo("/tables/ruins.fdgterrain"));
            Assert.That(read.HostSettings.ObjectivePlacementMode, Is.EqualTo(EObjectivePlacementMode.PlayerPlaced));
            Assert.That(read.HostSettings.CoverProximityExceptions, Is.False);
        });
    }

    [Test]
    public void EnumsAreWrittenByName_SoTheFileStaysHandEditable()
    {
        string path = PathFor("config.json");
        UserConfig.WriteTo(path, new UserConfig
        {
            HostSettings = new HostGameSettings { TurnStyle = ETurnStyle.BoltAction },
        });

        string json = File.ReadAllText(path);
        Assert.That(json, Does.Contain("\"BoltAction\""),
            "enum settings are stored as names, not the ordinals a reordered enum would shift.");
    }

    [Test]
    public void CorruptFile_FallsBackToDefaultsInsteadOfThrowing()
    {
        string path = PathFor("config.json");
        File.WriteAllText(path, "{ this is not json");

        UserConfig config = UserConfig.LoadFrom(path);

        Assert.That(config.PlayerName, Is.EqualTo("Newbie"));
    }

    [Test]
    public void WriteTo_CreatesMissingDirectories()
    {
        string path = Path.Combine(_dir, "nested", "deeper", "config.json");

        UserConfig.WriteTo(path, new UserConfig { PlayerName = "Newbie 2" });

        Assert.That(File.Exists(path), Is.True);
        Assert.That(UserConfig.LoadFrom(path).PlayerName, Is.EqualTo("Newbie 2"));
    }

    [Test]
    public void EnsureExists_WritesADefaultFileOnAFreshInstall()
    {
        string? previous = Environment.GetEnvironmentVariable("FDG_CONFIG_DIR");
        try
        {
            Environment.SetEnvironmentVariable("FDG_CONFIG_DIR", _dir);
            UserConfig.ResetCacheForTests();

            Assert.That(File.Exists(UserConfig.FilePath), Is.False, "precondition: nothing written yet.");
            UserConfig.EnsureExists();

            Assert.That(File.Exists(UserConfig.FilePath), Is.True, "an install always has a config file.");
            Assert.That(UserConfig.LoadFrom(UserConfig.FilePath).PlayerName, Is.EqualTo("Newbie"));
        }
        finally
        {
            Environment.SetEnvironmentVariable("FDG_CONFIG_DIR", previous);
            UserConfig.ResetCacheForTests();
        }
    }

    // ── Lobby round trip ───────────────────────────────────────────────────────────────────

    [Test]
    public void ApplyTo_PushesEverySavedSettingIntoAHostLobby()
    {
        var lobby = new FakeLobby { HasHostPrivileges = true };
        var saved = new HostGameSettings
        {
            ArmyPoints = 1750,
            TurnStyle = ETurnStyle.BoltAction,
            RandomnessType = ERandomnessType.Probabilistic,
            TerrainPlacementMode = ETerrainPlacementMode.Alternating,
            TerrainPieceCount = 11,
            TerrainPointsTotal = 21,
            TerrainPointsPerTurn = 4,
            TerrainLayoutPath = "layout.fdgterrain",
            ObjectivePlacementMode = EObjectivePlacementMode.PlayerPlaced,
            CoverProximityExceptions = false,
        };

        saved.ApplyTo(lobby);

        Assert.Multiple(() =>
        {
            Assert.That(lobby.ArmyPoints, Is.EqualTo(1750));
            Assert.That(lobby.TurnStyle, Is.EqualTo(ETurnStyle.BoltAction));
            Assert.That(lobby.RandomnessType, Is.EqualTo(ERandomnessType.Probabilistic));
            Assert.That(lobby.TerrainPlacementMode, Is.EqualTo(ETerrainPlacementMode.Alternating));
            Assert.That(lobby.TerrainCount, Is.EqualTo(11));
            Assert.That(lobby.TerrainPointsTotal, Is.EqualTo(21));
            Assert.That(lobby.TerrainPointsPerTurn, Is.EqualTo(4));
            Assert.That(lobby.TerrainLayoutPath, Is.EqualTo("layout.fdgterrain"));
            Assert.That(lobby.ObjectivePlacementMode, Is.EqualTo(EObjectivePlacementMode.PlayerPlaced));
            Assert.That(lobby.CoverProximityExceptions, Is.False);
        });
    }

    [Test]
    public void ApplyTo_LeavesTheTableBackgroundAlone_ItIsRandomizedPerLobby()
    {
        var lobby = new FakeLobby { HasHostPrivileges = true, TableBackground = ETableBackground.Ice };

        new HostGameSettings().ApplyTo(lobby);

        Assert.That(lobby.TableBackground, Is.EqualTo(ETableBackground.Ice),
            "the battlefield is deliberately not a remembered setting.");
    }

    [Test]
    public void ApplyTo_DoesNothingForAClientOrAResume()
    {
        var client = new FakeLobby { HasHostPrivileges = false, ArmyPoints = 2000 };
        var resume = new FakeLobby { HasHostPrivileges = true, IsResumeMode = true, ArmyPoints = 2000 };
        var saved = new HostGameSettings { ArmyPoints = 500 };

        saved.ApplyTo(client);
        saved.ApplyTo(resume);

        Assert.That(client.ArmyPoints, Is.EqualTo(2000), "only the host owns the settings.");
        Assert.That(resume.ArmyPoints, Is.EqualTo(2000), "a resumed game launches from its own save.");
    }

    [Test]
    public void CaptureFrom_ThenApplyTo_RoundTripsThroughDisk()
    {
        var source = new FakeLobby
        {
            HasHostPrivileges = true,
            ArmyPoints = 3000,
            TurnStyle = ETurnStyle.BoltAction,
            RandomnessType = ERandomnessType.Probabilistic,
            TerrainPlacementMode = ETerrainPlacementMode.AlternatingPoints,
            TerrainCount = 8,
            TerrainPointsTotal = 18,
            TerrainPointsPerTurn = 2,
            TerrainLayoutPath = null,
            ObjectivePlacementMode = EObjectivePlacementMode.PlayerPlaced,
            CoverProximityExceptions = false,
        };
        string path = PathFor("config.json");

        UserConfig.WriteTo(path, new UserConfig { HostSettings = HostGameSettings.CaptureFrom(source) });
        var nextLobby = new FakeLobby { HasHostPrivileges = true };
        UserConfig.LoadFrom(path).HostSettings.ApplyTo(nextLobby);

        Assert.Multiple(() =>
        {
            Assert.That(nextLobby.ArmyPoints, Is.EqualTo(3000));
            Assert.That(nextLobby.TurnStyle, Is.EqualTo(ETurnStyle.BoltAction));
            Assert.That(nextLobby.RandomnessType, Is.EqualTo(ERandomnessType.Probabilistic));
            Assert.That(nextLobby.TerrainPlacementMode, Is.EqualTo(ETerrainPlacementMode.AlternatingPoints));
            Assert.That(nextLobby.TerrainCount, Is.EqualTo(8));
            Assert.That(nextLobby.TerrainPointsTotal, Is.EqualTo(18));
            Assert.That(nextLobby.TerrainPointsPerTurn, Is.EqualTo(2));
            Assert.That(nextLobby.TerrainLayoutPath, Is.Null);
            Assert.That(nextLobby.ObjectivePlacementMode, Is.EqualTo(EObjectivePlacementMode.PlayerPlaced));
            Assert.That(nextLobby.CoverProximityExceptions, Is.False);
        });
    }

    /// <summary>
    /// Records the settings side of <see cref="ILobbyViewModel"/>; everything else throws, since the
    /// config only ever touches the host-owned settings.
    /// </summary>
    private sealed class FakeLobby : ILobbyViewModel
    {
        public bool HasHostPrivileges { get; set; }
        public bool IsResumeMode { get; set; }

        public int ArmyPoints { get; set; } = GameSettings.GetDefault().ArmyPoints;
        public int TerrainCount { get; set; }
        public int TerrainPointsTotal { get; set; }
        public int TerrainPointsPerTurn { get; set; }
        public ETerrainPlacementMode TerrainPlacementMode { get; set; }
        public string? TerrainLayoutPath { get; set; }
        public EObjectivePlacementMode ObjectivePlacementMode { get; set; }
        public ERandomnessType RandomnessType { get; set; }
        public ETurnStyle TurnStyle { get; set; }
        public bool CoverProximityExceptions { get; set; } = true;
        public ETableBackground TableBackground { get; set; }

        public void SetArmyPoints(int armyPoints) => ArmyPoints = armyPoints;
        public void SetTerrainCount(int terrainCount) => TerrainCount = terrainCount;
        public void SetTerrainPointsTotal(int points) => TerrainPointsTotal = points;
        public void SetTerrainPointsPerTurn(int points) => TerrainPointsPerTurn = points;
        public void SetTerrainPlacementMode(ETerrainPlacementMode mode) => TerrainPlacementMode = mode;
        public void SetTerrainLayoutPath(string? path) => TerrainLayoutPath = path;
        public void SetObjectivePlacementMode(EObjectivePlacementMode mode) => ObjectivePlacementMode = mode;
        public void SetRandomnessType(ERandomnessType randomnessType) => RandomnessType = randomnessType;
        public void SetTurnStyle(ETurnStyle turnStyle) => TurnStyle = turnStyle;
        public void SetCoverProximityExceptions(bool enabled) => CoverProximityExceptions = enabled;
        public void SetTableBackground(ETableBackground background) => TableBackground = background;

        public void Dispose() { }

        // ── Not exercised by the config ────────────────────────────────────────────────────
        public bool CanSaveGame => throw new NotSupportedException();
        public string? SaveGameToJson() => throw new NotSupportedException();
        public event Action<IFDGGame>? OnLaunched { add { } remove { } }
        public event Action<string>? OnGameEnded { add { } remove { } }
        public event Action<GameResult>? OnGameCompleted { add { } remove { } }
        public IObservable<string> ServerNameObservable => throw new NotSupportedException();
        public IObservable<LobbyChatMessage> ChatMessagesObservable => throw new NotSupportedException();
        public IObservable<IReadOnlyList<LobbyPlayerInfoSummary>> PlayerInfosObservable => throw new NotSupportedException();
        public IObservable<int> ArmyPointsObservable => throw new NotSupportedException();
        public IObservable<int> TerrainPieceCountObservable => throw new NotSupportedException();
        public IObservable<int> TerrainPointsTotalObservable => throw new NotSupportedException();
        public IObservable<int> TerrainPointsPerTurnObservable => throw new NotSupportedException();
        public IObservable<ETerrainPlacementMode> TerrainPlacementModeObservable => throw new NotSupportedException();
        public IObservable<string?> TerrainLayoutPathObservable => throw new NotSupportedException();
        public IObservable<EObjectivePlacementMode> ObjectivePlacementModeObservable => throw new NotSupportedException();
        public IObservable<ERandomnessType> RandomnessTypeObservable => throw new NotSupportedException();
        public IObservable<ETurnStyle> TurnStyleObservable => throw new NotSupportedException();
        public IObservable<bool> CoverProximityExceptionsObservable => throw new NotSupportedException();
        public IObservable<ETableBackground> TableBackgroundObservable => throw new NotSupportedException();
        public string ServerName => throw new NotSupportedException();
        public IReadOnlyList<LobbyChatMessage> ChatMessages => throw new NotSupportedException();
        public IReadOnlyList<LobbyPlayerInfoSummary> PlayerInfos => throw new NotSupportedException();
        public bool CheckCanModifyPlayerIDInfo(PlayerID playerID) => throw new NotSupportedException();
        public void AddLocalPlayer() => throw new NotSupportedException();
        public void AddAiPlayer(EAiProfile profile) => throw new NotSupportedException();
        public void SendMessage(string message) => throw new NotSupportedException();
        public void UpdateArmyListFile(PlayerID playerId, ArmyListFile armyListFile) => throw new NotSupportedException();
        public void SetPlayerColor(PlayerID playerId, int colorIndex) => throw new NotSupportedException();
        public void SetPlayerTeam(PlayerID playerId, ETeamOption teamNumber) => throw new NotSupportedException();
        public bool TryLaunchGame(out string? failReason) => throw new NotSupportedException();
        public IReadOnlyList<string> ValidateArmiesForLaunch() => throw new NotSupportedException();
        public void SetSavedSlotPlayerType(PlayerID slotPlayerID, EPlayerType playerType,
            EAiProfile aiProfile = EAiProfile.SoloRules) => throw new NotSupportedException();
        public bool TryResumeGame(out string? failReason) => throw new NotSupportedException();
    }
}
