using FdgRaylib.Audio;

namespace FdgRaylib.Rendering;

/// <summary>
/// App-wide facade for playing UI chrome sounds (button clicks, toggles, canvas commits). A thin static
/// front over the renderer's <see cref="AudioManager"/> so any screen or resolver can voice an
/// interaction without being handed the audio device - mirrors how screens already reach for
/// <see cref="RaylibRenderer.MenuFont"/>. GUI-only; safe (no-op) before <see cref="Attach"/> runs or
/// when no audio device is available.
///
/// <para>Main-thread only: <see cref="AudioManager"/> requires it, and every caller (ImGui button draws,
/// resolver Draw) already runs on the render thread.</para>
/// </summary>
public static class UiSound
{
    private static AudioManager? _audio;

    /// <summary>Points the facade at the live audio device and loads the UI cue bank. Call once, right
    /// after the <see cref="AudioManager"/> is constructed.</summary>
    public static void Attach(AudioManager audio)
    {
        _audio = audio;
        UiSoundCues.LoadInto(audio);
    }

    /// <summary>Plays a UI cue by key (no-op if not attached / audio unavailable / key unknown).</summary>
    public static void Play(string key) => _audio?.Play(key);

    // Named shortcuts for the action classes, so call sites read intention-first.
    public static void Navigate() => Play(UiSoundCues.Navigate);
    public static void Confirm()  => Play(UiSoundCues.Confirm);
    public static void Back()     => Play(UiSoundCues.Back);
    public static void Toggle()   => Play(UiSoundCues.Toggle);
    public static void Warn()     => Play(UiSoundCues.Warn);
}
