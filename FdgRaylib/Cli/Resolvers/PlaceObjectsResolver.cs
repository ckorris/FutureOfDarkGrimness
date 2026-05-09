using FDG;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FdgRaylib.Cli.Resolvers;

public class PlaceObjectsResolver<T> : IStageResolver<PlaceObjectsRequest<T>, List<PlacedObjectEntry<T>>>
{
    private readonly ITableState? _tableState;
    // Tracks how many times each player has deployed, to stagger Z rows.
    private readonly Dictionary<PlayerID, int> _deployCountPerPlayer = new();
    private const float ZRowOffset = 2f; // inches between successive unit rows

    public PlaceObjectsResolver(ITableState? tableState = null)
    {
        _tableState = tableState;
    }

    public Task<List<PlacedObjectEntry<T>>> Resolve(PlaceObjectsRequest<T> request)
    {
        var zone = request.DeploymentZone.GetValue();
        int total = request.ModelsToPlace.Count;

        _deployCountPerPlayer.TryGetValue(request.TargetPlayerID, out int deployIndex);
        _deployCountPerPlayer[request.TargetPlayerID] = deployIndex + 1;

        float maxRadius = request.ModelsToPlace
            .Select(b => GetBaseRadius(b.GetValue()))
            .DefaultIfEmpty(0.75f)
            .Max();
        float autoSpacing = maxRadius * 2 + 0.1f;

        float zoneCz = (zone.Bottom + zone.Top) / 2f;
        float cz = Math.Clamp(zoneCz + deployIndex * ZRowOffset, zone.Bottom, zone.Top);
        float xStagger = (deployIndex % 2) * autoSpacing / 2f;

        Console.WriteLine();
        Console.WriteLine($"--- Deploy: place {total} model{(total != 1 ? "s" : "")} ---");
        Console.WriteLine($"  Zone X: {zone.Left:F1}\" to {zone.Right:F1}\"  |  Zone Z: {zone.Bottom:F1}\" to {zone.Top:F1}\"");
        Console.WriteLine("  Enter position as 'x z' (inches). Bases must not overlap.");
        Console.WriteLine();

        var placed = new List<PlacedObjectEntry<T>>();
        for (int i = 0; i < request.ModelsToPlace.Count; i++)
        {
            var binding = request.ModelsToPlace[i];
            float r = GetBaseRadius(binding.GetValue());
            Console.WriteLine($"  [{i + 1}/{total}] Model {i + 1} (base radius {r:F2}\")");

            while (true)
            {
                Console.Write("  Position: ");
                string? raw = Console.ReadLine();
                if (raw == null)
                {
                    var pos = FindAutoPosition(r, autoSpacing, zone, cz, xStagger, placed);
                    Console.WriteLine($"    (EOF — auto-placing at {pos.x:F1}\", {pos.z:F1}\")");
                    placed.Add(new PlacedObjectEntry<T>(binding, pos));
                    break;
                }

                string[] parts = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && float.TryParse(parts[0], out float x) && float.TryParse(parts[1], out float z))
                {
                    if (x < zone.Left || x > zone.Right || z < zone.Bottom || z > zone.Top)
                    {
                        Console.WriteLine($"    ! Outside zone — X must be {zone.Left:F1}\"–{zone.Right:F1}\", Z must be {zone.Bottom:F1}\"–{zone.Top:F1}\".");
                        continue;
                    }

                    var newPos = new Position(x, z);
                    string? overlap = CheckOverlap(newPos, r, placed)
                        ?? CheckOverlapWithExisting(newPos, r, GetTableOccupants());
                    if (overlap != null)
                    {
                        Console.WriteLine($"    ! Bases would overlap ({overlap}).");
                        continue;
                    }

                    if (placed.Count > 0 && !IsInCohesion(newPos, r, placed))
                    {
                        Console.WriteLine($"    ! Outside cohesion — must be within {GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES}\" base-to-base of a placed model.");
                        continue;
                    }

                    placed.Add(new PlacedObjectEntry<T>(binding, newPos));
                    break;
                }

                Console.WriteLine("    Could not parse — enter 'x z' (e.g. '10 5').");
            }
        }

        return Task.FromResult(placed);
    }

    // Scans the zone left-to-right at zone-center Z to find the first free spot,
    // skipping over models already on the table from previous deployment requests.
    // Each deployment row is staggered by half a step on X to avoid vertical alignment.
    private Position FindAutoPosition(float r, float step, RectangularZone zone, float cz,
        float xStagger, List<PlacedObjectEntry<T>> placedSoFar)
    {
        var existing = GetTableOccupants();

        float xStart = zone.Left + r + xStagger;
        for (float x = xStart; x <= zone.Right - r; x += step)
        {
            var candidate = new Position(x, cz);
            if (CheckOverlap(candidate, r, placedSoFar) != null) continue;
            if (CheckOverlapWithExisting(candidate, r, existing) != null) continue;
            if (placedSoFar.Count > 0 && !IsInCohesion(candidate, r, placedSoFar)) continue;
            return candidate;
        }

        // Fallback: best effort at zone center (shouldn't happen on a 72" table)
        return new Position(Math.Clamp((zone.Left + zone.Right) / 2f, zone.Left + r, zone.Right - r), cz);
    }

    private IEnumerable<(Position pos, float radius)> GetTableOccupants()
    {
        if (_tableState == null) yield break;
        foreach (var model in _tableState.Models.Objects)
        {
            var pos = model.Position;
            // Default-constructed Position is (0,0,0); models there haven't been placed yet.
            if (pos.x == 0f && pos.z == 0f) continue;
            yield return (pos, model.BaseRadiusInches);
        }
    }

    private static string? CheckOverlap(Position newPos, float newRadius, List<PlacedObjectEntry<T>> placed)
    {
        foreach (var entry in placed)
        {
            float er = GetBaseRadius(entry.Binding.GetValue());
            if (Overlaps(newPos, newRadius, entry.Position, er))
                return $"needs {newRadius + er:F2}\" center-to-center, got {Dist(newPos, entry.Position):F2}\"";
        }
        return null;
    }

    private static string? CheckOverlapWithExisting(Position newPos, float newRadius,
        IEnumerable<(Position pos, float radius)> existing)
    {
        foreach (var (pos, radius) in existing)
        {
            if (Overlaps(newPos, newRadius, pos, radius))
                return $"needs {newRadius + radius:F2}\" center-to-center, got {Dist(newPos, pos):F2}\"";
        }
        return null;
    }

    private static bool Overlaps(Position a, float ra, Position b, float rb) =>
        Dist(a, b) < ra + rb;

    private static float Dist(Position a, Position b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    private static float GetBaseRadius(T value) =>
        value is ModelData m ? m.BaseRadiusInches : 0.75f;

    private static bool IsInCohesion(Position candidate, float radius, List<PlacedObjectEntry<T>> placed)
    {
        foreach (var entry in placed)
        {
            float er = GetBaseRadius(entry.Binding.GetValue());
            float b2b = Dist(candidate, entry.Position) - radius - er;
            if (b2b <= GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES)
                return true;
        }
        return false;
    }
}
