using FDG;
using FDG.GameModel;
using FDG.Players;
using FDG.Presentation;
using FDG.StageResolution;

namespace FdgLab;

/// <summary>
/// The engine's <see cref="ComputerPlayerController"/> with one addition: an optional log sink, so the
/// lab can capture the game narrative. Needed because benchmark numbers are never trusted without
/// readable game logs (plan G2), and because diffing two same-seed transcripts is how divergence gets
/// localized when the determinism contract breaks.
/// </summary>
public sealed class LabPlayerController : IPlayerController
{
    private readonly Action<string>? _logSink;

    public string Name { get; }
    public PlayerID ID { get; }
    public bool IsReady => true;
    public IPresentationSink? PresentationSink => null;

#pragma warning disable CS0067 // AI players never change readiness or chat.
    public event Action<bool>? OnReadyStateChanged;
    public event Action<PlayerID, EChatMessageType, string>? OnMessageSentByPlayer;
#pragma warning restore CS0067

    public LabPlayerController(string name, PlayerID id, FDGGame_AsLocal localGame,
        IStageResolverRegistry registry, Action<string>? logSink = null)
    {
        Name = name;
        ID = id;
        _logSink = logSink;
        localGame.AddLocalPlayerID(id);
        localGame.AssignInterfaces(null, null, registry, null, null);
    }

    public Task WaitUntilReadyAsync() => Task.CompletedTask;

    public void SendLogMessage(string logMessage, TextColor color) => _logSink?.Invoke(logMessage);

    public void SendPlayerMessage(string sendingPlayerName, EChatMessageType messageType, string message) { }
}
