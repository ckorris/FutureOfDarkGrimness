using System.Threading;
using FDG;

namespace FdgRaylib.Rendering;

/// <summary>
/// One log line: its text, colour, and a globally-monotonic sequence number. The sequence is shared
/// across ALL <see cref="GameLog"/> instances (engine log + chat), so entries from different logs can be
/// merged into one column in true arrival order (the console's combined Log+Chat view).
/// </summary>
public readonly record struct LogEntry(string Message, TextColor Color, long Sequence, bool IsDebug = false);

public class GameLog
{
    private readonly List<LogEntry> _messages = new();
    private readonly object _lock = new();

    // Shared across every GameLog so a merged view of two logs sorts correctly by arrival.
    private static long _globalSequence;

    public void Add(string message, TextColor color, bool isDebug = false)
    {
        // The sequence assignment and the append must be atomic together, or two concurrent callers
        // (engine thread + main-thread crash handler both call Add) can interleave so a lower sequence
        // number lands in this instance's list after a higher one, breaking the merge's sort assumption.
        lock (_lock) _messages.Add(new LogEntry(message, color, Interlocked.Increment(ref _globalSequence), isDebug));
    }

    public List<LogEntry> Snapshot()
    {
        lock (_lock) return new List<LogEntry>(_messages);
    }

    /// <summary>Current message count, cheap (no copy) -- for unread-indicator bookkeeping.</summary>
    public int Count
    {
        get { lock (_lock) return _messages.Count; }
    }
}
