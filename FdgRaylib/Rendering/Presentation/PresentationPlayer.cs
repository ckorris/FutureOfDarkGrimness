using System;
using System.Collections.Generic;
using FDG;
using FDG.Presentation;
using FDG.Presentation.Beats;

namespace FdgRaylib.Rendering.Presentation;

/// <summary>
/// The app-side consumer of the engine's presentation-beat stream. Implements
/// <see cref="IPresentationSink"/> (fed on the engine thread) and drives a small timeline +
/// retained "visual model" on the render thread, so movement glides and deaths linger instead of
/// teleporting / vanishing.
///
/// <para>
/// Beats arrive in real time (the host self-paces between them), so the queue stays shallow and a
/// single in-flight beat is the norm. Each beat is animated over its <see cref="PresentationBeat.NominalDuration"/>
/// using real frame time, which stays in lockstep with the host's wait on the same duration.
/// </para>
///
/// <para>
/// Thread-safety: <see cref="OnBeat"/> runs on the engine thread; <see cref="Update"/> and
/// <see cref="GetModelDrawState"/> run on the render thread. All mutable state is guarded by a
/// single lock (short, uncontended critical sections at 30 FPS).
/// </para>
/// </summary>
public class PresentationPlayer : IPresentationSink
{
    private readonly object _lock = new();

    private readonly Queue<PresentationBeat> _incoming = new();
    private PresentationBeat? _active;
    private float _elapsedSeconds;

    // Per-model display overrides, keyed by ModelID.ID. A model is "claimed" by the player while
    // an override exists; otherwise the renderer falls back to authoritative state.
    private readonly Dictionary<Guid, GlideState> _glides = new();
    private readonly Dictionary<Guid, DeathState> _deaths = new();

    // Screen-space dice display for the currently-active DiceRolledBeat (null when none).
    private DiceRolledBeat? _activeDice;
    private float _diceProgress;
    // A "held" dice beat parks after its lead-in: it stays displayed (settled) while later action beats
    // play, then lingers this long with no beat activity before clearing (or until a new dice beat
    // replaces it). Action beats reset the linger, so held dice survive through the wounds they caused.
    private bool  _diceHeld;
    private float _diceLingerSeconds;
    private const float DiceHoldLingerSeconds = 2.5f;

    // Screen-space banner for the currently-active BannerBeat (null when none).
    private BannerBeat? _activeBanner;
    private float _bannerProgress;

    // Screen-space roll-off (labelled name+die stack) for the currently-active RollOffBeat (null when none).
    private RollOffBeat? _activeRollOff;
    private float _rollOffProgress;

    // World-space attack (tracers / clash) for the currently-active AttackBeat (null when none).
    private AttackBeat? _activeAttack;
    private float _attackProgress;

    // Models flashing a "hurt but survived" tint (the wounded beat is active for each).
    private readonly HashSet<Guid> _wounded = new();

    // World-space save "pings" for the currently-active SaveBeat (null when none).
    private SaveBeat? _activeSave;
    private float _saveProgress;

    private static readonly TextColor DeathTint = new(220, 40, 40, 255);  // red, fades out
    private static readonly TextColor HurtTint  = new(255, 170, 60, 255); // orange flinch, no fade

    // Brief "hit-stop" at a melee clash: the whole timeline crawls for a moment so the strike lands with
    // weight. Fired once per melee AttackBeat, at the clash. The freeze decays in real time while the
    // beat animation runs slow, so it self-corrects and never desyncs from the engine's pacing.
    private float _hitStopRemaining;
    private bool  _hitStopFiredForActive;
    private const float HitStopDuration  = 0.07f; // real seconds of freeze
    private const float HitStopTimeScale = 0.12f; // how slow the timeline runs during it
    private const float HitStopTriggerT  = 0.42f; // beat progress at which the clash lands

    /// <summary>True while a beat is in flight or queued — used to gate interactive prompts.</summary>
    public bool IsAnimating
    {
        get { lock (_lock) return _active != null || _incoming.Count > 0; }
    }

    /// <summary>
    /// Raised on the render thread the frame a beat becomes active (dequeued), so consumers can fire
    /// effects that should start with the visual — e.g. sound cues. Kept audio-agnostic on purpose:
    /// the renderer wires this to the <c>AudioManager</c>. Invoked outside the internal lock.
    /// </summary>
    public Action<PresentationBeat>? BeatStarted;

