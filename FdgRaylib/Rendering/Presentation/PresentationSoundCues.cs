using System.IO;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FdgRaylib.Audio;

namespace FdgRaylib.Rendering.Presentation;

/// <summary>
/// Maps presentation beats to sound-cue keys and registers those cues with an <see cref="AudioManager"/>.
/// This is the only sound code that knows about beats — it parallels the visual overlays: the engine
/// owns the beats and their pacing, the app decides what each one sounds like. Pure mapping (<see
/// cref="CueFor"/>) so it could be unit-tested without an audio device; actual playback is GUI-only.
/// </summary>
public static class PresentationSoundCues
{
    // Cue keys double as the expected asset filenames under Assets/Sounds/{key}.wav.
    public const string Gunshot = "gunshot";
    public const string Melee   = "melee";
    public const string Dice    = "dice";
    public const string Save    = "save";
    public const string Wound   = "wound";
    public const string Death   = "death";
    public const string Banner  = "banner";
    public const string Move    = "move";

    private static readonly string[] AllCues = { Gunshot, Melee, Dice, Save, Wound, Death, Banner, Move };

    /// <summary>The sound-cue key for a beat, or null if the beat has no audio.</summary>
    public static string? CueFor(PresentationBeat beat) => beat switch
    {
        AttackBeat a       => a.IsMelee ? Melee : Gunshot,
        DiceRolledBeat     => Dice,
        RollOffBeat        => Dice,
        SaveBeat           => Save,
        ModelWoundedBeat   => Wound,
        ModelDiedBeat      => Death,
        UnitRoutedBeat     => Death,
        BannerBeat         => Banner,
        UnitMovedBeat      => Move,
        _                  => null,
    };

    /// <summary>
    /// Registers every cue with <paramref name="audio"/>: loads Assets/Sounds/{key}.wav if present,
    /// otherwise a per-cue synthesized placeholder so each beat sounds distinct before real assets exist.
    /// Call once after the audio device is up.
    /// </summary>
    public static void LoadInto(AudioManager audio)
    {
        string dir = Path.Combine(System.AppContext.BaseDirectory, "Assets", "Sounds");
        foreach (string cue in AllCues)
        {
            if (!audio.Load(cue, Path.Combine(dir, cue + ".wav")))
                audio.LoadSynthesized(cue, PlaceholderSamples(cue), ToneSynth.SampleRate);
        }
    }

    /// <summary>
    /// The synthesized placeholder clip for a cue — a distinct "voice" per beat until real assets land:
    /// a fast crack for shots, a metallic ring for melee, a clattering rattle for dice, a soft ping for
    /// saves, a low thud for wounds, a long descending tone for deaths, a rising two-note sting for
    /// banners, and a soft short blip for movement. Internal so it can be unit-tested.
    /// </summary>
    internal static short[] PlaceholderSamples(string cue) => cue switch
    {
        // Very fast, sharp: a square-wave crack into a short noise burst.
        Gunshot => ToneSynth.Concat(
            ToneSynth.Tone(1400f, 300f, 0.012f, 60f, ToneSynth.Waveform.Square, 0.35f),
            ToneSynth.Noise(0.05f, 45f, 0.5f, seed: 11)),

        // Metallic: a noise transient then a quick triangle ring sliding down.
        Melee => ToneSynth.Concat(
            ToneSynth.Noise(0.012f, 60f, 0.4f, seed: 21),
            ToneSynth.Tone(950f, 760f, 0.13f, 16f, ToneSynth.Waveform.Triangle, 0.32f)),

        // Clatter: four short noise ticks with gaps.
        Dice => ToneSynth.Concat(
            ToneSynth.Noise(0.018f, 90f, 0.40f, seed: 31), ToneSynth.Silence(0.030f),
            ToneSynth.Noise(0.018f, 90f, 0.35f, seed: 32), ToneSynth.Silence(0.025f),
            ToneSynth.Noise(0.022f, 80f, 0.40f, seed: 33), ToneSynth.Silence(0.020f),
            ToneSynth.Noise(0.018f, 90f, 0.30f, seed: 34)),

        // Soft mid ping.
        Save => ToneSynth.Tone(720f, 720f, 0.12f, 13f, ToneSynth.Waveform.Sine, 0.34f),

        // Low, quick thud.
        Wound => ToneSynth.Tone(190f, 140f, 0.16f, 11f, ToneSynth.Waveform.Sine, 0.45f),

        // Longer, slow descending tone.
        Death => ToneSynth.Tone(420f, 120f, 0.42f, 4.5f, ToneSynth.Waveform.Triangle, 0.40f),

        // Rising two-note sting (C5 -> G5).
        Banner => ToneSynth.Concat(
            ToneSynth.Tone(523f, 523f, 0.11f, 7f, ToneSynth.Waveform.Sine, 0.32f),
            ToneSynth.Tone(784f, 784f, 0.24f, 4.5f, ToneSynth.Waveform.Sine, 0.34f)),

        // Soft, short, quiet blip.
        Move => ToneSynth.Tone(300f, 360f, 0.10f, 16f, ToneSynth.Waveform.Sine, 0.20f),

        _ => ToneSynth.Tone(600f, 600f, 0.10f, 22f, ToneSynth.Waveform.Sine, 0.35f),
    };
}
