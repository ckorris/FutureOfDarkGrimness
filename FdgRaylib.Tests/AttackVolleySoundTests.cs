using System.Collections.Generic;
using FDG;
using FDG.Presentation.Beats;
using FdgRaylib.Rendering.Presentation;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #238 — attacks sound once per volley, so a 3-attack weapon plays three cracks in step with its
// three visible bursts. VolleysStarted is the timing seam (volley v's slice opens at t = v/volleys,
// matching AttackOverlay). #239 — cues are voiced per weapon effect set, and volleys that LAND
// something also sound an impact when their shots arrive (ImpactsLanded, trailing the firing cue by
// the style's LandFraction).
[TestFixture]
public class AttackVolleySoundTests
{
    private static AttackBeat Beat(bool isMelee, int volleys, string? effect = null,
        float hit = 0f, float attacks = 0f) => new(isMelee,
        new List<Position> { new(0f, 0f) }, new List<Position> { new(5f, 5f) },
        volleyCount: volleys, armorPenetration: 0,
        weaponEffect: effect, hitCount: hit, attackCount: attacks);

    [Test]
    public void VolleysStarted_StepsOncePerVolleySlice()
    {
        Assert.That(PresentationPlayer.VolleysStarted(0f, 3), Is.EqualTo(1),
            "the first volley fires the moment the attack starts");
        Assert.That(PresentationPlayer.VolleysStarted(0.32f, 3), Is.EqualTo(1));
        Assert.That(PresentationPlayer.VolleysStarted(0.34f, 3), Is.EqualTo(2),
            "the second volley's slice opens at t = 1/3");
        Assert.That(PresentationPlayer.VolleysStarted(0.67f, 3), Is.EqualTo(3));
        Assert.That(PresentationPlayer.VolleysStarted(1f, 3), Is.EqualTo(3),
            "never more cues than volleys");
    }

    [Test]
    public void VolleysStarted_SingleAndZeroVolleyCounts_CueExactlyOnce()
    {
        Assert.That(PresentationPlayer.VolleysStarted(0.5f, 1), Is.EqualTo(1));
        Assert.That(PresentationPlayer.VolleysStarted(1f, 1), Is.EqualTo(1));
        // VolleyCount 0 is drawn as one volley by AttackOverlay (Math.Max(1, ...)) - sound matches.
        Assert.That(PresentationPlayer.VolleysStarted(1f, 0), Is.EqualTo(1));
    }

    [Test]
    public void ImpactsLanded_TrailsTheFiringCueByTheLandFraction()
    {
        // 3 volleys, landing 85% into each slice: volley 0 lands at t = 0.85/3 ≈ 0.283.
        Assert.That(PresentationPlayer.ImpactsLanded(0f, 3, 0.85f), Is.EqualTo(0),
            "nothing has landed the moment the attack starts");
        Assert.That(PresentationPlayer.ImpactsLanded(0.28f, 3, 0.85f), Is.EqualTo(0));
        Assert.That(PresentationPlayer.ImpactsLanded(0.29f, 3, 0.85f), Is.EqualTo(1));
        Assert.That(PresentationPlayer.ImpactsLanded(0.62f, 3, 0.85f), Is.EqualTo(2));
        Assert.That(PresentationPlayer.ImpactsLanded(1f, 3, 0.85f), Is.EqualTo(3),
            "every volley has landed by the end");
    }

    [Test]
    public void ImpactsLanded_BeamLandsAlmostImmediately()
    {
        Assert.That(PresentationPlayer.ImpactsLanded(0.06f, 2, 0.10f), Is.EqualTo(1),
            "a beam's impact leads the slice, not trails it");
        Assert.That(PresentationPlayer.ImpactsLanded(1f, 2, 0.10f), Is.EqualTo(2));
    }

    [Test]
    public void AttackBeats_CuePerVolley_NotAtBeatStart()
    {
        Assert.That(PresentationSoundCues.CueFor(Beat(isMelee: false, volleys: 3)), Is.Null,
            "a start-of-beat cue would double the first volley's shot");
    }

    [Test]
    public void VolleyCue_IsVoicedByTheEffectSet_WithDefaultsForNull()
    {
        Assert.That(PresentationSoundCues.VolleyCue(Beat(false, 3, "plasma-bolt")),
            Is.EqualTo("fire-plasma-bolt"));
        Assert.That(PresentationSoundCues.VolleyCue(Beat(true, 2, "energy-blade")),
            Is.EqualTo("melee-energy-blade"));
        Assert.That(PresentationSoundCues.VolleyCue(Beat(false, 3)),
            Is.EqualTo("fire-" + WeaponEffectCatalog.DefaultRangedKey),
            "a keyless beat gets the global default voice");
        Assert.That(PresentationSoundCues.VolleyCue(Beat(true, 2, "no-such-set")),
            Is.EqualTo("melee-" + WeaponEffectCatalog.DefaultMeleeKey),
            "an unknown key degrades to the default voice");
    }

    [Test]
    public void ImpactCue_MatchesTheEffectSet()
    {
        Assert.That(PresentationSoundCues.ImpactCue(Beat(false, 3, "missile-rocket", hit: 2f, attacks: 3f)),
            Is.EqualTo("impact-missile-rocket"));
        Assert.That(PresentationSoundCues.ImpactCue(Beat(true, 2, "claw-rend", hit: 1f, attacks: 2f)),
            Is.EqualTo("meleehit-claw-rend"));
    }
}
