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

    // The rotation covers the LEGAL armies and then starts over. It used to run off the end of them and
    // into the over-limit ones - re-rolling a few times in the lobby started offering armies the launch
    // gate rejects, which is the bug this pins.
    [Test]
    public void RerollingWalksEveryLegalArmyThenStartsOver()
    {
        BotArmyPicker picker = NewPicker();
        string[] picks = Enumerable.Range(0, 6)
            .Select(_ => picker.PickNext(SlotA, Limit, NobodyElse)!.Value.Name).ToArray();

        Assert.That(picks, Is.EqualTo(new[]
        {
            "Nearest", "Near", "Far",     // every legal army, closest first...
            "Nearest", "Near", "Far",     // ...then the same cycle again
        }));
    }

    [Test]
    public void AnOverLimitArmyIsNeverHandedOutWhileALegalOneExists()
    {
        BotArmyPicker picker = NewPicker();
        string[] picks = Enumerable.Range(0, 20)
            .Select(_ => picker.PickNext(SlotA, Limit, NobodyElse)!.Value.Name).ToArray();

        Assert.That(picks, Has.None.EqualTo("Over"),
            "restarting the legal cycle beats reaching for an army the launch gate would flag");
    }

    // ...but an over-limit list is still better than no list at all.
    [Test]
    public void AnOverLimitArmyIsUsedWhenTheFolderHasNothingLegal()
    {
        var overOnly = new List<ArmyCatalogEntry> { Army("Over", 2200), Army("WayOver", 5000) };
        BotArmyPicker picker = new(overOnly);

        Assert.That(picker.PickNext(SlotA, Limit, NobodyElse)?.Name, Is.EqualTo("Over"),
            "closest first, even when every option is illegal");
        Assert.That(picker.PickNext(SlotA, Limit, NobodyElse)?.Name, Is.EqualTo("WayOver"),
            "and the no-repeat rotation still applies");
    }

    [Test]
    public void TheRotationStartsOverOnceEveryLegalArmyHasBeenShown()
    {
        BotArmyPicker picker = NewPicker();
        picker.PickNext(SlotA, Limit, NobodyElse);
        picker.PickNext(SlotA, Limit, NobodyElse);
        picker.PickNext(SlotA, Limit, NobodyElse);   // Nearest, Near, Far - the legal ones are exhausted

        Assert.That(picker.PickNext(SlotA, Limit, NobodyElse)?.Name, Is.EqualTo("Nearest"));
    }

    // Moving the limit changes both which armies are legal and which is closest, so every rotation
    // recorded against the old number is meaningless - a 1000-pt lobby must not carry on from where the
    // 2000-pt one left off.
    [Test]
    public void ChangingThePointsLimitRestartsTheRotation()
    {
        BotArmyPicker picker = NewPicker();
        picker.PickNext(SlotA, Limit, NobodyElse);          // Nearest (1990)

        Assert.That(picker.PickNext(SlotA, 1000, NobodyElse)?.Name, Is.EqualTo("Far"),
            "the 1000-pt limit re-picks from scratch, closest to 1000");
        Assert.That(picker.PickNext(SlotA, Limit, NobodyElse)?.Name, Is.EqualTo("Nearest"),
            "and moving back re-picks from scratch again");
    }

    [Test]
    public void ChangingThePointsLimitResetsEverySlotNotJustTheOneAsking()
    {
        BotArmyPicker picker = NewPicker();
        picker.PickNext(SlotA, Limit, NobodyElse);          // Nearest
        picker.PickNext(SlotB, Limit, NobodyElse);          // Nearest too - slots rotate independently
        picker.PickNext(SlotA, 1000, NobodyElse);           // limit moves, every rotation is dropped

        Assert.That(picker.PickNext(SlotB, 1000, NobodyElse)?.Name, Is.EqualTo("Far"),
            "slot B starts the 1000-pt cycle from the top rather than resuming a stale one");
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
