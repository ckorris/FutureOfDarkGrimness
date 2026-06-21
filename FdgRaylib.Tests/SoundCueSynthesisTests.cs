using FdgRaylib.Audio;
using FdgRaylib.Rendering.Presentation;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// Covers the synthesized placeholder cues: the ToneSynth DSP primitives and the per-cue recipes that
// give each beat a distinct voice before real .wav assets exist.
[TestFixture]
public class SoundCueSynthesisTests
{
    [Test]
    public void Tone_HasExpectedLength_AndStaysInRange()
    {
        short[] s = ToneSynth.Tone(440f, 440f, 0.10f, 12f, ToneSynth.Waveform.Sine, 0.5f);

        Assert.That(s.Length, Is.EqualTo((int)(ToneSynth.SampleRate * 0.10f)));
        // amp 0.5 with a fade-in/decay envelope never reaches full scale.
        foreach (short sample in s)
            Assert.That(System.Math.Abs((int)sample), Is.LessThanOrEqualTo(short.MaxValue));
        Assert.That(System.Array.Exists(s, x => x != 0), Is.True, "a tone should not be silent");
    }

    [Test]
    public void Noise_IsDeterministicForAGivenSeed()
    {
        short[] a = ToneSynth.Noise(0.05f, 40f, 0.5f, seed: 7);
        short[] b = ToneSynth.Noise(0.05f, 40f, 0.5f, seed: 7);
        short[] c = ToneSynth.Noise(0.05f, 40f, 0.5f, seed: 8);

        Assert.That(a, Is.EqualTo(b), "same seed -> identical samples");
        Assert.That(a, Is.Not.EqualTo(c), "different seed -> different samples");
    }

    [Test]
    public void Concat_LengthIsSumOfParts()
    {
        short[] one = ToneSynth.Silence(0.01f);
        short[] two = ToneSynth.Tone(300f, 300f, 0.02f, 10f, ToneSynth.Waveform.Sine, 0.3f);

        Assert.That(ToneSynth.Concat(one, two).Length, Is.EqualTo(one.Length + two.Length));
    }

    [Test]
    public void EveryCue_HasAudibleAndDistinctPlaceholder()
    {
        string[] cues =
        {
            PresentationSoundCues.Gunshot, PresentationSoundCues.Melee, PresentationSoundCues.Dice,
            PresentationSoundCues.Save, PresentationSoundCues.Wound, PresentationSoundCues.Death,
            PresentationSoundCues.Banner, PresentationSoundCues.Move,
        };

        var clips = new short[cues.Length][];
        for (int i = 0; i < cues.Length; i++)
        {
            clips[i] = PresentationSoundCues.PlaceholderSamples(cues[i]);
            Assert.That(clips[i].Length, Is.GreaterThan(0), $"{cues[i]} should produce samples");
            Assert.That(System.Array.Exists(clips[i], x => x != 0), Is.True, $"{cues[i]} should be audible");
        }

        // No two beats should sound identical.
        for (int i = 0; i < clips.Length; i++)
            for (int j = i + 1; j < clips.Length; j++)
                Assert.That(clips[i], Is.Not.EqualTo(clips[j]), $"{cues[i]} and {cues[j]} should differ");
    }
}
