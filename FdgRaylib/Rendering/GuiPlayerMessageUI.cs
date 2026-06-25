using FDG;
using FDG.Players;
using FDG.TextInterface;

namespace FdgRaylib.Rendering;

// In-game chat (#077 follow-up). Received player chat lands in the same GameLog shown on the side of the
// screen, interleaved with engine log lines; the bottom-of-window input box (drawn by RaylibRenderer)
// calls Submit, which raises OnMessageSentByPlayer — the engine relay forwards it to everyone and echoes
// it back here for display.
public class GuiPlayerMessageUI : IPlayerMessageUI
{
    private readonly GameLog _log;

    // Light blue so chat stands out from engine log lines.
    private static readonly TextColor ChatColor = new TextColor(150, 220, 255, 255);

    public event Action<string, EChatMessageType>? OnMessageSentByPlayer;

    public GuiPlayerMessageUI(GameLog log)
    {
        _log = log;
    }

    public void DisplayPlayerMessage(string sendingPlayerName, EChatMessageType messageType, string message)
    {
        _log.Add($"[{sendingPlayerName}] {message}", ChatColor);
    }

    // Called from the renderer's chat input box when the local player submits a line.
    public void Submit(string message, EChatMessageType type = EChatMessageType.Global)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        OnMessageSentByPlayer?.Invoke(message, type);
    }
}
