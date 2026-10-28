using System.Text.Json;

namespace FdgRaylib.Analytics;

/// <summary>
/// Measures how long a play session lasted, including sessions that never got to say goodbye
/// (#406). A small marker file in the config directory records when this launch started and when
/// it was last alive; the next launch reads it and reports the previous session on its
/// <c>app_launch</c>. That catches crashes and task-kills, which a send-on-quit never would.
///
/// <para>"Unclean" means the process ended without reaching the normal exit path: a crash, a
/// task-kill, a power cut. It is deliberately not called "crashed". The file holds only timestamps
/// and the app version - no identifier - and it is written whether or not analytics is enabled,
/// since it never leaves the machine on its own.</para>
/// </summary>
public sealed class SessionTracker
{
    public const string FileName = "last-session.json";

    /// <summary>What the previous launch's marker said.</summary>
    public readonly record struct PreviousSession(int Seconds, bool Clean);

    private sealed class Marker
    {
        public DateTime StartedUtc { get; set; }
        public DateTime LastAliveUtc { get; set; }
        public DateTime? ExitUtc { get; set; }
        public bool CleanExit { get; set; }
        public string Version { get; set; } = "";
    }

    private readonly string _path;
    private readonly object _lock = new();
    private Marker? _current;

    public SessionTracker(string path) => _path = path;

    /// <summary>Reads the previous launch's marker (null if there was none or it was unreadable) and
    /// starts this session's.</summary>
    public PreviousSession? Begin(DateTime nowUtc, string version)
    {
        PreviousSession? previous = null;
        try
        {
            if (File.Exists(_path) && JsonSerializer.Deserialize<Marker>(File.ReadAllText(_path)) is { } old)
            {
                DateTime end = old.CleanExit && old.ExitUtc is { } exit ? exit : old.LastAliveUtc;
                double seconds = (end - old.StartedUtc).TotalSeconds;
                // A marker from the future or a negative span is a clock change; report it as unknown.
                if (seconds >= 0 && seconds <= 30 * 86_400)
                    previous = new PreviousSession((int)seconds, old.CleanExit);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }

        lock (_lock)
        {
            _current = new Marker { StartedUtc = nowUtc, LastAliveUtc = nowUtc, Version = version };
            Write();
        }
        return previous;
    }

    /// <summary>Records that this session is still alive (called from the analytics timer).</summary>
    public void Heartbeat(DateTime nowUtc)
    {
        lock (_lock)
        {
            if (_current == null || _current.CleanExit) return;
            _current.LastAliveUtc = nowUtc;
            Write();
        }
    }

    /// <summary>Marks this session as having reached the normal exit path.</summary>
    public void End(DateTime nowUtc)
    {
        lock (_lock)
        {
            if (_current == null) return;
            _current.LastAliveUtc = nowUtc;
            _current.ExitUtc = nowUtc;
            _current.CleanExit = true;
            Write();
        }
    }

    private void Write()
    {
        try
        {
            string? dir = Path.GetDirectoryName(_path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_path, JsonSerializer.Serialize(_current));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
}
