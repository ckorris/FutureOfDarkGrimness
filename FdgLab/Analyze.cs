using FDG;
using FDG.Ai.Tactician;
using FDG.Ai.Tactician.Resolvers;
using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.SaveLoad;
using FDG.StageResolution.Requests;

namespace FdgLab;

/// <summary>
/// Save-file decision analysis (#191 tooling): loads a parked .fdgsave and prints, per living
/// unit, the full scored macro-action candidate table plus the action the Tactician would take
/// from that exact state. Replaces the throwaway-NUnit-test workflow used for the game-1 and
/// game-2 hand-play investigations - any parked save now turns around in seconds.
/// </summary>
public static class Analyze
{
    private static readonly List<string> AllActions = new() { "Move", "Charge", "Shoot", "Pass" };

    public static int Run(string[] args)
    {
        string? savePath = args.FirstOrDefault(a => !a.StartsWith("--"));
        if (savePath == null || !File.Exists(savePath))
        {
            Console.Error.WriteLine("analyze needs a .fdgsave path. See 'fdglab' for usage.");
            return 2;
        }
        string? unitFilter = ArgValue(args, "--unit");
        bool board = !args.Contains("--no-board");
        bool urgency = args.Contains("--urgency");

        GameDataStore store = GameSaveSerializer.Load(File.ReadAllText(savePath));
        var tableState = new TableState(store);
        var evaluator = new RuleEvaluator(new ProbabilisticDiceRoller());
        var planner = new TacticianPlanner(tableState, evaluator);

        if (board) PrintBoard(tableState);
        if (urgency) PrintUrgency(tableState, evaluator);

        foreach (IArmy army in tableState.Armies.Objects)
        {
            if (army is not ArmyData data) continue;
            foreach (DataBinding<UnitData> unit in data.UnitBindings)
            {
                UnitData u = unit.GetValue();
                if (!u.GetIsAlive() || !u.GetIsOnBattlefield()) continue;
                if (unitFilter != null && !u.Name.Contains(unitFilter, StringComparison.OrdinalIgnoreCase))
                    continue;

                Position c = Centroid(u);
                Console.WriteLine($"=== {u.Name} [{ShortId(u.PlayerID)}] models={LivingModels(u)} " +
                    $"at ({c.x:F1},{c.z:F1}) ===");

                planner.BeginActivation(unit);
                string? choice = planner.ChooseAction(AllActions);
                Console.WriteLine($"  ChooseAction -> {choice ?? "(null: solo fallback)"}");

                planner.BeginActivation(unit); // fresh state: ChooseAction above cached a plan
                foreach ((MacroAction k, float score) in MacroActionGenerator
                             .Enumerate(evaluator, tableState, unit)
                             .Select(k => (k, planner.Score(k)))
                             .OrderByDescending(p => p.Item2))
                {
                    string target = k.TargetEnemy != null ? $" vs {k.TargetEnemy.Name}"
                        : k.TargetObjective != null
                            ? $" obj({k.TargetObjective.Position.x:F0},{k.TargetObjective.Position.z:F0})"
                            : k.TargetAlly != null ? $" ally {k.TargetAlly.Name}" : "";
                    Console.WriteLine($"  {score,8:F4}  {k.Intent,-18} {k.ActionType,-8} {k.Feasibility,-13} " +
                        $"end=({k.ProjectedCentroid.x:F1},{k.ProjectedCentroid.z:F1}){target}");
                }
            }
        }
        return 0;
    }

    // #389: the activation-ordering layer, per army - each living unit's urgency score plus the
    // pick the activation resolver would make among the still-unactivated ones (with the #359
    // measurement narration, so a bias-decisive pick says so). The candidate tables below show
    // WHERE a unit would go; this shows WHEN it would get the turn.
    private static void PrintUrgency(ITableState tableState, RuleEvaluator evaluator)
    {
        foreach (IArmy army in tableState.Armies.Objects)
        {
            if (army is not ArmyData data) continue;
            var resolver = new TacticianActivationResolver(tableState, evaluator,
                planner: null, decisionLog: line => Console.WriteLine($"  pick: {line}"));
            Console.WriteLine($"--- urgency [{ShortId(army.PlayerID)}] ---");
            var options = new List<SelectionRequest<UnitData>.ValidOption>();
            foreach (DataBinding<UnitData> unit in data.UnitBindings)
            {
                UnitData u = unit.GetValue();
                if (!u.GetIsAlive() || !u.GetIsOnBattlefield()) continue;
                bool activated = u.Tokens.HasToken(TokenType.ActivatedThisRound);
                Console.WriteLine($"  {resolver.Urgency(unit),8:F4}  {u.Name}" +
                    (activated ? "  (activated)" : ""));
                if (!activated) options.Add(new(unit, u.Name));
            }
            if (options.Count > 1)
                resolver.Resolve(new ChooseUnitToActivateRequest(army.PlayerID, options,
                    new List<SelectionRequest<UnitData>.InvalidOption>())).GetAwaiter().GetResult();
            else if (options.Count == 1)
                Console.WriteLine($"  pick: {options[0].Option.GetValue().Name} (only option)");
        }
    }

    // A text substitute for the screenshot: every objective (with its projected owner) and every
    // living unit's position, so positions in the candidate tables below have context.
    private static void PrintBoard(ITableState tableState)
    {
        Console.WriteLine("--- board ---");
        foreach (ObjectiveProjection projection in TacticalAnalysis.ProjectObjectives(tableState))
        {
            Console.WriteLine($"objective ({projection.Objective.Position.x:F0},{projection.Objective.Position.z:F0}) " +
                $"owner={(projection.ProjectedOwner.HasValue ? ShortId(projection.ProjectedOwner.Value) : "-")} " +
                $"contested={projection.PlayersInRange.Distinct().Count() > 1}");
        }
        foreach (IArmy army in tableState.Armies.Objects)
        {
            if (army is not ArmyData data) continue;
            foreach (DataBinding<UnitData> unit in data.UnitBindings)
            {
                UnitData u = unit.GetValue();
                if (!u.GetIsAlive()) continue;
                Position c = Centroid(u);
                string where = u.GetIsOnBattlefield() ? $"({c.x:F1},{c.z:F1})" : "(off-table)";
                Console.WriteLine($"unit [{ShortId(u.PlayerID)}] {u.Name} models={LivingModels(u)} {where}");
            }
        }
        Console.WriteLine("-------------");
    }

    private static string ShortId(PlayerID id) => id.ID.ToString()[..8];

    private static int LivingModels(UnitData unit) => unit.Models.Count(m => m.GetIsAlive());

    private static Position Centroid(UnitData unit)
    {
        var alive = unit.Models.Where(m => m.GetIsAlive()).ToList();
        if (alive.Count == 0) return new Position(0f, 0f);
        return new Position(alive.Average(m => m.Position.x), alive.Average(m => m.Position.z));
    }

    private static string? ArgValue(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }
}
