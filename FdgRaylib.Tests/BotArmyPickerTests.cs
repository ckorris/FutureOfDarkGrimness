using System;
using System.Collections.Generic;
using System.Linq;
using FdgRaylib;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #372: how the lobby picks a starter army - closest to the points limit first, skipping what other
// players hold, and never repeating until the whole folder has been shown. #388 added the band: armies
// within BandPercentUnderLimit% of the limit are interchangeable and one is taken at random, because
// closest-first alone opened every lobby on the same file.
[TestFixture]
public class BotArmyPickerTests
{
    private const int Limit = 2000;

    // At Limit the band floor is 1900, so "Nearest" (1990) and "Near" (1950) are the interchangeable
    // pair and "Far" (1000) sits below it - reached for only once the band is used up.
    private static readonly string[] InBand = { "Nearest", "Near" };

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

    /// <summary>A picker whose band pick is reproducible, for the cases that need an exact answer.</summary>
    private static BotArmyPicker SeededPicker(int seed) => new(Catalog, new Random(seed));

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
    public void FirstPickComesFromTheBand()
    {
        Assert.That(NewPicker().PickNext(SlotA, Limit, NobodyElse)?.Name, Is.AnyOf(InBand));
    }

    // #388: the fault this fixes. Ranking alone made the opening pick a pure function of the folder -
    // the bundled armies tie at exactly 1000 points, so the path tiebreak picked the same file every
    // lobby, and three lobbies in a row opened on Alien Hives.
    [Test]
    public void FreshLobbiesDoNotAllOpenOnTheSameArmy()
    {
        var seen = new HashSet<string>();
        for (int seed = 0; seed < 20; seed++)
            seen.Add(SeededPicker(seed).PickNext(SlotA, Limit, NobodyElse)!.Value.Name);

        Assert.That(seen, Is.EquivalentTo(InBand),
            "every band army turns up as an opening pick, and nothing below the band does");
    }

    // The band is a tolerance, not a licence to hand out a half-size list: "Far" is legal at 1000 points
    // but it is 50% under, so it waits until both band armies have been shown.
    [Test]
    public void AnArmyFurtherUnderThanTheBandWaitsForTheBandToBeUsedUp()
    {
        BotArmyPicker picker = NewPicker();

        Assert.That(picker.PickNext(SlotA, Limit, NobodyElse)?.Name, Is.AnyOf(InBand));
        Assert.That(picker.PickNext(SlotA, Limit, NobodyElse)?.Name, Is.AnyOf(InBand));
        Assert.That(picker.PickNext(SlotA, Limit, NobodyElse)?.Name, Is.EqualTo("Far"));
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
        Assert.That(NewPicker().PickNext(SlotA, Limit, taken)?.Name, Is.AnyOf(InBand));
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

        // Each cycle is the same SET of legal armies - the two band ones in whichever order this lobby
        // rolled them (#388), then the one below the band - and then it starts over.
        Assert.That(picks.Take(3), Is.EquivalentTo(new[] { "Nearest", "Near", "Far" }));
        Assert.That(picks.Skip(3), Is.EquivalentTo(new[] { "Nearest", "Near", "Far" }));
        Assert.That(picks[2], Is.EqualTo("Far"), "the below-band army closes each cycle");
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
        picker.PickNext(SlotA, Limit, NobodyElse);   // both band armies + Far - the legal ones are exhausted

        Assert.That(picker.PickNext(SlotA, Limit, NobodyElse)?.Name, Is.AnyOf(InBand));
    }

    // Moving the limit changes both which armies are legal and which is closest, so every rotation
    // recorded against the old number is meaningless - a 1000-pt lobby must not carry on from where the
    // 2000-pt one left off.
    [Test]
    public void ChangingThePointsLimitRestartsTheRotation()
    {
        BotArmyPicker picker = NewPicker();
        picker.PickNext(SlotA, Limit, NobodyElse);          // a band army (1990 or 1950)

        Assert.That(picker.PickNext(SlotA, 1000, NobodyElse)?.Name, Is.EqualTo("Far"),
            "the 1000-pt limit re-picks from scratch - Far is the only army in ITS band");
        Assert.That(picker.PickNext(SlotA, Limit, NobodyElse)?.Name, Is.AnyOf(InBand),
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
        picker.PickNext(SlotA, Limit, NobodyElse);          // slot A has used up the band

        Assert.That(picker.PickNext(SlotB, Limit, NobodyElse)?.Name, Is.AnyOf(InBand),
            "slot B starts its own cycle at the band rather than resuming slot A's");
    }

    [Test]
    public void ForgettingASlotResetsItsRotation()
    {
        BotArmyPicker picker = SeededPicker(0);
        string first = picker.PickNext(SlotA, Limit, NobodyElse)!.Value.Name;
        picker.Forget(SlotA);

        Assert.That(picker.PickNext(SlotA, Limit, NobodyElse)?.Name, Is.AnyOf(InBand),
            "the rotation restarts at the band");
        Assert.That(SeededPicker(0).PickNext(SlotA, Limit, NobodyElse)?.Name, Is.EqualTo(first),
            "and the same seed still reproduces the same roll");
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
