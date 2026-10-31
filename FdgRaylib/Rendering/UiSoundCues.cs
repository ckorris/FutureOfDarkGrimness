using System.Collections.Generic;
using System.IO;
using FdgRaylib.Audio;

namespace FdgRaylib.Rendering;

/// <summary>
/// The UI sound bank: short "chrome" clicks for buttons, toggles and discrete canvas commits, kept
/// deliberately separate from <see cref="Presentation.PresentationSoundCues"/> (which voices in-game
/// battle beats). This is the only place that decides what each UI interaction sounds like.
///
/// <para>
/// The design follows the usual sound-UX rules: one sound PER ACTION CLASS (not per widget), with the
/// meaning carried by click shape instead of melody (single soft click = neutral navigate, double
/// click rising = forward/confirm, double click falling = back/cancel, tiny crisp tick = toggle, dull
/// low knocks = warn/blocked), and everything short and quiet so repeated presses never fatigue. Each
/// cue is a transient — a few ms of noise "tick" plus a tiny pitched body — rather than a held tone,
/// so they read as mechanical clicks, not beeps. Same drop-in story as the presentation cues: keys
/// double as asset filenames (<c>Assets/Sounds/ui-navigate.wav</c>, ...); drop a real .wav at the
/// mapped path and it takes over with no code change.
/// </para>
///
/// <para>Pure mapping/DSP (no Raylib, no ImGui) so <see cref="PlaceholderSamples"/> is unit-testable
/// without an audio device.</para>
/// </summary>
public static class UiSoundCues
{
    // Keys double as the expected asset filenames under Assets/Sounds/{key}.wav. The "ui-" prefix keeps
    // them from ever colliding with a presentation cue of the same short name.
    public const string Navigate = "ui-navigate";  // generic menu button (Host, Client, Army Builder, ...)
    public const string Confirm  = "ui-confirm";   // positive commit (Create, Connect, Launch, resolver Done/Fire)
    public const string Back     = "ui-back";      // Back / Cancel / low-stakes recede (Skip, Stay)
    public const string Toggle   = "ui-toggle";    // checkbox / radio / ready flip
    public const string Warn     = "ui-warn";      // destructive (Clear, Restart) or a blocked/disabled press

    private static readonly string[] Cues = { Navigate, Confirm, Back, Toggle, Warn };

    /// <summary>Every UI cue key this app registers.</summary>
    public static IEnumerable<string> AllCueKeys() => Cues;

    /// <summary>
    /// Registers every UI cue with <paramref name="audio"/>: loads Assets/Sounds/{key}.wav if present,
    /// otherwise a synthesized placeholder. Call once after the audio device is up.
    /// </summary>
    public static void LoadInto(AudioManager audio)
    {
        string dir = Path.Combine(System.AppContext.BaseDirectory, "Assets", "Sounds");
        foreach (string cue in Cues)
        {
            if (!audio.Load(cue, Path.Combine(dir, cue + ".wav")))
                audio.LoadSynthesized(cue, PlaceholderSamples(cue), ToneSynth.SampleRate);
        }
    }

    /// <summary>
    /// The synthesized placeholder clip for a UI cue. Deterministic (ToneSynth), so byte-identical every
    /// run. Kept quiet and short on purpose - UI chrome should sit under the game, never over it.
    /// Internal so it can be unit-tested.
    /// </summary>
    internal static short[] PlaceholderSamples(string cue) => cue switch
    {
        // Single soft click - "you pressed a thing". A few ms of noise transient, then a tiny low body.
        Navigate => ToneSynth.Concat(
            ToneSynth.Noise(0.006f, 420f, 0.16f, seed: 11),
            ToneSynth.Tone(300f, 220f, 0.025f, 130f, ToneSynth.Waveform.Sine, 0.15f)),

        // Double click, second slightly brighter - forward / committed / good.
        Confirm => ToneSynth.Concat(
            ToneSynth.Noise(0.005f, 460f, 0.14f, seed: 13),
            ToneSynth.Tone(420f, 470f, 0.020f, 130f, ToneSynth.Waveform.Sine, 0.14f),
            ToneSynth.Silence(0.028f),
            ToneSynth.Noise(0.005f, 460f, 0.13f, seed: 17),
            ToneSynth.Tone(560f, 620f, 0.022f, 120f, ToneSynth.Waveform.Sine, 0.16f)),

        // Double click, second duller - back / cancel / recede.
        Back => ToneSynth.Concat(
            ToneSynth.Noise(0.005f, 480f, 0.13f, seed: 19),
            ToneSynth.Tone(340f, 300f, 0.020f, 130f, ToneSynth.Waveform.Sine, 0.14f),
            ToneSynth.Silence(0.028f),
            ToneSynth.Noise(0.005f, 500f, 0.11f, seed: 23),
            ToneSynth.Tone(230f, 190f, 0.024f, 120f, ToneSynth.Waveform.Sine, 0.13f)),

        // Tiny crisp tick - a switch flipping. Brighter and shorter than Navigate.
        Toggle => ToneSynth.Concat(
            ToneSynth.Noise(0.004f, 600f, 0.14f, seed: 29),
            ToneSynth.Tone(820f, 760f, 0.012f, 220f, ToneSynth.Waveform.Sine, 0.10f)),

        // Two dull low knocks - stop / destructive / blocked. Weight without buzz.
        Warn => ToneSynth.Concat(
            ToneSynth.Tone(160f, 115f, 0.050f, 70f, ToneSynth.Waveform.Triangle, 0.18f),
            ToneSynth.Silence(0.035f),
            ToneSynth.Tone(140f, 100f, 0.055f, 62f, ToneSynth.Waveform.Triangle, 0.18f)),

        _ => ToneSynth.Concat(
            ToneSynth.Noise(0.006f, 420f, 0.16f, seed: 11),
            ToneSynth.Tone(300f, 220f, 0.025f, 130f, ToneSynth.Waveform.Sine, 0.15f)),
    };
}
