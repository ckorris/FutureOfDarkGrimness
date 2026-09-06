using FDG;
using FDG.Ai.Tactician;
using FDG.Ai.Tactician.Learning;
using FDG.Ai.Tactician.Search;
using FDG.Data;
using FDG.Network;
using FDG.Rules.Dispatch;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using Newtonsoft.Json;

namespace FdgLab.Export;

/// <summary>
/// Wraps one player's resolver registry to capture C1 rows (#191 step 4). Every decision -
/// including a LOCAL AI player's - is dispatched through <see cref="ResolveRequestAsJson"/>, not
/// the typed <c>ResolveRequest&lt;TRequest,TReply&gt;</c> path: <c>RequestMessageSender.
/// RequestDecision</c> serializes every request before sending it, local or networked (the
/// "~7% JSON round-trip" the 2026-09-03 profiling ledger entry recorded and left as a future
/// "bypass the bus inside a simulation" optimization for B - not yet built), so that is the only
/// seam that actually sees every game's decisions. Deserializing with the engine's own
/// <see cref="WireJsonSettings"/> (built from the SAME <c>IReadableGameDataStore</c> the call
/// already carries) reuses the exact wire format real networked play depends on, so this reads
/// state without risking a divergent parse.
/// <para>
/// Intercepts two request types that bracket an activation's decision - <see cref="ChooseUnitToActivateRequest"/>
/// (the activation boundary: encode state BEFORE the unit is known, then read off which unit was
/// chosen) and <see cref="ChooseActionRequest"/> (chosen_action, plus chosen_macro from the
/// Tactician planner when this profile plans - its own request type since #191 B1 step 5a, no
/// longer a string-instructions sniff). Everything else passes straight through untouched -
/// decision-neutral by construction, since it only reads the reply AFTER awaiting the real resolver.
/// </para>
/// </summary>
public sealed class ExportingRegistry : IStageResolverRegistry
{
    private static readonly string ChooseUnitTypeName = typeof(ChooseUnitToActivateRequest).FullName!;
    private static readonly string ChooseActionTypeName = typeof(ChooseActionRequest).FullName!;

    private readonly IStageResolverRegistry _inner;
    private readonly GameExportState _state;
    private readonly PlayerID _playerID;
    private readonly int _slotID;
    private readonly Func<ITableState> _tableState;
    private readonly RuleEvaluator _queryEvaluator;
    private readonly TacticianPlanner? _planner;
    private readonly float _totalGamePoints;

    private readonly HandWeightedEvaluator _handEvaluator = new();

    private ExportRow? _openRow;

    public ExportingRegistry(IStageResolverRegistry inner, GameExportState state, PlayerID playerID, int slotID,
        Func<ITableState> tableState, RuleEvaluator queryEvaluator, TacticianPlanner? planner, float totalGamePoints)
    {
        _inner = inner;
        _state = state;
        _playerID = playerID;
        _slotID = slotID;
        _tableState = tableState;
        _queryEvaluator = queryEvaluator;
        _planner = planner;
        _totalGamePoints = totalGamePoints;
    }

    public IStageResolverRegistry RegisterResolver<TRequest, TReply>(IStageResolver<TRequest, TReply> resolver)
        where TRequest : IStageTaskRequest<TReply>
    {
        _inner.RegisterResolver(resolver);
        return this;
    }

    public Task<TReply> ResolveRequest<TRequest, TReply>(TRequest request)
        where TRequest : IStageTaskRequest<TReply> =>
        _inner.ResolveRequest<TRequest, TReply>(request);

    public async Task<string> ResolveRequestAsJson(string typeFullName, string requestJson,
        IReadableGameDataStore gameDataStore)
    {
        if (typeFullName == ChooseUnitTypeName)
        {
            FlushOpenRowUnfinished(); // a prior activation that never reached Choose Action (back-out, delayed action)

            ITableState tableState = _tableState();
            var sw = System.Diagnostics.Stopwatch.StartNew();
            (int boundaryInRound, int expected, bool first) = _state.OpenBoundary(tableState, _playerID);
            float[] features = PositionEncoder.Encode(tableState, _queryEvaluator, _playerID,
                boundaryInRound, expected, first, _totalGamePoints);
            float handValue = HandValueFor(tableState, gameDataStore);
            sw.Stop();
            _state.RecordEncoderMs(sw.Elapsed.TotalMilliseconds);
            List<float[]>? entities = _state.EntitySampled
                ? PositionEncoder.EncodeEntities(tableState, _queryEvaluator, _playerID)
                : null;

            string replyJson = await _inner.ResolveRequestAsJson(typeFullName, requestJson, gameDataStore);
            JsonSerializerSettings wire = WireJsonSettings.For(gameDataStore);
            var chosen = JsonConvert.DeserializeObject<DataBinding<UnitData>>(replyJson, wire);

            var row = new ExportRow
            {
                GameId = _state.GameId,
                Boundary = _state.NextGlobalBoundary(),
                Round = tableState.Progress.RoundCount ?? 1,
                ActingSlot = _slotID,
                Features = features,
                HandValue = handValue,
                ChosenUnit = chosen == null ? -1 : RosterIndex(tableState, _playerID, chosen),
            };
            if (entities != null) _state.AddEntityRows(row.Boundary, entities);
            _openRow = row;
            return replyJson;
        }

        if (typeFullName == ChooseActionTypeName)
        {
            JsonSerializerSettings wire = WireJsonSettings.For(gameDataStore);
            string replyJson = await _inner.ResolveRequestAsJson(typeFullName, requestJson, gameDataStore);

            if (_openRow != null)
            {
                _openRow.ChosenAction = JsonConvert.DeserializeObject<string>(replyJson, wire) ?? "";
                _openRow.ChosenMacro = _planner?.LastMacroLabel ?? "";
                _state.FlushRow(_openRow);
                _openRow = null;
            }
            return replyJson;
        }

        return await _inner.ResolveRequestAsJson(typeFullName, requestJson, gameDataStore);
    }

    private void FlushOpenRowUnfinished()
    {
        if (_openRow == null) return;
        _state.FlushRow(_openRow); // chosen_action/chosen_macro stay "" per schema sec 1
        _openRow = null;
    }

    // The hand evaluator's value for the acting side (schema v3's hand_value). Same instance the
    // Strategist's leaf uses, called on the live table state - it is a pure read (IPositionEvaluator's
    // contract: never rolls, never mutates), so it cannot perturb the game being exported. Costs
    // ~1.8ms, inside the encoder stopwatch on purpose: the 5ms budget covers what the exporter adds
    // to a boundary, and this is part of it now (schema sec 7 check 6's canary watches the sum).
    private float HandValueFor(ITableState tableState, IReadableGameDataStore gameDataStore)
    {
        try
        {
            SideMap sides = SideMap.FromStore(gameDataStore);
            SideValues values = _handEvaluator.Evaluate(tableState, _queryEvaluator, sides);
            return values[sides.SideOf(_playerID)];
        }
        catch (KeyNotFoundException)
        {
            return float.NaN; // a player the slot records do not know: log nothing rather than a wrong number
        }
    }

    private static int RosterIndex(ITableState tableState, PlayerID player, DataBinding<UnitData> chosen)
    {
        int i = 0;
        foreach (IArmy army in tableState.Armies.Objects)
        {
            if (!army.PlayerID.Equals(player) || army is not ArmyData data) continue;
            foreach (DataBinding<UnitData> binding in data.UnitBindings)
            {
                if (binding.Reference.Equals(chosen.Reference)) return i;
                i++;
            }
        }
        return -1;
    }
}
