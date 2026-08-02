using System.Collections.Generic;
using FDG;
using FDG.Presentation.Beats;
using FdgRaylib.Rendering.Presentation;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #325 — a dice panel OUTLIVES its beat. The engine still paces each roll in full (the gap between
// rolls is the rhythm of the exchange, not dead time), but the panel stays up for seconds afterwards,
// so consecutive rolls overlap on screen and the player keeps a STACK of them (oldest first, drawn from
// the bottom anchor upward) instead of a single slot that the next roll evicts — the eviction is what
// made a two-threshold volley cut its own first roll short (engine ea91d68). Hovering the stack freezes
// every panel's timer.
//
// Also covers the concurrent attack LIST, which #325 introduced while rolls were briefly held and kept
// afterwards: "the dice envelope outlasts the attack animation" is a coincidence of two unrelated
// constants, not something the front-end should depend on.
//
// Alpha carries the #245 fade behaviour: eased in as a panel appears, out over its tail.
[TestFixture]
public class DiceStackTests
{
    private static DiceRolledBeat Dice(bool held = false, string label = "Roll to Hit") =>
        new(new[] { 1f, 1f, 1f, 1f, 1f, 1f }, 1, 4, ERandomnessType.Realistic, label, held: held);

    private static DiceRolledBeat Chippy() =>
        new(new[] { 1f, 1f, 1f, 1f, 1f, 1f }, 1, 4, ERandomnessType.Realistic, "Roll to Hit",
            modifierTags: new[] { "Quality 4+", "Stealth -1" });

    private static AttackBeat Attack(int volleys = 3) => new(isMelee: false,
        new List<Position> { new(0f, 0f) }, new List<Position> { new(5f, 5f) },
        volleyCount: volleys, armorPenetration: 0);

    // A normal roll: the engine paces its 1800ms envelope, then the panel lingers 3s more.
    private const float Paced        = 1.8f;
    private const float PanelLife    = 4.8f;

    [Test]
    public void ARoll_OwnsTheActiveSlotForItsEnvelope_AndItsPanelOutlivesIt()
    {
        var player = new PresentationPlayer();
        player.OnBeat(Dice());
        player.Update(0.05f);

        Assert.That(player.GetDiceStack(), Has.Count.EqualTo(1));
        Assert.That(player.IsAnimating, Is.True, "the engine is waiting for this roll, so the front-end "
            + "must be animating for exactly as long");

        player.Update(Paced);
        Assert.That(player.IsAnimating, Is.False, "the beat is done...");
        Assert.That(player.GetDiceStack(), Has.Count.EqualTo(1), "...and the panel is still up. That "
            + "overhang is what makes panels overlap, and therefore what makes them stack.");

        player.Update(PanelLife - Paced);
        Assert.That(player.GetDiceStack(), Is.Empty, "gone once its lifetime runs out");
    }

    [Test]
    public void AHeldRoll_ShowsWithoutGatingPrompts()
    {
        // The opt-in nothing uses today: pace the settle only. Kept covered so the path does not rot.
        var player = new PresentationPlayer();
        DiceRolledBeat beat = Dice(held: true);
        player.OnBeat(beat);
        player.Update(0.05f);

        Assert.That(player.GetDiceStack(), Has.Count.EqualTo(1), "the panel is up...");
        Assert.That(player.IsAnimating, Is.False, "...but it never gates a prompt");

        player.Update((float)beat.HoldLeadIn.TotalSeconds);
        Assert.That(player.GetDiceStack()[0].progress, Is.GreaterThanOrEqualTo(0.3f),
            "a held panel must finish tumbling within its lead-in (DiceOverlay locks the faces at 30% "
            + "of the envelope), or the dice would still be spinning after the engine moved on");
    }

    [Test]
    public void ASecondRoll_StacksOnTopInsteadOfEvictingTheFirst()
    {
        // Real pacing: the engine sends the save roll only after the to-hit roll's envelope elapsed.
        var player = new PresentationPlayer();
        player.OnBeat(Dice(label: "Rifle: 3 hits"));
        player.Update(Paced + 0.05f);
        player.OnBeat(Dice(label: "Rifle: 2 hits, Rending AP+1"));
        player.Update(0.05f);

        var stack = player.GetDiceStack();
        Assert.That(stack, Has.Count.EqualTo(2), "the to-hit roll is still readable under the saves");
        Assert.That(stack[0].beat.Label, Is.EqualTo("Rifle: 3 hits"), "oldest first");
        Assert.That(stack[1].beat.Label, Is.EqualTo("Rifle: 2 hits, Rending AP+1"));
        Assert.That(stack[0].progress, Is.GreaterThan(stack[1].progress),
            "each panel runs its own envelope; the older one is further along");
    }

    [Test]
    public void TheStack_IsCapped_AndDropsTheOldest()
    {
        // A safety net rather than an everyday path: at full pacing a panel's 4.8s life spans about two
        // rolls, so the stack sits at 2-3. Driven here with held rolls, which queue without waiting.
        var player = new PresentationPlayer();
        for (int i = 0; i < 5; i++)
        {
            player.OnBeat(Dice(held: true, label: $"Roll {i}"));
            player.Update(0.05f);
        }

        var stack = player.GetDiceStack();
        Assert.That(stack, Has.Count.EqualTo(3), "a burst of rolls cannot bury the table");
        Assert.That(stack[0].beat.Label, Is.EqualTo("Roll 2"), "the oldest drop out...");
        Assert.That(stack[2].beat.Label, Is.EqualTo("Roll 4"), "...and the newest always survives");
    }

    [Test]
    public void InfoChips_BuyThePanelMoreTime()
    {
        // #245: chips are extra reading, so they stretch the envelope AND the linger.
        var player = new PresentationPlayer();
        player.OnBeat(Chippy());

        player.Update(PanelLife + 0.2f);
        Assert.That(player.GetDiceStack(), Has.Count.EqualTo(1),
            "past a plain panel's lifetime, still up");

        player.Update(1.5f);
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

        player.Update(PanelLife - 0.4f);
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
        player.Update(PanelLife + 0.1f);
        Assert.That(player.GetDiceStack(), Is.Empty, "and they resume ageing on release");
    }

    [Test]
    public void OverlappingAttacks_PlayConcurrently_InsteadOfTruncating()
    {
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
