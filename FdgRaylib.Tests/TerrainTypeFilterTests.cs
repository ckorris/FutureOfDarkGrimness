using FDG;
using FDG.SaveLoad;
using FDG.Stages;
using FdgRaylib.Rendering.Resolvers;
using NUnit.Framework;
using System.Collections.Generic;
using System.Linq;

namespace FdgRaylib.Tests;

// #394 - the terrain picker's type filter. The ImGui row itself is hand-verified; what is pinned here is
// the predicate + counts the row and the list share, and the property that makes the whole thing safe:
// filtering is a VIEW, so the index a filtered row reports still indexes the unfiltered pool.
[TestFixture]
public class TerrainTypeFilterTests
{
    private static IReadOnlyList<TerrainPieceEntry> Palette() => DefaultTerrainPool.GetPalette();

    [Test]
    public void All_MatchesEveryPiece()
    {
        Assert.That(TerrainTypeFilter.CountMatching(Palette(), TerrainTypeFilter.All),
            Is.EqualTo(Palette().Count));
    }

    [Test]
    public void EachFilter_MatchesExactlyThePiecesCarryingItsFlag()
    {
        IReadOnlyList<TerrainPieceEntry> pool = Palette();

        foreach ((ETerrainType flag, string label) in TerrainTypeFilter.Options)
        {
            int expected = pool.Count(p => p.TerrainType.HasFlag(flag));

            Assert.That(TerrainTypeFilter.CountMatching(pool, flag), Is.EqualTo(expected),
                $"the {label} button's count must agree with the rows the list actually draws.");
            Assert.That(expected, Is.GreaterThan(0),
                $"the {label} button would open on an empty list.");
        }
    }

    [Test]
    public void Impassible_AlsoCoversBlockingPieces()
    {
        // Blocking has no button of its own (TerrainTypeFilter's doc comment): it is only ever set
        // together with Impassible in the built-in palette, so the Impassible button is where a player
        // finds a sight-blocking building. If a Blocking-only piece ever ships, it needs its own button.
        var blockingOnly = Palette()
            .Where(p => p.TerrainType.HasFlag(ETerrainType.Blocking)
                     && !p.TerrainType.HasFlag(ETerrainType.Impassible))
            .ToList();

        Assert.That(blockingOnly, Is.Empty,
            "a Blocking-only piece would be reachable in the picker only under All.");
    }

    [Test]
    public void FilteringIsAView_SoTheSurvivingIndicesStillAddressTheUnfilteredPool()
    {
        // The one way this feature could place the WRONG piece: re-packing the visible rows into their
        // own list and sending back that list's index. TerrainPlacementResult.TemplateIndex indexes the
        // pool the stage holds, so this walks the picker's real loop shape (skip, keep the pool index)
        // and checks every surviving index still names a matching piece.
        IReadOnlyList<TerrainPieceEntry> pool = Palette();

        foreach ((ETerrainType flag, string label) in TerrainTypeFilter.Options)
        {
            for (int i = 0; i < pool.Count; i++)
            {
                if (!TerrainTypeFilter.Matches(pool[i].TerrainType, flag)) continue;

                Assert.That(pool[i].TerrainType.HasFlag(flag), Is.True,
                    $"pool index {i} survived the {label} filter but does not carry the flag.");
            }
        }
    }

    [Test]
    public void SummaryLine_IsSilentWhenUnfiltered_AndNamesTheTypeOtherwise()
    {
        Assert.That(TerrainTypeFilter.SummaryLine(30, 30, TerrainTypeFilter.All), Is.Null,
            "an unfiltered picker must look exactly as it did before #394.");

        Assert.That(TerrainTypeFilter.SummaryLine(7, 30, ETerrainType.Cover),
            Is.EqualTo("Showing 7 of 30 pieces - Cover."));

        Assert.That(TerrainTypeFilter.SummaryLine(0, 30, ETerrainType.Dangerous),
            Does.Contain("All"),
            "an empty filtered list must say how to get back, or it reads as a broken picker.");
    }

    [Test]
    public void EveryFilterLabel_IsAscii()
    {
        // The ImGui font atlas bakes Basic Latin + Latin-1 only (CLAUDE.md).
        foreach ((ETerrainType _, string label) in TerrainTypeFilter.Options)
        {
            Assert.That(label.All(c => c <= 'ÿ'), Is.True, $"'{label}' would render as '?'.");
        }

        Assert.That(TerrainTypeFilter.LabelFor(TerrainTypeFilter.All), Is.EqualTo("All"));
    }
}
