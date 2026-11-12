using FDG;
using FDG.ArmyBuilding;
using FDG.Network.Connection;
using FDG.Network.Connection.Lobby;
using FDG.Players;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #441 F1 (signed off B, 2026-10-31): one amber line per army over its force-org caps under the lobby roster, beside
// the #440 / #444 legacy lines, the cap lines themselves in its hover. The host measures the caps against the lobby's
// points limit and the report rides the roster (ArmyListSummary.ForceOrgWarnings - engine LobbyForceOrgNoticeTests);
// this pins the app's line, hover and ordering. Advisory - never a launch blocker.
[TestFixture]
public class LobbyForceOrgNoticeTests
{
    private const string HeroCap = "Too many Heroes: 3 (max 2 at 1000 pts; 1 per 500).";
    private const string UnitCap = "Too many units: 6 (max 5 at 1000 pts; 1 per 200).";

    private static LobbyPlayerInfoSummary Slot(ArmyListSummary summary) =>
        new("player", summary, ETeamOption.Team1, EPlayerType.Local, new ConnectionID(Guid.NewGuid()),
            new PlayerID(Guid.NewGuid()));

    private static ArmyListSummary Army(string name, string? rulesVersion = "OPR v3.6.0", params string[] caps) =>
        new(true, name, "Demo", 1000, GameSystems.GrimdarkFuture, rulesVersion, caps);

    [Test]
    public void AnArmyOverTwoCaps_GetsOneLine_WithTheCapLinesInItsHover()
    {
        LobbyScreen.LobbyNotice? notice = LobbyScreen.ForceOrgNotice(Army("Brass Council", caps: new[] { HeroCap, UnitCap }));

        Assert.That(notice, Is.Not.Null);
        Assert.That(notice!.Line, Is.EqualTo("Army 'Brass Council' is over 2 force-org caps"));
        Assert.That(notice.Hover, Is.EqualTo(HeroCap + "\n" + UnitCap));
        Assert.That(notice.Line.All(c => c < 128), Is.True, "game text is ASCII-only");
    }

    [Test]
    public void OneCap_ReadsSingular()
    {
        Assert.That(LobbyScreen.ForceOrgNotice(Army("Brass Council", caps: new[] { HeroCap }))!.Line,
            Is.EqualTo("Army 'Brass Council' is over 1 force-org cap"));
    }

    [Test]
    public void ACleanArmy_AnUnassignedSlot_AndAHostWithoutTheReport_SayNothing()
    {
        Assert.Multiple(() =>
        {
            Assert.That(LobbyScreen.ForceOrgNotice(Army("Clean", caps: Array.Empty<string>())), Is.Null);
            Assert.That(LobbyScreen.ForceOrgNotice(new ArmyListSummary(true, "Old host", "Demo", 1000)), Is.Null,
                "a pre-#441 host sends no report (null) - nothing to say");
            Assert.That(LobbyScreen.ForceOrgNotice(new ArmyListSummary(false, "N/A", "N/A", 0, null, null, new[] { HeroCap })),
                Is.Null);
        });
    }

    [Test]
    public void TheRosterShowsTheLegacyLinesFirst_ThenTheForceOrgLines_OnePerDistinctArmy()
    {
        var players = new[]
        {
            Slot(Army("Old Guard", "OPR v3.5.3", HeroCap)),
            Slot(Army("Fresh", caps: Array.Empty<string>())),
            Slot(Army("Brass Council", caps: new[] { HeroCap, UnitCap })),
            Slot(Army("Brass Council", caps: new[] { HeroCap, UnitCap })), // the same list fielded twice
        };

        IReadOnlyList<LobbyScreen.LobbyNotice> notices = LobbyScreen.RosterNotices(players, isResume: false, null);

        Assert.That(notices.Select(n => n.Line), Is.EqualTo(new[]
        {
            "Army 'Old Guard' was built for OPR v3.5.3; it plays with 3.6 rules",
            "Army 'Old Guard' is over 1 force-org cap",
            "Army 'Brass Council' is over 2 force-org caps",
        }));
        Assert.That(notices[0].Hover, Is.Null, "the legacy line is its own detail");
        Assert.That(notices[1].Hover, Is.EqualTo(HeroCap));
    }
}
