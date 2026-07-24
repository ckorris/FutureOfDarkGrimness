using System.Collections.Generic;
using System.IO;
using FDG.ArmyBuilding;
using FDG.Presentation;
using FDG.Presentation.Beats;
using FdgRaylib.Audio;

namespace FdgRaylib.Rendering.Presentation;

/// <summary>
/// Maps presentation beats to sound-cue keys and registers those cues with an <see cref="AudioManager"/>.
/// This is the only sound code that knows about beats — it parallels the visual overlays: the engine
/// owns the beats and their pacing, the app decides what each one sounds like. Pure mapping (<see
/// cref="CueFor"/>/<see cref="VolleyCue"/>/<see cref="ImpactCue"/>) so it can be unit-tested without
/// an audio device; actual playback is GUI-only.
///
/// <para>
/// #239: attacks are voiced per weapon effect SET. Each ranged set has a firing cue and an impact
/// cue, each melee set a swing cue and a connect cue — cue keys double as the drop-in asset
/// filenames (<c>Assets/Sounds/fire-plasma-bolt.wav</c>, <c>impact-plasma-bolt.wav</c>,
/// <c>melee-energy-blade.wav</c>, <c>meleehit-energy-blade.wav</c>); anything without a .wav gets a
/// synthesized placeholder voice.
/// </para>
/// </summary>
public static class PresentationSoundCues
{
    // Cue keys double as the expected asset filenames under Assets/Sounds/{key}.wav.
    public const string Dice    = "dice";
    public const string Save    = "save";
    public const string Wound   = "wound";
    public const string Death   = "death";
    public const string Move    = "move";

    // #274: one voice per banner tier. Volume and weight fall off with the tier, so a Toast can fire
    // five times in a row without grating and a Headline still means something when it does land.
    public const string BannerHeadline = "banner-headline";
    public const string BannerNotice   = "banner-notice";
    public const string BannerToast    = "banner-toast";

    private static readonly string[] BaseCues =
        { Dice, Save, Wound, Death, Move, BannerHeadline, BannerNotice, BannerToast };

    /// <summary>The firing cue key for a RESOLVED ranged set key (see <see cref="WeaponEffectCatalog"/>).</summary>
    public static string FireCue(string rangedKey) => "fire-" + rangedKey;

    /// <summary>The impact cue key for a resolved ranged set key — a shot connecting (#239).</summary>
    public static string RangedImpactCue(string rangedKey) => "impact-" + rangedKey;

    /// <summary>The swing cue key for a resolved melee set key.</summary>
    public static string SwingCue(string meleeKey) => "melee-" + meleeKey;

    /// <summary>The connect cue key for a resolved melee set key — a swing that lands (#239).</summary>
    public static string MeleeImpactCue(string meleeKey) => "meleehit-" + meleeKey;

    /// <summary>Every cue key this app registers — base beats plus the per-set attack voices.</summary>
    public static IEnumerable<string> AllCueKeys()
    {
        foreach (string cue in BaseCues) yield return cue;
        foreach (string key in WeaponEffectCatalog.RangedKeys)
        {
            yield return FireCue(key);
            yield return RangedImpactCue(key);
        }
        foreach (string key in WeaponEffectCatalog.MeleeKeys)
        {
            yield return SwingCue(key);
            yield return MeleeImpactCue(key);
        }
    }

    /// <summary>The sound-cue key for a beat, or null if the beat has no audio.</summary>
    public static string? CueFor(PresentationBeat beat) => beat switch
    {
        // #238: attacks cue PER VOLLEY via VolleyCue/AttackVolleyStarted (and per landing volley via
        // ImpactCue/AttackVolleyImpact, #239), not once at beat start — three swings sound three times.
        AttackBeat         => null,
        DiceRolledBeat     => Dice,
        RollOffBeat        => Dice,
        SaveBeat           => Save,
        ModelWoundedBeat   => Wound,
        ModelDiedBeat      => Death,
        UnitRoutedBeat     => Death,
        // #274: the banner's tier picks its voice.
        BannerBeat banner  => banner.Tier switch
        {
            EBannerTier.Notice => BannerNotice,
            EBannerTier.Toast  => BannerToast,
            _                  => BannerHeadline,
        },
        UnitMovedBeat      => Move,
        _                  => null,
    };

