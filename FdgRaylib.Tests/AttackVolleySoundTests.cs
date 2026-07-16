using System.Collections.Generic;
using FDG;
using FDG.Presentation.Beats;
using FdgRaylib.Rendering.Presentation;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #238 — attacks sound once per volley, so a 3-attack weapon plays three cracks in step with its
// three visible bursts. VolleysStarted is the timing seam (volley v's slice opens at t = v/volleys,
// matching AttackOverlay); the cue mapping moves from CueFor (beat start) to VolleyCue.
[TestFixture]
public class AttackVolleySoundTests
{
    private static AttackBeat Beat(bool isMelee, int volleys) => new(isMelee,
        new List<Position> { new(0f, 0f) }, new List<Position> { new(5f, 5f) },
        volleyCount: volleys, armorPenetration: 0);

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
    public void AttackBeats_CuePerVolley_NotAtBeatStart()
    {
        Assert.That(PresentationSoundCues.CueFor(Beat(isMelee: false, volleys: 3)), Is.Null,
            "a start-of-beat cue would double the first volley's shot");
        Assert.That(PresentationSoundCues.VolleyCue(Beat(isMelee: false, volleys: 3)),
            Is.EqualTo(PresentationSoundCues.Gunshot));
        Assert.That(PresentationSoundCues.VolleyCue(Beat(isMelee: true, volleys: 2)),
            Is.EqualTo(PresentationSoundCues.Melee));
    }
}