    // ---------------- engine thread ----------------

    public void OnBeat(PresentationBeat beat)
    {
        lock (_lock)
        {
            _incoming.Enqueue(beat);

            // Pre-register overrides at enqueue so the renderer holds the model in place (move) or
            // keeps drawing it (death) until the beat actually plays — no one-frame jump/vanish
            // between the authoritative state change and the animation starting.
            switch (beat)
            {
                case UnitMovedBeat moved:
                    foreach (ModelMove move in moved.Moves)
                        _glides[move.Model.ID] = new GlideState(move.Waypoints);
                    break;
                case ModelDiedBeat died:
                    _deaths[died.Model.ID] = new DeathState(died.Position);
                    break;
                case UnitRoutedBeat routed:
                    foreach (RoutedModel rm in routed.Models)
                        _deaths[rm.Model.ID] = new DeathState(rm.Position);
                    break;
                case ModelWoundedBeat wounded:
                    _wounded.Add(wounded.Model.ID);
                    break;
            }
        }
    }

    // ---------------- render thread ----------------

    public void Update(float realDt)
    {
        PresentationBeat? started = null;
        lock (_lock)
        {
            float dtSeconds = realDt;
            if (_hitStopRemaining > 0f)
            {
                _hitStopRemaining -= realDt;      // the freeze decays in real time...
                dtSeconds *= HitStopTimeScale;    // ...but the timeline crawls while it lasts
            }

            // A parked (held) dice display lingers independently of the action queue; it clears after
            // DiceHoldLingerSeconds of no beat activity (Advance resets that timer whenever an action
            // beat plays) or as soon as a new dice beat replaces it.
            if (_diceHeld)
            {
                _diceLingerSeconds += dtSeconds;
                if (_diceLingerSeconds >= DiceHoldLingerSeconds)
                {
                    _activeDice = null;
                    _diceHeld = false;
                }
            }

            if (_active == null && _incoming.Count > 0)
            {
                _active = _incoming.Dequeue();
                _elapsedSeconds = 0f;
                started = _active;
                _hitStopFiredForActive = false;
            }
            if (_active != null)
            {
                _elapsedSeconds += dtSeconds;
                float dur = (float)_active.NominalDuration.TotalSeconds;
                float t = dur <= 0f ? 1f : Math.Clamp(_elapsedSeconds / dur, 0f, 1f);

                Advance(_active, t);

                // A melee clash fires a one-time hit-stop for weight.
                if (!_hitStopFiredForActive && _active is AttackBeat { IsMelee: true } && t >= HitStopTriggerT)
                {
                    _hitStopRemaining = HitStopDuration;
                    _hitStopFiredForActive = true;
                }

                // A held dice beat parks once past its lead-in: keep it displayed (settled) and free the
                // active slot so following action beats play WHILE it lingers.
                if (_active is DiceRolledBeat heldDice && heldDice.Held
                    && _elapsedSeconds >= heldDice.HoldLeadIn.TotalSeconds)
                {
                    _diceHeld = true;
                    _diceLingerSeconds = 0f;
                    _diceProgress = 1f; // fully settled while parked
                    _active = null;
                }
                else if (_elapsedSeconds >= dur)
                {
                    Finish(_active);
                    _active = null;
                }
            }
        }

        // Outside the lock: the cue handler (sound) shouldn't run under our lock, and it never
        // re-enters the player.
        if (started != null) BeatStarted?.Invoke(started);
    }

