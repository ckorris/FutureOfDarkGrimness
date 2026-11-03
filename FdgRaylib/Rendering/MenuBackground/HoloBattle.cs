namespace FdgRaylib.Rendering.MenuBackground;

/// <summary>
/// Everything one battle on the menu's war table needs (#448): the terrain, the two armies' units and
/// what they do. <see cref="Generate"/> deals a fresh one each battle - random terrain, random pairing,
/// random positions and routes, and from those the shooting (owner, 2026-10-30) - keeping only layouts
/// worth watching: both sides firing, at least <see cref="MinFights"/> fights, about two thirds of the
/// units in combat (#464), and a measuring line.
/// </summary>
internal sealed record HoloBattle(HoloTableScene Scene, HoloSkirmish Skirmish, HoloEngagement Engagement, bool IsFallback)
{
    public const int MinFights = 2;

    /// <summary>How many of the eight units are in combat - shooting, shot at or in melee - in a battle
    /// worth watching: about two thirds (owner, 2026-11-02, #464), the rest patrolling or out of reach.</summary>
    public const int MinInCombat = 5, MaxInCombat = 6;
    private const int Attempts = 60;

    public static HoloBattle Generate(Random rng)
    {
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            // Units first, on a bare table, then the terrain fitted round them (#464): placing units among
            // terrain failed so often - a melee needs a clear run across the centre line - that it was most
            // of the cost.
            var (near, far) = MenuArmies.PickPair(rng);
            HoloSkirmish? skirmish = HoloSkirmish.Generate(near, far, Array.Empty<HoloFootprint>(), rng);
            if (skirmish is null) continue;
            HoloTableScene scene = HoloTableScene.BuildRandom(rng, skirmish.KeepOut);
            HoloEngagement plan = HoloEngagement.Plan(skirmish, scene.Footprints);
            if (IsWorthWatching(plan)) return new HoloBattle(scene, skirmish, plan, IsFallback: false);
        }

        // Practically unreachable (tests measure it), but never leave the table empty: the signed-off layout.
        var (n, f) = MenuArmies.PickPair(rng);
        HoloTableScene fixedScene = HoloTableScene.BuildDefault();
        var fixedSkirmish = new HoloSkirmish(n, f);
        return new HoloBattle(fixedScene, fixedSkirmish, HoloEngagement.Plan(fixedSkirmish, fixedScene.Footprints), IsFallback: true);
    }

    /// <summary>Both sides shooting, at least <see cref="MinFights"/> fights, about two thirds of the table
    /// in combat, and something to measure.</summary>
    public static bool IsWorthWatching(HoloEngagement plan) =>
        plan.Fights.Count >= MinFights
        && plan.InCombat.Count is >= MinInCombat and <= MaxInCombat
        && plan.Fights.Select(f => f.Shooter.Side).Distinct().Count() == 2
        && plan.Measure is not null;
}
