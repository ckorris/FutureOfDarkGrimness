using FDG.ArmyBuilding;
using FDG.Players;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #378 — the lobby's mixed-system note (owner ruling: GDF and AoF armies may meet; warn, never
// block). The absent-means-GDF rule matters most: every pre-#378 army file has no GameSystem field
// and must read as Grimdark Future, not as a third system.
[TestFixture]
public class MixedSystemWarningTests
{
    private static ArmyListSummary Army(string? system) => new(true, "List", "Faction", 1000, system);

    [Test]
    public void SameSystem_NoWarning()
    {
        Assert.That(LobbyScreen.MixedSystemWarning(new[] { Army(null), Army(null) }), Is.Null);
        Assert.That(LobbyScreen.MixedSystemWarning(new[]
        {
            Army(GameSystems.AgeOfFantasy), Army(GameSystems.AgeOfFantasy),
        }), Is.Null);
    }

    [Test]
    public void AbsentField_ReadsAsGrimdarkFuture()
    {
        Assert.That(LobbyScreen.MixedSystemWarning(new[] { Army(null), Army(GameSystems.GrimdarkFuture) }),
            Is.Null, "a pre-#378 army (no field) and an explicit GDF army are the same system");
    }

    [Test]
    public void MixedSystems_WarnsWithBothNames()
    {
        string? warning = LobbyScreen.MixedSystemWarning(new[] { Army(null), Army(GameSystems.AgeOfFantasy) });
        Assert.That(warning, Does.Contain("Age of Fantasy").And.Contain("Grimdark Future"));
        Assert.That(warning, Does.Contain("compatible"), "warn, never block - the note says launching is fine");
    }

    [Test]
    public void UnassignedSlots_DoNotCount()
    {
        var empty = new ArmyListSummary(false, "N/A", "N/A", 0);
        Assert.That(LobbyScreen.MixedSystemWarning(new[] { empty, Army(GameSystems.AgeOfFantasy) }), Is.Null);
    }
}
