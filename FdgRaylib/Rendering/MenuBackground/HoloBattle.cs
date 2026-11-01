namespace FdgRaylib.Rendering.MenuBackground;

/// <summary>
/// Everything one battle on the menu's war table needs (#448): the terrain, the two armies' units and
/// what they do. <see cref="Generate"/> deals a fresh one each battle - random terrain, random pairing,
/// random positions and routes, and from those the shooting (owner, 2026-10-30) - keeping only layouts
/// worth watching: both sides firing, at least <see cref="MinFights"/> fights, and a measuring line.
/// </summary>
internal sealed record HoloBattle(HoloTableScene Scene, HoloSkirmish Skirmish, HoloEngagement Engagement, bool IsFallback)
{
    public const int MinFights = 2;
    private const int Attempts = 60;

    public static HoloBattle Generate(Random rng)
    {
        for (int attempt = 0; attempt < Attempts; attempt++)
        {
            HoloTableScene scene = HoloTableScene.BuildRandom(rng);
            var (near, far) = MenuArmies.PickPair(rng);
            HoloSkirmish? skirmish = HoloSkirmish.Generate(near, far, scene, rng);
            if (skirmish is null) continue;
            HoloEngagement plan = HoloEngagement.Plan(skirmish, scene.Footprints);
            if (IsWorthWatching(plan)) return new HoloBattle(scene, skirmish, plan, IsFallback: false);
        }

        // Practically unreachable (tests measure it), but never leave the table empty: the signed-off layout.
        var (n, f) = MenuArmies.PickPair(rng);
        HoloTableScene fixedScene = HoloTableScene.BuildDefault();
        var fixedSkirmish = new HoloSkirmish(n, f);
        return new HoloBattle(fixedScene, fixedSkirmish, HoloEngagement.Plan(fixedSkirmish, fixedScene.Footprints), IsFallback: true);
    }

    /// <summary>Both sides shooting, at least <see cref="MinFights"/> fights, and something to measure.</summary>
    public static bool IsWorthWatching(HoloEngagement plan) =>
        plan.Fights.Count >= MinFights
        && plan.Fights.Select(f => f.Shooter.Side).Distinct().Count() == 2
        && plan.Measure is not null;
}
