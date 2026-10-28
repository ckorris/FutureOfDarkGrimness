using FDG;
using FDG.Ai;
using FDG.ArmyBuilding;
using FDG.GameModel;
using FDG.Network.Connection.Lobby;
using FDG.Players;
using FdgRaylib.Config;

namespace FdgRaylib.Analytics;

/// <summary>
/// The usage-analytics view of one launched game (#406): its ID, when it started, and whether it has
/// already ended, so <c>game_start</c>, <c>game_end</c> and <c>game_abandon</c> pair up and an
/// abandon is never sent after a finish.
///
/// <para>The host records the game (start, end or abandon); a client records only that it joined.
/// Each game is therefore counted once, while guests still show up as unique players with their
/// faction. The prop builders are static and pure so tests can feed them a fake roster.</para>
/// </summary>
public sealed class GameAnalyticsSession
{
    /// <summary>The most recently launched game in this process, or null before the first launch.</summary>
    public static GameAnalyticsSession? Current { get; private set; }

    private readonly Action<string, IReadOnlyDictionary<string, object?>?, string?> _track;
    private readonly Func<DateTime> _clock;
    private readonly DateTime _startedUtc;
    private readonly HashSet<PlayerID> _aiPlayers;
    private int _ended;

    public string? GameId { get; }
    public bool IsHost { get; }
    public bool HasEnded => Volatile.Read(ref _ended) == 1;

    internal GameAnalyticsSession(string? gameId, bool isHost, IEnumerable<PlayerID> aiPlayers,
        Action<string, IReadOnlyDictionary<string, object?>?, string?> track, Func<DateTime> clock)
    {
        GameId = gameId;
        IsHost = isHost;
        _aiPlayers = aiPlayers.ToHashSet();
        _track = track;
        _clock = clock;
        _startedUtc = clock();
    }

    // ── Lifecycle (called from the lobby screen and Program.cs) ────────────────────────────

    /// <summary>A game just launched on this machine: the host sends <c>game_start</c>, a client
    /// <c>game_join</c>. Replaces any previous session. Never throws.</summary>
    public static void Launched(ILobbyViewModel lobby, IReadOnlyList<PlayerID> localPlayerIds, bool listedPublicly)
    {
        try
        {
            IReadOnlyList<LobbyPlayerInfoSummary> roster = lobby.PlayerInfos;
            var session = new GameAnalyticsSession(lobby.LaunchedGameId, lobby.HasHostPrivileges,
                roster.Where(p => p.PlayerType == EPlayerType.AI).Select(p => p.PlayerID),
                UsageAnalytics.Track, () => DateTime.UtcNow);
            Current = session;

            if (session.IsHost)
            {
                session._track(AnalyticsEvents.GameStart,
                    BuildStartProps(roster, lobby.GetAiProfile, HostGameSettings.CaptureFrom(lobby),
                        listedPublicly, lobby.IsResumeMode),
                    session.GameId);
            }
            else if (roster.FirstOrDefault(p => localPlayerIds.Contains(p.PlayerID)) is { } mine)
            {
                session._track(AnalyticsEvents.GameJoin, BuildJoinProps(mine), session.GameId);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"analytics: could not record the game launch ({ex.Message})");
        }
    }

    /// <summary>The host's engine finished the game (engine thread). Host only; at most once.</summary>
    public void Completed(GameResult result)
    {
        if (Interlocked.Exchange(ref _ended, 1) == 1 || !IsHost) return;
        _track(AnalyticsEvents.GameEnd, BuildEndProps(result, _aiPlayers, Seconds()), GameId);
    }

    /// <summary>The player left a running game: <paramref name="reason"/> is "menu" (escape menu back
    /// to the main menu, or Load), "quit" (Quit to Desktop) or "close" (the window was closed).
    /// Host only; suppressed when the game already ended.</summary>
    public void Abandoned(string reason)
    {
        if (Interlocked.Exchange(ref _ended, 1) == 1 || !IsHost) return;
        _track(AnalyticsEvents.GameAbandon,
            new Dictionary<string, object?> { ["seconds"] = Seconds(), ["reason"] = reason }, GameId);
    }

    /// <summary>A client saw the host end the game: nothing to send, but no abandon may follow.</summary>
    public void EndedRemotely() => Interlocked.Exchange(ref _ended, 1);

    private int Seconds() => (int)Math.Clamp((_clock() - _startedUtc).TotalSeconds, 0, 30 * 86_400);

    // ── Prop builders (pure) ───────────────────────────────────────────────────────────────

