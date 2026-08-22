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
/// Two ways to register a cue: <see cref="Load"/> a real asset file, or <see cref="LoadSynthesized"/>
/// an in-memory PCM clip (e.g. a <see cref="ToneSynth"/> placeholder). Callers typically try the file
/// first and fall back to a synthesized clip, so the whole cue pipeline is audible end-to-end before any
/// real assets exist — drop a real file at the mapped path later and it takes over with no code change.
/// </para>
///
/// <para>Not thread-safe: call <see cref="Load"/>/<see cref="LoadSynthesized"/>/<see cref="Play"/>/
/// <see cref="Dispose"/> from the render (main) thread only, where Raylib audio calls are valid.</para>
/// </summary>
public sealed class AudioManager : IDisposable
{
    private readonly Dictionary<string, Sound> _sounds = new();
    private readonly bool _enabled;

    /// <summary>True once the audio device initialized successfully; false makes every op a no-op.</summary>
    public bool Enabled => _enabled;

    // Last value handed to Raylib.SetMasterVolume, so the Options slider can show the current level.
    // Raylib's device default is 1.0 (full).
    private float _masterVolume = 1f;

    /// <summary>Current master volume, 0..1 (1 = full). Set via <see cref="SetMasterVolume"/>.</summary>
    public float MasterVolume => _masterVolume;

    public AudioManager()
    {
        Raylib.InitAudioDevice();
        _enabled = Raylib.IsAudioDeviceReady();
    }

    /// <summary>
    /// Sets the global playback volume for every cue (clamped to 0..1). No-op if audio is unavailable,
    /// but the value is still remembered so the UI stays consistent.
    /// </summary>
    public void SetMasterVolume(float volume)
    {
        _masterVolume = Math.Clamp(volume, 0f, 1f);
        if (_enabled) Raylib.SetMasterVolume(_masterVolume);
    }

    /// <summary>
    /// Registers the asset file at <paramref name="path"/> under <paramref name="key"/>. Returns true if
    /// the file existed and loaded; false (no registration) if it was missing or audio is unavailable, so
    /// the caller can supply a synthesized fallback via <see cref="LoadSynthesized"/>.
    /// </summary>
    public bool Load(string key, string path)
    {
        if (!_enabled || !File.Exists(path)) return false;
        _sounds[key] = Raylib.LoadSound(path);
        return true;
    }

    /// <summary>
    /// Registers an in-memory 16-bit mono PCM clip under <paramref name="key"/> (used for the generated
    /// placeholder cues). The wave is uploaded to the audio device and freed immediately afterward.
    /// </summary>
    public void LoadSynthesized(string key, short[] samples, int sampleRate)
    {
        if (!_enabled) return;
        byte[] wav = BuildPcmWav(samples, sampleRate, channels: 1);
        Wave wave = LoadWaveBytes(".wav", wav);
        _sounds[key] = Raylib.LoadSoundFromWave(wave);
        Raylib.UnloadWave(wave);
    }

    /// <summary>Plays the sound registered under <paramref name="key"/> (no-op if unknown/disabled).</summary>
    public void Play(string key)
    {
        if (!_enabled) return;
        if (_sounds.TryGetValue(key, out var sound))
            Raylib.PlaySound(sound);
    }

    /// <summary>
    /// Plays <paramref name="key"/> shifted in pitch and scaled in volume — one cue voiced many ways
    /// (#294: footfalls pitch down with a unit's weight, and alternate slightly foot to foot). Pitch is
    /// a playback-rate multiplier, so a lower pitch also plays LONGER: 0.6 turns a 60 ms tick into a
    /// 100 ms thump, which is the heavier sound we want anyway. Volume multiplies the master level.
    ///
    /// <para>Both settings persist on the cached <see cref="Sound"/>, so every call sets both rather
    /// than inheriting whatever the last one left behind. Re-playing a cue restarts it (Raylib gives
    /// each <c>Sound</c> one voice), which is fine at footstep cadence — steps never overlap.</para>
    /// </summary>
    public void Play(string key, float pitch, float volume)
    {
        if (!_enabled) return;
        if (!_sounds.TryGetValue(key, out var sound)) return;

        Raylib.SetSoundPitch(sound, Math.Clamp(pitch, 0.05f, 4f));
        Raylib.SetSoundVolume(sound, Math.Clamp(volume, 0f, 1f));
        Raylib.PlaySound(sound);
    }

    public void Dispose()
    {
        if (!_enabled) return;
        foreach (var sound in _sounds.Values)
            Raylib.UnloadSound(sound);
        _sounds.Clear();
        Raylib.CloseAudioDevice();
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
