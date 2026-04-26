namespace FdgRaylib.Rendering;

public class GameLog
{
    private readonly List<string> _messages = new();
    private readonly object _lock = new();

    public void Add(string message)
    {
        lock (_lock) _messages.Add(message);
    }

    public List<string> Snapshot()
    {
        lock (_lock) return new List<string>(_messages);
    }
}
