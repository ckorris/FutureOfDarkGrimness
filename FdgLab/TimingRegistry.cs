using System.Collections.Concurrent;
using System.Diagnostics;
using FDG.Data;
using FDG.StageResolution;

namespace FdgLab;

/// <summary>
/// Wraps a resolver registry to time every decision, feeding <see cref="DecisionStats"/>. The sample
/// list is per-game and appended from whatever thread the engine resolves on, hence the lock.
/// <para>
/// Optionally also tallies time BY REQUEST TYPE (#191 campaign step 3/5): "where does an
/// activation's policy time actually go" decides whether Phase B needs a cheap in-simulation
/// policy at all - a cost concentrated in movement planning disappears by construction when the
/// search prescribes a macro-action, with no fidelity trade to make.
/// </para>
/// </summary>
public sealed class TimingRegistry : IStageResolverRegistry
{
    private readonly IStageResolverRegistry _inner;
    private readonly List<double> _samplesMs;
    private readonly object _lock;
    private readonly ConcurrentDictionary<string, TypeTally>? _byType;

    public TimingRegistry(IStageResolverRegistry inner, List<double> samplesMs, object sampleLock,
        ConcurrentDictionary<string, TypeTally>? byType = null)
    {
        _inner = inner;
        _samplesMs = samplesMs;
        _lock = sampleLock;
        _byType = byType;
    }

    /// <summary>Per-request-type tally. Mutable by design - updated under the dictionary's own lock.</summary>
    public sealed class TypeTally
    {
        private long _count;
        private double _totalMs;
        public long Count => Interlocked.Read(ref _count);
        public double TotalMs { get { lock (this) return _totalMs; } }
        public void Add(double ms)
        {
            Interlocked.Increment(ref _count);
            lock (this) _totalMs += ms;
        }
    }

    private void Record(string typeName, double ms)
    {
        lock (_lock) _samplesMs.Add(ms);
        _byType?.GetOrAdd(Short(typeName), _ => new TypeTally()).Add(ms);
    }

    // "FDG...DefineMovementPathRequest" -> "DefineMovementPathRequest", and a GENERIC request
    // ("FDG...SelectionRequest`1[[FDG...UnitData, Assembly, Version=..., Culture=...]]") ->
    // "SelectionRequest<UnitData>". Naive last-dot splitting mangles generics into the tail of an
    // assembly-qualified argument name, which is exactly how an unidentified 10% line appeared in
    // the first breakdown.
    private static string Short(string fullName)
    {
        int tick = fullName.IndexOf('`');
        if (tick < 0) return AfterLastDot(fullName);

        string outer = AfterLastDot(fullName[..tick]);
        int open = fullName.IndexOf("[[", StringComparison.Ordinal);
        if (open < 0) return outer;

        // Each type argument is "Namespace.Type, Assembly, Version=..."; keep the type name only.
        string args = fullName[(open + 2)..].TrimEnd(']');
        var names = args.Split("],[", StringSplitOptions.RemoveEmptyEntries)
            .Select(a => AfterLastDot(a.Split(',')[0].Trim()));
        return $"{outer}<{string.Join(", ", names)}>";
    }

    private static string AfterLastDot(string name)
    {
        int dot = name.LastIndexOf('.');
        return dot >= 0 && dot + 1 < name.Length ? name[(dot + 1)..] : name;
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
            Record(typeof(TRequest).FullName ?? typeof(TRequest).Name, sw.Elapsed.TotalMilliseconds);
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
            Record(typeFullName, sw.Elapsed.TotalMilliseconds);
        }
    }
}
