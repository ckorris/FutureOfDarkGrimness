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
        Console.WriteLine("Choose your deployment zone:");

        for (int i = 0; i < request.AvailableZones.Count; i++)
        {
            var zone = request.AvailableZones[i].GetValue();
            float cx = (zone.Left + zone.Right) / 2f;
            float cz = (zone.Bottom + zone.Top) / 2f;
            Console.WriteLine($"  [{i + 1}] Zone {i + 1} (center {cx:F1}\", {cz:F1}\")");
        }

        if (request.UnavailableZones.Count > 0)
        {
            Console.WriteLine("  Unavailable zones:");
            foreach (var z in request.UnavailableZones)
                Console.WriteLine($"    - (taken)");
        }

        while (true)
        {
            Console.Write("Choice: ");
            string? input = Console.ReadLine()?.Trim();
            if (int.TryParse(input, out int choice) &&
                choice >= 1 && choice <= request.AvailableZones.Count)
            {
                return Task.FromResult(request.AvailableZones[choice - 1]);
            }
            Console.WriteLine($"Enter a number between 1 and {request.AvailableZones.Count}.");
        }
    }
}
