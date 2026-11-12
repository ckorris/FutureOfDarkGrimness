using System;
using System.Collections.Generic;
using System.Linq;
using FDG;
using FdgRaylib.Rendering;
using NUnit.Framework;
using Raylib_cs;

namespace FdgRaylib.Tests;

// #331 — the fireworks behind the game-over card. Two things are worth pinning and neither needs a window:
// which colours a result celebrates in (the part that has to agree with the engine's winner), and that the
// particle pool stays bounded and drains (the part that would hurt a slower machine if it were wrong).
[TestFixture]
public class VictoryFireworksTests
{
    private static readonly Color Orange = PlayerColorOptions.Options[0].Color;
    private static readonly Color Purple = PlayerColorOptions.Options[1].Color;
    private static readonly Color Green = PlayerColorOptions.Options[2].Color;

    // ── ColorsForWinners ────────────────────────────────────────────────────────────────────────────

    [Test]
    public void SingleWinner_CelebratesInThatPlayersColor()
    {
        PlayerID alpha = Player();
        var top = new[] { new TeamScore(1, new[] { alpha }, 3) };

        IReadOnlyList<Color> colors = VictoryFireworks.ColorsForWinners(top, Lookup((alpha, Orange)));

        Assert.That(colors, Is.EqualTo(new[] { Orange }));
    }

    [Test]
    public void TeamWin_CelebratesInEveryTeammatesColor()
    {
        PlayerID alpha = Player();
        PlayerID bravo = Player();
        var top = new[] { new TeamScore(1, new[] { alpha, bravo }, 4) };

        IReadOnlyList<Color> colors = VictoryFireworks.ColorsForWinners(
            top, Lookup((alpha, Orange), (bravo, Purple)));

        Assert.That(colors, Is.EqualTo(new[] { Orange, Purple }),
            "#257 pools objectives per team, so the celebration is the team's, not the top scorer's.");
    }

    [Test]
    public void Tie_CelebratesInEveryTiedSidesColors()
    {
        PlayerID alpha = Player();
        PlayerID bravo = Player();
        var top = new[]
        {
            new TeamScore(1, new[] { alpha }, 2),
            new TeamScore(2, new[] { bravo }, 2),
        };

        IReadOnlyList<Color> colors = VictoryFireworks.ColorsForWinners(
            top, Lookup((alpha, Orange), (bravo, Green)));

        Assert.That(colors, Is.EqualTo(new[] { Orange, Green }),
            "a tie reads as shared rather than as nobody winning.");
    }

    [Test]
    public void NobodyHoldsAnything_ProducesNoColorsAndNoFireworks()
    {
        IReadOnlyList<Color> colors = VictoryFireworks.ColorsForWinners(
            Array.Empty<TeamScore>(), _ => Orange);

        Assert.That(colors, Is.Empty);

        var fireworks = new VictoryFireworks();
        fireworks.Restart(colors);

        Assert.That(fireworks.IsActive, Is.False, "no leader, no celebration.");
    }

    // ── Simulation ──────────────────────────────────────────────────────────────────────────────────

    [Test]
    public void FirstUpdate_LaunchesABurstImmediately()
    {
        var fireworks = new VictoryFireworks();
        fireworks.Restart(new[] { Orange });

        fireworks.Update(1f / 30f, areaW: 1000, screenH: 700);

        Assert.That(fireworks.LiveCount, Is.GreaterThan(0),
            "the card and the fireworks should arrive together, not a second apart.");
    }

    [Test]
    public void ParticleCountStaysBounded_OverALongCelebration()
    {
        var fireworks = new VictoryFireworks();
        fireworks.Restart(new[] { Orange, Purple });

        // Two solid minutes at 30 FPS - far longer than anyone stares at the card.
        int peak = 0;
        for (int frame = 0; frame < 30 * 120; frame++)
        {
            fireworks.Update(1f / 30f, areaW: 1000, screenH: 700);
            peak = Math.Max(peak, fireworks.LiveCount);
        }

        Assert.That(peak, Is.LessThanOrEqualTo(900), "the pool is fixed and must never be exceeded.");
        Assert.That(peak, Is.LessThan(600),
            "and the tuning should leave real headroom under the ceiling, not ride it.");
    }

    [Test]
    public void ParticlesExpire_SoTheEffectDrainsWhenBurstsStop()
    {
        var fireworks = new VictoryFireworks();
        fireworks.Restart(new[] { Orange });
        fireworks.Update(1f / 30f, areaW: 1000, screenH: 700);
        Assert.That(fireworks.LiveCount, Is.GreaterThan(0));

        fireworks.Stop();
        // Stop() clears the palette, so Update is inert and nothing lingers.
        fireworks.Update(1f / 30f, areaW: 1000, screenH: 700);

        Assert.That(fireworks.IsActive, Is.False);
        Assert.That(fireworks.LiveCount, Is.Zero);
    }

    [Test]
    public void SparksDieOfOldAge_WithinTheirMaximumLifetime()
    {
        var fireworks = new VictoryFireworks();
        fireworks.Restart(new[] { Orange });
        fireworks.Update(1f / 30f, areaW: 1000, screenH: 700);
        int born = fireworks.LiveCount;

        // Sparks live at most 2.4s, so one 3s slice outlives every one of them. A fresh burst does fire at
        // the top of that same call, but it is aged by the same 3s before the call returns, so it dies too
        // - which is exactly why this can assert on an empty pool rather than a smaller one.
        fireworks.Update(3.0f, areaW: 1000, screenH: 700);

        Assert.That(born, Is.GreaterThan(0));
        Assert.That(fireworks.LiveCount, Is.Zero,
            "sparks must age out; a leak would fill the pool and freeze the effect.");
    }

    [Test]
    public void ZeroDelta_IsIgnoredRatherThanSpawningEveryCall()
    {
        var fireworks = new VictoryFireworks();
        fireworks.Restart(new[] { Orange });

        for (int i = 0; i < 50; i++)
            fireworks.Update(0f, areaW: 1000, screenH: 700);

        Assert.That(fireworks.LiveCount, Is.Zero,
            "a paused or zero-length frame must not launch anything.");
    }

    // Helpers

    private static PlayerID Player() => new PlayerID(Guid.NewGuid());

    private static Func<PlayerID, Color> Lookup(params (PlayerID Player, Color Color)[] pairs)
    {
        var map = pairs.ToDictionary(pair => pair.Player, pair => pair.Color);
        return player => map.TryGetValue(player, out Color color) ? color : Color.White;
    }
}
