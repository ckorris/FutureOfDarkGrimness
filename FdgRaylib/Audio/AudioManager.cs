using System;
using System.Collections.Generic;
using System.IO;
using Raylib_cs;

namespace FdgRaylib.Audio;

/// <summary>
/// App-wide sound manager: owns the Raylib audio device, loads/caches sounds by string key, and
/// plays them by key. General-purpose — presentation cues, UI clicks, menu stings can all share one
/// instance. GUI-only (the renderer builds it after the window opens; headless never does), and safe
/// to use even when no audio device is available, in which case every operation no-ops.
///
/// <para>
/// Missing asset files fall back to a built-in in-memory placeholder tone, so the whole cue pipeline
/// is audible end-to-end before any real assets exist — drop a real file at the mapped path later and
/// it takes over with no code change.
/// </para>
///
/// <para>Not thread-safe: call <see cref="Load"/>/<see cref="Play"/>/<see cref="Dispose"/> from the
/// render (main) thread only, where Raylib audio calls are valid.</para>
/// </summary>
public sealed class AudioManager : IDisposable
{
    private readonly Dictionary<string, Sound> _sounds = new();
    private readonly HashSet<string> _placeholderKeys = new();
    private readonly bool _enabled;

    private Wave _placeholderWave;
    private Sound _placeholder;
    private readonly bool _hasPlaceholder;

    /// <summary>True once the audio device initialized successfully; false makes every op a no-op.</summary>
    public bool Enabled => _enabled;

    public AudioManager()
    {
        Raylib.InitAudioDevice();
        _enabled = Raylib.IsAudioDeviceReady();
        if (!_enabled) return;

        _placeholderWave = GeneratePlaceholderWave();
        _placeholder = Raylib.LoadSoundFromWave(_placeholderWave);
        _hasPlaceholder = true;
    }

    /// <summary>
    /// Registers a sound under <paramref name="key"/>. Loads the file at <paramref name="path"/> if it
    /// exists; otherwise the key resolves to the built-in placeholder so the cue still fires.
    /// </summary>
    public void Load(string key, string path)
    {
        if (!_enabled) return;
        if (File.Exists(path))
            _sounds[key] = Raylib.LoadSound(path);
        else if (_hasPlaceholder)
            _placeholderKeys.Add(key);
    }

    /// <summary>Plays the sound registered under <paramref name="key"/> (no-op if unknown/disabled).</summary>
    public void Play(string key)
    {
        if (!_enabled) return;
        if (_sounds.TryGetValue(key, out var sound))
            Raylib.PlaySound(sound);
        else if (_hasPlaceholder && _placeholderKeys.Contains(key))
            Raylib.PlaySound(_placeholder);
    }

    public void Dispose()
    {
        if (!_enabled) return;
        foreach (var sound in _sounds.Values)
            Raylib.UnloadSound(sound);
        _sounds.Clear();
        if (_hasPlaceholder)
        {
            Raylib.UnloadSound(_placeholder);
            Raylib.UnloadWave(_placeholderWave);
        }
        Raylib.CloseAudioDevice();
    }

    // A short, soft percussive blip (sine with exponential decay) used for any cue whose real asset
    // file is missing. Built as an in-memory WAV so no binary placeholder needs to ship.
    private static Wave GeneratePlaceholderWave()
    {
        const int sampleRate = 22050;
        const float seconds = 0.10f;
        const float freq = 600f;
        int count = (int)(sampleRate * seconds);

        var samples = new short[count];
        for (int i = 0; i < count; i++)
        {
            float t = (float)i / sampleRate;
            float env = MathF.Exp(-22f * t);                  // fast decay → "click", not a tone
            float s = MathF.Sin(2f * MathF.PI * freq * t) * env * 0.35f;
            samples[i] = (short)Math.Clamp(s * short.MaxValue, short.MinValue, short.MaxValue);
        }

        byte[] wav = BuildPcmWav(samples, sampleRate, channels: 1);
        return LoadWaveBytes(".wav", wav);
    }

    // LoadWaveFromMemory in this binding is pointer-only; pin a null-terminated type string and the
    // data and hand over the raw pointers.
    private static unsafe Wave LoadWaveBytes(string fileType, byte[] data)
    {
        byte[] ftype = System.Text.Encoding.ASCII.GetBytes(fileType + '\0');
        fixed (byte* ft = ftype)
        fixed (byte* d = data)
            return Raylib.LoadWaveFromMemory((sbyte*)ft, d, data.Length);
    }

    // Minimal canonical 16-bit PCM WAV container around the given samples.
    private static byte[] BuildPcmWav(short[] samples, int sampleRate, int channels)
    {
        const int bitsPerSample = 16;
        int blockAlign = channels * bitsPerSample / 8;
        int byteRate = sampleRate * blockAlign;
        int dataSize = samples.Length * sizeof(short);

        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8.ToArray());
        w.Write(36 + dataSize);
        w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray());
        w.Write(16);                          // fmt chunk size
        w.Write((short)1);                    // PCM
        w.Write((short)channels);
        w.Write(sampleRate);
        w.Write(byteRate);
        w.Write((short)blockAlign);
        w.Write((short)bitsPerSample);
        w.Write("data"u8.ToArray());
        w.Write(dataSize);
        foreach (short s in samples) w.Write(s);
        w.Flush();
        return ms.ToArray();
    }
}
