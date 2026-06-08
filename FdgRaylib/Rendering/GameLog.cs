using FDG;

namespace FdgRaylib.Rendering;

/// <summary>One log line plus the color it should be shown in.</summary>
public readonly record struct LogEntry(string Message, TextColor Color);

public class GameLog
{
    private readonly List<LogEntry> _messages = new();
    private readonly object _lock = new();

    public void Add(string message, TextColor color)
    {
        lock (_lock) _messages.Add(new LogEntry(message, color));
    }

    public List<LogEntry> Snapshot()
    {
        lock (_lock) return new List<LogEntry>(_messages);
    }
}
