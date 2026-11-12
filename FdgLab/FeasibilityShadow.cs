using FDG;
using FDG.Ai.Tactician;
using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Utilities;

namespace FdgLab;

/// <summary>
/// The #191 A3 feasibility instrument: wraps an AI registry so every REAL movement decision of a
/// benchmark game also runs <see cref="MacroActionGenerator"/> in the shadows and tallies whether
/// at least one valid non-Hold candidate existed. The solo bot still makes the actual decision, so
/// games are unchanged (and stay hash-identical); the tally answers the plan's A3 gate metric:
/// "on positions sampled from benchmark games, >= 95% of activations yield at least one valid
/// non-Hold candidate".
/// </summary>
public sealed class FeasibilityShadow
{
    private long _activations;
    private long _withNonHoldCandidate;
    private long _generatorFaults;

    public long Activations => Interlocked.Read(ref _activations);
    public long WithNonHoldCandidate => Interlocked.Read(ref _withNonHoldCandidate);
    public long GeneratorFaults => Interlocked.Read(ref _generatorFaults);

    public double Fraction => Activations == 0 ? 0 : (double)WithNonHoldCandidate / Activations;

    public IStageResolverRegistry Wrap(IStageResolverRegistry inner, ITableState tableState) =>
        new ShadowRegistry(inner, tableState, this);

    private void Observe(ITableState tableState, DefineMovementPathRequest request)
    {
        Interlocked.Increment(ref _activations);
        try
        {
            var evaluator = new RuleEvaluator(new ProbabilisticDiceRoller());
            List<MacroAction> actions = MacroActionGenerator.Enumerate(evaluator, tableState,
                request.UnitDataBinding);
            if (actions.Any(a => a.Intent != EMacroIntent.Hold && a.Feasibility != EFeasibility.Blocked))
                Interlocked.Increment(ref _withNonHoldCandidate);
        }
        catch
        {
            // A generator crash must never break the host game - it IS the finding. Count it;
            // the metric treats it as a miss.
            Interlocked.Increment(ref _generatorFaults);
        }
    }

    private sealed class ShadowRegistry : IStageResolverRegistry
    {
        private readonly IStageResolverRegistry _inner;
        private readonly ITableState _tableState;
        private readonly FeasibilityShadow _shadow;

        public ShadowRegistry(IStageResolverRegistry inner, ITableState tableState, FeasibilityShadow shadow)
        {
            _inner = inner;
            _tableState = tableState;
            _shadow = shadow;
        }

        public IStageResolverRegistry RegisterResolver<TRequest, TReply>(IStageResolver<TRequest, TReply> resolver)
            where TRequest : IStageTaskRequest<TReply>
        {
            _inner.RegisterResolver(resolver);
            return this;
        }

        public Task<TReply> ResolveRequest<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            if (request is DefineMovementPathRequest movement)
                _shadow.Observe(_tableState, movement);
            return _inner.ResolveRequest<TRequest, TReply>(request);
        }

        // Local games deliver requests through the JSON path (the registry's typed delegate calls
        // its own ResolveRequest internally), so the interception must happen HERE: deserialize a
        // shadow copy of movement requests, observe, then hand the untouched JSON to the inner
        // registry as if we were never there.
        public Task<string> ResolveRequestAsJson(string typeFullName, string requestJson,
            IReadableGameDataStore gameDataStore)
        {
            if (typeFullName == typeof(DefineMovementPathRequest).FullName)
            {
                DefineMovementPathRequest? request = Newtonsoft.Json.JsonConvert
                    .DeserializeObject<DefineMovementPathRequest>(requestJson, gameDataStore.GetJsonSettings());
                if (request != null)
                    _shadow.Observe(_tableState, request);
            }
            return _inner.ResolveRequestAsJson(typeFullName, requestJson, gameDataStore);
        }
    }
}