    public const int MaxSlots = 8;
    public const int MaxFactionChars = 60;
    public const string CustomFaction = "custom";
    public const string MixedSystem = "mixed";

    internal static Dictionary<string, object?> BuildStartProps(IReadOnlyList<LobbyPlayerInfoSummary> roster,
        Func<PlayerID, EAiProfile?> aiProfileOf, HostGameSettings settings, bool listedPublicly, bool resume)
    {
        var slots = roster.Take(MaxSlots).ToList();
        return new Dictionary<string, object?>
        {
            ["points"] = Math.Clamp(settings.ArmyPoints, 0, 100_000),
            ["system"] = CombinedSystem(slots.Select(p => p.ArmyListSummary)),
            ["players"] = roster.Count,
            ["humans"] = roster.Count(p => p.PlayerType is EPlayerType.Local or EPlayerType.Network),
            ["ais"] = roster.Count(p => p.PlayerType == EPlayerType.AI),
            ["remote"] = roster.Count(p => p.PlayerType == EPlayerType.Network),
            ["teams"] = roster.Select(p => p.TeamNumber).Distinct().Count(),
            ["ai_profiles"] = slots.Where(p => p.PlayerType == EPlayerType.AI)
                .Select(p => aiProfileOf(p.PlayerID)?.ToString() ?? "Unknown").ToArray(),
            ["factions"] = slots.Select(p => Faction(p.ArmyListSummary)).ToArray(),
            ["systems"] = slots.Select(p => SystemSlug(p.ArmyListSummary)).ToArray(),
            ["slot_points"] = slots.Select(p => Math.Clamp(p.ArmyListSummary.PointCost, 0, 100_000)).ToArray(),
            ["listed"] = listedPublicly,
            ["resume"] = resume,
            ["terrain_mode"] = settings.TerrainPlacementMode.ToString(),
            ["terrain_count"] = Math.Clamp(settings.TerrainPieceCount, 0, 1_000),
            ["objective_mode"] = settings.ObjectivePlacementMode.ToString(),
            ["randomness"] = settings.RandomnessType.ToString(),
            ["turn_style"] = settings.TurnStyle.ToString(),
            ["shooting_mode"] = settings.ShootingMode.ToString(),
            ["cover_exceptions"] = settings.CoverProximityExceptions,
            ["see_through_friendlies"] = settings.SeeThroughFriendlyUnits,
            ["unlimited_split_fire"] = settings.UnlimitedSplitFire,
        };
    }

    internal static Dictionary<string, object?> BuildJoinProps(LobbyPlayerInfoSummary mine) => new()
    {
        ["faction"] = Faction(mine.ArmyListSummary),
        ["system"] = CombinedSystem(new[] { mine.ArmyListSummary }),
        ["points"] = Math.Clamp(mine.ArmyListSummary.PointCost, 0, 100_000),
    };

    internal static Dictionary<string, object?> BuildEndProps(GameResult result, IReadOnlySet<PlayerID> aiPlayers,
        int seconds) => new()
    {
        ["outcome"] = result.Outcome.ToString(),
        ["rounds"] = Math.Clamp(result.RoundsPlayed, 0, 1_000),
        ["seconds"] = seconds,
        // A team win with any human on it counts as a human win: the question is "do people beat
        // the bots", and a human-plus-bot team that wins is a person winning.
        ["winner"] = result.Outcome != EGameOutcome.Win || result.WinnerPlayers.Count == 0 ? "none"
            : result.WinnerPlayers.Any(id => !aiPlayers.Contains(id)) ? "human"
            : "ai",
    };

    /// <summary>The Army Forge book faction, or "custom" for a hand-built list with none. Only the
    /// faction is sent - never the army's own name, which the player typed.</summary>
    internal static string Faction(ArmyListSummary army)
    {
        string faction = army.FactionName?.Trim() ?? "";
        if (faction.Length == 0) return CustomFaction;
        return faction.Length > MaxFactionChars ? faction[..MaxFactionChars] : faction;
    }

    internal static string SystemSlug(ArmyListSummary army)
    {
        string slug = GameSystems.Normalize(army.GameSystem);
        return slug is GameSystems.GrimdarkFuture or GameSystems.AgeOfFantasy ? slug : "other";
    }

    /// <summary>One system when every army shares it, otherwise "mixed".</summary>
    internal static string CombinedSystem(IEnumerable<ArmyListSummary> armies)
    {
        var systems = armies.Select(SystemSlug).Distinct().ToList();
        return systems.Count == 1 && systems[0] != "other" ? systems[0] : MixedSystem;
    }
}
