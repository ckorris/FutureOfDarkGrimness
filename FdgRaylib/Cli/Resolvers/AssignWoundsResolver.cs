using FDG;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FdgRaylib.Rendering;

namespace FdgRaylib.Cli.Resolvers;

public class AssignWoundsResolver : IStageResolver<AssignWoundsRequest, AssignWoundsResults>
{
    public Task<AssignWoundsResults> Resolve(AssignWoundsRequest request)
    {
        var results = new AssignWoundsResults(request.UnitReceivingWounds, request.Packets);

        Console.WriteLine();
        Console.WriteLine($"Assign wounds to '{request.UnitReceivingWounds.GetValue().Name}'");
        Console.WriteLine("  Enter a model number to assign wounds to it, or 'a' to auto-assign all remaining.");
        if (results.HasConfinedPackets)
            Console.WriteLine($"  {WoundAssignmentText.Explanation(results)}");
        int RowOf(PacketCommit commit) => results.PendingWounds.FindIndex(e => e.Model == commit.Model) + 1;

        while (!results.IsFinishedAssigning)
        {
            // #287: the shared rounder - the raw floats printed "8.666667", and F0 below hid the fraction.
            // #401: a Deadly queue lists every clump (placed / next / pending), then reports landed/lost
            // and what the next pick places, via the shared text.
            if (results.HasConfinedPackets)
            {
                Console.WriteLine("  Clumps: " + string.Join(" | ", Enumerable.Range(0, results.Packets.Count)
                    .Select(i => WoundAssignmentText.ChipLabel(results, i, RowOf) + (i == results.PacketsCommitted ? " (next)" : ""))));
            }
            foreach (string line in WoundAssignmentText.Progress(results).Split('\n'))
                Console.WriteLine($"  {line}");

            var models = results.PendingWounds;
            for (int i = 0; i < models.Count; i++)
            {
                var m = models[i].Model.GetValue();
                float pending   = models[i].Wounds;
                float remaining = m.TotalWounds - m.WoundsDealt - pending;
                string assigned = pending > 0 ? $", {WoundFormat.Format(pending)} already assigned" : "";
                string effect = results.HasConfinedPackets ? WoundAssignmentText.ModelEffect(results, models[i]) : "";
                string preview = effect.Length > 0 ? $" - {effect}" : "";
                Console.WriteLine($"  [{i + 1}] Model (wounds remaining: {WoundFormat.Format(remaining)}{assigned}){preview}");
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
