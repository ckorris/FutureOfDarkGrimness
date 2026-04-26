using FDG;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FdgRaylib.Cli.Resolvers;

public class PlaceObjectsResolver<T> : IStageResolver<PlaceObjectsRequest<T>, List<PlacedObjectEntry<T>>>
{
    public Task<List<PlacedObjectEntry<T>>> Resolve(PlaceObjectsRequest<T> request)
    {
        var zone = request.DeploymentZone.GetValue();
        int total = request.ModelsToPlace.Count;
        float cx = (zone.Left + zone.Right) / 2f;
        float cz = (zone.Bottom + zone.Top) / 2f;

        Console.WriteLine();
        Console.WriteLine($"--- Deploy: place {total} model{(total != 1 ? "s" : "")} ---");
        Console.WriteLine($"  Zone X: {zone.Left:F1}\" to {zone.Right:F1}\"  |  Zone Z: {zone.Bottom:F1}\" to {zone.Top:F1}\"");
        Console.WriteLine("  Enter position as 'x z' (inches). Positions must be inside the zone.");
        Console.WriteLine();

        var placed = new List<PlacedObjectEntry<T>>();
        for (int i = 0; i < request.ModelsToPlace.Count; i++)
        {
            var binding = request.ModelsToPlace[i];
            Console.WriteLine($"  [{i + 1}/{total}] Model {i + 1}");

            while (true)
            {
                Console.Write("  Position: ");
                string? raw = Console.ReadLine();
                if (raw == null) // EOF: spread models 2" apart in a line around zone center
                {
                    float offsetX = (i - (total - 1) / 2f) * 2f;
                    float ex = Math.Clamp(cx + offsetX, zone.Left + 1f, zone.Right - 1f);
                    Console.WriteLine($"    (EOF — auto-placing at {ex:F1}\", {cz:F1}\")");
                    placed.Add(new PlacedObjectEntry<T>(binding, new Position(ex, cz)));
                    break;
                }
                string? input = raw.Trim();
                string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);

                if (parts.Length >= 2 && float.TryParse(parts[0], out float x) && float.TryParse(parts[1], out float z))
                {
                    if (x < zone.Left || x > zone.Right || z < zone.Bottom || z > zone.Top)
                    {
                        Console.WriteLine($"    ! Outside zone — X must be {zone.Left:F1}\"–{zone.Right:F1}\", Z must be {zone.Bottom:F1}\"–{zone.Top:F1}\".");
                        continue;
                    }
                    placed.Add(new PlacedObjectEntry<T>(binding, new Position(x, z)));
                    break;
                }

                Console.WriteLine("    Could not parse — enter 'x z' (e.g. '10 5').");
            }
        }

        return Task.FromResult(placed);
    }
}
