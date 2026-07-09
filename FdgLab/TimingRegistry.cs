using System.Diagnostics;
using FDG.Data;
using FDG.StageResolution;

namespace FdgLab;

/// <summary>
/// Wraps a resolver registry to time every decision, feeding <see cref="DecisionStats"/>. The sample
/// list is per-game and appended from whatever thread the engine resolves on, hence the lock.
/// </summary>
public sealed class TimingRegistry : IStageResolverRegistry
{
    private readonly IStageResolverRegistry _inner;
    private readonly List<double> _samplesMs;
    private readonly object _lock;

    public TimingRegistry(IStageResolverRegistry inner, List<double> samplesMs, object sampleLock)
    {
        _inner = inner;
        _samplesMs = samplesMs;
        _lock = sampleLock;
    }

    public IStageResolverRegistry RegisterResolver<TRequest, TReply>(IStageResolver<TRequest, TReply> resolver)
        where TRequest : IStageTaskRequest<TReply>
    {
        _inner.RegisterResolver(resolver);
        return this;
    }

    public async Task<TReply> ResolveRequest<TRequest, TReply>(TRequest request)
        where TRequest : IStageTaskRequest<TReply>
    {
        var sw = Stopwatch.StartNew();
        try
        {
            return await _inner.ResolveRequest<TRequest, TReply>(request);
        }
        finally
        {
            sw.Stop();
            lock (_lock) _samplesMs.Add(sw.Elapsed.TotalMilliseconds);
        }
    }

    public async Task<string> ResolveRequestAsJson(string typeFullName, string requestJson, IReadableGameDataStore gameDataStore)
    {
        var sw = Stopwatch.StartNew();
        try
        {
            return await _inner.ResolveRequestAsJson(typeFullName, requestJson, gameDataStore);
        }
        finally
        {
            sw.Stop();
            lock (_lock) _samplesMs.Add(sw.Elapsed.TotalMilliseconds);
        }
    }
}
