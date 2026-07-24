using System;
using FDG;
using FDG.Presentation.Beats;
using FdgRaylib.Rendering.Presentation;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #274 banner tiers - the app-side half of the contract. A Headline still holds the active slot and
// blocks everything behind it; a Notice or Toast transfers to the player's concurrent held-banner
// track instead, so play carries on underneath while the words are still up. Mirrors
// CasualtyCascadePlayerTests, which pins the same "Held means its own track" shape for casualties.
[TestFixture]
public class BannerTierPlayerTests
{
    private static readonly TextColor White = new(255, 255, 255, 255);

    private static BannerBeat Banner(string text, EBannerTier tier) => new(text, White, tier);

    private static ModelDiedBeat Death() => new(
        new ModelID(Guid.NewGuid()), new UnitID(Guid.NewGuid()), "Unit", new Position(1f, 1f), false);

    [Test]
    public void Headline_HoldsTheActiveSlot_AndBlocksWhatFollows()
    {
        var player = new PresentationPlayer();
        player.OnBeat(Banner("Deployment", EBannerTier.Headline));
        player.OnBeat(Death());

        player.Update(0.05f);

        Assert.That(player.TryGetActiveBanner(out _, out _), Is.True,
            "a Headline is the one tier that still owns the active slot");
        Assert.That(player.GetActiveDeathBursts(), Is.Empty,
            "the beat behind it waits - a Headline stops the game, which is what makes it a Headline");
        Assert.That(player.GetHeldBanners(), Is.Empty, "a Headline never joins the held track");
    }

    [Test]
    public void ToastAndNotice_FreeTheActiveSlot_SoTheNextBeatPlaysUnderThem()
    {
        var player = new PresentationPlayer();
        player.OnBeat(Banner("Warriors embarked Rhino.", EBannerTier.Toast));
        player.OnBeat(Banner("Alice deploys first", EBannerTier.Notice));
        player.OnBeat(Death());

        player.Update(0.05f);

        Assert.That(player.GetHeldBanners(), Has.Count.EqualTo(2),
            "both lower tiers animate concurrently on the held track");
        Assert.That(player.TryGetActiveBanner(out _, out _), Is.False,
            "neither reached the active slot");
        Assert.That(player.GetActiveDeathBursts(), Has.Count.EqualTo(1),
            "the death behind them started immediately instead of queueing");
    }

    [Test]
    public void HeldBanners_DoNotGateInteractivePrompts()
    {
        // IsAnimating is what holds resolver prompts back. A message that does not stop the engine must
        // not stop the player from acting either, or "non-blocking" would only be half true.
        var player = new PresentationPlayer();
        player.OnBeat(Banner("Warriors embarked Rhino.", EBannerTier.Toast));

        player.Update(0.05f);

        Assert.That(player.GetHeldBanners(), Has.Count.EqualTo(1), "the toast is up...");
        Assert.That(player.IsAnimating, Is.False, "...and the game is not waiting on it");
    }

    [Test]
    public void ANewNotice_SupersedesThePreviousOne_ButToastsStack()
    {
        var player = new PresentationPlayer();
        player.OnBeat(Banner("Alice deploys first", EBannerTier.Notice));
        player.OnBeat(Banner("Warriors embarked Rhino.", EBannerTier.Toast));
        player.OnBeat(Banner("Seer boosts their cast (+2).", EBannerTier.Toast));
        player.OnBeat(Banner("Bravo Squad fails morale - Shaken!", EBannerTier.Notice));

        player.Update(0.05f);

        var held = player.GetHeldBanners();
        Assert.That(held, Has.Count.EqualTo(3), "two toasts stacked, one notice survived");
        Assert.That(held.Count(h => h.beat.Tier == EBannerTier.Notice), Is.EqualTo(1),
            "the notice band holds one statement at a time - the newer one wins");
        Assert.That(held.Single(h => h.beat.Tier == EBannerTier.Notice).beat.BannerText,
            Is.EqualTo("Bravo Squad fails morale - Shaken!"));
    }

    [Test]
    public void HeldBanners_RetireAfterTheirOwnFullDuration()
    {
        var player = new PresentationPlayer();
        player.OnBeat(Banner("Alice deploys first", EBannerTier.Notice));   // 900ms
        player.OnBeat(Banner("Warriors embarked Rhino.", EBannerTier.Toast)); // 2200ms

        player.Update(0.05f);
        Assert.That(player.GetHeldBanners(), Has.Count.EqualTo(2));

        // Past the notice's 900ms but well short of the toast's 2200ms.
        for (int i = 0; i < 20; i++) player.Update(0.05f);
        var mid = player.GetHeldBanners();
        Assert.That(mid, Has.Count.EqualTo(1), "the shorter notice retired on its own clock");
        Assert.That(mid[0].beat.Tier, Is.EqualTo(EBannerTier.Toast));

        for (int i = 0; i < 30; i++) player.Update(0.05f);
        Assert.That(player.GetHeldBanners(), Is.Empty, "every banner eventually clears");
    }

    [Test]
    public void ToastStack_IsBounded_SoABurstCannotFillTheScreen()
    {
        var player = new PresentationPlayer();
        for (int i = 0; i < 12; i++) player.OnBeat(Banner($"Caster {i} assists (+1).", EBannerTier.Toast));

        player.Update(0.05f);

        var held = player.GetHeldBanners();
        Assert.That(held, Has.Count.EqualTo(5), "the stack is capped");
        Assert.That(held[^1].beat.BannerText, Is.EqualTo("Caster 11 assists (+1)."),
            "the newest survive - the oldest drop off the top");
    }

    [Test]
    public void EachTier_HasItsOwnSoundCue()
    {
        Assert.That(PresentationSoundCues.CueFor(Banner("Deployment", EBannerTier.Headline)),
            Is.EqualTo(PresentationSoundCues.BannerHeadline));
        Assert.That(PresentationSoundCues.CueFor(Banner("Alice deploys first", EBannerTier.Notice)),
            Is.EqualTo(PresentationSoundCues.BannerNotice));
        Assert.That(PresentationSoundCues.CueFor(Banner("Warriors embarked.", EBannerTier.Toast)),
            Is.EqualTo(PresentationSoundCues.BannerToast));
    }

    [Test]
    public void HeldBanners_StillFireTheirSoundCueAsTheyStart()
    {
        var player = new PresentationPlayer();
        int cues = 0;
        player.BeatStarted = _ => cues++;

        player.OnBeat(Banner("Warriors embarked Rhino.", EBannerTier.Toast));
        player.OnBeat(Banner("Alice deploys first", EBannerTier.Notice));
        player.Update(0.05f);

        Assert.That(cues, Is.EqualTo(2),
            "diverting to the held track must not cost a banner its voice");
    }
}
