using System.Text.RegularExpressions;
using FDG;
using FDG.Ai;
using FDG.ArmyBuilding;
using FDG.Network.Connection;
using FDG.Network.Connection.Lobby;
using FDG.Players;
using FdgRaylib.Analytics;
using FdgRaylib.Config;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// #406 game events: what the host's game_start / game_end / game_abandon and a client's game_join
/// carry, that each game ends at most once (no abandon after a finish), that clients never report a
/// game's end, and that every prop key the client builds is one the Worker's allowlist accepts.
/// </summary>
[TestFixture]
public class GameAnalyticsSessionTests
{
    private static readonly PlayerID Host = new(Guid.NewGuid());
    private static readonly PlayerID Guest = new(Guid.NewGuid());
    private static readonly PlayerID Bot = new(Guid.NewGuid());

    private static LobbyPlayerInfoSummary Slot(PlayerID id, EPlayerType type, ETeamOption team, string faction,
        int points, string? system) =>
        new("name typed by a player", new ArmyListSummary(true, "army name typed by a player", faction, points, system),
            team, type, new ConnectionID(Guid.NewGuid()), id);

    private static readonly IReadOnlyList<LobbyPlayerInfoSummary> Roster = new[]
    {
        Slot(Host, EPlayerType.Local, ETeamOption.Team1, "Battle Brothers", 995, GameSystems.GrimdarkFuture),
        Slot(Guest, EPlayerType.Network, ETeamOption.Team2, "", 1000, GameSystems.AgeOfFantasy),
        Slot(Bot, EPlayerType.AI, ETeamOption.Team2, "Machine Cults", 980, null),
    };

    private static EAiProfile? Profiles(PlayerID id) => id == Bot ? EAiProfile.Strategist : null;

    // ── Props ──────────────────────────────────────────────────────────────────────────────

    [Test]
    public void GameStartDescribesTheRosterAndSettings()
    {
        var settings = HostGameSettings.FromDefaults();
        settings.ArmyPoints = 1000;

        var props = GameAnalyticsSession.BuildStartProps(Roster, Profiles, settings, listedPublicly: true, resume: false);

        Assert.That(props["points"], Is.EqualTo(1000));
        Assert.That(props["players"], Is.EqualTo(3));
        Assert.That(props["humans"], Is.EqualTo(2));
        Assert.That(props["ais"], Is.EqualTo(1));
        Assert.That(props["remote"], Is.EqualTo(1), "one network player: a multiplayer game");
        Assert.That(props["teams"], Is.EqualTo(2));
        Assert.That(props["ai_profiles"], Is.EqualTo(new[] { "Strategist" }));
        Assert.That(props["factions"], Is.EqualTo(new[] { "Battle Brothers", "custom", "Machine Cults" }),
            "a list with no book faction reads 'custom'");
        Assert.That(props["systems"], Is.EqualTo(new[] { "grimdark-future", "age-of-fantasy", "grimdark-future" }),
            "a null system is Grimdark Future, as everywhere else in the app");
        Assert.That(props["system"], Is.EqualTo("mixed"));
        Assert.That(props["slot_points"], Is.EqualTo(new[] { 995, 1000, 980 }));
        Assert.That(props["listed"], Is.EqualTo(true));
        Assert.That(props["terrain_mode"], Is.EqualTo(settings.TerrainPlacementMode.ToString()));
    }

    [Test]
    public void NothingAPlayerTypedIsSent()
    {
        var start = GameAnalyticsSession.BuildStartProps(Roster, Profiles, HostGameSettings.FromDefaults(), false, false);
        var join = GameAnalyticsSession.BuildJoinProps(Roster[1]);

        string all = string.Join("|", start.Values.Concat(join.Values).Select(v =>
            v is System.Collections.IEnumerable e and not string ? string.Join(",", e.Cast<object>()) : v?.ToString()));
        Assert.That(all, Does.Not.Contain("typed by a player"), "player names and army names must never be sent");
    }

    [Test]
    public void ASingleSystemGameNamesIt()
    {
        var gdfOnly = Roster.Where(p => p.PlayerID != Guest).ToList();
        var props = GameAnalyticsSession.BuildStartProps(gdfOnly, Profiles, HostGameSettings.FromDefaults(), false, false);
        Assert.That(props["system"], Is.EqualTo("grimdark-future"));
    }

    [Test]
    public void GameJoinCarriesOnlyTheGuestsOwnArmy()
    {
        var props = GameAnalyticsSession.BuildJoinProps(Roster[2]);
        Assert.That(props, Is.EquivalentTo(new Dictionary<string, object?>
        {
            ["faction"] = "Machine Cults", ["system"] = "grimdark-future", ["points"] = 980,
        }));
    }

