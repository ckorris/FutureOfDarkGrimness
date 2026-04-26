using FDG.MessageBus;
using FDG.Network.Connection;

namespace FdgRaylib.Cli;

/// <summary>
/// In-process message bus for local (non-networked) games. Implements both host and client
/// interfaces so the server and local client can share it without any network layer.
/// </summary>
public class LocalMessageBus : IMessageBusHost, IMessageBusClient
{
    private readonly Dictionary<Type, List<Delegate>> _handlers = new();

    public void RegisterForMessageEvent<T>(Action<T> onMessageReceived)
    {
        if (!_handlers.TryGetValue(typeof(T), out var list))
        {
            list = new List<Delegate>();
            _handlers[typeof(T)] = list;
        }
        list.Add(onMessageReceived);
    }

    public void DeregisterForMessageEvent<T>(Action<T> handler)
    {
        if (_handlers.TryGetValue(typeof(T), out var list))
            list.Remove(handler);
    }

    public Task SendCommandToAllAsync<TMessage>(TMessage message)
    {
        Dispatch(message);
        return Task.CompletedTask;
    }

    public Task SendCommandToHostAsync<TMessage>(TMessage message)
    {
        Dispatch(message);
        return Task.CompletedTask;
    }

    public Task SendCommandToSingleAsync<TMessage>(TMessage message, ConnectionID connectionID)
    {
        Dispatch(message);
        return Task.CompletedTask;
    }

    // Only needed when processing network messages; not applicable locally.
    public ConnectionID GetCurrentMessageConnectionID() => ConnectionID.Host;

    public void Dispose() { }

    private void Dispatch<TMessage>(TMessage message)
    {
        if (message == null) return;
        if (_handlers.TryGetValue(typeof(TMessage), out var list))
        {
            foreach (var handler in list.ToList())
                ((Action<TMessage>)handler)(message);
        }
    }
}