    /// <summary>The firing/swing cue for one volley of an attack (#238), voiced by its weapon
    /// effect set (#239) — fired once per volley via <c>PresentationPlayer.AttackVolleyStarted</c>.</summary>
    public static string VolleyCue(AttackBeat beat) => beat.IsMelee
        ? SwingCue(WeaponEffectCatalog.ResolveMeleeKey(beat.WeaponEffect))
        : FireCue(WeaponEffectCatalog.ResolveRangedKey(beat.WeaponEffect));

    /// <summary>The impact cue for a volley that LANDED something (#239) — fired via
    /// <c>PresentationPlayer.AttackVolleyImpact</c> when the volley's shots arrive; whiffed
    /// volleys never sound an impact.</summary>
    public static string ImpactCue(AttackBeat beat) => beat.IsMelee
        ? MeleeImpactCue(WeaponEffectCatalog.ResolveMeleeKey(beat.WeaponEffect))
        : RangedImpactCue(WeaponEffectCatalog.ResolveRangedKey(beat.WeaponEffect));

    /// <summary>
    /// Registers every cue with <paramref name="audio"/>: loads Assets/Sounds/{key}.wav if present,
    /// otherwise a per-cue synthesized placeholder so each beat/effect-set sounds distinct before
    /// real assets exist. Call once after the audio device is up.
    /// </summary>
    public static void LoadInto(AudioManager audio)
    {
        string dir = Path.Combine(System.AppContext.BaseDirectory, "Assets", "Sounds");
        foreach (string cue in AllCueKeys())
        {
            if (!audio.Load(cue, Path.Combine(dir, cue + ".wav")))
                audio.LoadSynthesized(cue, PlaceholderSamples(cue), ToneSynth.SampleRate);
        }
    }

    // Shorthand for the recipe table below.
    private const string R_Plasma  = "fire-" + WeaponEffectAssigner.Sets.PlasmaBolt;
    private const string R_Fusion  = "fire-" + WeaponEffectAssigner.Sets.FusionMelta;
    private const string R_Flame   = "fire-" + WeaponEffectAssigner.Sets.FlameJet;
    private const string R_Grav    = "fire-" + WeaponEffectAssigner.Sets.GravityPulse;
    private const string R_Gauss   = "fire-" + WeaponEffectAssigner.Sets.GaussParticle;
    private const string R_Laser   = "fire-" + WeaponEffectAssigner.Sets.LaserBeam;
    private const string R_Missile = "fire-" + WeaponEffectAssigner.Sets.MissileRocket;
    private const string R_Mortar  = "fire-" + WeaponEffectAssigner.Sets.MortarArtillery;
    private const string R_Bio     = "fire-" + WeaponEffectAssigner.Sets.BioOrganic;
    private const string R_Storm   = "fire-" + WeaponEffectAssigner.Sets.StormTracer;
    private const string R_Slug    = "fire-" + WeaponEffectAssigner.Sets.BallisticSlug;
    private const string R_Arcane  = "fire-" + WeaponEffectAssigner.Sets.ArcanePsychic;
    private const string R_Shard   = "fire-" + WeaponEffectAssigner.Sets.ShardCrystal;

