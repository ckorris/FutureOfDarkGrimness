using FDG;
using FDG.GameModel;
using FDG.Players;
using FDG.Presentation;
using FDG.StageResolution;

namespace FdgRaylib.Cli;

/// <summary>
/// The engine's <c>ComputerPlayerController</c> plus an optional log sink - FdgLab's
/// <c>LabPlayerController</c> brought to the <c>--scenario --all-ai</c> path (#167), where no human
/// game exists to receive the narrative. One slot (slot 0) carries the sink; the host broadcasts the
/// same lines to every slot, so one sink sees the whole game.
/// </summary>
public sealed class ScenarioAiPlayerController : IPlayerController
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

    public ScenarioAiPlayerController(string name, PlayerID id, FDGGame_AsLocal localGame,
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
