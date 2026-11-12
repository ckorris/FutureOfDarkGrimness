using FDG;
using FDG.Players;
using FDG.TextInterface;

namespace FdgRaylib.Rendering;

// In-game chat (#077, reworked in #105). Received chat lands in its OWN store (ChatLog), NOT the engine
// GameLog, so it reads as its own thing in the bottom console's Chat tab instead of being buried between
// stage-change log lines. Each line is tagged with the sender's player-palette colour (looked up by name,
// supplied at construction from the lobby roster). Submitting from the console's input routes through
// OnMessageSentByPlayer -> the engine relay, which forwards to everyone and echoes it back here.
public class GuiPlayerMessageUI : IPlayerMessageUI
{
    /// <summary>Chat scrollback, shown in the console's Chat tab (separate from the engine log).</summary>
    public GameLog ChatLog { get; } = new GameLog();

    private readonly Func<string, TextColor> _senderColor;
    private static readonly TextColor DefaultChatColor = new TextColor(150, 220, 255, 255);

    public event Action<string, EChatMessageType>? OnMessageSentByPlayer;

    /// <param name="senderColor">Maps a sender's display name to its palette colour; null = flat blue.</param>
    public GuiPlayerMessageUI(Func<string, TextColor>? senderColor = null)
    {
        _senderColor = senderColor ?? (_ => DefaultChatColor);
    }

    public void DisplayPlayerMessage(string sendingPlayerName, EChatMessageType messageType, string message)
    {
        TextColor color = _senderColor(sendingPlayerName);
        string channelTag = messageType == EChatMessageType.Team ? "[Team] " : "";
        ChatLog.Add($"{channelTag}[{sendingPlayerName}] {message}", color);
    }

    // Called from the console's chat input when the local player submits a line, on the chosen channel.
    public void Submit(string message, EChatMessageType type = EChatMessageType.Global)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        OnMessageSentByPlayer?.Invoke(message, type);
    }
}
