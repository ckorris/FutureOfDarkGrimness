using FDG;
using FDG.TextInterface;

namespace FdgRaylib.Rendering;

// Forwards engine log messages to both the console and the in-window ImGui log.
public class GuiLogMessageUI : ILogMessageUI
{
    private readonly GameLog _log;
    private string? _lastMessage;

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
}
