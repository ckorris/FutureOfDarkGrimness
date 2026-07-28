using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FDG;
using FDG.ArmyBuilding;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.StageResolution.Requests;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #197 P22 - the shipped Repel Ambushers (24 refs, 8 books) and Ambush Beacon (6 refs, 3 books) data.
// The engine mechanism (discs, waiver-overrides-both, side-awareness, stage wiring) is pinned by
// AmbushArrivalConstraintTests over LOCAL definitions; these tests pin the DATA as authored - the real
// supplement JSON driven through the real evaluator and AmbushArrivalRules - plus the book embedding,
// which --validate-rules and RuleFireLint cannot check.
[TestFixture]
public class AmbushConstraintShippedDataTests
{
    private const string RepelName = "Repel Ambushers";
    private const string BeaconName = "Ambush Beacon";

    private static IReadOnlyList<SpecialRuleDefinition> Supplement() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
            Path.Combine(AppContext.BaseDirectory, "Assets", "Books", "GdfRuleSupplement.json")));

    private static SpecialRuleDefinition Definition(string name) =>
        Supplement().Single(r => r.Name == name);

    [Test]
    public void RepelAmbushers_IsACapabilityEntry_AtTwelveInches()
    {
        HookEntry entry = Definition(RepelName).Passive.Single();

        Assert.That(entry.HookID, Is.EqualTo(EHookID.Lifecycle_OnCapabilityQuery));
        Assert.That(entry.Effect, Is.InstanceOf<Effect.RepelAmbushers>());
        Assert.That(((Effect.RepelAmbushers)entry.Effect).DistanceInches, Is.EqualTo(12f).Within(0.001f),
            "'must be set up over 12\" away from this model's unit'");
    }

    [Test]
    public void AmbushBeacon_IsACapabilityEntry_AtSixInches()
    {
        HookEntry entry = Definition(BeaconName).Passive.Single();

        Assert.That(entry.HookID, Is.EqualTo(EHookID.Lifecycle_OnCapabilityQuery));
        Assert.That(entry.Effect, Is.InstanceOf<Effect.AmbushBeacon>());
        Assert.That(((Effect.AmbushBeacon)entry.Effect).RangeInches, Is.EqualTo(6f).Within(0.001f),
            "'if they are deployed within 6\" of this model'");
    }

    [Test]
    public void ShippedRepel_ProjectsItsKeepOutDiscs_AgainstAnEnemyArrival()
    {
        var harness = new Harness(RepelName);
        IUnit repeller = harness.BuildUnit("Enemy", new Position(30f, 30f), RepelName);
        IUnit arriver = harness.BuildUnit("Us", new Position(1f, 1f));

        IReadOnlyList<PlacementDisc> discs = AmbushArrivalRules.KeepOutDiscs(
            arriver, harness.TableState, harness.Evaluator);

        Assert.That(discs, Has.Count.EqualTo(2), "one disc per living model of the repelling unit");
        Assert.That(discs.All(d => Math.Abs(d.RadiusInches - 12f) < 0.001f), Is.True);
    }

    [Test]
    public void ShippedBeacon_ProjectsItsWaiverDiscs_ForAFriendlyArrival()
    {
        var harness = new Harness(BeaconName);
        // Same player owns both: the beacon must light the way for its OWN side only.
        IUnit beacon = harness.BuildUnit("Us", new Position(10f, 10f), BeaconName);
        IUnit arriver = harness.BuildUnit("Us", new Position(1f, 1f));

        IReadOnlyList<PlacementDisc> waivers = AmbushArrivalRules.WaiverDiscs(
            arriver, harness.TableState, harness.Evaluator);

        Assert.That(waivers, Has.Count.EqualTo(2), "one disc per living model of the beacon unit");
        Assert.That(waivers.All(d => Math.Abs(d.RadiusInches - 6f) < 0.001f), Is.True);

        Assert.That(AmbushArrivalRules.KeepOutDiscs(arriver, harness.TableState, harness.Evaluator),
            Is.Empty, "a friendly beacon is not a keep-out source");
    }

    [Test]
    public void EveryBookReferencingEitherRule_EmbedsItsDefinition()
    {
        string booksDir = Path.Combine(AppContext.BaseDirectory, "Assets", "Books");
        List<string> missing = new List<string>();

        foreach (string path in Directory.GetFiles(booksDir, "*.fdgbook"))
        {
            string json = File.ReadAllText(path);
            if (json.Contains($"\"name\": \"{RepelName}\"", StringComparison.Ordinal)
                && !json.Contains("\"repelAmbushers\"", StringComparison.Ordinal))
            {
                missing.Add(Path.GetFileName(path) + " (Repel Ambushers)");
            }

            if (json.Contains($"\"name\": \"{BeaconName}\"", StringComparison.Ordinal)
                && !json.Contains("\"ambushBeacon\"", StringComparison.Ordinal))
            {
                missing.Add(Path.GetFileName(path) + " (Ambush Beacon)");
            }
        }

        Assert.That(missing, Is.Empty,
            "books referencing a rule without its embedded definition: " + string.Join(", ", missing));
    }

    /// <summary>Live resolver + evaluator carrying the real shipped definitions, over a real store so
    /// AmbushArrivalRules can scan the table.</summary>
    private sealed class Harness
    {
        private readonly GameDataStore _store = GameDataStore.GameDataStoreBuilder.GetDefault();
        private readonly RuleResolver _resolver = new();
        private readonly Dictionary<string, PlayerID> _players = new();

        public RuleEvaluator Evaluator { get; }
        public TableState TableState { get; }

        public Harness(params string[] ruleNames)
        {
            var byName = Supplement().ToDictionary(r => r.Name, StringComparer.OrdinalIgnoreCase);
            foreach (string name in ruleNames) _resolver.Register(byName[name]);
            Evaluator = new RuleEvaluator(new ProbabilisticDiceRoller(), ruleResolver: _resolver);
            TableState = new TableState(_store);
        }

        public IUnit BuildUnit(string playerName, Position position, params string[] ruleNames)
        {
            if (!_players.TryGetValue(playerName, out PlayerID player))
            {
                player = new PlayerID(Guid.NewGuid());
                _players[playerName] = player;
            }

            var modelBindings = new List<DataBinding<ModelData>>();
            for (int i = 0; i < 2; i++)
            {
                var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                    initialPosition: new Position(position.x + i, position.z), gameDataStore: _store);
                modelBindings.Add(_store.GetDataBinding<ModelData>(_store.Create(model)));
            }

            var unit = new UnitData(player, $"{playerName}-unit", quality: 4, defense: 4,
                modelBindings: modelBindings);
            foreach (string name in ruleNames) unit.AttachRuleDefinition(_resolver.Resolve(name));
            _store.Create(unit);
            return unit;
        }
    }
}