    private void Advance(PresentationBeat beat, float t)
    {
        // Any non-dice beat playing keeps a parked dice display alive, so held dice survive through the
        // wound/death animations that immediately follow them.
        if (_diceHeld && beat is not DiceRolledBeat) _diceLingerSeconds = 0f;

        switch (beat)
        {
            case UnitMovedBeat moved:
                foreach (ModelMove move in moved.Moves)
                    if (_glides.TryGetValue(move.Model.ID, out var glide))
                        glide.SetProgress(t);
                break;
            case ModelDiedBeat died:
                if (_deaths.TryGetValue(died.Model.ID, out var death))
                    death.SetProgress(t);
                break;
            case UnitRoutedBeat routed:
                foreach (RoutedModel rm in routed.Models)
                    if (_deaths.TryGetValue(rm.Model.ID, out var routDeath))
                        routDeath.SetProgress(t); // all routed models fade together
                break;
            case DiceRolledBeat dice:
                _activeDice = dice;
                _diceProgress = t;
                _diceHeld = false; // an actively-animating dice beat is not parked; it replaces any parked one
                break;
            case BannerBeat banner:
                _activeBanner = banner;
                _bannerProgress = t;
                break;
            case RollOffBeat rollOff:
                _activeRollOff = rollOff;
                _rollOffProgress = t;
                break;
            case AttackBeat attack:
                _activeAttack = attack;
                _attackProgress = t;
                break;
            case SaveBeat save:
                _activeSave = save;
                _saveProgress = t;
                break;
            // ModelWoundedBeat is a presence flag (registered at enqueue, cleared on finish) — no per-frame work.
        }
    }

    private void Finish(PresentationBeat beat)
    {
        switch (beat)
        {
            case UnitMovedBeat moved:
                // Drop the overrides — the model now draws at its authoritative position, which is
                // the glide destination, so there is no snap.
                foreach (ModelMove move in moved.Moves)
                    _glides.Remove(move.Model.ID);
                break;
            case ModelDiedBeat died:
                if (_deaths.TryGetValue(died.Model.ID, out var death))
                    death.Done = true; // stays hidden from here on
                break;
            case UnitRoutedBeat routed:
                foreach (RoutedModel rm in routed.Models)
                    if (_deaths.TryGetValue(rm.Model.ID, out var routDeath))
                        routDeath.Done = true;
                break;
            case DiceRolledBeat:
                _activeDice = null;
                _diceHeld = false;
                break;
            case BannerBeat:
                _activeBanner = null;
                break;
            case RollOffBeat:
                _activeRollOff = null;
                break;
            case AttackBeat:
                _activeAttack = null;
                break;
            case SaveBeat:
                _activeSave = null;
                break;
            case ModelWoundedBeat wounded:
                _wounded.Remove(wounded.Model.ID); // back to normal color
                break;
        }
    }

    /// <summary>The dice roll being shown this frame, if any, with its 0..1 progress.</summary>
    public bool TryGetActiveDice(out DiceRolledBeat beat, out float progress)
    {
        lock (_lock)
        {
            beat = _activeDice!;
            progress = _diceProgress;
            return _activeDice != null;
        }
    }

    /// <summary>The banner being shown this frame, if any, with its 0..1 progress.</summary>
    public bool TryGetActiveBanner(out BannerBeat beat, out float progress)
    {
        lock (_lock)
        {
            beat = _activeBanner!;
            progress = _bannerProgress;
            return _activeBanner != null;
        }
    }

    /// <summary>The roll-off being shown this frame, if any, with its 0..1 progress.</summary>
    public bool TryGetActiveRollOff(out RollOffBeat beat, out float progress)
    {
        lock (_lock)
        {
            beat = _activeRollOff!;
            progress = _rollOffProgress;
            return _activeRollOff != null;
        }
    }

    /// <summary>The attack (tracers / clash) being shown this frame, if any, with its 0..1 progress.</summary>
    public bool TryGetActiveAttack(out AttackBeat beat, out float progress)
    {
        lock (_lock)
        {
            beat = _activeAttack!;
            progress = _attackProgress;
            return _activeAttack != null;
        }
    }

    /// <summary>The save "pings" being shown this frame, if any, with their 0..1 progress.</summary>
    public bool TryGetActiveSave(out SaveBeat beat, out float progress)
    {
        lock (_lock)
        {
            beat = _activeSave!;
            progress = _saveProgress;
            return _activeSave != null;
        }
    }

