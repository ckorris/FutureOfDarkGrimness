using System.Collections.Generic;
using System.Linq;
using FDG;
using FDG.ArmyBuilding;
using FDG.Presentation.Beats;
using FdgRaylib.Rendering.Presentation;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #239 — the shot plan is the single source of truth for which visual shots connect; the overlay's
// impact visuals and the per-volley impact sound cues both consume it, so it must be deterministic,
// exact in count, and evenly spread.
[TestFixture]
public class AttackShotPlanTests
{
    [Test]
    public void VisualHits_ScalesTheTrueFractionOntoTheVisualGrid()
    {
        // 3 of 6 attacks hit; 4 visual shots -> 2 land.
        Assert.That(AttackShotPlan.VisualHits(hitCount: 3f, attackCount: 6f, totalShots: 4), Is.EqualTo(2));
    }

    [Test]
    public void VisualHits_LegacyBeatWithoutCounts_AllShotsConnect()
    {
        Assert.That(AttackShotPlan.VisualHits(0f, attackCount: 0f, totalShots: 5), Is.EqualTo(5),
            "AttackCount <= 0 means the beat predates #239: keep the old all-hit rendering");
    }

    [Test]
    public void VisualHits_TotalWhiff_ShowsNoImpacts()
    {
        Assert.That(AttackShotPlan.VisualHits(0f, attackCount: 4f, totalShots: 8), Is.EqualTo(0));
    }

    [Test]
    public void VisualHits_TinyFractionalHit_StillShowsOneImpact()
    {
        // Realistic-mode dice: 0.4 hits of 6 attacks would round to zero visual hits, but wounds
        // may still land — show at least one impact.
        Assert.That(AttackShotPlan.VisualHits(0.4f, attackCount: 6f, totalShots: 5), Is.EqualTo(1));
    }

    [TestCase(12, 5)]
    [TestCase(12, 12)]
    [TestCase(12, 0)]
    [TestCase(7, 3)]
    [TestCase(1, 1)]
    public void ShotHits_ExactCount_AndDeterministic(int total, int hits)
    {
        List<int> landing = Enumerable.Range(0, total)
            .Where(s => AttackShotPlan.ShotHits(s, total, hits)).ToList();

        Assert.That(landing, Has.Count.EqualTo(hits), "exactly the visual hit count must land");
        Assert.That(landing, Is.EqualTo(Enumerable.Range(0, total)
            .Where(s => AttackShotPlan.ShotHits(s, total, hits)).ToList()),
            "the plan is a pure function of its inputs");
    }

    [Test]
    public void ShotHits_SpreadsHitsInsteadOfFrontLoading()
    {
        // 3 hits over 9 shots: one hit per 3-shot stretch, not the first three shots.
        List<int> landing = Enumerable.Range(0, 9)
            .Where(s => AttackShotPlan.ShotHits(s, 9, 3)).ToList();
        Assert.That(landing, Is.EqualTo(new[] { 2, 5, 8 }));
    }

    [Test]
    public void VolleyHasHit_ReflectsThePerShotPlan()
    {
        // 2 shooters x 3 volleys = 6 shots, 2 hits -> shots 2 and 5 land (volleys 1 and 2).
        Assert.That(AttackShotPlan.VolleyHasHit(0, shooterCount: 2, volleyCount: 3, visualHits: 2), Is.False);
        Assert.That(AttackShotPlan.VolleyHasHit(1, shooterCount: 2, volleyCount: 3, visualHits: 2), Is.True);
        Assert.That(AttackShotPlan.VolleyHasHit(2, shooterCount: 2, volleyCount: 3, visualHits: 2), Is.True);
    }

