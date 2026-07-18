using System;
using System.Collections.Generic;
using FDG;
using FDG.Presentation.Beats;
using FdgRaylib.Rendering.Presentation;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #232 casualty cascade - overlapped (Held) death beats transfer to the player's concurrent cascade
// track instead of serializing through the active slot: all of a volley's deaths animate at once,
// each over its own full duration, and each still fires its BeatStarted sound cue as it begins.
[TestFixture]
public class CasualtyCascadePlayerTests
{
    private static ModelDiedBeat Death(bool overlap) => new(
        new ModelID(Guid.NewGuid()), new UnitID(Guid.NewGuid()), "Unit",
        new Position(1f, 1f), overlap);

    [Test]
    public void OverlappedDeaths_AnimateConcurrently_AndAllFinish()
    {
        var player = new PresentationPlayer();
        int cues = 0;
        player.BeatStarted = _ => cues++;

        player.OnBeat(Death(overlap: true));
        player.OnBeat(Death(overlap: true));
        player.OnBeat(Death(overlap: false));

        player.Update(0.05f);
        Assert.That(player.GetActiveDeathBursts(), Has.Count.EqualTo(3),
            "all three deaths animate at once - overlapped ones never queue behind each other");
        Assert.That(cues, Is.EqualTo(3), "each death still fires its own sound cue");
        Assert.That(player.IsAnimating, Is.True);

        // ModelDeath is 500ms nominal; run well past it so every animation completes.
        for (int i = 0; i < 40; i++) player.Update(0.05f);
        Assert.That(player.GetActiveDeathBursts(), Is.Empty, "every death animation completed");
        Assert.That(player.IsAnimating, Is.False);
    }

    [Test]
    public void OverlappedDeath_FreesTheActiveSlot_ForFollowingBeats()
    {
        var player = new PresentationPlayer();
        player.OnBeat(Death(overlap: true));
        player.OnBeat(new BannerBeat("Next", new TextColor(255, 255, 255, 255)));

        player.Update(0.05f);
        Assert.That(player.TryGetActiveBanner(out _, out _), Is.True,
            "a following beat starts immediately - the overlapped death does not hold the active slot");
    }
}
