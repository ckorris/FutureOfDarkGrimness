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

    // #327 dice stack. A panel OUTLIVES its beat: the engine paces a roll in full (the gap between rolls
    // is the rhythm of the exchange — see DiceRolledBeat.Held for the two attempts at removing it), and
    // then the panel stays up for seconds more while play carries on. So several panels are legible at
    // once, and a new roll STACKS on top of the last instead of evicting it — the single-slot eviction is
    // what made a two-threshold volley cut its own first roll short. Oldest first; the overlay anchors
    // the oldest at the bottom and grows upward.
    // Roll-offs share the stack (#327). They dock to the same caption zone and are the same kind of
    // statement — "here is a roll and what it meant" — so a roll-off drawn over a still-lingering dice
    // panel was the exact overlap this item exists to remove. The objective-count roll and the first-turn
    // roll-off at game start are the case that showed it.
    private readonly List<RollPanel> _rollStack = new();
    private const int MaxRollStack = 3;
    // How long a panel stays up after it has finished settling. Chips are extra reading, so a panel
    // carrying them lingers longer — the same principle as the engine's stretched beat duration (#245).
    private const float RollLingerSeconds      = 3.0f;
    private const float RollLingerPerInfoBlock = 0.4f;
    // Display alpha for a panel (#245): eased in as it appears and out over the tail of its lifetime,
    // so panels fade instead of popping.
    private const float RollFadeInSeconds  = 0.12f;
    private const float RollFadeOutSeconds = 0.35f;
    // Hovering the stack freezes every panel's timer (and the overlay un-dims the older ones), so a
    // player who wants to re-read a roll can, without slowing down one who doesn't. Set from the render
    // thread once the overlay knows the stack's own screen bounds.
    private bool _rollHovered;

    /// <summary>
    /// One panel on the roll stack — a <see cref="DiceRolledBeat"/> or a <see cref="RollOffBeat"/>. Its
    /// lifetime is FIXED at construction rather than reset by later beat activity (which is what the old
    /// single-slot linger did): with several panels up, a lifetime that depends on what happens to follow
    /// makes the stack's layout unpredictable.
    /// </summary>
    private sealed class RollPanel
    {
        public readonly PresentationBeat Beat;
        public readonly float Lifetime;
        public float Elapsed;

        public RollPanel(PresentationBeat beat)
        {
            Beat = beat;
            // A normal roll owns the active slot for its whole duration; a held one settles over its
            // lead-in. Either way the linger is what the panel gets ON TOP of the paced part — that
            // overhang is what makes panels overlap and therefore stack.
            float paced = (float)(beat.Held ? beat.HoldLeadIn : beat.NominalDuration).TotalSeconds;
            int infoBlocks = beat is DiceRolledBeat dice ? dice.InfoBlocks : 0;
            Lifetime = paced + RollLingerSeconds + RollLingerPerInfoBlock * infoBlocks;
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
            Elapsed / RollFadeInSeconds,
            Math.Clamp((Lifetime - Elapsed) / RollFadeOutSeconds, 0f, 1f));
    }

    // #275 banner tiers, all three on one display track since #327. Notice/Toast are Held, so they
    // transfer here the frame they are dequeued and the engine keeps moving underneath them (it paces
    // only their lead-in: 300ms for a Notice, nothing at all for a Toast). A Headline ALSO lands here,
    // but unlike the others it additionally holds the active slot for its full duration - it is the one
    // tier that stops play, and that is what makes it a Headline.
    //
    // EVERY tier stacks now. A banner used to be replaced by the next one in its band (a Notice wiped
    // out the Notice before it; a Headline replaced the last), which is the same "evicted mid-read"
    // problem the dice stack was built to fix, so the tiers got the same treatment: the older statement
    // is pushed aside and dimmed rather than overwritten. Per-tier caps, oldest dropped first.
    // Deliberately excluded from IsAnimating - a message that does not stop the engine must not stop an
    // interactive prompt either.
    private readonly List<BannerPanel> _banners = new();

    private static int MaxBanners(EBannerTier tier) => tier switch
    {
        EBannerTier.Toast => 5,      // small, out of the way, and they arrive in bursts
        EBannerTier.Notice => 3,     // mid-screen; the overlay trims further when space is tight
        _ => 2,                      // a Headline is huge - two is already a lot of screen
    };

    // A Headline blocks for its whole duration, so consecutive ones could never overlap without an
    // overhang - the same linger that lets dice panels stack. The lower tiers are held and already
    // outlive their pacing, so they keep the durations #275 tuned.
    private const float HeadlineLingerSeconds = 1.5f;
    private const float BannerFadeInSeconds   = 0.12f;
    private const float BannerFadeOutSeconds  = 0.4f;

    private sealed class BannerPanel
    {
        public readonly BannerBeat Beat;
        public readonly float Lifetime;
        public float Elapsed;

        public BannerPanel(BannerBeat beat)
        {
            Beat = beat;
            float duration = (float)beat.NominalDuration.TotalSeconds;
            Lifetime = beat.Tier == EBannerTier.Headline
                ? duration + HeadlineLingerSeconds
                : duration;
        }

        public float Alpha => Math.Min(
            Elapsed / BannerFadeInSeconds,
            Math.Clamp((Lifetime - Elapsed) / BannerFadeOutSeconds, 0f, 1f));
    }

    // World-space attacks (tracers / clash) — each runs on its OWN timeline, concurrent with the active
    // beat (#238): AttackBeat is a held beat with zero lead-in, so it transfers here the frame it is
    // dequeued and animates over its full NominalDuration WHILE the to-hit dice that always follow
    // it tumble.
    //
    // #327 made this a LIST, and it stays one. The to-hit roll's 1800ms envelope outlasts the longest
    // attack animation (VolleyMax, 1600ms), so in practice a second attack does not arrive before the
    // first has finished — but that is a coincidence of two unrelated constants, and the brief spell when
    // #327 held rolls at ~600ms turned it into a real truncation (a whiffed attack has no saves or wounds
    // behind it, so the next weapon fires almost immediately). Overlapping attacks play side by side.
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
                // #327: every roll - dice or roll-off - joins the stack for display, which is what lets
                // its panel outlive its beat. A normal (non-held) roll ALSO takes the active slot below,
                // so the front-end animates for exactly as long as the engine waits. A held roll — the
                // opt-in nothing uses today — stops here, and the next beat becomes active immediately.
                if (next is DiceRolledBeat or RollOffBeat)
                {
                    PushRoll(next);
                    if (next.Held) continue;
                }
                // #232: an overlapped casualty beat never holds the active slot - it transfers to the
                // cascade track and animates concurrently while later beats play.
                if (next.Held && next is ModelDiedBeat or ModelWoundedBeat)
                {
                    _cascading.Add(new CascadeState(next));
                    continue;
                }
                // #275/#327: every banner joins the display track, which is what lets it outlive its
                // beat and stack. A Notice or Toast stops there - it announces over the top of play
                // instead of interrupting it. A Headline falls through to the active slot as well,
                // because it is the tier that stops play.
                if (next is BannerBeat banner)
                {
                    PushBanner(banner);
                    if (banner.Held) continue;
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

            // #327: the roll stack, likewise independent of the active slot - each panel lives its own
            // fixed lifetime and retires when it runs out. Aged AFTER the dequeue above, like every other
            // concurrent track, so a panel that appeared this frame gets this frame's time too.
            // Hovering the stack freezes all of them at once.
            if (!_rollHovered)
            {
                for (int i = _rollStack.Count - 1; i >= 0; i--)
                {
                    RollPanel panel = _rollStack[i];
                    panel.Elapsed += dtSeconds;
                    if (panel.Elapsed >= panel.Lifetime) _rollStack.RemoveAt(i);
                }
            }

            // #275: the banner track, likewise independent of the active slot. Pure display - nothing
            // here touches model state, so it only ages and retires.
            for (int i = _banners.Count - 1; i >= 0; i--)
            {
                BannerPanel b = _banners[i];
                b.Elapsed += dtSeconds;
                if (b.Elapsed >= b.Lifetime) _banners.RemoveAt(i);
            }

            // The concurrent attack track advances every frame, independent of the active slot. Each
            // attack keeps its own cue counters, so two overlapping ones never steal each other's
            // volley/impact cues (#327).
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
    /// #327. Called under the lock. A new roll goes on TOP of the stack; the oldest drops out once the
    /// stack is full, so the newest panel is never the one squeezed off screen.
    /// </summary>
    private void PushRoll(PresentationBeat beat)
    {
        _rollStack.Add(new RollPanel(beat));
        while (_rollStack.Count > MaxRollStack) _rollStack.RemoveAt(0);
    }

    /// <summary>
    /// #327. Called under the lock. A banner goes on TOP of its tier's stack and the OLDEST of that tier
    /// drops out once it is full — the newest statement is always the one that survives. Tiers are capped
    /// separately because they occupy different amounts of screen: five toasts are a ticker, five
    /// headlines would be the whole table.
    ///
    /// <para>A Notice used to supersede the Notice before it outright, on the grounds that two in one
    /// band would overlap into mush. They stack instead now — the overlay offsets them and dims the older
    /// one, so nothing is overwritten mid-read.</para>
    /// </summary>
    private void PushBanner(BannerBeat banner)
    {
        _banners.Add(new BannerPanel(banner));

        int cap = MaxBanners(banner.Tier);
        for (int i = 0; i < _banners.Count && CountOfTier(banner.Tier) > cap; )
        {
            if (_banners[i].Beat.Tier == banner.Tier) _banners.RemoveAt(i);
            else i++;
        }
    }

    private int CountOfTier(EBannerTier tier)
    {
        int n = 0;
        foreach (BannerPanel b in _banners) if (b.Beat.Tier == tier) n++;
        return n;
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
            // on its own timeline (#327). A non-held roll still occupies the active slot, but only so the
            // engine's full-duration wait has something animating behind it.
            // BannerBeat never reaches here: every banner is displayed from the banner track, which ages
            // on its own timeline (#327). A Headline still occupies the active slot, but only so the
            // engine's full-duration wait has something animating behind it.
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
            // A DiceRolledBeat's panel outlives the beat by design (#327) — the stack retires it.
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
    /// The roll panels on screen this frame, OLDEST FIRST (#327) — dice rolls and roll-offs share one
    /// stack — each with its 0..1 progress against its own envelope and its display alpha (fade in/out
    /// easing, #245 — the overlay multiplies its colors by it). The overlay anchors the oldest at the
    /// bottom of the table area and stacks the rest above it. Snapshot taken under the lock.
    /// </summary>
    public IReadOnlyList<(PresentationBeat beat, float progress, float alpha)> GetRollStack()
    {
        lock (_lock)
        {
            var live = new List<(PresentationBeat, float, float)>(_rollStack.Count);
            foreach (RollPanel p in _rollStack) live.Add((p.Beat, p.Progress, p.Alpha));
            return live;
        }
    }

    /// <summary>
    /// #327. Tells the player the pointer is over the roll stack, which freezes every panel's timer until
    /// it leaves — the "wait, why did that happen?" affordance. Set from the render thread each frame
    /// from the bounds the overlay reports; a one-frame lag is invisible.
    /// </summary>
    public void SetRollStackHovered(bool hovered)
    {
        lock (_lock) _rollHovered = hovered;
    }

    /// <summary>Whether the roll stack is currently frozen under the pointer (#327).</summary>
    public bool IsRollStackHovered
    {
        get { lock (_lock) return _rollHovered; }
    }

    /// <summary>
    /// Every banner on screen this frame, ALL TIERS, oldest first (#275, #327), each with its display
    /// alpha (eased in as it appears, out over the tail of its lifetime). The overlay lays each tier out
    /// in its own band, stacking within it. Snapshot taken under the lock.
    /// </summary>
    public IReadOnlyList<(BannerBeat beat, float alpha)> GetBanners()
    {
        lock (_lock)
        {
            var live = new List<(BannerBeat, float)>(_banners.Count);
            foreach (BannerPanel b in _banners) live.Add((b.Beat, b.Alpha));
            return live;
        }
    }

    /// <summary>
    /// The attacks (tracers / clash) being shown this frame, oldest first, each with its 0..1 progress.
    /// Usually one; a second appears when the next weapon fires before the previous animation has
    /// finished, which held dice made possible (#327). Snapshot taken under the lock.
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
