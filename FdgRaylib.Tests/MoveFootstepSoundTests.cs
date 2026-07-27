using System;
using System.Collections.Generic;
using FDG;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FdgRaylib.Audio;
using FdgRaylib.Rendering.Presentation;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #294 - movement is voiced as a run of footfalls across the glide rather than one blip at its start.
// StepsStarted is the timing seam (step s's slice opens at t = s/steps, the same shape as
// VolleysStarted); StepVoice is the voicing seam (one cue, pitched down by the unit's weight and
// alternated foot to foot). The whole point is subtlety, so the two properties under test are that
// more models add steps SUB-linearly and that heavier units step lower and less often.
[TestFixture]
public class MoveFootstepSoundTests
{
    private static UnitMovedBeat Beat(int models, int toughness = 1, int durationMs = 600)
    {
        var moves = new List<ModelMove>(models);
        for (int i = 0; i < models; i++)
            moves.Add(new ModelMove(new ModelID(Guid.NewGuid()),
                new List<Position> { new(i, 0f), new(i, 6f) }));

        return new UnitMovedBeat(new UnitID(Guid.NewGuid()), "Warriors", moves,
            TimeSpan.FromMilliseconds(durationMs), toughness);
    }

    [Test]
    public void MoveBeats_CuePerFootfall_NotAtBeatStart()
    {
        Assert.That(PresentationSoundCues.CueFor(Beat(models: 5)), Is.Null,
            "a start-of-beat cue would stack the old single blip on top of the first footfall");
    }

    [Test]
    public void StepsStarted_StepsOncePerSlice()
    {
        // 5 models over a 600ms Advance works out to 3 steps; each opens at t = s/3.
        const float dur = 0.6f;
        Assert.That(PresentationPlayer.StepsStarted(0f, dur, 5, 1), Is.EqualTo(1),
            "the first footfall lands the moment the unit starts moving");
        Assert.That(PresentationPlayer.StepsStarted(0.32f, dur, 5, 1), Is.EqualTo(1));
        Assert.That(PresentationPlayer.StepsStarted(0.34f, dur, 5, 1), Is.EqualTo(2));
        Assert.That(PresentationPlayer.StepsStarted(0.67f, dur, 5, 1), Is.EqualTo(3));
        Assert.That(PresentationPlayer.StepsStarted(1f, dur, 5, 1), Is.EqualTo(3),
            "never more cues than the glide has steps");
    }

    [Test]
    public void StepsStarted_ShortestMoveStillMakesExactlyOneSound()
    {
        // MoveMin (200ms) by one model: below one step's worth of cadence, but a move that makes no
        // sound at all would be a regression from the single blip this replaced.
        float min = (float)PresentationDurations.MoveMin.TotalSeconds;
        Assert.That(PresentationPlayer.StepsStarted(1f, min, 1, 1), Is.EqualTo(1));
        Assert.That(PresentationPlayer.StepsStarted(0f, min, 1, 1), Is.EqualTo(1));
    }

    [Test]
    public void MoreModelsAddStepsSubLinearly()
    {
        const float dur = 1.2f;
        int solo  = PresentationPlayer.StepsStarted(1f, dur, 1,  1);
        int squad = PresentationPlayer.StepsStarted(1f, dur, 5,  1);
        int horde = PresentationPlayer.StepsStarted(1f, dur, 20, 1);

        Assert.That(squad, Is.GreaterThan(solo), "a squad should sound busier underfoot than a scout");
        Assert.That(horde, Is.GreaterThan(squad));
        Assert.That(horde, Is.LessThan(solo * 20),
            "20x the models must not mean 20x the beeps - that is the whole point of the curve");
        Assert.That(horde, Is.LessThanOrEqualTo(9), "hard ceiling on footfalls per move");
    }

    [Test]
    public void CadenceIsCappedSoAHordeNeverMachineGuns()
    {
        // Well past the cap on both axes: a 5s move by 40 models still tops out at the backstop.
        Assert.That(PresentationPlayer.StepsStarted(1f, 5f, 40, 1), Is.EqualTo(9));
        Assert.That(PresentationPlayer.StepsPerSecond(40, 1), Is.EqualTo(6f).Within(0.001f));
        Assert.That(PresentationPlayer.StepsPerSecond(200, 1),
            Is.EqualTo(PresentationPlayer.StepsPerSecond(40, 1)).Within(0.001f));
    }

