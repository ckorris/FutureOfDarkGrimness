using FDG;
using FDG.TextInterface;

namespace FdgRaylib.Rendering;

// Forwards engine log messages to both the console and the in-window ImGui log.
public class GuiLogMessageUI : ILogMessageUI
{
    private readonly GameLog _log;
    private string? _lastMessage;
    private string? _lastDebugMessage;

    public GuiLogMessageUI(GameLog log)
    {
        _log = log;
    }

    public void DisplayLogMessage(string message, TextColor color)
    {
        if (message == _lastMessage) return;
        _lastMessage = message;
        Console.WriteLine($"[LOG] {message}");
        _log.Add(message, color);
    }

    // Debug-category line: tagged so the console shows it only when the Debug toggle is on. Deduped
    // separately from normal lines so an identical debug line right after a normal one still lands.
    public void DisplayDebugMessage(string message, TextColor color)
    {
        if (message == _lastDebugMessage) return;
        _lastDebugMessage = message;
        Console.WriteLine($"[DEBUG] {message}");
        _log.Add(message, color, isDebug: true);
    }
}
