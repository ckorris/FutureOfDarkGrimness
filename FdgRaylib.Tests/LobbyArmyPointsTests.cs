using FDG.ArmyBuilding;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #430: the lobby's Army Points follow the suggested game sizes of the selected Army Source (Core Rules
// v3.6.0: GF "1000pts to start, and 2000pts for a full match"; AoF 750 / 1500). The live combo's setter is
// ImGui-bound; what it computes is ForceOrgLimits.Retarget, and the hover text is a pure function.
[TestFixture]
public class LobbyArmyPointsTests
{
    [TestCase(2000, EAllowedGameSystems.All, EAllowedGameSystems.AgeOfFantasy, 1500)]
    [TestCase(1000, EAllowedGameSystems.All, EAllowedGameSystems.AgeOfFantasy, 750)]
    [TestCase(1500, EAllowedGameSystems.AgeOfFantasy, EAllowedGameSystems.All, 2000)]
    [TestCase(750, EAllowedGameSystems.AgeOfFantasy, EAllowedGameSystems.GrimdarkFuture, 1000)]
    [TestCase(2000, EAllowedGameSystems.GrimdarkFuture, EAllowedGameSystems.AgeOfFantasy, 1500)]
    [TestCase(2000, EAllowedGameSystems.All, EAllowedGameSystems.GrimdarkFuture, 2000)]   // All reads as GDF
    [TestCase(2000, EAllowedGameSystems.GrimdarkFuture, EAllowedGameSystems.All, 2000)]
    [TestCase(1750, EAllowedGameSystems.All, EAllowedGameSystems.AgeOfFantasy, 1750)]     // typed, not a preset
    [TestCase(2500, EAllowedGameSystems.AgeOfFantasy, EAllowedGameSystems.All, 2500)]
    public void ChangingArmySource_RetargetsOnlyAStockBudget(
        int points, EAllowedGameSystems from, EAllowedGameSystems to, int expected) =>
        Assert.That(ForceOrgLimits.Retarget(points, from, to), Is.EqualTo(expected));

    [Test]
    public void TheDefaultLobbyBudget_IsTheGdfFullMatchSize()
    {
        // GameSettings.GetDefault() is 2000 with Army Source "All": the Grimdark Future full-match size.
        Assert.That(FDG.GameSettings.GetDefault().ArmyPoints,
            Is.EqualTo(ForceOrgLimits.GrimdarkFuture.SuggestedFullPoints));
    }

    [Test]
    public void ArmyPointsTooltip_NamesTheSelectedSystemsSuggestedSizes_InAscii()
    {
        string all = LobbyScreen.ArmyPointsTooltip(EAllowedGameSystems.All);
        string gdf = LobbyScreen.ArmyPointsTooltip(EAllowedGameSystems.GrimdarkFuture);
        string aof = LobbyScreen.ArmyPointsTooltip(EAllowedGameSystems.AgeOfFantasy);

        Assert.That(gdf, Does.Contain("Grimdark Future: 1000 to start, 2000 for a full match"));
        Assert.That(aof, Does.Contain("Age of Fantasy: 750 to start, 1500 for a full match"));
        Assert.That(aof, Does.Not.Contain("2000"));
        Assert.That(all, Does.Contain("1000 to start").And.Contain("2000 for a full match").And.Contain("750 / 1500"));
        Assert.That(all + gdf + aof, Is.All.LessThan('Ā'));
    }
}