    [Test]
    public void HeavyUnitsStepLessOften()
    {
        Assert.That(PresentationPlayer.StepsPerSecond(1, 12),
            Is.LessThan(PresentationPlayer.StepsPerSecond(1, 1)),
            "a monolith takes fewer, longer strides than a trooper");
        Assert.That(PresentationPlayer.StepsStarted(1f, 1.2f, 1, 12),
            Is.LessThan(PresentationPlayer.StepsStarted(1f, 1.2f, 1, 1)));
        // ...but never grinds to a halt.
        Assert.That(PresentationPlayer.StepsPerSecond(1, 99), Is.GreaterThan(1.3f));
    }

    [Test]
    public void StepVoice_PitchesDownWithToughness_AndBottomsOut()
    {
        float light  = PresentationSoundCues.StepVoice(Beat(5, toughness: 1),  0).Pitch;
        float medium = PresentationSoundCues.StepVoice(Beat(5, toughness: 6),  0).Pitch;
        float heavy  = PresentationSoundCues.StepVoice(Beat(1, toughness: 12), 0).Pitch;
        float titan  = PresentationSoundCues.StepVoice(Beat(1, toughness: 24), 0).Pitch;

        Assert.That(light, Is.EqualTo(1f).Within(0.001f), "an ordinary model plays the clip as recorded");
        Assert.That(medium, Is.LessThan(light));
        Assert.That(heavy, Is.LessThan(medium));
        Assert.That(titan, Is.EqualTo(PresentationSoundCues.StepMinPitch).Within(0.001f),
            "the floor keeps a heavy tread from turning into a groan");
        Assert.That(heavy, Is.GreaterThanOrEqualTo(PresentationSoundCues.StepMinPitch));
    }

    [Test]
    public void StepVoice_AlternatesFeet()
    {
        var lead = PresentationSoundCues.StepVoice(Beat(5), 0);
        var off  = PresentationSoundCues.StepVoice(Beat(5), 1);
        var next = PresentationSoundCues.StepVoice(Beat(5), 2);

        Assert.That(off.Pitch,  Is.LessThan(lead.Pitch),  "the off foot lands a touch lower");
        Assert.That(off.Volume, Is.LessThan(lead.Volume), "...and a touch softer");
        Assert.That(next.Pitch, Is.EqualTo(lead.Pitch).Within(0.0001f),
            "feet alternate rather than drifting - step 2 matches step 0");
    }

    [Test]
    public void StepVoice_StaysSubtle_AndAlwaysNamesTheOneStepCue()
    {
        foreach (int tough in new[] { 1, 3, 6, 12, 24 })
            foreach (int step in new[] { 0, 1, 2, 3 })
            {
                var (cue, pitch, volume) = PresentationSoundCues.StepVoice(Beat(5, tough), step);

                Assert.That(cue, Is.EqualTo(PresentationSoundCues.Step),
                    "one cue key, voiced many ways - a real step.wav must tier itself");
                Assert.That(volume, Is.InRange(0.5f, 1f),
                    "footfalls sit under the action; over 1.0 would clip the master mix");
                Assert.That(pitch, Is.InRange(PresentationSoundCues.StepMinPitch, 1f));
            }
    }

    [Test]
    public void StepPlaceholder_IsQuieterThanTheSingleBlipItReplaced()
    {
        short[] step = PresentationSoundCues.PlaceholderSamples(PresentationSoundCues.Step);

        int peak = 0;
        foreach (short s in step) peak = Math.Max(peak, Math.Abs((int)s));

        Assert.That(step.Length, Is.GreaterThan(0), "the step cue must be audible");
        // The retired "move" blip ran amp 0.20; a cue that plays up to 9 times per move must sit under
        // it, or repetition turns a subtle patter into an alarm.
        Assert.That(peak, Is.LessThan((int)(short.MaxValue * 0.20f)),
            "a repeated cue must be quieter than the one-shot it replaced");
        Assert.That(step.Length, Is.LessThan((int)(ToneSynth.SampleRate * 0.10f)),
            "and shorter, so consecutive steps never smear into each other");
    }
}
