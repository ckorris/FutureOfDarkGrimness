using FDG;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;

namespace FdgRaylib.Rendering;

/// <summary>
/// #411: the rules the renderer's overlays read with - the core catalog PLUS every army's own embedded
/// definitions (#059, carried on <see cref="ArmyData"/> since #095). The overlays used to build a core-only
/// resolver, so a book rule granted mid-game (Versatile Attack's "Versatile Attack (Piercing)") was unknown
/// to them: the tooltip's capability query logged "has no definition in the registry" - a false alarm that,
/// being warn-once, would also swallow a real one later - and token chips showed such grants with no
/// description or valence.
///
/// <para>The army data replicates with the store, so host and clients build the same set. Armies are
/// checked for data only until each one has delivered it (a client can see an <see cref="ArmyData"/>
/// before its blob is meaningful), after which a refresh is a count compare. Render thread only.</para>
/// </summary>
internal sealed class DisplayRuleSet
{
    private RuleResolver _resolver = CoreRuleCatalog.CreateResolver();
    private RuleEvaluator? _evaluator;
    private readonly HashSet<ArmyData> _absorbed = new();
    private int _armyCount = -1;

    /// <summary>Token display info: names, descriptions, valence.</summary>
    public IRuleResolver Resolver => _resolver;

    /// <summary>Capability questions (transport, casting...). Read-only; its dice roller is never reached,
    /// since a capability entry's effect rolls nothing.</summary>
    public RuleEvaluator Evaluator => _evaluator ??= new RuleEvaluator(new ProbabilisticDiceRoller(), ruleResolver: _resolver);

    /// <summary>Picks up any army whose rule data has not been absorbed yet. Cheap once every army has.</summary>
    public void Refresh(ITableState? tableState)
    {
        if (tableState == null) return;

        List<ArmyData> armies = tableState.Armies.Objects.OfType<ArmyData>().ToList();
        if (armies.Count == _armyCount && _absorbed.Count == armies.Count) return;

        // A different game (fewer armies, or ones we never saw) starts from the core catalog again, so one
        // game's book rules never leak into the next.
        if (armies.Count < _absorbed.Count || _absorbed.Any(a => !armies.Contains(a)))
        {
            _resolver = CoreRuleCatalog.CreateResolver();
            _evaluator = null;
            _absorbed.Clear();
        }

        _armyCount = armies.Count;
        foreach (ArmyData army in armies)
        {
            if (_absorbed.Contains(army)) continue;
            if (army.RestoreRuleData() is not { } data) continue;

            foreach (SpecialRuleDefinition definition in data.RuleDefinitions)
                _resolver.RegisterOrReplace(definition);
            _absorbed.Add(army);
            _evaluator = null; // rebuilt on next use against the grown resolver
        }
    }
}
