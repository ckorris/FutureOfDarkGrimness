using System;
using System.Collections.Generic;
using System.Linq;
using FDG;
using FDG.Presentation.Beats;
using FdgRaylib.Rendering.Presentation;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #274 spell visuals - the app side of the cast presentation: the player's spell track (one beat at a
// time, in emission order), the per-variant sound cue mapping, and the multi-model stagger the overlay
// uses to make a unit ripple instead of flashing as one block.
[TestFixture]
public class SpellPresentationTests
{
    private static SpellEffectBeat Spell(ESpellVisual visual, int positions = 1,
        IReadOnlyList<Position>? sources = null, int magnitude = 0)
    {
        var pos = new List<Position>();
        for (int i = 0; i < positions; i++) pos.Add(new Position(10f + i, 10f));
        return new SpellEffectBeat(visual, pos, "Bless", sources, magnitude);
    }

    // ---- player track ----

    [Test]
    public void SpellBeat_BecomesActive_AndClearsWhenItsDurationElapses()
    {
        var player = new PresentationPlayer();
        player.OnBeat(Spell(ESpellVisual.CastSuccess));

        player.Update(0.05f);
        Assert.That(player.TryGetActiveSpell(out SpellEffectBeat beat, out float progress), Is.True);
        Assert.That(beat.Visual, Is.EqualTo(ESpellVisual.CastSuccess));
        Assert.That(progress, Is.GreaterThan(0f).And.LessThan(1f));

        // SpellCast is 700ms nominal; run well past it.
        for (int i = 0; i < 30; i++) player.Update(0.05f);
        Assert.That(player.TryGetActiveSpell(out _, out _), Is.False, "the effect clears when it finishes");
        Assert.That(player.IsAnimating, Is.False);
    }

    [Test]
    public void SpellBeats_PlayOneAtATime_InEmissionOrder()
    {
        // The whole point of the sequence is that the assist resolves, then the outcome, then the
        // landing - so these must never overlap the way casualties (Held) deliberately do.
        var player = new PresentationPlayer();
        player.OnBeat(Spell(ESpellVisual.AssistBoost));
        player.OnBeat(Spell(ESpellVisual.CastSuccess));
        player.OnBeat(Spell(ESpellVisual.TargetBoon));

        var seen = new List<ESpellVisual>();
        for (int i = 0; i < 60; i++)
        {
            player.Update(0.05f);
            if (player.TryGetActiveSpell(out SpellEffectBeat active, out _)
                && (seen.Count == 0 || seen[^1] != active.Visual))
            {
                seen.Add(active.Visual);
            }
        }

        Assert.That(seen, Is.EqualTo(new[]
        {
            ESpellVisual.AssistBoost, ESpellVisual.CastSuccess, ESpellVisual.TargetBoon,
        }), "each spell beat holds the active slot for its full duration, in order");
    }

    [Test]
    public void EverySpellBeat_FiresItsOwnSoundCue()
    {
        var player = new PresentationPlayer();
        var cues = new List<string?>();
        player.BeatStarted = b => cues.Add(PresentationSoundCues.CueFor(b));

        player.OnBeat(Spell(ESpellVisual.AssistHinder));
        player.OnBeat(Spell(ESpellVisual.CastFailure));
        for (int i = 0; i < 40; i++) player.Update(0.05f);

        Assert.That(cues, Is.EqualTo(new[]
        {
            PresentationSoundCues.SpellHinder, PresentationSoundCues.SpellFail,
        }));
    }

    // ---- cue mapping ----

    [Test]
    public void EveryVariant_MapsToADistinctRegisteredCue()
    {
        ESpellVisual[] visuals = Enum.GetValues<ESpellVisual>();
        string[] cues = visuals.Select(PresentationSoundCues.SpellCue).ToArray();

        Assert.That(cues.Distinct().Count(), Is.EqualTo(visuals.Length),
            "success/failure and boon/bane and boost/hinder must each be audibly different");

        string[] registered = PresentationSoundCues.AllCueKeys().ToArray();
        foreach (string cue in cues)
            Assert.That(registered, Does.Contain(cue), $"{cue} must be registered so a clip is loaded for it");
    }

    [Test]
    public void CueFor_RoutesTheBeatThroughTheSameVariantMapping()
    {
        foreach (ESpellVisual visual in Enum.GetValues<ESpellVisual>())
        {
            Assert.That(PresentationSoundCues.CueFor(Spell(visual)),
                Is.EqualTo(PresentationSoundCues.SpellCue(visual)));
        }
    }

    // ---- overlay stagger ----

    [Test]
    public void Stagger_RipplesAcrossModels_AndEveryModelCompletes()
    {
        // Early in the beat the first model is ahead of the last; by the end all have finished, so no
        // model is left mid-animation when the beat clears.
        Assert.That(SpellOverlay.Staggered(0.2f, 0, 5), Is.GreaterThan(SpellOverlay.Staggered(0.2f, 4, 5)),
            "the first model leads the last");
        for (int i = 0; i < 5; i++)
            Assert.That(SpellOverlay.Staggered(1f, i, 5), Is.EqualTo(1f), $"model {i} completes by the end");
    }

    [Test]
    public void Stagger_SingleModel_TracksTheBeatDirectly()
    {
        Assert.That(SpellOverlay.Staggered(0f, 0, 1), Is.EqualTo(0f));
        Assert.That(SpellOverlay.Staggered(0.5f, 0, 1), Is.EqualTo(0.5f));
        Assert.That(SpellOverlay.Staggered(1f, 0, 1), Is.EqualTo(1f));
    }

    [Test]
    public void Stagger_StaysInRange_ForEveryProgressAndIndex()
    {
        for (int count = 1; count <= 12; count++)
            for (int i = 0; i < count; i++)
                for (float p = 0f; p <= 1.001f; p += 0.05f)
                    Assert.That(SpellOverlay.Staggered(p, i, count), Is.InRange(0f, 1f));
    }
}
