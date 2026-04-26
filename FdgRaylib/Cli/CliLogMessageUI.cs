using FDG.TextInterface;

namespace FdgRaylib.Cli;

public class CliLogMessageUI : ILogMessageUI
{
    // The server broadcasts each log message to every local player slot. Since all local
    // players share this single UI, deduplicate by skipping consecutive identical messages.
    private string? _lastMessage;

    public void DisplayLogMessage(string message)
    {
        if (message == _lastMessage) return;
        _lastMessage = message;
        Console.WriteLine($"[LOG] {message}");
    }
}