    [Test]
    public void HasAnyHit_GatesOnCountsWithLegacyFallback()
    {
        Assert.That(AttackShotPlan.HasAnyHit(Beat(hit: 0f, attacks: 3f)), Is.False, "a whiff lands nothing");
        Assert.That(AttackShotPlan.HasAnyHit(Beat(hit: 1f, attacks: 3f)), Is.True);
        Assert.That(AttackShotPlan.HasAnyHit(Beat(hit: 0f, attacks: 0f)), Is.True, "legacy beats keep the old behavior");
    }

    private static AttackBeat Beat(float hit, float attacks) => new(isMelee: true,
        from: new List<Position> { new(0f, 0f) },
        to: new List<Position> { new(1f, 0f) },
        volleyCount: 3, armorPenetration: 0,
        weaponEffect: null, hitCount: hit, attackCount: attacks);
}

// #239 — every key the engine-side assigner can emit must resolve to a real style here, and
// unknown/null keys must degrade to the pre-#239 defaults instead of throwing.
[TestFixture]
public class WeaponEffectCatalogTests
{
    [Test]
    public void EveryAssignerRangedKey_HasAStyle()
    {
        foreach (string key in new[]
        {
            WeaponEffectAssigner.Sets.PlasmaBolt, WeaponEffectAssigner.Sets.FusionMelta,
            WeaponEffectAssigner.Sets.FlameJet, WeaponEffectAssigner.Sets.GravityPulse,
            WeaponEffectAssigner.Sets.GaussParticle, WeaponEffectAssigner.Sets.LaserBeam,
            WeaponEffectAssigner.Sets.MissileRocket, WeaponEffectAssigner.Sets.MortarArtillery,
            WeaponEffectAssigner.Sets.BioOrganic, WeaponEffectAssigner.Sets.StormTracer,
            WeaponEffectAssigner.Sets.BallisticSlug, WeaponEffectAssigner.Sets.ArcanePsychic,
            WeaponEffectAssigner.Sets.ShardCrystal,
        })
        {
            Assert.That(WeaponEffectCatalog.ResolveRangedKey(key), Is.EqualTo(key),
                $"ranged key '{key}' must resolve to itself, not fall back to the default");
        }
    }

    [Test]
    public void EveryAssignerMeleeKey_HasAStyle()
    {
        foreach (string key in new[]
        {
            WeaponEffectAssigner.Sets.EnergyBlade, WeaponEffectAssigner.Sets.TitanImpact,
            WeaponEffectAssigner.Sets.ShockMelee, WeaponEffectAssigner.Sets.ChainBlade,
            WeaponEffectAssigner.Sets.ToxicMelee, WeaponEffectAssigner.Sets.DaemonArcaneMelee,
            WeaponEffectAssigner.Sets.SpearPierce, WeaponEffectAssigner.Sets.ClawRend,
            WeaponEffectAssigner.Sets.CrudeMelee, WeaponEffectAssigner.Sets.BladeStandard,
        })
        {
            Assert.That(WeaponEffectCatalog.ResolveMeleeKey(key), Is.EqualTo(key),
                $"melee key '{key}' must resolve to itself, not fall back to the default");
        }
    }

    [Test]
    public void UnknownAndNullKeys_FallBackToTheDefaults()
    {
        Assert.That(WeaponEffectCatalog.ResolveRangedKey(null), Is.EqualTo(WeaponEffectCatalog.DefaultRangedKey));
        Assert.That(WeaponEffectCatalog.ResolveRangedKey("from-a-newer-build"), Is.EqualTo(WeaponEffectCatalog.DefaultRangedKey));
        Assert.That(WeaponEffectCatalog.ResolveMeleeKey(null), Is.EqualTo(WeaponEffectCatalog.DefaultMeleeKey));
        Assert.That(WeaponEffectCatalog.ResolveMeleeKey("from-a-newer-build"), Is.EqualTo(WeaponEffectCatalog.DefaultMeleeKey));
        Assert.That(() => WeaponEffectCatalog.Ranged(null), Throws.Nothing);
        Assert.That(() => WeaponEffectCatalog.Melee("???"), Throws.Nothing);
    }
}
