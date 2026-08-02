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
    private int   _moveStepsCued;   // #294: footfall cues fired so far for the active move beat

    // Per-model display overrides, keyed by ModelID.ID. A model is "claimed" by the player while
    // an override exists; otherwise the renderer falls back to authoritative state.
    private readonly Dictionary<Guid, GlideState> _glides = new();
    private readonly Dictionary<Guid, DeathState> _deaths = new();

    // #322 dice stack. Rolls are held beats (see DiceRolledBeat.Held), so the engine moves on after each
    // one's settle lead-in and several panels are on screen at once. A new roll STACKS on top of the last
    // instead of evicting it — single-slot eviction is precisely what forced dice to be non-held before
    // (ea91d68), because the second of two save thresholds cut the first short. Oldest first; the overlay
    // anchors the oldest at the bottom and grows upward.
    private readonly List<DiceEntry> _diceStack = new();
    private const int MaxDiceStack = 3;
    // How long a panel stays up after it has finished settling. Chips are extra reading, so a panel
    // carrying them lingers longer — the same principle as the engine's stretched beat duration (#245).
    private const float DiceLingerSeconds        = 3.0f;
    private const float DiceLingerPerInfoBlock   = 0.4f;
    // Display alpha for a dice panel (#245): eased in as it appears and out over the tail of its
    // lifetime, so panels fade instead of popping.
    private const float DiceFadeInSeconds  = 0.12f;
    private const float DiceFadeOutSeconds = 0.35f;
    // Hovering the stack freezes every panel's timer (and the overlay un-dims the older ones), so a
    // player who wants to re-read a roll can, without slowing down one who doesn't. Set from the render
    // thread once the overlay knows the stack's own screen bounds.
    private bool _diceHovered;

    /// <summary>
    /// One panel on the dice stack. Its lifetime is FIXED at construction rather than reset by later beat
    /// activity (which is what the old single-slot linger did): with several panels up, a lifetime that
    /// depends on what happens to follow makes the stack's layout unpredictable.
    /// </summary>
    private sealed class DiceEntry
    {
        public readonly DiceRolledBeat Beat;
        public readonly float Lifetime;
        public float Elapsed;

        public DiceEntry(DiceRolledBeat beat)
        {
            Beat = beat;
            // A held roll settles over its lead-in; a non-held one owns the active slot for its whole
            // duration. Either way the linger is what the panel gets ON TOP of that.
            float paced = (float)(beat.Held ? beat.HoldLeadIn : beat.NominalDuration).TotalSeconds;
            Lifetime = paced + DiceLingerSeconds + DiceLingerPerInfoBlock * beat.InfoBlocks;
        }

        /// <summary>0..1 against the beat's own envelope — drives the overlay's tumble-then-settle. A
        /// panel outlives its envelope, so this simply saturates at 1 and stays settled.</summary>
        public float Progress
        {
            get
            {
                float dur = (float)Beat.NominalDuration.TotalSeconds;
                return dur <= 0f ? 1f : Math.Clamp(Elapsed / dur, 0f, 1f);
            }
        }

        public float Alpha => Math.Min(
            Elapsed / DiceFadeInSeconds,
            Math.Clamp((Lifetime - Elapsed) / DiceFadeOutSeconds, 0f, 1f));
    }

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

    // World-space attacks (tracers / clash) — each runs on its OWN timeline, concurrent with the active
    // beat (#238): AttackBeat is a held beat with zero lead-in, so it transfers here the frame it is
    // dequeued and animates over its full NominalDuration WHILE the to-hit dice that always follow
    // it tumble.
    //
    // #322 made this a LIST. The to-hit roll used to pace 1800ms, comfortably outlasting the longest
    // attack animation (VolleyMax, 1600ms), so a second attack could never arrive before the first had
    // finished. Held rolls pace ~600ms instead, and a whiffed attack has no saves or wounds behind it, so
    // the next weapon's AttackBeat really can land mid-flight — overlapping attacks now play side by side
    // rather than truncating each other.
    private readonly List<AttackState> _attacks = new();
    private const int MaxConcurrentAttacks = 3;

    private sealed class AttackState
    {
        public readonly AttackBeat Beat;
        public float Elapsed;
        public float Progress;
        public int   VolleysCued;   // volley sound cues fired so far for this attack
        public int   ImpactsCued;   // volley LANDINGS processed so far (#239 impact sounds)
        public bool  HitStopFired;
        public AttackState(AttackBeat beat) => Beat = beat;
    }

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

    // #274 world-space spell effect (cast success/failure, per-target landing, assist streams) for the
    // currently-active SpellEffectBeat (null when none). Non-held, so it owns the active slot for its
    // full duration — that ordering IS the feature: assists, then the outcome, then the targets.
    private SpellEffectBeat? _activeSpell;
    private float _spellProgress;

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
                return _active != null || _incoming.Count > 0 || _attacks.Count > 0
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

    /// <summary>
    /// Raised on the render thread each time a footfall of the moving unit lands (#294) — once per
    /// step, including the first, with that step's index so consumers can alternate feet. Steps are
    /// spread across the glide by <see cref="StepsStarted"/>, so the patter tracks the models actually
    /// crossing the table. Same audio-agnostic contract as <see cref="BeatStarted"/>; invoked outside
    /// the lock.
    /// </summary>
    public Action<UnitMovedBeat, int>? UnitStepped;

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
        List<AttackBeat>? volleysCued = null;
        List<AttackBeat>? impactsCued = null;
        UnitMovedBeat? stepCued = null;
        int stepIndex = 0;
        lock (_lock)
        {
            float dtSeconds = realDt;
            if (_hitStopRemaining > 0f)
            {
                _hitStopRemaining -= realDt;      // the freeze decays in real time...
                dtSeconds *= HitStopTimeScale;    // ...but the timeline crawls while it lasts
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
                    _attacks.Add(new AttackState(attack));
                    while (_attacks.Count > MaxConcurrentAttacks) _attacks.RemoveAt(0);
                    continue;
                }
                // #322: every roll joins the dice stack for display. A HELD roll (the default) stops
                // there and the next beat becomes active immediately; a non-held one — the explicit
                // "stop play for this roll" opt-out — also takes the active slot below, which is what
                // makes the engine's matching full-duration wait line up with an animating front-end.
                if (next is DiceRolledBeat dice)
                {
                    PushDice(dice);
                    if (dice.Held) continue;
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
                _moveStepsCued = 0;
            }

            if (_active != null)
            {
                _elapsedSeconds += dtSeconds;
                float dur = (float)_active.NominalDuration.TotalSeconds;
                float t = dur <= 0f ? 1f : Math.Clamp(_elapsedSeconds / dur, 0f, 1f);

                Advance(_active, t);

                // #294: one footfall cue per step of the glide, when its slice begins (at most one
                // per frame; a dropped frame catches up on the next), so movement patters along with
                // the models instead of announcing itself once and going quiet.
                if (_active is UnitMovedBeat movingBeat
                    && _moveStepsCued < StepsStarted(t, dur, movingBeat.Moves.Count, movingBeat.Toughness))
                {
                    stepCued  = movingBeat;
                    stepIndex = _moveStepsCued;
                    _moveStepsCued++;
                }

                if (_elapsedSeconds >= dur)
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

            // #322: the dice stack, likewise independent of the active slot - each panel lives its own
            // fixed lifetime and retires when it runs out. Aged AFTER the dequeue above, like every other
            // concurrent track, so a panel that appeared this frame gets this frame's time too.
            // Hovering the stack freezes all of them at once.
            if (!_diceHovered)
            {
                for (int i = _diceStack.Count - 1; i >= 0; i--)
                {
                    DiceEntry entry = _diceStack[i];
                    entry.Elapsed += dtSeconds;
                    if (entry.Elapsed >= entry.Lifetime) _diceStack.RemoveAt(i);
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

            // The concurrent attack track advances every frame, independent of the active slot. Each
            // attack keeps its own cue counters, so two overlapping ones never steal each other's
            // volley/impact cues (#322).
            for (int i = _attacks.Count - 1; i >= 0; i--)
            {
                AttackState a = _attacks[i];
                a.Elapsed += dtSeconds;
                float dur = (float)a.Beat.NominalDuration.TotalSeconds;
                float t = dur <= 0f ? 1f : Math.Clamp(a.Elapsed / dur, 0f, 1f);
                a.Progress = t;

                // One sound cue per volley, when its time slice begins (at most one per frame; a
                // dropped frame catches up on the next).
                if (a.VolleysCued < VolleysStarted(t, a.Beat.VolleyCount))
                {
                    (volleysCued ??= new List<AttackBeat>()).Add(a.Beat);
                    a.VolleysCued++;
                }

                // #239: one impact cue per volley that LANDS something, at the moment its shots
                // arrive (the effect style's LandFraction into the volley slice; melee at the
                // clash). Whiffed volleys advance the counter silently. At most one per frame.
                if (a.ImpactsCued < ImpactsLanded(t, a.Beat.VolleyCount, LandFraction(a.Beat)))
                {
                    int volley = a.ImpactsCued;
                    a.ImpactsCued++;
                    int volleys = Math.Max(1, a.Beat.VolleyCount);
                    int visualHits = AttackShotPlan.VisualHits(a.Beat.HitCount, a.Beat.AttackCount,
                        AttackShotPlan.TotalShots(a.Beat.From.Count, volleys));
                    if (AttackShotPlan.VolleyHasHit(volley, a.Beat.From.Count, volleys, visualHits))
                        (impactsCued ??= new List<AttackBeat>()).Add(a.Beat);
                }

                // A melee clash fires a one-time hit-stop for weight — but only when something
                // actually connects (#239): a whiffed swing has no impact to freeze on.
                if (!a.HitStopFired && a.Beat.IsMelee && t >= HitStopTriggerT)
                {
                    a.HitStopFired = true;
                    if (AttackShotPlan.HasAnyHit(a.Beat))
                        _hitStopRemaining = HitStopDuration;
                }

                if (a.Elapsed >= dur) _attacks.RemoveAt(i);
            }
        }

        // Outside the lock: the cue handlers (sound) shouldn't run under our lock, and they never
        // re-enter the player.
        if (started != null)
            foreach (PresentationBeat beat in started) BeatStarted?.Invoke(beat);
        if (volleysCued != null)
            foreach (AttackBeat beat in volleysCued) AttackVolleyStarted?.Invoke(beat);
        if (impactsCued != null)
            foreach (AttackBeat beat in impactsCued) AttackVolleyImpact?.Invoke(beat);
        if (stepCued != null) UnitStepped?.Invoke(stepCued, stepIndex);
    }

    /// <summary>
    /// #322. Called under the lock. A new roll goes on TOP of the stack; the oldest drops out once the
    /// stack is full, so the newest panel is never the one squeezed off screen.
    /// </summary>
    private void PushDice(DiceRolledBeat beat)
    {
        _diceStack.Add(new DiceEntry(beat));
        while (_diceStack.Count > MaxDiceStack) _diceStack.RemoveAt(0);
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

    // #294 footfall cadence. Deliberately SUB-LINEAR in the model count: a ten-model horde should
    // sound busier underfoot than a lone scout, but ten times the beeps would drown the table. A
    // doubling of the unit adds a bit over one step per second and the whole thing tops out at
    // StepsPerSecondMax, so the difference between five models and twenty is texture, not volume.
    private const float StepsPerSecondBase = 2.4f;   // a single model's pace
    private const float StepsPerSecondPerDoubling = 1.15f;
    private const float StepsPerSecondMax  = 6.0f;
    // Weight drags the pace down alongside the pitch (see PresentationSoundCues.StepVoice): heavy
    // things take fewer, longer strides. Bottomed out so a Tough(24) titan still walks.
    private const float StepWeightSlowdown = 0.06f;
    private const float StepWeightMinScale = 0.55f;
    // A backstop, not a tuning knob: at the fastest cadence the longest move (MoveMax, 1500ms) works
    // out to exactly this many steps, so it only ever bites if the engine's move pacing grows.
    private const int   MaxStepsPerMove = 9;

    /// <summary>Footfalls per second for a unit of <paramref name="modelCount"/> models at
    /// <paramref name="toughness"/> weight (#294). Internal for tests.</summary>
    internal static float StepsPerSecond(int modelCount, int toughness)
    {
        int n = Math.Max(1, modelCount);
        float cadence = Math.Clamp(
            StepsPerSecondBase + StepsPerSecondPerDoubling * MathF.Log2(n),
            StepsPerSecondBase, StepsPerSecondMax);

        float weight = Math.Clamp(1f / (1f + StepWeightSlowdown * (Math.Max(1, toughness) - 1)),
            StepWeightMinScale, 1f);

        return cadence * weight;
    }

    /// <summary>
    /// #294: how many footfalls have landed by progress <paramref name="t"/> of a move beat lasting
    /// <paramref name="durationSeconds"/>. The cadence sets how many steps the whole glide gets (at
    /// least one — a shuffle still makes a sound); those are then spread evenly across it exactly the
    /// way <see cref="VolleysStarted"/> spreads volleys, so step s opens at t = s/steps and the patter
    /// stays locked to the models' progress rather than to wall-clock time. Internal for tests.
    /// </summary>
    internal static int StepsStarted(float t, float durationSeconds, int modelCount, int toughness)
    {
        int steps = Math.Clamp(
            (int)MathF.Round(Math.Max(0f, durationSeconds) * StepsPerSecond(modelCount, toughness)),
            1, MaxStepsPerMove);
        return Math.Min(steps, (int)(Math.Clamp(t, 0f, 1f) * steps) + 1);
    }

    // Where in the volley slice the shots land: melee at the clash instant (the same moment the
    // hit-stop bites); ranged per the effect style (beams are near-instant, lobbed shells late).
    private static float LandFraction(AttackBeat attack) => attack.IsMelee
        ? HitStopTriggerT
        : WeaponEffectCatalog.Ranged(attack.WeaponEffect).LandFraction;

    private void Advance(PresentationBeat beat, float t)
    {
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
            // DiceRolledBeat never reaches here: every roll is displayed from the dice stack, which ages
            // on its own timeline (#322). A non-held roll still occupies the active slot, but only so the
            // engine's full-duration wait has something animating behind it.
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
            case SpellEffectBeat spell:
                _activeSpell = spell;
                _spellProgress = t;
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
            // A DiceRolledBeat's panel outlives the beat by design (#322) — the stack retires it.
            case BannerBeat:
                _activeBanner = null;
                break;
            case RollOffBeat:
                _activeRollOff = null;
                break;
            case SaveBeat:
                _activeSave = null;
                break;
            case SpellEffectBeat:
                _activeSpell = null;
                break;
            case ModelWoundedBeat wounded:
                _wounded.Remove(wounded.Model.ID); // back to normal color
                break;
        }
    }

    /// <summary>
    /// The dice panels on screen this frame, OLDEST FIRST (#322), each with its 0..1 progress against
    /// its own envelope and its display alpha (fade in/out easing, #245 — the overlay multiplies its
    /// colors by it). The overlay anchors the oldest at the bottom of the table area and stacks the rest
    /// above it. Snapshot taken under the lock.
    /// </summary>
    public IReadOnlyList<(DiceRolledBeat beat, float progress, float alpha)> GetDiceStack()
    {
        lock (_lock)
        {
            var live = new List<(DiceRolledBeat, float, float)>(_diceStack.Count);
            foreach (DiceEntry e in _diceStack) live.Add((e.Beat, e.Progress, e.Alpha));
            return live;
        }
    }

    /// <summary>
    /// #322. Tells the player the pointer is over the dice stack, which freezes every panel's timer until
    /// it leaves — the "wait, why did that happen?" affordance. Set from the render thread each frame
    /// from the bounds the overlay reports; a one-frame lag is invisible.
    /// </summary>
    public void SetDiceStackHovered(bool hovered)
    {
        lock (_lock) _diceHovered = hovered;
    }

    /// <summary>Whether the dice stack is currently frozen under the pointer (#322).</summary>
    public bool IsDiceStackHovered
    {
        get { lock (_lock) return _diceHovered; }
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

    /// <summary>
    /// The attacks (tracers / clash) being shown this frame, oldest first, each with its 0..1 progress.
    /// Usually one; a second appears when the next weapon fires before the previous animation has
    /// finished, which held dice made possible (#322). Snapshot taken under the lock.
    /// </summary>
    public IReadOnlyList<(AttackBeat beat, float progress)> GetActiveAttacks()
    {
        lock (_lock)
        {
            var live = new List<(AttackBeat, float)>(_attacks.Count);
            foreach (AttackState a in _attacks) live.Add((a.Beat, a.Progress));
            return live;
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

    /// <summary>The spell effect being shown this frame, if any, with its 0..1 progress (#274).</summary>
    public bool TryGetActiveSpell(out SpellEffectBeat beat, out float progress)
    {
        lock (_lock)
        {
            beat = _activeSpell!;
            progress = _spellProgress;
            return _activeSpell != null;
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
