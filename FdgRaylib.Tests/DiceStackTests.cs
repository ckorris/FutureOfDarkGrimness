using System.Collections.Generic;
using FDG;
using FDG.Presentation.Beats;
using FdgRaylib.Rendering.Presentation;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #322 — dice rolls are HELD beats, so the engine moves on after each one's settle lead-in while the
// panel stays readable for several seconds more. Because rolls now overlap, the player keeps a STACK
// of them (oldest first, drawn from the bottom anchor upward) instead of a single slot that the next
// roll evicts — the eviction is exactly what forced dice to be non-held before (engine ea91d68).
// Hovering the stack freezes every panel's timer.
//
// Also covers the knock-on the same change forced (see the item's Decisions): attacks animate on a
// concurrent LIST, because a ~600ms roll no longer outlasts a 1600ms attack animation the way the old
// 1800ms one always did.
//
// Alpha carries the #245 fade behaviour: eased in as a panel appears, out over its tail.
[TestFixture]
public class DiceStackTests
{
    private static DiceRolledBeat Dice(bool held = true, string label = "Roll to Hit") =>
        new(new[] { 1f, 1f, 1f, 1f, 1f, 1f }, 1, 4, ERandomnessType.Realistic, label, held: held);

    private static DiceRolledBeat Chippy() =>
        new(new[] { 1f, 1f, 1f, 1f, 1f, 1f }, 1, 4, ERandomnessType.Realistic, "Roll to Hit",
            held: true, modifierTags: new[] { "Quality 4+", "Stealth -1" });

    private static AttackBeat Attack(int volleys = 3) => new(isMelee: false,
        new List<Position> { new(0f, 0f) }, new List<Position> { new(5f, 5f) },
        volleyCount: volleys, armorPenetration: 0);

    // Lifetime of a plain held panel: 600ms lead-in + 3s linger.
    private const float PlainLifetime = 3.6f;

    [Test]
    public void AHeldRoll_ShowsWithoutHoldingTheActiveSlot()
    {
        var player = new PresentationPlayer();
        player.OnBeat(Dice());
        player.Update(0.05f);

        Assert.That(player.GetDiceStack(), Has.Count.EqualTo(1), "the panel is up...");
        Assert.That(player.IsAnimating, Is.False,
            "...but it never gates a prompt - that is what keeps a practiced player moving");
    }

    [Test]
    public void ANonHeldRoll_StillOwnsTheActiveSlot()
    {
        // The explicit opt-out: the engine waits the full duration for this one, so the front-end has
        // to be animating for that whole time or the two would drift apart.
        var player = new PresentationPlayer();
        player.OnBeat(Dice(held: false));
        player.Update(0.05f);

        Assert.That(player.GetDiceStack(), Has.Count.EqualTo(1));
        Assert.That(player.IsAnimating, Is.True);

        player.Update(2.0f); // past the 1800ms duration
        Assert.That(player.IsAnimating, Is.False);
        Assert.That(player.GetDiceStack(), Has.Count.EqualTo(1),
            "the panel outlives the beat either way - it is the stack that retires it");
    }

    [Test]
    public void ASecondRoll_StacksOnTopInsteadOfEvictingTheFirst()
    {
        var player = new PresentationPlayer();
        player.OnBeat(Dice(label: "Rifle: 3 hits"));
        player.Update(0.7f);
        player.OnBeat(Dice(label: "Rifle: 2 hits, Rending AP+1"));
        player.Update(0.05f);

        var stack = player.GetDiceStack();
        Assert.That(stack, Has.Count.EqualTo(2), "a two-threshold volley shows both rolls at once");
        Assert.That(stack[0].beat.Label, Is.EqualTo("Rifle: 3 hits"), "oldest first");
        Assert.That(stack[1].beat.Label, Is.EqualTo("Rifle: 2 hits, Rending AP+1"));
        Assert.That(stack[0].progress, Is.GreaterThan(stack[1].progress),
            "each panel runs its own envelope; the older one is further along");
    }

    [Test]
    public void TheStack_IsCapped_AndDropsTheOldest()
    {
        var player = new PresentationPlayer();
        for (int i = 0; i < 5; i++)
        {
            player.OnBeat(Dice(label: $"Roll {i}"));
            player.Update(0.05f);
        }

        var stack = player.GetDiceStack();
        Assert.That(stack, Has.Count.EqualTo(3), "a burst of rolls cannot bury the table");
        Assert.That(stack[0].beat.Label, Is.EqualTo("Roll 2"), "the oldest drop out...");
        Assert.That(stack[2].beat.Label, Is.EqualTo("Roll 4"), "...and the newest always survives");
    }

