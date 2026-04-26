using FDG.Players;
using FDG.TextInterface;

namespace FdgRaylib.Cli;

public class CliPlayerMessageUI : IPlayerMessageUI
{
    public event Action<string, EChatMessageType>? OnMessageSentByPlayer;

    public void DisplayPlayerMessage(string sendingPlayerName, EChatMessageType messageType, string message)
    {
        Console.WriteLine($"[{sendingPlayerName}] {message}");
    }

    // Allows the CLI itself to send chat messages into the game (not currently used).
    public void SendMessage(string message, EChatMessageType type = EChatMessageType.Global)
    {
        OnMessageSentByPlayer?.Invoke(message, type);
    }
}