    /// <summary>
    /// How the renderer should draw <paramref name="model"/> this frame: at its glide/death
    /// override if one is active, else at its authoritative position (or hidden if dead with no
    /// death beat, e.g. a dangerous-terrain casualty).
    /// </summary>
    public ModelDrawState GetModelDrawState(IModel model)
    {
        Guid id = model.ID.ID;
        lock (_lock)
        {
            if (_deaths.TryGetValue(id, out var death))
            {
                return death.Done
                    ? ModelDrawState.Hidden
                    : new ModelDrawState(true, death.Position, death.Alpha, DeathTint);
            }

            if (_glides.TryGetValue(id, out var glide))
                return new ModelDrawState(true, glide.Current, 1f, null);

            if (_wounded.Contains(id))
                return new ModelDrawState(true, model.Position, 1f, HurtTint);

            if (model.GetIsDead())
                return ModelDrawState.Hidden;

            return new ModelDrawState(true, model.Position, 1f, null);
        }
    }

    // ---------------- per-model animation state ----------------

    /// <summary>Linear glide along a polyline, time distributed across segments by length.</summary>
    private sealed class GlideState
    {
        private readonly Position[] _points;
        private readonly float[] _cumulative; // cumulative length up to each point
        private readonly float _total;

        public Position Current { get; private set; }

        public GlideState(IReadOnlyList<Position> waypoints)
        {
            _points = new Position[waypoints.Count];
            for (int i = 0; i < waypoints.Count; i++) _points[i] = waypoints[i];

            _cumulative = new float[_points.Length];
            float running = 0f;
            for (int i = 1; i < _points.Length; i++)
            {
                running += Distance2D(_points[i - 1], _points[i]);
                _cumulative[i] = running;
            }
            _total = running;
            Current = _points.Length > 0 ? _points[0] : new Position(0f, 0f);
        }

        public void SetProgress(float t)
        {
            if (_points.Length == 1 || _total <= 0f) { Current = _points[^1]; return; }

            float target = t * _total;
            for (int i = 1; i < _points.Length; i++)
            {
                if (target <= _cumulative[i] || i == _points.Length - 1)
                {
                    float segLen = _cumulative[i] - _cumulative[i - 1];
                    float f = segLen <= 0f ? 1f : Math.Clamp((target - _cumulative[i - 1]) / segLen, 0f, 1f);
                    Current = Lerp(_points[i - 1], _points[i], f);
                    return;
                }
            }
        }

        private static float Distance2D(Position a, Position b)
        {
            float dx = b.x - a.x, dz = b.z - a.z;
            return MathF.Sqrt(dx * dx + dz * dz);
        }

        private static Position Lerp(Position a, Position b, float f) =>
            new Position(a.x + (b.x - a.x) * f, a.z + (b.z - a.z) * f);
    }

    /// <summary>Red tint throughout, holding full alpha briefly then fading to nothing.</summary>
    private sealed class DeathState
    {
        private const float FlashFraction = 0.35f;

        public Position Position { get; }
        public float Alpha { get; private set; } = 1f;
        public float Progress { get; private set; }
        public bool Done;

        public DeathState(Position position) => Position = position;

        public void SetProgress(float t)
        {
            Progress = t;
            Alpha = t < FlashFraction ? 1f : 1f - (t - FlashFraction) / (1f - FlashFraction);
        }
    }

    /// <summary>
    /// Active death locations + their 0..1 animation progress, for the renderer's dust-puff effect. Only
    /// deaths still animating (not yet fully hidden) are returned. Snapshot taken under the lock.
    /// </summary>
    public IReadOnlyList<(Position pos, float progress)> GetActiveDeathBursts()
    {
        lock (_lock)
        {
            var bursts = new List<(Position, float)>();
            foreach (DeathState d in _deaths.Values)
                if (!d.Done && d.Progress > 0f)
                    bursts.Add((d.Position, d.Progress));
            return bursts;
        }
    }
}

/// <summary>How a single model should be drawn this frame.</summary>
public readonly struct ModelDrawState
{
    public bool Visible { get; }
    public Position Position { get; }
    public float Alpha { get; }

    /// <summary>Replacement color (death = red, hurt = orange), or null to use the player's team color.</summary>
    public TextColor? Tint { get; }

    public ModelDrawState(bool visible, Position position, float alpha, TextColor? tint)
    {
        Visible = visible;
        Position = position;
        Alpha = alpha;
        Tint = tint;
    }

    public static ModelDrawState Hidden => new ModelDrawState(false, new Position(0f, 0f), 0f, null);
}