    [Test]
    public void APanel_OutlivesThePacing_ThenExpires()
    {
        var player = new PresentationPlayer();
        DiceRolledBeat beat = Dice();
        player.OnBeat(beat);

        player.Update((float)beat.HoldLeadIn.TotalSeconds + 0.05f);
        Assert.That(player.GetDiceStack(), Has.Count.EqualTo(1),
            "the engine has already moved on, the panel has not");

        player.Update(PlainLifetime - 1f);
        Assert.That(player.GetDiceStack(), Has.Count.EqualTo(1), "still up seconds after the roll paced");

        player.Update(1.1f);
        Assert.That(player.GetDiceStack(), Is.Empty, "and gone once its lifetime runs out");
    }

    [Test]
    public void TheLeadIn_OutlastsTheTumble_SoNoPanelParksMidRoll()
    {
        // Load-bearing: DiceOverlay locks the faces at 30% of the beat's envelope, and the lead-in is
        // 600ms of an 1800ms envelope. Shorten the lead-in below that and every panel would settle
        // AFTER the engine had moved on - the dice would still be spinning as the wounds landed.
        var player = new PresentationPlayer();
        DiceRolledBeat beat = Dice();
        player.OnBeat(beat);
        player.Update((float)beat.HoldLeadIn.TotalSeconds);

        Assert.That(player.GetDiceStack()[0].progress, Is.GreaterThanOrEqualTo(0.3f));
    }

    [Test]
    public void InfoChips_BuyThePanelMoreTime()
    {
        // #245: chips are extra reading, so they stretch both the settle and the linger.
        var player = new PresentationPlayer();
        player.OnBeat(Chippy());

        player.Update(PlainLifetime + 0.2f);
        Assert.That(player.GetDiceStack(), Has.Count.EqualTo(1),
            "past a plain panel's lifetime, still up");

        player.Update(1.2f);
        Assert.That(player.GetDiceStack(), Is.Empty);
    }

    [Test]
    public void APanel_FadesInAndOut()
    {
        var player = new PresentationPlayer();
        player.OnBeat(Dice());

        player.Update(0.05f);
        Assert.That(player.GetDiceStack()[0].alpha, Is.GreaterThan(0f).And.LessThan(1f), "mid fade-in");

        player.Update(0.15f);
        Assert.That(player.GetDiceStack()[0].alpha, Is.EqualTo(1f), "solid once eased in");

        player.Update(PlainLifetime - 0.4f);
        Assert.That(player.GetDiceStack()[0].alpha, Is.GreaterThan(0f).And.LessThan(1f),
            "fading over its tail instead of popping");
    }

    [Test]
    public void Hovering_FreezesEveryPanel()
    {
        var player = new PresentationPlayer();
        player.OnBeat(Dice());
        player.Update(0.2f);
        float before = player.GetDiceStack()[0].progress;

        player.SetDiceStackHovered(true);
        Assert.That(player.IsDiceStackHovered, Is.True);
        player.Update(10f); // far past its lifetime

        Assert.That(player.GetDiceStack(), Has.Count.EqualTo(1), "frozen panels do not expire");
        Assert.That(player.GetDiceStack()[0].progress, Is.EqualTo(before), "nor advance");

        player.SetDiceStackHovered(false);
        player.Update(PlainLifetime + 0.1f);
        Assert.That(player.GetDiceStack(), Is.Empty, "and they resume ageing on release");
    }

    [Test]
    public void OverlappingAttacks_PlayConcurrently_InsteadOfTruncating()
    {
        // The regression held dice opened up: a whiffed attack has no saves or wounds behind it, so the
        // next weapon's AttackBeat can arrive ~600ms in, while the first is still mid-flight.
        var player = new PresentationPlayer();
        player.OnBeat(Attack());
        player.Update(0.6f);
        player.OnBeat(Attack());
        player.Update(0.05f);

        var attacks = player.GetActiveAttacks();
        Assert.That(attacks, Has.Count.EqualTo(2), "the first attack is not cut off by the second");
        Assert.That(attacks[0].progress, Is.GreaterThan(attacks[1].progress),
            "each runs its own timeline");
    }

    [Test]
    public void EachOverlappingAttack_CuesItsOwnVolleys()
    {
        var cued = new List<AttackBeat>();
        var player = new PresentationPlayer();
        player.AttackVolleyStarted = cued.Add;

        AttackBeat first = Attack(volleys: 3);
        AttackBeat second = Attack(volleys: 3);
        player.OnBeat(first);
        player.Update(0.05f);   // first attack's opening volley
        player.OnBeat(second);
        player.Update(0.05f);   // second attack's opening volley - not swallowed by the first's counter

        Assert.That(cued, Is.EqualTo(new[] { first, second }),
            "per-attack cue counters, or the second attack would fire silently");
    }
}
