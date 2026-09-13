using System;

namespace FdgRaylib.Audio;

/// <summary>
/// Tiny in-memory PCM tone generator for placeholder sound cues. Pure DSP — produces 16-bit mono
/// sample arrays from simple parameters (waveform, pitch glide, duration, decay) with no dependency on
/// Raylib, so it's trivially testable. <see cref="AudioManager"/> wraps the samples in a WAV and uploads
/// them; <see cref="FdgRaylib.Rendering.Presentation.PresentationSoundCues"/> defines the per-cue recipes.
///
/// Everything is deterministic (noise takes an explicit seed) so the generated cues are byte-identical
/// every run.
/// </summary>
public static class ToneSynth
{
    public const int SampleRate = 22050;

    public enum Waveform { Sine, Square, Triangle }

    /// <summary>
    /// A pitched tone that glides linearly from <paramref name="freqStart"/> to <paramref name="freqEnd"/>
    /// (pass them equal for a steady pitch) over <paramref name="seconds"/>, shaped by an exponential
    /// <paramref name="decay"/> envelope (higher = snappier). A ~3 ms fade-in removes the start click.
    /// </summary>
    public static short[] Tone(float freqStart, float freqEnd, float seconds, float decay,
        Waveform wave, float amp)
    {
        int count = Math.Max(1, (int)(SampleRate * seconds));
        var samples = new short[count];
        int attack = Math.Min(count, (int)(SampleRate * 0.003f));
        double phase = 0;

        for (int i = 0; i < count; i++)
        {
            float t = (float)i / SampleRate;
            float frac = count > 1 ? (float)i / (count - 1) : 0f;
            float freq = freqStart + (freqEnd - freqStart) * frac;
            phase += 2.0 * Math.PI * freq / SampleRate;

            float env = MathF.Exp(-decay * t);
            if (attack > 0 && i < attack) env *= (float)i / attack;

            samples[i] = ToSample(WaveValue(wave, phase) * env * amp);
        }
        return samples;
    }

    /// <summary>White-noise burst with the same exponential decay shaping — for cracks, rattles, clangs.
    /// <paramref name="seed"/> keeps it reproducible (and lets successive bursts differ).</summary>
    public static short[] Noise(float seconds, float decay, float amp, int seed)
    {
        int count = Math.Max(1, (int)(SampleRate * seconds));
        var samples = new short[count];
        int attack = Math.Min(count, (int)(SampleRate * 0.002f));
        var rng = new Random(seed);

        for (int i = 0; i < count; i++)
        {
            float t = (float)i / SampleRate;
            float env = MathF.Exp(-decay * t);
            if (attack > 0 && i < attack) env *= (float)i / attack;

            float v = (float)(rng.NextDouble() * 2.0 - 1.0);
            samples[i] = ToSample(v * env * amp);
        }
        return samples;
    }

    /// <summary>A run of silence, for spacing parts inside <see cref="Concat"/> (e.g. a dice rattle).</summary>
    public static short[] Silence(float seconds) => new short[Math.Max(0, (int)(SampleRate * seconds))];

    /// <summary>Concatenates parts back-to-back into one clip.</summary>
    public static short[] Concat(params short[][] parts)
    {
        int total = 0;
        foreach (var p in parts) total += p.Length;

        var result = new short[total];
        int offset = 0;
        foreach (var p in parts)
        {
            Array.Copy(p, 0, result, offset, p.Length);
            offset += p.Length;
        }
        return result;
    }

    private static float WaveValue(Waveform wave, double phase) => wave switch
    {
        Waveform.Square   => MathF.Sin((float)phase) >= 0f ? 1f : -1f,
        Waveform.Triangle => 2f / MathF.PI * MathF.Asin(MathF.Sin((float)phase)),
        _                 => MathF.Sin((float)phase),
    };

    private static short ToSample(float v) =>
        (short)Math.Clamp(v * short.MaxValue, short.MinValue, short.MaxValue);
}