    private const string I_Plasma  = "impact-" + WeaponEffectAssigner.Sets.PlasmaBolt;
    private const string I_Fusion  = "impact-" + WeaponEffectAssigner.Sets.FusionMelta;
    private const string I_Flame   = "impact-" + WeaponEffectAssigner.Sets.FlameJet;
    private const string I_Grav    = "impact-" + WeaponEffectAssigner.Sets.GravityPulse;
    private const string I_Gauss   = "impact-" + WeaponEffectAssigner.Sets.GaussParticle;
    private const string I_Laser   = "impact-" + WeaponEffectAssigner.Sets.LaserBeam;
    private const string I_Missile = "impact-" + WeaponEffectAssigner.Sets.MissileRocket;
    private const string I_Mortar  = "impact-" + WeaponEffectAssigner.Sets.MortarArtillery;
    private const string I_Bio     = "impact-" + WeaponEffectAssigner.Sets.BioOrganic;
    private const string I_Storm   = "impact-" + WeaponEffectAssigner.Sets.StormTracer;
    private const string I_Slug    = "impact-" + WeaponEffectAssigner.Sets.BallisticSlug;
    private const string I_Arcane  = "impact-" + WeaponEffectAssigner.Sets.ArcanePsychic;
    private const string I_Shard   = "impact-" + WeaponEffectAssigner.Sets.ShardCrystal;

    private const string M_Energy = "melee-" + WeaponEffectAssigner.Sets.EnergyBlade;
    private const string M_Titan  = "melee-" + WeaponEffectAssigner.Sets.TitanImpact;
    private const string M_Shock  = "melee-" + WeaponEffectAssigner.Sets.ShockMelee;
    private const string M_Chain  = "melee-" + WeaponEffectAssigner.Sets.ChainBlade;
    private const string M_Toxic  = "melee-" + WeaponEffectAssigner.Sets.ToxicMelee;
    private const string M_Daemon = "melee-" + WeaponEffectAssigner.Sets.DaemonArcaneMelee;
    private const string M_Spear  = "melee-" + WeaponEffectAssigner.Sets.SpearPierce;
    private const string M_Claw   = "melee-" + WeaponEffectAssigner.Sets.ClawRend;
    private const string M_Crude  = "melee-" + WeaponEffectAssigner.Sets.CrudeMelee;
    private const string M_Blade  = "melee-" + WeaponEffectAssigner.Sets.BladeStandard;

    private const string H_Energy = "meleehit-" + WeaponEffectAssigner.Sets.EnergyBlade;
    private const string H_Titan  = "meleehit-" + WeaponEffectAssigner.Sets.TitanImpact;
    private const string H_Shock  = "meleehit-" + WeaponEffectAssigner.Sets.ShockMelee;
    private const string H_Chain  = "meleehit-" + WeaponEffectAssigner.Sets.ChainBlade;
    private const string H_Toxic  = "meleehit-" + WeaponEffectAssigner.Sets.ToxicMelee;
    private const string H_Daemon = "meleehit-" + WeaponEffectAssigner.Sets.DaemonArcaneMelee;
    private const string H_Spear  = "meleehit-" + WeaponEffectAssigner.Sets.SpearPierce;
    private const string H_Claw   = "meleehit-" + WeaponEffectAssigner.Sets.ClawRend;
    private const string H_Crude  = "meleehit-" + WeaponEffectAssigner.Sets.CrudeMelee;
    private const string H_Blade  = "meleehit-" + WeaponEffectAssigner.Sets.BladeStandard;

