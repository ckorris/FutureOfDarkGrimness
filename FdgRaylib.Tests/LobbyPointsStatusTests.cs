using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// The lobby Pts column's colour rule: red over the limit, yellow 50+ points under it, plain otherwise.
[TestFixture]
public class LobbyPointsStatusTests
{
    private const int Limit = 2000;

    private static ELobbyPointsStatus Classify(int pointCost, bool isAssigned = true) =>
        LobbyPointsStatus.Classify(isAssigned, pointCost, Limit);

    [Test]
    public void ExactlyOnTheLimitIsOk() =>
        Assert.That(Classify(Limit), Is.EqualTo(ELobbyPointsStatus.Ok));

    [Test]
    public void OnePointOverIsOver() =>
        Assert.That(Classify(Limit + 1), Is.EqualTo(ELobbyPointsStatus.Over));

    // The boundary both ways: 49 under is still fine, 50 under warns.
    [Test]
    public void JustInsideTheUnderThresholdIsOk() =>
        Assert.That(Classify(Limit - LobbyPointsStatus.UnderWarningThreshold + 1),
            Is.EqualTo(ELobbyPointsStatus.Ok));

    [Test]
    public void ExactlyTheUnderThresholdWarns() =>
        Assert.That(Classify(Limit - LobbyPointsStatus.UnderWarningThreshold),
            Is.EqualTo(ELobbyPointsStatus.Under));

    [Test]
    public void WellUnderWarns() =>
        Assert.That(Classify(100), Is.EqualTo(ELobbyPointsStatus.Under));

    // An empty slot has no army to be under WITH - painting every fresh row yellow is noise, not a warning.
    [Test]
    public void UnassignedSlotIsNeverUnder() =>
        Assert.That(Classify(0, isAssigned: false), Is.EqualTo(ELobbyPointsStatus.Ok));

    [Test]
    public void OverBeatsUnassigned() =>
        Assert.That(LobbyPointsStatus.Classify(isAssigned: false, pointCost: 10, pointsLimit: 0),
            Is.EqualTo(ELobbyPointsStatus.Over));
}
