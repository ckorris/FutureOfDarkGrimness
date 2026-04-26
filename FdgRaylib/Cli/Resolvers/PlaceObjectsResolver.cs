using FDG;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FdgRaylib.Cli.Resolvers;

/// <summary>
/// CLI placement resolver. Prompts for x z coordinates per object to place.
/// Used during deployment and map setup.
/// </summary>
public class PlaceObjectsResolver<T> : IStageResolver<PlaceObjectsRequest<T>, List<PlacedObjectEntry<T>>>
{
    public Task<List<PlacedObjectEntry<T>>> Resolve(PlaceObjectsRequest<T> request)
    {
        var zone = request.DeploymentZone.GetValue();
        float cx = (zone.Left + zone.Right) / 2f;
        float cz = (zone.Bottom + zone.Top) / 2f;
        float w = zone.Right - zone.Left;
        float h = zone.Top - zone.Bottom;
        Console.WriteLine();
        Console.WriteLine($"Place {request.ModelsToPlace.Count} object(s) in zone " +
            $"(center {cx:F1}\", {cz:F1}\", size {w:F1}\" x {h:F1}\")");
        Console.WriteLine("Enter position as: x z  (inches)");

        var placed = new List<PlacedObjectEntry<T>>();
        foreach (var binding in request.ModelsToPlace)
        {
            Console.Write($"  Place '{typeof(T).Name}': ");
            string? input = Console.ReadLine()?.Trim();
            string[] parts = input?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];

            Position pos;
            if (parts.Length >= 2 &&
                float.TryParse(parts[0], out float x) &&
                float.TryParse(parts[1], out float z))
            {
                pos = new Position(x, z);
            }
            else
            {
                // Default to zone center if input invalid
                pos = new Position(cx, cz);
                Console.WriteLine("  Could not parse — placing at zone center.");
            }

            placed.Add(new PlacedObjectEntry<T>(binding, pos));
        }

        return Task.FromResult(placed);
    }
}