    /// <summary>
    /// The synthesized placeholder clip for a cue — a distinct "voice" per beat and per weapon
    /// effect set until real assets land. Deterministic (ToneSynth), so byte-identical every run.
    /// Internal so it can be unit-tested.
    /// </summary>
    internal static short[] PlaceholderSamples(string cue) => cue switch
    {
        // ---------------- base beats (unchanged voices) ----------------

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

        // #274 Headline: the pre-tier banner voice - a rising two-note chime (C5 -> G5). This is the
        // sound the game has always made when it announces something, so it belongs on the handful of
        // moments that are actually worth announcing rather than on the ones that merely happen often.
        BannerHeadline => ToneSynth.Concat(
            ToneSynth.Tone(523f, 523f, 0.11f, 7f, ToneSynth.Waveform.Sine, 0.32f),
            ToneSynth.Tone(784f, 784f, 0.24f, 4.5f, ToneSynth.Waveform.Sine, 0.34f)),

        // #274 Notice: a struck low hit - a short noise transient into a low body that sags from B2
        // toward F2 and rings out. Non-melodic on purpose, and low enough to sit under the chime above
        // it: this is the busiest tier, so it has to register without asking for the whole room.
        BannerNotice => ToneSynth.Concat(
            ToneSynth.Noise(0.03f, 45f, 0.26f, seed: 71),
            ToneSynth.Tone(124f, 88f, 0.44f, 3.2f, ToneSynth.Waveform.Sine, 0.42f)),

        // #274 Toast: a single soft high blip, quiet and quick. Deliberately the least interesting
        // sound in the game - five of them in a row should read as texture, not as an alarm.
        BannerToast => ToneSynth.Tone(1046f, 1046f, 0.07f, 26f, ToneSynth.Waveform.Sine, 0.14f),

        // Soft, short, quiet blip.
        Move => ToneSynth.Tone(300f, 360f, 0.10f, 16f, ToneSynth.Waveform.Sine, 0.20f),

        // ---------------- ranged firing (#239) ----------------

        // Rising electric whine into a heavy release.
        R_Plasma => ToneSynth.Concat(
            ToneSynth.Tone(280f, 900f, 0.10f, 6f, ToneSynth.Waveform.Sine, 0.28f),
            ToneSynth.Tone(220f, 90f, 0.09f, 18f, ToneSynth.Waveform.Square, 0.30f)),

        // Blowtorch ignition roar.
        R_Fusion => ToneSynth.Concat(
            ToneSynth.Noise(0.05f, 25f, 0.35f, seed: 101),
            ToneSynth.Tone(130f, 75f, 0.16f, 9f, ToneSynth.Waveform.Square, 0.26f)),

        // Sustained gas whoosh.
        R_Flame => ToneSynth.Noise(0.28f, 8f, 0.30f, seed: 102),

        // Deep bass charge-whump.
        R_Grav => ToneSynth.Concat(
            ToneSynth.Tone(45f, 90f, 0.08f, 8f, ToneSynth.Waveform.Sine, 0.40f),
            ToneSynth.Tone(90f, 38f, 0.14f, 12f, ToneSynth.Waveform.Sine, 0.45f)),

        // Rising electric buzz.
        R_Gauss => ToneSynth.Tone(240f, 560f, 0.13f, 9f, ToneSynth.Waveform.Triangle, 0.30f),

        // Sharp zap "pew".
        R_Laser => ToneSynth.Tone(1500f, 380f, 0.08f, 26f, ToneSynth.Waveform.Square, 0.26f),

        // Launch whoosh + motor roar.
        R_Missile => ToneSynth.Concat(
            ToneSynth.Noise(0.07f, 18f, 0.32f, seed: 103),
            ToneSynth.Tone(210f, 120f, 0.16f, 8f, ToneSynth.Waveform.Square, 0.22f)),

        // Heavy thump, then the falling whistle.
        R_Mortar => ToneSynth.Concat(
            ToneSynth.Tone(95f, 55f, 0.10f, 16f, ToneSynth.Waveform.Square, 0.40f),
            ToneSynth.Silence(0.05f),
            ToneSynth.Tone(1250f, 620f, 0.20f, 7f, ToneSynth.Waveform.Sine, 0.14f)),

        // Wet gurgling launch.
        R_Bio => ToneSynth.Concat(
            ToneSynth.Tone(150f, 95f, 0.06f, 14f, ToneSynth.Waveform.Sine, 0.32f),
            ToneSynth.Tone(120f, 160f, 0.06f, 14f, ToneSynth.Waveform.Sine, 0.30f),
            ToneSynth.Noise(0.05f, 35f, 0.28f, seed: 104)),

        // Mechanical double crack — a bolt cycling.
        R_Storm => ToneSynth.Concat(
            ToneSynth.Tone(1350f, 320f, 0.012f, 60f, ToneSynth.Waveform.Square, 0.34f),
            ToneSynth.Noise(0.025f, 55f, 0.40f, seed: 105), ToneSynth.Silence(0.020f),
            ToneSynth.Tone(1250f, 300f, 0.012f, 60f, ToneSynth.Waveform.Square, 0.30f),
            ToneSynth.Noise(0.030f, 50f, 0.35f, seed: 106)),

        // The classic sharp crack (the pre-#239 gunshot).
        R_Slug => ToneSynth.Concat(
            ToneSynth.Tone(1400f, 300f, 0.012f, 60f, ToneSynth.Waveform.Square, 0.35f),
            ToneSynth.Noise(0.05f, 45f, 0.5f, seed: 11)),

        // Eerie chant-like rise with a shimmer.
        R_Arcane => ToneSynth.Concat(
            ToneSynth.Tone(210f, 690f, 0.18f, 5f, ToneSynth.Waveform.Triangle, 0.26f),
            ToneSynth.Tone(920f, 880f, 0.10f, 9f, ToneSynth.Waveform.Sine, 0.18f)),

        // Crystal chime release.
        R_Shard => ToneSynth.Concat(
            ToneSynth.Tone(1750f, 1400f, 0.10f, 10f, ToneSynth.Waveform.Sine, 0.24f),
            ToneSynth.Tone(2350f, 2350f, 0.06f, 16f, ToneSynth.Waveform.Sine, 0.18f)),

        // ---------------- ranged impacts (#239) ----------------

        // Bright flash + electric sizzle.
        I_Plasma => ToneSynth.Concat(
            ToneSynth.Tone(1050f, 480f, 0.05f, 22f, ToneSynth.Waveform.Sine, 0.30f),
            ToneSynth.Noise(0.09f, 30f, 0.26f, seed: 111)),

        // Molten splash, sizzling metal.
        I_Fusion => ToneSynth.Concat(
            ToneSynth.Tone(150f, 78f, 0.07f, 15f, ToneSynth.Waveform.Sine, 0.34f),
            ToneSynth.Noise(0.16f, 14f, 0.24f, seed: 112)),

        // Crackling burn: staggered ticks.
        I_Flame => ToneSynth.Concat(
            ToneSynth.Noise(0.03f, 40f, 0.30f, seed: 113), ToneSynth.Silence(0.025f),
            ToneSynth.Noise(0.025f, 45f, 0.24f, seed: 114), ToneSynth.Silence(0.02f),
            ToneSynth.Noise(0.03f, 40f, 0.20f, seed: 115)),

        // Implosive inward crumple: pitch pulled UP into a thud.
        I_Grav => ToneSynth.Concat(
            ToneSynth.Tone(60f, 190f, 0.08f, 9f, ToneSynth.Waveform.Sine, 0.34f),
            ToneSynth.Tone(105f, 60f, 0.08f, 18f, ToneSynth.Waveform.Sine, 0.40f)),

        // Green spark crackle + fizzle-out.
        I_Gauss => ToneSynth.Concat(
            ToneSynth.Noise(0.04f, 45f, 0.30f, seed: 116),
            ToneSynth.Tone(700f, 190f, 0.10f, 12f, ToneSynth.Waveform.Triangle, 0.24f)),

        // Small spark snap.
        I_Laser => ToneSynth.Concat(
            ToneSynth.Noise(0.025f, 60f, 0.30f, seed: 117),
            ToneSynth.Tone(900f, 900f, 0.04f, 30f, ToneSynth.Waveform.Sine, 0.22f)),

        // Hard boom.
        I_Missile => ToneSynth.Concat(
            ToneSynth.Noise(0.16f, 16f, 0.45f, seed: 118),
            ToneSynth.Tone(72f, 40f, 0.20f, 8f, ToneSynth.Waveform.Sine, 0.40f)),

        // Bigger, longer boom with rumble.
        I_Mortar => ToneSynth.Concat(
            ToneSynth.Noise(0.22f, 12f, 0.48f, seed: 119),
            ToneSynth.Tone(58f, 34f, 0.28f, 6f, ToneSynth.Waveform.Sine, 0.42f)),

        // Wet splatter + acid hiss.
        I_Bio => ToneSynth.Concat(
            ToneSynth.Tone(130f, 85f, 0.05f, 20f, ToneSynth.Waveform.Sine, 0.32f),
            ToneSynth.Noise(0.12f, 18f, 0.18f, seed: 120)),

        // Sharp metallic ping.
        I_Storm => ToneSynth.Concat(
            ToneSynth.Noise(0.015f, 70f, 0.30f, seed: 121),
            ToneSynth.Tone(1150f, 760f, 0.07f, 18f, ToneSynth.Waveform.Triangle, 0.26f)),

        // Dull thud + small spark tick.
        I_Slug => ToneSynth.Concat(
            ToneSynth.Tone(200f, 135f, 0.08f, 16f, ToneSynth.Waveform.Sine, 0.36f),
            ToneSynth.Noise(0.02f, 60f, 0.22f, seed: 122)),

        // Reality-tear: descending warble + shimmer.
        I_Arcane => ToneSynth.Concat(
            ToneSynth.Tone(820f, 190f, 0.13f, 9f, ToneSynth.Waveform.Triangle, 0.28f),
            ToneSynth.Tone(1300f, 1150f, 0.08f, 12f, ToneSynth.Waveform.Sine, 0.16f)),

        // Glass-shatter tinkle: three quick high pings.
        I_Shard => ToneSynth.Concat(
            ToneSynth.Tone(2250f, 2250f, 0.04f, 25f, ToneSynth.Waveform.Sine, 0.22f), ToneSynth.Silence(0.015f),
            ToneSynth.Tone(1850f, 1850f, 0.04f, 25f, ToneSynth.Waveform.Sine, 0.20f), ToneSynth.Silence(0.012f),
            ToneSynth.Tone(2600f, 2600f, 0.05f, 22f, ToneSynth.Waveform.Sine, 0.18f)),

        // ---------------- melee swings (#239) ----------------

        // Electric hum into the sweep.
        M_Energy => ToneSynth.Concat(
            ToneSynth.Tone(215f, 235f, 0.10f, 5f, ToneSynth.Waveform.Sine, 0.24f),
            ToneSynth.Tone(360f, 520f, 0.08f, 9f, ToneSynth.Waveform.Triangle, 0.22f)),

        // Massive whoosh with bass weight.
        M_Titan => ToneSynth.Concat(
            ToneSynth.Noise(0.14f, 12f, 0.28f, seed: 131),
            ToneSynth.Tone(82f, 48f, 0.12f, 9f, ToneSynth.Waveform.Sine, 0.36f)),

        // Buzzing charge.
        M_Shock => ToneSynth.Tone(290f, 640f, 0.12f, 7f, ToneSynth.Waveform.Square, 0.18f),

        // Revving chainsaw snarl.
        M_Chain => ToneSynth.Concat(
            ToneSynth.Tone(85f, 190f, 0.14f, 4f, ToneSynth.Waveform.Square, 0.22f),
            ToneSynth.Noise(0.07f, 20f, 0.22f, seed: 132)),

        // Wet squelching swing.
        M_Toxic => ToneSynth.Concat(
            ToneSynth.Tone(145f, 88f, 0.07f, 12f, ToneSynth.Waveform.Sine, 0.28f),
            ToneSynth.Noise(0.06f, 28f, 0.18f, seed: 133)),

        // Warped, wrong-sounding whoosh.
        M_Daemon => ToneSynth.Tone(520f, 140f, 0.17f, 6f, ToneSynth.Waveform.Triangle, 0.26f),

        // Whoosh-thrust rising to the point.
        M_Spear => ToneSynth.Concat(
            ToneSynth.Noise(0.05f, 26f, 0.24f, seed: 134),
            ToneSynth.Tone(390f, 720f, 0.06f, 14f, ToneSynth.Waveform.Sine, 0.22f)),

        // Slashing screech.
        M_Claw => ToneSynth.Concat(
            ToneSynth.Noise(0.08f, 22f, 0.26f, seed: 135),
            ToneSynth.Tone(880f, 470f, 0.08f, 12f, ToneSynth.Waveform.Triangle, 0.20f)),

        // Slow heavy whoosh.
        M_Crude => ToneSynth.Concat(
            ToneSynth.Noise(0.11f, 14f, 0.22f, seed: 136),
            ToneSynth.Tone(115f, 68f, 0.09f, 11f, ToneSynth.Waveform.Sine, 0.30f)),

        // The classic metallic ring (the pre-#239 melee voice).
        M_Blade => ToneSynth.Concat(
            ToneSynth.Noise(0.012f, 60f, 0.4f, seed: 21),
            ToneSynth.Tone(950f, 760f, 0.13f, 16f, ToneSynth.Waveform.Triangle, 0.32f)),

        // ---------------- melee connects (#239) ----------------

        // Crackling "zzt-chak".
        H_Energy => ToneSynth.Concat(
            ToneSynth.Noise(0.02f, 55f, 0.30f, seed: 141),
            ToneSynth.Tone(720f, 240f, 0.06f, 20f, ToneSynth.Waveform.Square, 0.26f)),

        // Metal-on-metal crunch, deep.
        H_Titan => ToneSynth.Concat(
            ToneSynth.Noise(0.09f, 22f, 0.38f, seed: 142),
            ToneSynth.Tone(64f, 42f, 0.18f, 9f, ToneSynth.Waveform.Sine, 0.42f)),

        // Snap-zap discharge.
        H_Shock => ToneSynth.Concat(
            ToneSynth.Tone(1020f, 290f, 0.05f, 26f, ToneSynth.Waveform.Square, 0.26f),
            ToneSynth.Noise(0.025f, 55f, 0.24f, seed: 143)),

        // Grinding chunk-tear.
        H_Chain => ToneSynth.Concat(
            ToneSynth.Noise(0.05f, 24f, 0.32f, seed: 144), ToneSynth.Silence(0.015f),
            ToneSynth.Noise(0.06f, 20f, 0.28f, seed: 145),
            ToneSynth.Tone(140f, 95f, 0.07f, 14f, ToneSynth.Waveform.Square, 0.20f)),

        // Corrosive hiss.
        H_Toxic => ToneSynth.Noise(0.14f, 13f, 0.20f, seed: 146),

        // Burst of torn reality.
        H_Daemon => ToneSynth.Concat(
            ToneSynth.Tone(640f, 105f, 0.11f, 10f, ToneSynth.Waveform.Triangle, 0.28f),
            ToneSynth.Noise(0.05f, 30f, 0.22f, seed: 147)),

        // Sharp puncture crunch.
        H_Spear => ToneSynth.Concat(
            ToneSynth.Noise(0.03f, 50f, 0.28f, seed: 148),
            ToneSynth.Tone(245f, 115f, 0.07f, 17f, ToneSynth.Waveform.Sine, 0.30f)),

        // Wet tearing rip.
        H_Claw => ToneSynth.Concat(
            ToneSynth.Noise(0.08f, 20f, 0.28f, seed: 149),
            ToneSynth.Tone(490f, 240f, 0.07f, 13f, ToneSynth.Waveform.Triangle, 0.22f)),

        // Heavy blunt thud-crunch.
        H_Crude => ToneSynth.Concat(
            ToneSynth.Tone(118f, 66f, 0.11f, 12f, ToneSynth.Waveform.Sine, 0.38f),
            ToneSynth.Noise(0.045f, 35f, 0.24f, seed: 150)),

        // Clean clang-cut.
        H_Blade => ToneSynth.Concat(
            ToneSynth.Tone(1010f, 820f, 0.09f, 15f, ToneSynth.Waveform.Triangle, 0.28f),
            ToneSynth.Noise(0.02f, 60f, 0.20f, seed: 151)),

        _ => ToneSynth.Tone(600f, 600f, 0.10f, 22f, ToneSynth.Waveform.Sine, 0.35f),
    };
}
