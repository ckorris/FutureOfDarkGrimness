using FDG;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FdgRaylib.Cli.Resolvers;

public class AssignWoundsResolver : IStageResolver<AssignWoundsRequest, AssignWoundsResults>
{
    public Task<AssignWoundsResults> Resolve(AssignWoundsRequest request)
    {
        var results = new AssignWoundsResults(request.UnitReceivingWounds, request.TotalWoundsToAssign);

        Console.WriteLine();
        Console.WriteLine($"Assign wounds to '{request.UnitReceivingWounds.GetValue().Name}'");
        Console.WriteLine("  Enter a model number to assign wounds to it, or 'a' to auto-assign all remaining.");

        while (!results.IsFinishedAssigning)
        {
            Console.WriteLine($"  Wounds: {results.TotalAssignedWounds}/{results.TotalWoundsToAssign} assigned");

            var models = results.PendingWounds;
            for (int i = 0; i < models.Count; i++)
            {
                var m = models[i].Model.GetValue();
                float pending   = models[i].Wounds;
                float remaining = m.TotalWounds - m.WoundsDealt - pending;
                string assigned = pending > 0 ? $", {pending:F0} already assigned" : "";
                Console.WriteLine($"  [{i + 1}] Model (wounds remaining: {remaining:F0}{assigned})");
            }

            Console.Write("  Choice: ");
            string? input = Console.ReadLine()?.Trim().ToLower();

            if (input == null || input == "a" || string.IsNullOrEmpty(input))
            {
                results.AutoFill();
                break;
            }

            if (int.TryParse(input, out int choice) && choice >= 1 && choice <= models.Count)
            {
                if (!results.TryAddWounds(models[choice - 1].Model))
                    Console.WriteLine("  That model cannot take more wounds.");
            }
            else
            {
                Console.WriteLine($"  Enter a number between 1 and {models.Count}, or 'a'.");
            }
        }

        return Task.FromResult(results);
    }

}
