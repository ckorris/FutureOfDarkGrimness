using System.Collections.Generic;
using System.IO;
using FdgRaylib.Audio;

namespace FdgRaylib.Rendering;

/// <summary>
/// The UI sound bank: short "chrome" beeps for buttons, toggles and discrete canvas commits, kept
/// deliberately separate from <see cref="Presentation.PresentationSoundCues"/> (which voices in-game
/// battle beats). This is the only place that decides what each UI interaction sounds like.
///
/// <para>
/// The design follows the usual sound-UX rules: one tone PER ACTION CLASS (not per widget), pitch
/// carrying meaning (rising = forward/confirm, falling = back/cancel, flat = neutral navigate, buzzy
/// = warn/blocked), and everything short and quiet so repeated presses never fatigue. Same drop-in
/// story as the presentation cues: keys double as asset filenames (<c>Assets/Sounds/ui-navigate.wav</c>,
/// ...); drop a real .wav at the mapped path and it takes over with no code change.
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
        // Neutral flat mid blip - "you pressed a thing".
        Navigate => ToneSynth.Tone(520f, 520f, 0.05f, 30f, ToneSynth.Waveform.Sine, 0.22f),

        // Rising two-note - forward / committed / good.
        Confirm => ToneSynth.Concat(
            ToneSynth.Tone(620f, 620f, 0.045f, 18f, ToneSynth.Waveform.Sine, 0.24f),
            ToneSynth.Tone(930f, 930f, 0.090f, 12f, ToneSynth.Waveform.Sine, 0.26f)),

        // Falling two-note - back / cancel / recede.
        Back => ToneSynth.Concat(
            ToneSynth.Tone(500f, 500f, 0.045f, 20f, ToneSynth.Waveform.Sine, 0.20f),
            ToneSynth.Tone(360f, 360f, 0.080f, 14f, ToneSynth.Waveform.Sine, 0.20f)),

        // Tiny high tick - a switch flipping.
        Toggle => ToneSynth.Tone(1180f, 1180f, 0.028f, 55f, ToneSynth.Waveform.Square, 0.14f),

        // Short low descending square buzz - stop / destructive / blocked.
        Warn => ToneSynth.Tone(210f, 150f, 0.14f, 10f, ToneSynth.Waveform.Square, 0.22f),

        _ => ToneSynth.Tone(520f, 520f, 0.05f, 30f, ToneSynth.Waveform.Sine, 0.22f),
    };
}
