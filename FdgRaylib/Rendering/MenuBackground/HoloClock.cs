namespace FdgRaylib.Rendering.MenuBackground;

/// <summary>
/// The war table's own clock (#448 slice 5). It only runs while the table is animating and no dialog is
/// open over it, so pausing freezes the picture where it was rather than skipping ahead when the dialog
/// closes; and it hands out "battle time" (see <see cref="HoloDirector"/>), starting a new battle each
/// time one runs its full length.
/// </summary>
internal sealed class HoloClock
{
    /// <summary>Longest step one frame may take: a hitch (a file dialog, a slow first frame) must not
    /// fast-forward the battle.</summary>
    public const float MaxStep = 0.1f;

    private float? _battleStart;

    /// <summary>Seconds the clock has run in total - the camera drift reads this.</summary>
    public float Wall { get; private set; }

    /// <summary>Seconds into the current battle.</summary>
    public float Battle => _battleStart is { } start ? Wall - start : 0f;

    /// <summary>
    /// Moves the clock on by one frame of <paramref name="dt"/> seconds unless <paramref name="paused"/>.
    /// Returns true when that wrapped into a NEW battle - the caller deals the next pairing.
    /// </summary>
    public bool Tick(float dt, bool paused)
    {
        if (!paused) Wall += Math.Clamp(dt, 0f, MaxStep);
        _battleStart ??= Wall;
        if (Wall - _battleStart.Value < HoloDirector.BattleLength) return false;
        _battleStart = Wall;
        return true;
    }

    /// <summary>Starts a fresh battle on the next tick (a new pairing was dealt from outside).</summary>
    public void RestartBattle() => _battleStart = null;
}
