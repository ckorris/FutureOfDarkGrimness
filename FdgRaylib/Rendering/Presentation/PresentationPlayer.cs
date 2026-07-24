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
    // Display alpha for the dice panel (#245): eases in as a beat starts, out over the end of a
    // non-held beat's duration or the tail of a held beat's linger — the panel fades instead of
    // popping. When a new dice beat replaces a still-visible panel the fade-in is skipped (no blink).
    private float _diceAlpha = 1f;
    private bool  _diceSkipFadeIn;
    private const float DiceFadeInSeconds  = 0.12f;
    private const float DiceFadeOutSeconds = 0.35f;

    // Screen-space banner for the currently-active HEADLINE BannerBeat (null when none). Only the
    // Headline tier reaches the active slot; the two lower tiers ride the concurrent track below.
    private BannerBeat? _activeBanner;
    private float _bannerProgress;

    // #275 banner tiers: Notice/Toast banners are Held, so they transfer here the frame they are
    // dequeued and animate over their full duration WHILE later beats play in the active slot. The
    // engine paces only their lead-in (300ms for a Notice, nothing at all for a Toast), so the game
    // keeps moving underneath them. Deliberately excluded from IsAnimating - a message that does not
    // stop the engine must not stop an interactive prompt either.
    private readonly List<HeldBannerState> _heldBanners = new();
    // Toasts stack, so a burst needs a ceiling; the oldest drops out when a newer one arrives.
    private const int MaxHeldBanners = 5;

    private sealed class HeldBannerState
    {
        public readonly BannerBeat Beat;
        public float Elapsed;
        public HeldBannerState(BannerBeat beat) => Beat = beat;
    }

    // Screen-space roll-off (labelled name+die stack) for the currently-active RollOffBeat (null when none).
    private RollOffBeat? _activeRollOff;
    private float _rollOffProgress;

    // World-space attack (tracers / clash) — runs on its OWN timeline, concurrent with the active
    // beat (#238): AttackBeat is a held beat with zero lead-in, so it transfers here the frame it is
    // dequeued and animates over its full NominalDuration WHILE the to-hit dice that always follow
    // it tumble in the active slot.
    private AttackBeat? _activeAttack;
    private float _attackProgress;
    private float _attackElapsedSeconds;
    private int   _attackVolleysCued;    // volley sound cues fired so far for the current attack
    private int   _attackImpactsCued;    // volley LANDINGS processed so far (#239 impact sounds)
    private bool  _attackHitStopFired;

    // Models flashing a "hurt but survived" tint (the wounded beat is active for each).
    private readonly HashSet<Guid> _wounded = new();

    // #232 casualty cascade: overlapped (Held) death/flinch beats play out here, concurrent with the
    // active slot, each over its own full NominalDuration - the engine paces only the short stagger
    // between them, so a multi-kill volley reads as rapid-fire deaths with the last one (non-held,
    // played through the active slot as usual) finishing on its own.
    private readonly List<CascadeState> _cascading = new();

    private sealed class CascadeState
    {
        public readonly PresentationBeat Beat;
        public float Elapsed;
        public CascadeState(PresentationBeat beat) => Beat = beat;
    }

    // World-space save "pings" for the currently-active SaveBeat (null when none).
    private SaveBeat? _activeSave;
    private float _saveProgress;

    private static readonly TextColor DeathTint = new(220, 40, 40, 255);  // red, fades out
    private static readonly TextColor HurtTint  = new(255, 170, 60, 255); // orange flinch, no fade

    // Brief "hit-stop" at a melee clash: the whole timeline crawls for a moment so the strike lands with
    // weight. Fired once per melee AttackBeat, at the clash. The freeze decays in real time while the
    // beat animation runs slow, so it self-corrects and never desyncs from the engine's pacing.
    private float _hitStopRemaining;
    private const float HitStopDuration  = 0.07f; // real seconds of freeze
    private const float HitStopTimeScale = 0.12f; // how slow the timeline runs during it
    private const float HitStopTriggerT  = 0.42f; // beat progress at which the clash lands

    /// <summary>True while a beat is in flight or queued — used to gate interactive prompts.</summary>
    public bool IsAnimating
    {
        get
        {
            lock (_lock)
                return _active != null || _incoming.Count > 0 || _activeAttack != null
                    || _cascading.Count > 0;
        }
    }

    /// <summary>
    /// Raised on the render thread the frame a beat becomes active (dequeued), so consumers can fire
    /// effects that should start with the visual — e.g. sound cues. Kept audio-agnostic on purpose:
    /// the renderer wires this to the <c>AudioManager</c>. Invoked outside the internal lock.
    /// </summary>
    public Action<PresentationBeat>? BeatStarted;

    /// <summary>
    /// Raised on the render thread each time a volley of the current attack starts firing — once per
    /// volley, including the first — so each visible burst of shots/swings gets its own sound cue
    /// (#238). Same audio-agnostic contract as <see cref="BeatStarted"/>. Invoked outside the lock.
    /// </summary>
    public Action<AttackBeat>? AttackVolleyStarted;

    /// <summary>
    /// Raised on the render thread when a volley's shots LAND and that volley contains at least one
    /// visual hit (#239) — the impact-sound moment. Whiffed volleys never raise it. Landing time is
    /// the effect style's LandFraction within the volley slice (melee: the clash instant). Same
    /// audio-agnostic contract as <see cref="BeatStarted"/>. Invoked outside the lock.
    /// </summary>
    public Action<AttackBeat>? AttackVolleyImpact;

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
        List<PresentationBeat>? started = null;
        AttackBeat? volleyCued = null;
        AttackBeat? impactCued = null;
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
                // A panel carrying info chips lingers longer — same principle as the engine's
                // stretched beat duration: more to read, more time to read it (#245).
                float lingerLimit = DiceHoldLingerSeconds + 0.4f * (_activeDice?.InfoBlocks ?? 0);
                // Fade the parked panel out over the linger's tail instead of popping (#245).
                _diceAlpha = Math.Clamp((lingerLimit - _diceLingerSeconds) / DiceFadeOutSeconds, 0f, 1f);
                if (_diceLingerSeconds >= lingerLimit)
                {
                    _activeDice = null;
                    _diceHeld = false;
                }
            }

            // Dequeue until a beat holds the active slot. An AttackBeat never does (#238): it
            // transfers to its own concurrent track, so the to-hit dice behind it become active this
            // same frame and shots fly while the dice tumble.
            while (_active == null && _incoming.Count > 0)
            {
                PresentationBeat next = _incoming.Dequeue();
                (started ??= new List<PresentationBeat>()).Add(next);
                if (next is AttackBeat attack)
                {
                    _activeAttack         = attack;
                    _attackProgress       = 0f;
                    _attackElapsedSeconds = 0f;
                    _attackVolleysCued    = 0;
                    _attackImpactsCued    = 0;
                    _attackHitStopFired   = false;
                    continue;
                }
                // #232: an overlapped casualty beat never holds the active slot - it transfers to the
                // cascade track and animates concurrently while later beats play.
                if (next.Held && next is ModelDiedBeat or ModelWoundedBeat)
                {
                    _cascading.Add(new CascadeState(next));
                    continue;
                }
                // #275: same treatment for a Notice/Toast banner - it announces over the top of play
                // instead of interrupting it.
                if (next is BannerBeat heldBanner && heldBanner.Held)
                {
                    AddHeldBanner(heldBanner);
                    continue;
                }
                _active = next;
                _elapsedSeconds = 0f;
            }

            if (_active != null)
            {
                _elapsedSeconds += dtSeconds;
                float dur = (float)_active.NominalDuration.TotalSeconds;
                float t = dur <= 0f ? 1f : Math.Clamp(_elapsedSeconds / dur, 0f, 1f);

                Advance(_active, t);

                // A held dice beat parks once past its lead-in: keep it displayed (settled) and free the
                // active slot so following action beats play WHILE it lingers.
                if (_active is DiceRolledBeat heldDice && heldDice.Held
                    && _elapsedSeconds >= heldDice.HoldLeadIn.TotalSeconds)
                {
                    _diceHeld = true;
                    _diceLingerSeconds = 0f;
                    _diceProgress = 1f; // fully settled while parked
                    _diceAlpha = 1f;
                    _active = null;
                }
                else if (_elapsedSeconds >= dur)
                {
                    Finish(_active);
                    _active = null;
                }
            }

            // The cascade track (#232) advances every overlapped casualty animation each frame,
            // independent of the active slot; each finishes after its own full duration.
            for (int i = _cascading.Count - 1; i >= 0; i--)
            {
                CascadeState c = _cascading[i];
                c.Elapsed += dtSeconds;
                float cDur = (float)c.Beat.NominalDuration.TotalSeconds;
                Advance(c.Beat, cDur <= 0f ? 1f : Math.Clamp(c.Elapsed / cDur, 0f, 1f));
                if (c.Elapsed >= cDur)
                {
                    Finish(c.Beat);
                    _cascading.RemoveAt(i);
                }
            }

            // #275: the held-banner track, likewise independent of the active slot. Pure display -
            // nothing here touches model state, so it only ages and retires.
            for (int i = _heldBanners.Count - 1; i >= 0; i--)
            {
                HeldBannerState b = _heldBanners[i];
                b.Elapsed += dtSeconds;
                if (b.Elapsed >= (float)b.Beat.NominalDuration.TotalSeconds) _heldBanners.RemoveAt(i);
            }

            // The concurrent attack track advances every frame, independent of the active slot.
            if (_activeAttack != null)
            {
                _attackElapsedSeconds += dtSeconds;
                float dur = (float)_activeAttack.NominalDuration.TotalSeconds;
                float t = dur <= 0f ? 1f : Math.Clamp(_attackElapsedSeconds / dur, 0f, 1f);
                _attackProgress = t;

                // Attack activity keeps a parked dice display alive, like any action beat would.
                if (_diceHeld) _diceLingerSeconds = 0f;

                // One sound cue per volley, when its time slice begins (at most one per frame; a
                // dropped frame catches up on the next).
                if (_attackVolleysCued < VolleysStarted(t, _activeAttack.VolleyCount))
                {
                    volleyCued = _activeAttack;
                    _attackVolleysCued++;
                }

                // #239: one impact cue per volley that LANDS something, at the moment its shots
                // arrive (the effect style's LandFraction into the volley slice; melee at the
                // clash). Whiffed volleys advance the counter silently. At most one per frame.
                if (_attackImpactsCued < ImpactsLanded(t, _activeAttack.VolleyCount, LandFraction(_activeAttack)))
                {
                    int volley = _attackImpactsCued;
                    _attackImpactsCued++;
                    int volleys = Math.Max(1, _activeAttack.VolleyCount);
                    int visualHits = AttackShotPlan.VisualHits(_activeAttack.HitCount, _activeAttack.AttackCount,
                        AttackShotPlan.TotalShots(_activeAttack.From.Count, volleys));
                    if (AttackShotPlan.VolleyHasHit(volley, _activeAttack.From.Count, volleys, visualHits))
                        impactCued = _activeAttack;
                }

                // A melee clash fires a one-time hit-stop for weight — but only when something
                // actually connects (#239): a whiffed swing has no impact to freeze on.
                if (!_attackHitStopFired && _activeAttack.IsMelee && t >= HitStopTriggerT)
                {
                    _attackHitStopFired = true;
                    if (AttackShotPlan.HasAnyHit(_activeAttack))
                        _hitStopRemaining = HitStopDuration;
                }

                if (_attackElapsedSeconds >= dur) _activeAttack = null;
            }
        }

        // Outside the lock: the cue handlers (sound) shouldn't run under our lock, and they never
        // re-enter the player.
        if (started != null)
            foreach (PresentationBeat beat in started) BeatStarted?.Invoke(beat);
        if (volleyCued != null) AttackVolleyStarted?.Invoke(volleyCued);
        if (impactCued != null) AttackVolleyImpact?.Invoke(impactCued);
    }

    // #275. Called under the lock. A Notice supersedes any Notice already up: they share one band in
    // the middle of the screen, and two of them there would overlap into mush - the newer statement is
    // the one that matters. Toasts stack instead (that IS the tier), bounded by MaxHeldBanners.
    private void AddHeldBanner(BannerBeat banner)
    {
        if (banner.Tier == EBannerTier.Notice)
            _heldBanners.RemoveAll(b => b.Beat.Tier == EBannerTier.Notice);

        _heldBanners.Add(new HeldBannerState(banner));

        while (_heldBanners.Count > MaxHeldBanners) _heldBanners.RemoveAt(0);
    }

    // #238: how many volleys have begun by progress t. Volley v owns the slice starting at t = v/volleys
    // (matching AttackOverlay), so this is floor(t*volleys)+1 capped at the volley count — 1 the moment
    // the attack starts, stepping up as each later volley's slice opens. Internal for tests.
    internal static int VolleysStarted(float t, int volleyCount)
    {
        int volleys = Math.Max(1, volleyCount);
        return Math.Min(volleys, (int)(Math.Clamp(t, 0f, 1f) * volleys) + 1);
    }

    // #239: how many volleys' shots have LANDED by progress t — volley v lands at
    // t = (v + landFraction)/volleys, so the impact trails the firing cue by the flight time the
    // effect style declares. Internal for tests.
    internal static int ImpactsLanded(float t, int volleyCount, float landFraction)
    {
        int volleys = Math.Max(1, volleyCount);
        float x = Math.Clamp(t, 0f, 1f) * volleys - Math.Clamp(landFraction, 0f, 0.99f);
        return Math.Clamp((int)MathF.Floor(x) + 1, 0, volleys);
    }

    // Where in the volley slice the shots land: melee at the clash instant (the same moment the
    // hit-stop bites); ranged per the effect style (beams are near-instant, lobbed shells late).
    private static float LandFraction(AttackBeat attack) => attack.IsMelee
        ? HitStopTriggerT
        : WeaponEffectCatalog.Ranged(attack.WeaponEffect).LandFraction;

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
                if (!ReferenceEquals(_activeDice, dice))
                    _diceSkipFadeIn = _activeDice != null; // replacing a visible panel — no blink to zero
                _activeDice = dice;
                _diceProgress = t;
                _diceHeld = false; // an actively-animating dice beat is not parked; it replaces any parked one
                float fadeIn = _diceSkipFadeIn ? 1f : Math.Min(1f, _elapsedSeconds / DiceFadeInSeconds);
                float fadeOut = 1f;
                if (!dice.Held) // a held beat parks and fades on the linger instead
                {
                    float diceDur = (float)dice.NominalDuration.TotalSeconds;
                    fadeOut = Math.Clamp((diceDur - _elapsedSeconds) / DiceFadeOutSeconds, 0f, 1f);
                }
                _diceAlpha = Math.Min(fadeIn, fadeOut);
                break;
            // Headline only: a Held (Notice/Toast) banner was diverted to the held track at dequeue and
            // never reaches the active slot (#275).
            case BannerBeat banner:
                _activeBanner = banner;
                _bannerProgress = t;
                break;
            case RollOffBeat rollOff:
                _activeRollOff = rollOff;
                _rollOffProgress = t;
                break;
            // AttackBeat never reaches here: it runs on its own concurrent track (see Update, #238).
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
                _diceSkipFadeIn = false;
                break;
            case BannerBeat:
                _activeBanner = null;
                break;
            case RollOffBeat:
                _activeRollOff = null;
                break;
            case SaveBeat:
                _activeSave = null;
                break;
            case ModelWoundedBeat wounded:
                _wounded.Remove(wounded.Model.ID); // back to normal color
                break;
        }
    }

    /// <summary>
    /// The dice roll being shown this frame, if any, with its 0..1 progress and display alpha
    /// (fade in/out easing, #245 — the overlay multiplies its colors by it).
    /// </summary>
    public bool TryGetActiveDice(out DiceRolledBeat beat, out float progress, out float alpha)
    {
        lock (_lock)
        {
            beat = _activeDice!;
            progress = _diceProgress;
            alpha = _diceAlpha;
            return _activeDice != null;
        }
    }

    /// <summary>
    /// The HEADLINE banner being shown this frame, if any, with its 0..1 progress. Lower tiers never
    /// reach the active slot — see <see cref="GetHeldBanners"/> (#275).
    /// </summary>
    public bool TryGetActiveBanner(out BannerBeat beat, out float progress)
    {
        lock (_lock)
        {
            beat = _activeBanner!;
            progress = _bannerProgress;
            return _activeBanner != null;
        }
    }

    /// <summary>
    /// The Notice/Toast banners on screen this frame with their 0..1 progress, oldest first (#275) —
    /// at most one Notice, plus however many toasts are stacked. Snapshot taken under the lock.
    /// </summary>
    public IReadOnlyList<(BannerBeat beat, float progress)> GetHeldBanners()
    {
        lock (_lock)
        {
            var live = new List<(BannerBeat, float)>(_heldBanners.Count);
            foreach (HeldBannerState b in _heldBanners)
            {
                float dur = (float)b.Beat.NominalDuration.TotalSeconds;
                live.Add((b.Beat, dur <= 0f ? 1f : Math.Clamp(b.Elapsed / dur, 0f, 1f)));
            }
            return live;
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
