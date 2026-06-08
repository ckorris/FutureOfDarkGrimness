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
        SaveBeat           => Save,
        ModelWoundedBeat   => Wound,
        ModelDiedBeat      => Death,
        BannerBeat         => Banner,
        UnitMovedBeat      => Move,
        _                  => null,
    };

    /// <summary>
    /// Registers every cue with <paramref name="audio"/>, loading Assets/Sounds/{key}.wav if present
    /// (else the placeholder). Call once after the audio device is up.
    /// </summary>
    public static void LoadInto(AudioManager audio)
    {
        string dir = Path.Combine(System.AppContext.BaseDirectory, "Assets", "Sounds");
        foreach (string cue in AllCues)
            audio.Load(cue, Path.Combine(dir, cue + ".wav"));
    }
}
