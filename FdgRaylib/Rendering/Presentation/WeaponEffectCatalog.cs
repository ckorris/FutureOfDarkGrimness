using System.Collections.Generic;
using FDG.ArmyBuilding;
using Raylib_cs;

namespace FdgRaylib.Rendering.Presentation;

/// <summary>
/// #239: what each weapon effect-set KEY looks and sounds like in THIS front-end. The engine
/// transports the keys as opaque strings (army data -> Weapon.EffectKey -> AttackBeat.WeaponEffect);
/// this catalog is the one place that gives them meaning — a draw form + palette for the overlay
/// and sound-cue names for the audio side. Unknown or null keys resolve to the global defaults
/// (ballistic-slug / blade-standard), which reproduce the pre-#239 look, so stale data degrades
/// gracefully instead of breaking.
/// </summary>
public static class WeaponEffectCatalog
{
    /// <summary>How a ranged shot is drawn.</summary>
    public enum RangedForm
    {
        Tracer,  // fast line streak + head (kinetic fire)
        Bolt,    // glowing orb with a short trail
        Beam,    // instantaneous line, flash-in/flash-out
        Rocket,  // bolt with a smoke trail and a hard explosion
        Lobbed,  // parabolic shell arc, big burst
        Cone,    // expanding particle cone (flame), no projectile head
        Glob,    // wobbling organic lump with droplets
    }

    /// <summary>What the landing of a hit looks like.</summary>
    public enum ImpactKind { Spark, Explosion, Splatter, Ring, Shatter, Bloom }

    /// <summary>How a melee swing is drawn.</summary>
    public enum MeleeForm
    {
        Slash,   // arcing blade sweep (the classic)
        Smash,   // overhead slam, ground ring + cracks
        Thrust,  // straight lunge with a tip glint
        Rake,    // three parallel claw streaks
    }

    /// <summary>Extra particles layered on a melee swing.</summary>
    public enum MeleeAccent { None, ElectricArcs, Ooze, Smoke, Teeth }

    /// <param name="Form">Draw form.</param>
    /// <param name="Core">Primary (bright) color.</param>
    /// <param name="Glow">Accent/halo color.</param>
    /// <param name="Width">Thickness/size multiplier relative to the classic tracer.</param>
    /// <param name="Impact">Landing visual for hits.</param>
    /// <param name="LandFraction">Where in the volley's time slice the shot lands (0..1) — beams
    /// land almost immediately, lobbed shells late. Drives the impact sound cue timing too.</param>
    public sealed record RangedEffectStyle(
        RangedForm Form, Color Core, Color Glow, float Width, ImpactKind Impact, float LandFraction);

    /// <param name="Blade">Fill color of the swing.</param>
    /// <param name="Edge">Edge/outline color.</param>
    /// <param name="Width">Size multiplier.</param>
    /// <param name="Afterimage">Draw a trailing ghost of the swing (energy/daemon weapons).</param>
    public sealed record MeleeEffectStyle(
        MeleeForm Form, Color Blade, Color Edge, float Width, MeleeAccent Accent, bool Afterimage);

    /// <summary>The key every unknown/null ranged key resolves to.</summary>
    public const string DefaultRangedKey = WeaponEffectAssigner.Sets.BallisticSlug;

    /// <summary>The key every unknown/null melee key resolves to.</summary>
    public const string DefaultMeleeKey = WeaponEffectAssigner.Sets.BladeStandard;

    private static readonly Dictionary<string, RangedEffectStyle> RangedStyles = new()
    {
        [WeaponEffectAssigner.Sets.PlasmaBolt] = new(RangedForm.Bolt,
            new Color(150, 220, 255, 255), new Color(70, 130, 255, 255), 1.2f, ImpactKind.Bloom, 0.9f),
        [WeaponEffectAssigner.Sets.FusionMelta] = new(RangedForm.Beam,
            new Color(255, 150, 60, 255), new Color(255, 60, 20, 255), 2.0f, ImpactKind.Bloom, 0.2f),
        [WeaponEffectAssigner.Sets.FlameJet] = new(RangedForm.Cone,
            new Color(255, 190, 60, 255), new Color(255, 90, 20, 255), 1.4f, ImpactKind.Bloom, 0.6f),
        [WeaponEffectAssigner.Sets.GravityPulse] = new(RangedForm.Bolt,
            new Color(190, 120, 255, 255), new Color(100, 40, 190, 255), 1.5f, ImpactKind.Ring, 0.9f),
        [WeaponEffectAssigner.Sets.GaussParticle] = new(RangedForm.Beam,
            new Color(130, 255, 150, 255), new Color(30, 190, 80, 255), 1.1f, ImpactKind.Spark, 0.2f),
        [WeaponEffectAssigner.Sets.LaserBeam] = new(RangedForm.Beam,
            new Color(255, 90, 90, 255), new Color(255, 170, 170, 255), 0.8f, ImpactKind.Spark, 0.15f),
        [WeaponEffectAssigner.Sets.MissileRocket] = new(RangedForm.Rocket,
            new Color(255, 220, 160, 255), new Color(140, 140, 140, 255), 1.3f, ImpactKind.Explosion, 0.9f),
        [WeaponEffectAssigner.Sets.MortarArtillery] = new(RangedForm.Lobbed,
            new Color(120, 115, 100, 255), new Color(255, 180, 80, 255), 1.6f, ImpactKind.Explosion, 0.95f),
        [WeaponEffectAssigner.Sets.BioOrganic] = new(RangedForm.Glob,
            new Color(150, 220, 80, 255), new Color(120, 60, 160, 255), 1.3f, ImpactKind.Splatter, 0.9f),
        [WeaponEffectAssigner.Sets.StormTracer] = new(RangedForm.Tracer,
            new Color(255, 250, 230, 255), new Color(255, 200, 90, 255), 1.2f, ImpactKind.Spark, 0.85f),
        [WeaponEffectAssigner.Sets.BallisticSlug] = new(RangedForm.Tracer,
            new Color(255, 220, 80, 255), new Color(255, 220, 80, 255), 1.0f, ImpactKind.Spark, 0.85f),
        [WeaponEffectAssigner.Sets.ArcanePsychic] = new(RangedForm.Bolt,
            new Color(240, 110, 240, 255), new Color(255, 210, 90, 255), 1.4f, ImpactKind.Ring, 0.9f),
        [WeaponEffectAssigner.Sets.ShardCrystal] = new(RangedForm.Bolt,
            new Color(150, 240, 255, 255), new Color(60, 170, 220, 160), 1.0f, ImpactKind.Shatter, 0.85f),
    };

