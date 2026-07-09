using FDG.MessageBus;
using FDG.Network.Connection;

namespace FdgLab;

/// <summary>
/// In-process message bus for lab games — one per game, never shared. Functional twin of the app's
/// <c>FdgRaylib.Cli.LocalMessageBus</c> and the engine tests' <c>InProcessBus</c>; copied rather than
/// referenced because FdgLab deliberately depends on the engine only (no FdgRaylib), and the tests'
/// copy is internal to the engine assembly.
/// </summary>
public sealed class LabMessageBus : IMessageBusHost, IMessageBusClient
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = new();

#pragma warning disable CS0067 // No real connections in-process; required by IMessageBusHost.
    public event Action<ConnectionID>? OnClientDisconnected;
#pragma warning restore CS0067

    public void RegisterForMessageEvent<T>(Action<T> handler) => Add(typeof(T), handler);
    public void DeregisterForMessageEvent<T>(Action<T> handler) => Remove(typeof(T), handler);
    public void RegisterForConnectionMessageEvent<T>(Action<T, ConnectionID> handler) => Add(typeof(T), handler);
    public void DeregisterForConnectionMessageEvent<T>(Action<T, ConnectionID> handler) => Remove(typeof(T), handler);

    public Task SendCommandToAllAsync<TMessage>(TMessage message) { Dispatch(message); return Task.CompletedTask; }
    public Task SendCommandToHostAsync<TMessage>(TMessage message) { Dispatch(message); return Task.CompletedTask; }
    public Task SendCommandToSingleAsync<TMessage>(TMessage message, ConnectionID connectionID) { Dispatch(message); return Task.CompletedTask; }
    public Task SendCommandToLocalAsync<TMessage>(TMessage message) { Dispatch(message); return Task.CompletedTask; }
    public void Dispose() { }

    private void Add(Type type, Delegate handler)
    {
        if (!_handlers.TryGetValue(type, out var list)) _handlers[type] = list = new List<Delegate>();
        list.Add(handler);
    }

    private void Remove(Type type, Delegate handler)
    {
        if (_handlers.TryGetValue(type, out var list)) list.Remove(handler);
    }

    private void Dispatch<TMessage>(TMessage message)
    {
        if (message == null || !_handlers.TryGetValue(typeof(TMessage), out var list)) return;
        foreach (Delegate handler in list.ToList())
        {
            if (handler is Action<TMessage> plain) plain(message);
            else if (handler is Action<TMessage, ConnectionID> withConnection) withConnection(message, ConnectionID.Host);
        }
    }
}
