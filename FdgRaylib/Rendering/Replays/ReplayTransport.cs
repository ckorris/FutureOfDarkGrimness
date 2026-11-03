namespace FdgRaylib.Rendering.Replays;

public enum EReplaySpeed
{
    Half,
    Normal,
    Double,
    Quadruple,

    /// <summary>No animations: each step's state appears at once.</summary>
    Instant,
}

/// <summary>
/// The replay viewer's play / pause / step state (#456), kept apart from any store or screen so it can be tested on
/// its own. <see cref="Position"/> is the step the state stands at (0 .. StepCount - 1), or <see cref="StepCount"/>
/// at the end of the recording. The session moves the player and reports where it landed (<see cref="Arrived"/>);
/// autoplay asks <see cref="Tick"/> each frame whether to take the next step.
/// </summary>
public sealed class ReplayTransport
{
    /// <summary>F1 (Chris, 2026-10-30): once a step's animations finish, wait this long - scaled by the speed - before
    /// taking the next.</summary>
    public const float DwellSeconds = 0.75f;

    /// <summary>Instant plays no animations; a short fixed pause keeps the steps legible as they go by. Shorter than
    /// the plan's 0.1 s so a 300-step game still finishes in well under 30 s at 60 frames a second.</summary>
    public const float InstantDwellSeconds = 0.05f;

    private float _idleSeconds;

    public ReplayTransport(int stepCount)
    {
        StepCount = stepCount;
    }

    public int StepCount { get; }

    public int Position { get; private set; }

    public bool IsPlaying { get; private set; }

    public EReplaySpeed Speed { get; set; } = EReplaySpeed.Normal;

    public bool AtEnd => Position >= StepCount;

    /// <summary>False at <see cref="EReplaySpeed.Instant"/>: steps are applied without their animations.</summary>
    public bool PlaysBeats => Speed != EReplaySpeed.Instant;

    /// <summary>How fast animations run.</summary>
    public float TimeScale => Speed switch
    {
        EReplaySpeed.Half => 0.5f,
        EReplaySpeed.Double => 2f,
        EReplaySpeed.Quadruple => 4f,
        _ => 1f,
    };

    /// <summary>The pause after a step's animations before autoplay takes the next.</summary>
    public float Dwell => Speed == EReplaySpeed.Instant ? InstantDwellSeconds : DwellSeconds / TimeScale;

    public void Play()
    {
        if (AtEnd) return;
        IsPlaying = true;
        _idleSeconds = 0f;
    }

    public void Pause()
    {
        IsPlaying = false;
        _idleSeconds = 0f;
    }

    public void TogglePlay()
    {
        if (IsPlaying) Pause();
        else Play();
    }

    /// <summary>
    /// One frame of autoplay: true when the next step should be taken now - playing, not at the end, the last step's
    /// animations finished (<paramref name="animationsIdle"/>) and the dwell since then run out. The dwell only counts
    /// idle frames, so a long animation never eats into it.
    /// </summary>
    public bool Tick(float seconds, bool animationsIdle)
    {
        if (IsPlaying == false || AtEnd)
        {
            return false;
        }

        if (animationsIdle == false)
        {
            _idleSeconds = 0f;
            return false;
        }

        _idleSeconds += seconds;
        if (_idleSeconds < Dwell)
        {
            return false;
        }

        _idleSeconds = 0f;
        return true;
    }

    /// <summary>Where a move landed. Autoplay stops at the end, and at a resume: the state was replaced there, and
    /// the viewer says so before going on.</summary>
    public void Arrived(int position, bool crossedResume)
    {
        Position = Math.Clamp(position, 0, StepCount);
        _idleSeconds = 0f;
        if (AtEnd || crossedResume)
        {
            IsPlaying = false;
        }
    }
}
