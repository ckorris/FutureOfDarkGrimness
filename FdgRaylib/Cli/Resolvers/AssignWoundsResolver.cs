using FDG;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FdgRaylib.Cli.Resolvers;

/// <summary>
/// CLI wound assignment. Lists alive models and lets the player number them.
/// Falls back to auto-fill if input can't be parsed.
/// </summary>
public class AssignWoundsResolver : IStageResolver<AssignWoundsRequest, AssignWoundsResults>
{
    public Task<AssignWoundsResults> Resolve(AssignWoundsRequest request)
    {
        var results = new AssignWoundsResults(request.UnitReceivingWounds, request.TotalWoundsToAssign);
        var aliveModels = results.PendingWounds;

        Console.WriteLine();
        Console.WriteLine($"Assign {request.TotalWoundsToAssign} wound(s) to '{request.UnitReceivingWounds.GetValue().Name}':");

        for (int i = 0; i < aliveModels.Count; i++)
        {
            var m = aliveModels[i].Model.GetValue();
            Console.WriteLine($"  [{i + 1}] Model (wounds remaining: {m.TotalWounds - m.WoundsDealt})");
        }

        Console.WriteLine("  [a] Auto-assign");
        Console.Write("Choice: ");
        string? input = Console.ReadLine()?.Trim().ToLower();

        if (input == "a" || string.IsNullOrEmpty(input))
        {
            results.AutoFill();
            return Task.FromResult(results);
        }

        if (int.TryParse(input, out int choice) && choice >= 1 && choice <= aliveModels.Count)
        {
            results.TryAddWounds(aliveModels[choice - 1].Model);
        }
        else
        {
            Console.WriteLine("Invalid input — auto-assigning.");
            results.AutoFill();
        }

        return Task.FromResult(results);
    }
}
