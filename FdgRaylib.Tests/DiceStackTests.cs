using System.Collections.Generic;
using FDG;
using FDG.Presentation.Beats;
using FdgRaylib.Rendering;
using FdgRaylib.Rendering.Presentation;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #327 — a dice panel OUTLIVES its beat. The engine still paces each roll in full (the gap between
// rolls is the rhythm of the exchange, not dead time), but the panel stays up for seconds afterwards,
// so consecutive rolls overlap on screen and the player keeps a STACK of them (oldest first, drawn from
// the bottom anchor upward) instead of a single slot that the next roll evicts — the eviction is what
// made a two-threshold volley cut its own first roll short (engine ea91d68). Hovering the stack freezes
// every panel's timer.
//
// Also covers the concurrent attack LIST, which #327 introduced while rolls were briefly held and kept
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

    private static RollOffBeat RollOff(string label = "Map Side Roll-Off") =>
        new(label, new List<RollOffEntry>
        {
            new("Fragsteel", 5, ERollOffResult.Won),
            new("Tactician Bot 1", 2, ERollOffResult.Lost),
        });

    private static AttackBeat Attack(int volleys = 3) => new(isMelee: false,
        new List<Position> { new(0f, 0f) }, new List<Position> { new(5f, 5f) },
        volleyCount: volleys, armorPenetration: 0);

    // A normal roll: the engine paces its 1800ms envelope, then the panel lingers 3s more.
    private const float Paced        = 1.8f;
    private const float PanelLife    = 4.8f;

    // #344's slider is a static display setting, so every test runs at the default unless it says otherwise.
    [TearDown]
    public void ResetDiceLingerScale() => ViewSettings.DiceLingerScale = 1f;

    [Test]
    public void ARoll_OwnsTheActiveSlotForItsEnvelope_AndItsPanelOutlivesIt()
    {
        var player = new PresentationPlayer();
        player.OnBeat(Dice());
        player.Update(0.05f);

        Assert.That(player.GetRollStack(), Has.Count.EqualTo(1));
        Assert.That(player.IsAnimating, Is.True, "the engine is waiting for this roll, so the front-end "
            + "must be animating for exactly as long");

        player.Update(Paced);
        Assert.That(player.IsAnimating, Is.False, "the beat is done...");
        Assert.That(player.GetRollStack(), Has.Count.EqualTo(1), "...and the panel is still up. That "
            + "overhang is what makes panels overlap, and therefore what makes them stack.");

        player.Update(PanelLife - Paced);
        Assert.That(player.GetRollStack(), Is.Empty, "gone once its lifetime runs out");
    }

    [Test]
    public void AHeldRoll_ShowsWithoutGatingPrompts()
    {
        // The opt-in nothing uses today: pace the settle only. Kept covered so the path does not rot.
        var player = new PresentationPlayer();
        DiceRolledBeat beat = Dice(held: true);
        player.OnBeat(beat);
        player.Update(0.05f);

        Assert.That(player.GetRollStack(), Has.Count.EqualTo(1), "the panel is up...");
        Assert.That(player.IsAnimating, Is.False, "...but it never gates a prompt");

        player.Update((float)beat.HoldLeadIn.TotalSeconds);
        Assert.That(player.GetRollStack()[0].progress, Is.GreaterThanOrEqualTo(0.3f),
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

        var stack = player.GetRollStack();
        Assert.That(stack, Has.Count.EqualTo(2), "the to-hit roll is still readable under the saves");
        Assert.That(((DiceRolledBeat)stack[0].beat).Label, Is.EqualTo("Rifle: 3 hits"), "oldest first");
        Assert.That(((DiceRolledBeat)stack[1].beat).Label, Is.EqualTo("Rifle: 2 hits, Rending AP+1"));
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

        var stack = player.GetRollStack();
        Assert.That(stack, Has.Count.EqualTo(3), "a burst of rolls cannot bury the table");
        Assert.That(((DiceRolledBeat)stack[0].beat).Label, Is.EqualTo("Roll 2"), "the oldest drop out...");
        Assert.That(((DiceRolledBeat)stack[2].beat).Label, Is.EqualTo("Roll 4"), "...and the newest always survives");
    }

    // #344 — the Options slider scales the LINGER (the "let me re-read that" tail), never the paced part.
    // Scaling the paced part would retire the panel while the engine was still waiting on the roll it is
    // showing, i.e. the dice would vanish mid-tumble.
    [Test]
    public void TheDiceLingerSetting_ScalesTheLingerAndLeavesThePacedPartAlone()
    {
        const float Linger = PanelLife - Paced;   // 3.0s at 1.00x

        ViewSettings.DiceLingerScale = ViewSettings.DiceLingerMin;   // a third
        var quick = new PresentationPlayer();
        quick.OnBeat(Dice());
        quick.Update(Paced + 0.05f);
        Assert.That(quick.GetRollStack(), Has.Count.EqualTo(1),
            "the panel still outlives the engine's own wait at the shortest setting - the dice are never "
            + "pulled while the roll they show is still being paced");
        quick.Update(Linger / 3f);
        Assert.That(quick.GetRollStack(), Is.Empty, "and it clears in a third of the default linger");

        ViewSettings.DiceLingerScale = ViewSettings.DiceLingerMax;   // double
        var slow = new PresentationPlayer();
        slow.OnBeat(Dice());
        slow.Update(PanelLife + 0.05f);
        Assert.That(slow.GetRollStack(), Has.Count.EqualTo(1), "still up well past the default lifetime");
        slow.Update(Linger);
        Assert.That(slow.GetRollStack(), Is.Empty, "gone at paced + 2x linger");
    }

    [Test]
    public void TheDiceLingerSetting_IsClampedAgainstAStrayValue()
    {
        ViewSettings.DiceLingerScale = 0f;   // would otherwise retire the panel the frame it appeared
        var player = new PresentationPlayer();
        player.OnBeat(Dice());
        player.Update(Paced + 0.05f);

        Assert.That(player.GetRollStack(), Has.Count.EqualTo(1),
            "a 0 scale floors at the minimum rather than showing the dice for no time at all");
    }

    [Test]
    public void InfoChips_BuyThePanelMoreTime()
    {
        // #245: chips are extra reading, so they stretch the envelope AND the linger.
        var player = new PresentationPlayer();
        player.OnBeat(Chippy());

        player.Update(PanelLife + 0.2f);
        Assert.That(player.GetRollStack(), Has.Count.EqualTo(1),
            "past a plain panel's lifetime, still up");

        player.Update(1.5f);
        Assert.That(player.GetRollStack(), Is.Empty);
    }

    [Test]
    public void APanel_FadesInAndOut()
    {
        var player = new PresentationPlayer();
        player.OnBeat(Dice());

        player.Update(0.05f);
        Assert.That(player.GetRollStack()[0].alpha, Is.GreaterThan(0f).And.LessThan(1f), "mid fade-in");

        player.Update(0.15f);
        Assert.That(player.GetRollStack()[0].alpha, Is.EqualTo(1f), "solid once eased in");

        player.Update(PanelLife - 0.4f);
        Assert.That(player.GetRollStack()[0].alpha, Is.GreaterThan(0f).And.LessThan(1f),
            "fading over its tail instead of popping");
    }

    // #386 — hover freezes the LINGER, never the paced part. The engine waits out a roll's envelope in
    // real time regardless of the pointer, so a panel whose tumble froze under the cursor would show
    // dice still rolling while the wounds they caused are audibly being applied. The stack grows upward
    // toward a parked cursor (screen-recording sessions surfaced this), so the freeze is not a
    // deliberate-hover-only path and must never hold back the settle.
    [Test]
    public void Hovering_FreezesTheLinger_ButTheRollStillSettles()
    {
        var player = new PresentationPlayer();
        player.OnBeat(Dice());
        player.Update(0.2f);

        player.SetRollStackHovered(true);
        Assert.That(player.IsRollStackHovered, Is.True);
        player.Update(10f); // far past its lifetime

        Assert.That(player.GetRollStack(), Has.Count.EqualTo(1), "frozen panels do not expire");
        Assert.That(player.GetRollStack()[0].progress, Is.EqualTo(1f).Within(1e-4f),
            "but the paced part kept advancing — the dice settle in lockstep with the engine's own "
            + "wait even under a parked cursor");

        player.SetRollStackHovered(false);
        player.Update(PanelLife + 0.1f);
        Assert.That(player.GetRollStack(), Is.Empty, "and the linger resumes on release");
    }

    [Test]
    public void Hovering_MidLinger_HoldsThePanelThere()
    {
        // The re-read affordance #327 built the freeze for is intact: a settled panel holds under the
        // pointer at full strength instead of fading away mid-read.
        var player = new PresentationPlayer();
        player.OnBeat(Dice());
        player.Update(Paced + 0.5f);   // settled, mid-linger

        player.SetRollStackHovered(true);
        player.Update(10f); // far past its lifetime

        Assert.That(player.GetRollStack(), Has.Count.EqualTo(1), "held for as long as the pointer stays");
        Assert.That(player.GetRollStack()[0].alpha, Is.EqualTo(1f), "at full strength, not mid-fade");
    }

    [Test]
    public void ARollOff_JoinsTheSameStack_InsteadOfDrawingOverIt()
    {
        // Game start: the objective-count roll, then the first-turn roll-off. Both dock to the caption
        // zone, so before #327 the roll-off drew straight over the still-lingering dice panel.
        var player = new PresentationPlayer();
        player.OnBeat(Dice(label: "Roll for Objectives (D3 + 2)"));
        player.Update(Paced + 0.05f);
        player.OnBeat(RollOff("Map Side Roll-Off"));
        player.Update(0.05f);

        var stack = player.GetRollStack();
        Assert.That(stack, Has.Count.EqualTo(2), "both are up, stacked - not one over the other");
        Assert.That(stack[0].beat, Is.TypeOf<DiceRolledBeat>(), "the objective roll keeps the anchor");
        Assert.That(stack[1].beat, Is.TypeOf<RollOffBeat>(), "the roll-off queues above it");
    }

    [Test]
    public void ARollOffPanel_OutlivesItsBeat_LikeAnyOtherRoll()
    {
        var player = new PresentationPlayer();
        player.OnBeat(RollOff("Map Side Roll-Off"));

        player.Update(Paced + 0.05f);
        Assert.That(player.IsAnimating, Is.False, "the beat has finished pacing...");
        Assert.That(player.GetRollStack(), Has.Count.EqualTo(1), "...and the panel is still readable");

        player.Update(PanelLife);
        Assert.That(player.GetRollStack(), Is.Empty);
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
