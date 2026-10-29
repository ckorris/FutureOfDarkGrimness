namespace FdgRaylib.Analytics;

/// <summary>
/// Fires a callback once per visit when a screen stays open for at least a set time (#412: the Combat
/// Calculator counts as "used" after a minute, so a peek-and-back records nothing). The callback runs on
/// a thread-pool thread at the moment the threshold passes, so it doesn't matter how the visit ends -
/// back to the menu, Quit, or closing the window - and a visit that ends earlier never fires.
/// </summary>
public sealed class DwellTimer
{
    private readonly TimeSpan _threshold;
    private readonly Action _onDwelled;
    private object? _visit; // identity of the current visit; null when not on the screen

    public DwellTimer(TimeSpan threshold, Action onDwelled)
    {
        _threshold = threshold;
        _onDwelled = onDwelled;
    }

    /// <summary>The screen was opened. Starts a new visit (re-entering restarts the clock).</summary>
    public void Entered()
    {
        var visit = new object();
        Volatile.Write(ref _visit, visit);
        _ = Task.Delay(_threshold).ContinueWith(_ =>
        {
            // Fire only if this same visit is still current, and only once for it.
            if (Interlocked.CompareExchange(ref _visit, null, visit) == visit)
            {
                try { _onDwelled(); } catch { /* analytics never takes anything down */ }
            }
        }, TaskScheduler.Default);
    }

    /// <summary>The screen was left. A visit shorter than the threshold records nothing.</summary>
    public void Left() => Volatile.Write(ref _visit, null);
}
