using FDG;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FdgRaylib.Cli.Resolvers;

public class ChooseDeploymentZoneResolver : IStageResolver<ChooseDeploymentZoneRequest, DataBinding<RectangularZone>>
{
    public Task<DataBinding<RectangularZone>> Resolve(ChooseDeploymentZoneRequest request)
    {
        Console.WriteLine();
        Console.WriteLine("--- Choose Deployment Zone ---");
        Console.WriteLine($"  Table: {GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES:F0}\" wide x {GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES:F0}\" deep");
        Console.WriteLine($"  You must deploy within {GameWideConstants.DEPLOYMENT_DISTANCE_INCHES:F0}\" of your table edge.");
        Console.WriteLine();

        for (int i = 0; i < request.AvailableZones.Count; i++)
        {
            var zone = request.AvailableZones[i].GetValue();
            Console.WriteLine($"  [{i + 1}] Zone {i + 1}  (X: {zone.Left:F1}\"-{zone.Right:F1}\", Z: {zone.Bottom:F1}\"-{zone.Top:F1}\")");
        }

        if (request.UnavailableZones.Count > 0)
        {
            Console.WriteLine();
            foreach (var zb in request.UnavailableZones)
            {
                var zone = zb.GetValue();
                Console.WriteLine($"      [taken] X: {zone.Left:F1}\"-{zone.Right:F1}\", Z: {zone.Bottom:F1}\"-{zone.Top:F1}\"");
            }
        }

        while (true)
        {
            Console.Write("Choice: ");
            string? input = Console.ReadLine()?.Trim();
            if (input == null) return Task.FromResult(request.AvailableZones[0]); // EOF default: first zone
            if (int.TryParse(input, out int choice) && choice >= 1 && choice <= request.AvailableZones.Count)
                return Task.FromResult(request.AvailableZones[choice - 1]);
            Console.WriteLine($"  Enter a number between 1 and {request.AvailableZones.Count}.");
        }
    }
}