    private static readonly Dictionary<string, MeleeEffectStyle> MeleeStyles = new()
    {
        [WeaponEffectAssigner.Sets.EnergyBlade] = new(MeleeForm.Slash,
            new Color(120, 230, 255, 255), new Color(30, 120, 220, 255), 1.0f, MeleeAccent.None, Afterimage: true),
        [WeaponEffectAssigner.Sets.TitanImpact] = new(MeleeForm.Smash,
            new Color(205, 195, 175, 255), new Color(120, 110, 90, 255), 1.6f, MeleeAccent.None, Afterimage: false),
        [WeaponEffectAssigner.Sets.ShockMelee] = new(MeleeForm.Slash,
            new Color(175, 210, 255, 255), new Color(80, 140, 255, 255), 1.0f, MeleeAccent.ElectricArcs, Afterimage: false),
        [WeaponEffectAssigner.Sets.ChainBlade] = new(MeleeForm.Slash,
            new Color(205, 205, 210, 255), new Color(80, 80, 85, 255), 1.1f, MeleeAccent.Teeth, Afterimage: false),
        [WeaponEffectAssigner.Sets.ToxicMelee] = new(MeleeForm.Slash,
            new Color(150, 220, 90, 255), new Color(90, 160, 50, 255), 1.0f, MeleeAccent.Ooze, Afterimage: false),
        [WeaponEffectAssigner.Sets.DaemonArcaneMelee] = new(MeleeForm.Slash,
            new Color(185, 90, 225, 255), new Color(60, 20, 90, 255), 1.1f, MeleeAccent.Smoke, Afterimage: true),
        [WeaponEffectAssigner.Sets.SpearPierce] = new(MeleeForm.Thrust,
            new Color(225, 230, 240, 255), new Color(140, 150, 165, 255), 1.0f, MeleeAccent.None, Afterimage: false),
        [WeaponEffectAssigner.Sets.ClawRend] = new(MeleeForm.Rake,
            new Color(235, 85, 85, 255), new Color(150, 30, 30, 255), 1.0f, MeleeAccent.None, Afterimage: false),
        [WeaponEffectAssigner.Sets.CrudeMelee] = new(MeleeForm.Slash,
            new Color(185, 155, 120, 255), new Color(95, 75, 55, 255), 1.3f, MeleeAccent.None, Afterimage: false),
        [WeaponEffectAssigner.Sets.BladeStandard] = new(MeleeForm.Slash,
            new Color(205, 210, 220, 255), new Color(60, 65, 75, 255), 1.0f, MeleeAccent.None, Afterimage: false),
    };

    /// <summary>The known ranged keys, for per-set sound-cue registration.</summary>
    public static IReadOnlyCollection<string> RangedKeys => RangedStyles.Keys;

    /// <summary>The known melee keys, for per-set sound-cue registration.</summary>
    public static IReadOnlyCollection<string> MeleeKeys => MeleeStyles.Keys;

    /// <summary>The key a beat's WeaponEffect actually resolves to (default for null/unknown).</summary>
    public static string ResolveRangedKey(string? key) =>
        key != null && RangedStyles.ContainsKey(key) ? key : DefaultRangedKey;

    /// <inheritdoc cref="ResolveRangedKey"/>
    public static string ResolveMeleeKey(string? key) =>
        key != null && MeleeStyles.ContainsKey(key) ? key : DefaultMeleeKey;

    public static RangedEffectStyle Ranged(string? key) => RangedStyles[ResolveRangedKey(key)];

    public static MeleeEffectStyle Melee(string? key) => MeleeStyles[ResolveMeleeKey(key)];
}
