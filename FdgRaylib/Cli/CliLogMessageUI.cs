using FDG.TextInterface;

namespace FdgRaylib.Cli;

public class CliLogMessageUI : ILogMessageUI
{
    public void DisplayLogMessage(string message)
    {
        Console.WriteLine($"[LOG] {message}");
    }
}
