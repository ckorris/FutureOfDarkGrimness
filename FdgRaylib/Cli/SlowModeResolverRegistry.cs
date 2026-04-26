using FDG.Data;
using FDG.StageResolution;

namespace FdgRaylib.Cli;

// Wraps a resolver registry and inserts a fixed delay before each resolution so a
// human observer can follow the game state between actions.
public class SlowModeResolverRegistry : IStageResolverRegistry
{
    private readonly IStageResolverRegistry _inner;
    private readonly int _delayMs;

    public SlowModeResolverRegistry(IStageResolverRegistry inner, int delayMs)
    {
        _inner = inner;
        _delayMs = delayMs;
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
        await Task.Delay(_delayMs);
        return await _inner.ResolveRequest<TRequest, TReply>(request);
    }

    public async Task<string> ResolveRequestAsJson(string typeFullName, string requestJson,
        IReadableGameDataStore gameDataStore)
    {
        await Task.Delay(_delayMs);
        return await _inner.ResolveRequestAsJson(typeFullName, requestJson, gameDataStore);
    }
}
