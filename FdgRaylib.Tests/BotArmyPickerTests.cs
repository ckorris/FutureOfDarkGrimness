using System;
using System.Collections.Generic;
using System.Linq;
using FdgRaylib;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #372: how the lobby picks a starter army for a bot - closest to the points limit first, skipping what
// other players hold, and never repeating until the whole folder has been shown.
[TestFixture]
public class BotArmyPickerTests
{
    private const int Limit = 2000;

    private static readonly Guid SlotA = new("11111111-1111-1111-1111-111111111111");
    private static readonly Guid SlotB = new("22222222-2222-2222-2222-222222222222");

    private static ArmyCatalogEntry Army(string name, int points) =>
        new($"/armies/{name}.fdgarmy", name, name, points);

    // Deliberately out of order, so a passing test means the ranking sorted rather than got lucky.
    private static readonly List<ArmyCatalogEntry> Catalog = new()
    {
        Army("Far",     1000),
        Army("Nearest", 1990),
        Army("Over",    2200),
        Army("Near",    1950),
    };

    private static readonly HashSet<string> NobodyElse = new();

    private static BotArmyPicker NewPicker() => new(Catalog);

    [Test]
    public void RankPutsTheClosestUnderTheLimitFirstAndOverLimitLast()
    {
        string[] order = BotArmyPicker.Rank(Catalog, Limit).Select(a => a.Name).ToArray();
        Assert.That(order, Is.EqualTo(new[] { "Nearest", "Near", "Far", "Over" }));
    }

    // "Over" is 200 out and "Far" is 1000 out, so distance alone would rank Over first. It sorts last
    // anyway: the launch gate flags an over-limit army, so it is the LAST resort, not the second choice.
    [Test]
    public void AnOverLimitArmyLosesToEveryLegalOneHoweverFarOut()
    {
        Assert.That(BotArmyPicker.Rank(Catalog, Limit).Last().Name, Is.EqualTo("Over"));
    }

    [Test]
    public void FirstPickIsTheClosestToTheLimit()
    {
        Assert.That(NewPicker().PickNext(SlotA, Limit, NobodyElse)?.Name, Is.EqualTo("Nearest"));
    }

    [Test]
    public void ArmiesHeldByOtherPlayersAreSkipped()
    {
        var taken = new HashSet<string> { Army("Nearest", 1990).Key, Army("Near", 1950).Key };
        Assert.That(NewPicker().PickNext(SlotA, Limit, taken)?.Name, Is.EqualTo("Far"));
    }

    // Fewer distinct armies than players: a duplicate beats leaving the bot on the 100-pt test stub.
    [Test]
    public void EverythingTakenFallsBackToTheBestArmyAnyway()
    {
        var taken = Catalog.Select(a => a.Key).ToHashSet();
        Assert.That(NewPicker().PickNext(SlotA, Limit, taken)?.Name, Is.EqualTo("Nearest"));
    }

    [Test]
    public void RerollingWalksTheWholeCatalogBeforeRepeating()
    {
        BotArmyPicker picker = NewPicker();
        string[] first = Enumerable.Range(0, Catalog.Count)
            .Select(_ => picker.PickNext(SlotA, Limit, NobodyElse)!.Value.Name).ToArray();

        Assert.That(first, Is.EqualTo(new[] { "Nearest", "Near", "Far", "Over" }),
            "the rotation should walk the ranked order");
        Assert.That(first.Distinct().Count(), Is.EqualTo(Catalog.Count), "no repeats within one cycle");
    }

    [Test]
    public void TheRotationStartsOverOnceEveryArmyHasBeenShown()
    {
        BotArmyPicker picker = NewPicker();
        for (int i = 0; i < Catalog.Count; i++) picker.PickNext(SlotA, Limit, NobodyElse);

        Assert.That(picker.PickNext(SlotA, Limit, NobodyElse)?.Name, Is.EqualTo("Nearest"));
    }

    // Each slot cycles independently; two bots don't share one cursor. They still avoid each other via
    // the in-use set, which is the caller's job to supply.
    [Test]
    public void EachSlotHasItsOwnRotation()
    {
        BotArmyPicker picker = NewPicker();
        picker.PickNext(SlotA, Limit, NobodyElse);
        picker.PickNext(SlotA, Limit, NobodyElse);

        Assert.That(picker.PickNext(SlotB, Limit, NobodyElse)?.Name, Is.EqualTo("Nearest"));
    }

    [Test]
    public void ForgettingASlotResetsItsRotation()
    {
        BotArmyPicker picker = NewPicker();
        picker.PickNext(SlotA, Limit, NobodyElse);
        picker.Forget(SlotA);

        Assert.That(picker.PickNext(SlotA, Limit, NobodyElse)?.Name, Is.EqualTo("Nearest"));
    }

    [Test]
    public void AnEmptyCatalogPicksNothing()
    {
        Assert.That(new BotArmyPicker(new List<ArmyCatalogEntry>()).PickNext(SlotA, Limit, NobodyElse),
            Is.Null);
    }

    // A 1000-pt lobby should reach for the 1000-pt lists, not the 2k ones.
    [Test]
    public void TheLimitDecidesWhatCountsAsClosest()
    {
        Assert.That(NewPicker().PickNext(SlotA, 1000, NobodyElse)?.Name, Is.EqualTo("Far"));
    }
}
