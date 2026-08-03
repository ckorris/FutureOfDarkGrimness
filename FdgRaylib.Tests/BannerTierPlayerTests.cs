using System;
using System.Linq;
using FDG;
using FDG.Presentation.Beats;
using FdgRaylib.Rendering.Presentation;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #275 banner tiers - the app-side half of the contract. A Headline still holds the active slot and
// blocks everything behind it; a Notice or Toast frees it, so play carries on underneath while the words
// are still up. Mirrors CasualtyCascadePlayerTests, which pins the same "Held means its own track" shape
// for casualties.
//
// #325: every tier now STACKS rather than being replaced in its band, and a Headline's panel lingers
// past its beat so consecutive ones can overlap at all. The pacing is untouched - a Headline still stops
// the game for exactly as long as it did.
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

        Assert.That(player.GetBanners(), Has.Count.EqualTo(1));
        Assert.That(player.IsAnimating, Is.True,
            "a Headline is the one tier that still owns the active slot");
        Assert.That(player.GetActiveDeathBursts(), Is.Empty,
            "the beat behind it waits - a Headline stops the game, which is what makes it a Headline");
    }

    [Test]
    public void AHeadlinePanel_OutlivesItsBeat()
    {
        // #325: without an overhang two headlines could never be on screen together, since each blocks
        // for its whole duration - the linger is what makes the stack reachable.
        var player = new PresentationPlayer();
        player.OnBeat(Banner("Round 1", EBannerTier.Headline));

        player.Update(1.4f); // past the 1300ms duration
        Assert.That(player.IsAnimating, Is.False, "the game is moving again...");
        Assert.That(player.GetBanners(), Has.Count.EqualTo(1), "...and the words are still up");

        player.Update(1.6f); // past the 1.5s linger
        Assert.That(player.GetBanners(), Is.Empty);
    }

    [Test]
    public void ToastAndNotice_FreeTheActiveSlot_SoTheNextBeatPlaysUnderThem()
    {
        var player = new PresentationPlayer();
        player.OnBeat(Banner("Warriors embarked Rhino.", EBannerTier.Toast));
        player.OnBeat(Banner("Alice deploys first", EBannerTier.Notice));
        player.OnBeat(Death());

        player.Update(0.05f);

        Assert.That(player.GetBanners(), Has.Count.EqualTo(2),
            "both lower tiers animate concurrently");
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

        Assert.That(player.GetBanners(), Has.Count.EqualTo(1), "the toast is up...");
        Assert.That(player.IsAnimating, Is.False, "...and the game is not waiting on it");
    }

    [Test]
    public void EveryTierStacks_InsteadOfBeingReplaced()
    {
        // A Notice used to supersede the Notice before it outright. #325: it is pushed aside and dimmed
        // instead, like a dice panel - nothing is overwritten mid-read.
        var player = new PresentationPlayer();
        player.OnBeat(Banner("Alice deploys first", EBannerTier.Notice));
        player.OnBeat(Banner("Warriors embarked Rhino.", EBannerTier.Toast));
        player.OnBeat(Banner("Seer boosts their cast (+2).", EBannerTier.Toast));
        player.OnBeat(Banner("Bravo Squad fails morale - Shaken!", EBannerTier.Notice));

        player.Update(0.05f);

        var banners = player.GetBanners();
        Assert.That(banners, Has.Count.EqualTo(4), "two toasts AND two notices");
        Assert.That(banners.Count(b => b.beat.Tier == EBannerTier.Notice), Is.EqualTo(2),
            "the first notice survived the second");
        Assert.That(banners.Select(b => b.beat.BannerText).First(), Is.EqualTo("Alice deploys first"),
            "oldest first, so the overlay can anchor the oldest and stack newer ones above it");
    }

    [Test]
    public void ConsecutiveHeadlines_Stack()
    {
        var player = new PresentationPlayer();
        player.OnBeat(Banner("Deployment", EBannerTier.Headline));
        player.Update(1.4f);   // the first has finished pacing but is still lingering
        player.OnBeat(Banner("Round 1", EBannerTier.Headline));
        player.Update(0.05f);

        var banners = player.GetBanners();
        Assert.That(banners, Has.Count.EqualTo(2), "the new headline does not wipe out the last");
        Assert.That(banners[0].beat.BannerText, Is.EqualTo("Deployment"), "oldest first");
        Assert.That(banners[1].beat.BannerText, Is.EqualTo("Round 1"));
    }

    [Test]
    public void Banners_RetireAfterTheirOwnLifetime()
    {
        var player = new PresentationPlayer();
        player.OnBeat(Banner("Alice deploys first", EBannerTier.Notice));   // 900ms
        player.OnBeat(Banner("Warriors embarked Rhino.", EBannerTier.Toast)); // 2200ms

        player.Update(0.05f);
        Assert.That(player.GetBanners(), Has.Count.EqualTo(2));

        // Past the notice's 900ms but well short of the toast's 2200ms.
        for (int i = 0; i < 20; i++) player.Update(0.05f);
        var mid = player.GetBanners();
        Assert.That(mid, Has.Count.EqualTo(1), "the shorter notice retired on its own clock");
        Assert.That(mid[0].beat.Tier, Is.EqualTo(EBannerTier.Toast));

        for (int i = 0; i < 30; i++) player.Update(0.05f);
        Assert.That(player.GetBanners(), Is.Empty, "every banner eventually clears");
    }

    [Test]
    public void EachTierIsCappedSeparately_ByHowMuchScreenItEats()
    {
        // Five toasts are a ticker; five headlines would be the whole table.
        var player = new PresentationPlayer();
        for (int i = 0; i < 12; i++) player.OnBeat(Banner($"Caster {i} assists (+1).", EBannerTier.Toast));
        for (int i = 0; i < 6; i++) player.OnBeat(Banner($"Squad {i} fails morale", EBannerTier.Notice));
        player.Update(0.05f);

        var banners = player.GetBanners();
        Assert.That(banners.Count(b => b.beat.Tier == EBannerTier.Toast), Is.EqualTo(5));
        Assert.That(banners.Count(b => b.beat.Tier == EBannerTier.Notice), Is.EqualTo(3));
        Assert.That(banners.Last(b => b.beat.Tier == EBannerTier.Toast).beat.BannerText,
            Is.EqualTo("Caster 11 assists (+1)."), "the newest survive - the oldest drop off");
        Assert.That(banners.Last(b => b.beat.Tier == EBannerTier.Notice).beat.BannerText,
            Is.EqualTo("Squad 5 fails morale"));
    }

    [Test]
    public void OneTiersCap_DoesNotEvictAnother()
    {
        var player = new PresentationPlayer();
        player.OnBeat(Banner("Alice deploys first", EBannerTier.Notice));
        for (int i = 0; i < 12; i++) player.OnBeat(Banner($"Caster {i} assists (+1).", EBannerTier.Toast));
        player.Update(0.05f);

        Assert.That(player.GetBanners().Count(b => b.beat.Tier == EBannerTier.Notice), Is.EqualTo(1),
            "a burst of toasts must not push the notice off its own band");
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
            "diverting to the banner track must not cost a banner its voice");
    }
}
