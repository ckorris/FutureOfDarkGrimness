using System;
using System.Collections.Generic;
using System.Numerics;
using FDG;
using Raylib_cs;

namespace FdgRaylib.Rendering;

/// <summary>
/// #331: firework bursts behind the game-over card, in the winning side's player colours.
///
/// <para>
/// Tuned for the app's 30 FPS cap (<c>Raylib.SetTargetFPS(30)</c>). At 33ms a frame, fast pinpoint sparks
/// strobe - they travel too far between frames to read as motion - so every particle is drawn as a LINE
/// from where it was last frame to where it is now. That segment is the spark, and it restores the
/// continuity the frame rate takes away; it is also cheaper than the circle it replaces. Speeds stay
/// moderate and lifetimes long for the same reason: the slow, drifting tail is the part that reads well.
/// </para>
///
/// <para>
/// Cost is a non-issue at these counts - a few hundred line segments against a 33ms budget - but the pool
/// is preallocated and never grows, so a long celebration allocates nothing per frame.
/// </para>
/// </summary>
public sealed class VictoryFireworks
{
    // Pool ceiling. Roughly three bursts can overlap at the tuning below, so this is ~4x headroom; when it
    // is full, new sparks are simply dropped rather than growing the array.
    private const int MaxParticles = 900;
    private const int ParticlesPerBurst = 64;

    private const float BurstIntervalMin = 0.55f;
    private const float BurstIntervalMax = 1.15f;

    private const float GravityPxPerSec2 = 260f;
    private const float SpeedMin = 130f;
    private const float SpeedMax = 340f;
    private const float LifeMin = 1.4f;
    private const float LifeMax = 2.4f;
    private const float SparkThickness = 2.2f;

    // Per-second velocity retention, applied as pow(Drag, dt) so the decay is frame-rate independent.
    private const float Drag = 0.35f;

    private struct Particle
    {
        public Vector2 Pos;
        public Vector2 Prev;
        public Vector2 Vel;
        public float Life;
        public float MaxLife;
        public Color Color;
        public bool Alive;
    }

    private readonly Particle[] _particles = new Particle[MaxParticles];
    private readonly Random _rng = new Random();
    private IReadOnlyList<Color> _palette = Array.Empty<Color>();
    private float _nextBurstIn;
    private int _burstIndex;

    /// <summary>True once <see cref="Restart"/> has been handed at least one colour to celebrate with.</summary>
    public bool IsActive => _palette.Count > 0;

    /// <summary>
    /// Begin (or restart) a celebration in <paramref name="colors"/>. An empty list stands the effect down
    /// entirely, which is what a game nobody won should do.
    /// </summary>
    public void Restart(IReadOnlyList<Color> colors)
    {
        _palette = colors;
        _burstIndex = 0;
        _nextBurstIn = 0f; // first burst on the next update, so the card and the fireworks arrive together
        Array.Clear(_particles, 0, _particles.Length);
    }

    public void Stop() => Restart(Array.Empty<Color>());

    /// <summary>
    /// The colours to celebrate in: every player on every winning team, so a team win shows both members'
    /// colours and a tie shows all the tied sides. Empty in, empty out - no leader, no fireworks.
    /// </summary>
    public static IReadOnlyList<Color> ColorsForWinners(IReadOnlyList<TeamScore> topTeams,
        Func<PlayerID, Color> colorForPlayer)
    {
        var colors = new List<Color>();
        foreach (TeamScore team in topTeams)
            foreach (PlayerID player in team.Players)
                colors.Add(colorForPlayer(player));
        return colors;
    }

    /// <summary>
    /// Advance the simulation. <paramref name="areaW"/> is the board area's width, so bursts stay over the
    /// table rather than behind the right-hand panel column.
    /// </summary>
    public void Update(float dt, int areaW, int screenH)
    {
        if (!IsActive || dt <= 0f) return;

        _nextBurstIn -= dt;
        if (_nextBurstIn <= 0f)
        {
            Burst(areaW, screenH);
            _nextBurstIn = BurstIntervalMin + (float)_rng.NextDouble() * (BurstIntervalMax - BurstIntervalMin);
        }

        float retain = MathF.Pow(Drag, dt);
        for (int i = 0; i < _particles.Length; i++)
        {
            ref Particle p = ref _particles[i];
            if (!p.Alive) continue;

            p.Life -= dt;
            if (p.Life <= 0f)
            {
                p.Alive = false;
                continue;
            }

            // Previous position is captured before integrating: the segment between the two IS the spark.
            p.Prev = p.Pos;
            p.Vel = new Vector2(p.Vel.X * retain, p.Vel.Y * retain + GravityPxPerSec2 * dt);
            p.Pos += p.Vel * dt;
        }
    }

    public void Draw()
    {
        if (!IsActive) return;

        for (int i = 0; i < _particles.Length; i++)
        {
            ref Particle p = ref _particles[i];
            if (!p.Alive) continue;

            // Fade over the back half of the life only, so a spark stays bright while it is moving fastest
            // and dims as it drifts - fading from the instant of the burst reads as washed out.
            float t = p.Life / p.MaxLife;
            float fade = t > 0.5f ? 1f : t * 2f;
            var color = new Color(p.Color.R, p.Color.G, p.Color.B, (byte)Math.Clamp(fade * 255f, 0f, 255f));
            Raylib.DrawLineEx(p.Prev, p.Pos, SparkThickness, color);
        }
    }

    // One shell: a ring of sparks from a single point, in a single colour. Successive bursts step through
    // the palette rather than mixing within a shell, so a two-colour team reads as alternating shells.
    private void Burst(int areaW, int screenH)
    {
        Color color = _palette[_burstIndex % _palette.Count];
        _burstIndex++;

        float marginX = areaW * 0.14f;
        float originX = marginX + (float)_rng.NextDouble() * MathF.Max(1f, areaW - 2f * marginX);
        float originY = screenH * (0.14f + (float)_rng.NextDouble() * 0.34f);
        var origin = new Vector2(originX, originY);

        // A slight per-shell squash and rotation keeps repeated bursts from reading as the same stamp.
        float tilt = (float)_rng.NextDouble() * MathF.PI;
        float squash = 0.75f + (float)_rng.NextDouble() * 0.35f;

        for (int i = 0; i < ParticlesPerBurst; i++)
        {
            int slot = FindFreeSlot();
            if (slot < 0) return; // pool full: drop the rest of the shell rather than grow

            float angle = (float)_rng.NextDouble() * MathF.PI * 2f;
            float speed = SpeedMin + (float)_rng.NextDouble() * (SpeedMax - SpeedMin);
            var dir = new Vector2(MathF.Cos(angle) * squash, MathF.Sin(angle));
            dir = new Vector2(
                dir.X * MathF.Cos(tilt) - dir.Y * MathF.Sin(tilt),
                dir.X * MathF.Sin(tilt) + dir.Y * MathF.Cos(tilt));

            float life = LifeMin + (float)_rng.NextDouble() * (LifeMax - LifeMin);
            _particles[slot] = new Particle
            {
                Pos = origin,
                Prev = origin,
                Vel = dir * speed,
                Life = life,
                MaxLife = life,
                Color = color,
                Alive = true,
            };
        }
    }

    private int FindFreeSlot()
    {
        for (int i = 0; i < _particles.Length; i++)
            if (!_particles[i].Alive) return i;
        return -1;
    }

    /// <summary>Live particle count - for tests, which need to see the pool fill and drain.</summary>
    public int LiveCount
    {
        get
        {
            int count = 0;
            foreach (Particle p in _particles)
                if (p.Alive) count++;
            return count;
        }
    }
}