    [Test]
    public void WinnerIsHumanIfAnyHumanIsOnTheWinningTeam()
    {
        var bots = new HashSet<PlayerID> { Bot };
        string Winner(GameResult r) => (string)GameAnalyticsSession.BuildEndProps(r, bots, 60)["winner"]!;

        Assert.That(Winner(GameResult.ForWin(new[] { Bot }, new[] { "b" }, [], 4)), Is.EqualTo("ai"));
        Assert.That(Winner(GameResult.ForWin(new[] { Guest, Bot }, new[] { "g", "b" }, [], 4)), Is.EqualTo("human"));
        Assert.That(Winner(GameResult.ForTie([], 4)), Is.EqualTo("none"));
        Assert.That(Winner(GameResult.ForDisconnect("x")), Is.EqualTo("none"));

        var end = GameAnalyticsSession.BuildEndProps(GameResult.ForWin(new[] { Host }, new[] { "h" }, [], 5), bots, 1800);
        Assert.That(end["outcome"], Is.EqualTo("Win"));
        Assert.That(end["rounds"], Is.EqualTo(5));
        Assert.That(end["seconds"], Is.EqualTo(1800));
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────────────────────

    private sealed class Recorder
    {
        public readonly List<(string Name, IReadOnlyDictionary<string, object?>? Props, string? Game)> Events = new();
        public void Track(string n, IReadOnlyDictionary<string, object?>? p, string? g) => Events.Add((n, p, g));
    }

    private static readonly string GameId = Guid.NewGuid().ToString("D");

    [Test]
    public void AFinishedGameIsNeverAlsoAbandoned()
    {
        var rec = new Recorder();
        var clock = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);
        var session = new GameAnalyticsSession(GameId, isHost: true, new[] { Bot }, rec.Track, () => clock);

        clock = clock.AddMinutes(30);
        session.Completed(GameResult.ForWin(new[] { Host }, new[] { "h" }, [], 4));
        session.Abandoned("menu");    // the game-over card's exit also funnels through OnGameExited
        session.Abandoned("close");
        session.Completed(GameResult.ForTie([], 4));

        Assert.That(rec.Events.Select(e => e.Name), Is.EqualTo(new[] { "game_end" }));
        Assert.That(rec.Events[0].Game, Is.EqualTo(GameId));
        Assert.That(rec.Events[0].Props!["seconds"], Is.EqualTo(1800));
    }

    [Test]
    public void LeavingARunningGameIsOneAbandon()
    {
        var rec = new Recorder();
        var session = new GameAnalyticsSession(GameId, isHost: true, [], rec.Track, () => DateTime.UtcNow);

        session.Abandoned("quit");
        session.Abandoned("close");
        session.Completed(GameResult.ForFault("x"));

        Assert.That(rec.Events.Select(e => e.Name), Is.EqualTo(new[] { "game_abandon" }));
        Assert.That(rec.Events[0].Props!["reason"], Is.EqualTo("quit"));
    }

    [Test]
    public void AClientNeverReportsTheGamesEnd()
    {
        var rec = new Recorder();
        var session = new GameAnalyticsSession(GameId, isHost: false, [], rec.Track, () => DateTime.UtcNow);

        session.Completed(GameResult.ForTie([], 4));
        session.Abandoned("menu");

        Assert.That(rec.Events, Is.Empty, "the host records the game; a client only records that it joined");
    }

    // ── Contract with the Worker ───────────────────────────────────────────────────────────

    [Test]
    public void EveryPropKeyIsInTheWorkersAllowlist()
    {
        string schema = File.ReadAllText(FindRepoFile("tools/list-server/src/analytics.ts"));
        var built = new Dictionary<string, IEnumerable<string>>
        {
            ["game_start"] = GameAnalyticsSession.BuildStartProps(Roster, Profiles, HostGameSettings.FromDefaults(), false, false).Keys,
            ["game_join"] = GameAnalyticsSession.BuildJoinProps(Roster[0]).Keys,
            ["game_end"] = GameAnalyticsSession.BuildEndProps(GameResult.ForTie([], 1), new HashSet<PlayerID>(), 1).Keys,
            ["game_abandon"] = new[] { "seconds", "reason" },
            ["app_launch"] = new[] { "first_launch", "prev_session_seconds", "prev_session_clean" },
        };

        foreach (var (eventName, keys) in built)
        {
            Match block = Regex.Match(schema, $@"\n  {eventName}: \{{(?<body>.*?)\n  \}},", RegexOptions.Singleline);
            Assert.That(block.Success, Is.True, $"no SCHEMA block for {eventName}");
            foreach (string key in keys)
                Assert.That(block.Groups["body"].Value, Does.Contain($"\n    {key}:"), $"{eventName}.{key} is not in the Worker SCHEMA");
        }
    }

    private static string FindRepoFile(string relative)
    {
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }
        Assert.Fail($"could not find {relative} above the test directory");
        return "";
    }
}
