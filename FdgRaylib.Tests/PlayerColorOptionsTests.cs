using System.Linq;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #221: lobby colour picker palette + pick resolution. Pure logic - the dropdown UI and launch wiring
// consume Options/ResolveIndices; only those are pinned here.
[TestFixture]
public class PlayerColorOptionsTests
{
    [Test]
    public void Options_AreEightDistinctNames_StartingWithTheLegacyDefaults()
    {
        Assert.That(PlayerColorOptions.Count, Is.EqualTo(8));
        Assert.That(PlayerColorOptions.Options.Select(o => o.Name).Distinct().Count(), Is.EqualTo(8));

        // Order is load-bearing: slot defaults consume Options in order, so the first four must keep the
        // pre-picker assignments (P1 orange, P2 purple, P3 green, P4 yellow) for an untouched lobby.
        Assert.That(PlayerColorOptions.Options[0].Name, Is.EqualTo("Orange"));
        Assert.That(PlayerColorOptions.Options[1].Name, Is.EqualTo("Purple"));
        Assert.That(PlayerColorOptions.Options[2].Name, Is.EqualTo("Green"));
        Assert.That(PlayerColorOptions.Options[3].Name, Is.EqualTo("Yellow"));
    }

    [Test]
    public void Resolve_NoPicks_AssignsPaletteInOrder()
    {
        int[] r = PlayerColorOptions.ResolveIndices(new int?[] { null, null, null, null });
        Assert.That(r, Is.EqualTo(new[] { 0, 1, 2, 3 }));
    }

    [Test]
    public void Resolve_ExplicitPickAlwaysWins()
    {
        int[] r = PlayerColorOptions.ResolveIndices(new int?[] { 7, null });
        Assert.That(r[0], Is.EqualTo(7), "picked pink stays pink");
        Assert.That(r[1], Is.EqualTo(0), "unpicked slot takes the first free default");
    }

    [Test]
    public void Resolve_PickCollidingWithAnothersDefault_BumpsTheDefaultDeterministically()
    {
        // The dropdown (IsTakenByAnother) refuses picks of anyone's current colour, so this input only
        // arises from stale/raced state - the resolver's fallback keeps it deterministic: the explicit
        // pick holds and the defaulted slot shifts to the next free colour.
        int[] r = PlayerColorOptions.ResolveIndices(new int?[] { null, 0 });
        Assert.That(r[1], Is.EqualTo(0), "explicit pick holds");
        Assert.That(r[0], Is.EqualTo(1), "bumped to the next free (purple)");
    }

    [Test]
    public void IsTakenByAnother_ReservesDefaultsAndPicksAlike()
    {
        // Two untouched slots: effective = [orange, purple]. Row 1 may not take orange (row 0's DEFAULT
        // is reserved - no stealing), may keep its own purple, and may take any free colour.
        int[] effective = PlayerColorOptions.ResolveIndices(new int?[] { null, null });
        Assert.That(PlayerColorOptions.IsTakenByAnother(effective, 1, 0), Is.True, "another row's default is taken");
        Assert.That(PlayerColorOptions.IsTakenByAnother(effective, 1, 1), Is.False, "own current colour is not taken");
        Assert.That(PlayerColorOptions.IsTakenByAnother(effective, 1, 5), Is.False, "a free colour is not taken");
        Assert.That(PlayerColorOptions.IsTakenByAnother(effective, 0, 1), Is.True, "symmetric for the other row");
    }

    [Test]
    public void Resolve_DefaultsNeverCollideWithPicksOrEachOther()
    {
        int[] r = PlayerColorOptions.ResolveIndices(new int?[] { null, 2, null, 0 });
        Assert.That(r, Is.EqualTo(new[] { 1, 2, 3, 0 }));
        Assert.That(r.Distinct().Count(), Is.EqualTo(4), "all four distinct");
    }

    [Test]
    public void Resolve_OutOfRangePickIsTreatedAsUnpicked()
    {
        int[] r = PlayerColorOptions.ResolveIndices(new int?[] { 99, -1 });
        Assert.That(r, Is.EqualTo(new[] { 0, 1 }));
    }

    [Test]
    public void Resolve_MorePlayersThanColours_WrapsInsteadOfThrowing()
    {
        var chosen = new int?[10];
        int[] r = PlayerColorOptions.ResolveIndices(chosen);
        Assert.That(r.Take(8), Is.EqualTo(Enumerable.Range(0, 8)), "first eight get the palette");
        Assert.That(r[8], Is.InRange(0, 7));
        Assert.That(r[9], Is.InRange(0, 7));
    }
}
