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

    // Snapshot of impassible terrain for the current Resolve call; models can't be placed overlapping it.
    private IReadOnlyList<ITerrain> _impassibleTerrain = Array.Empty<ITerrain>();

    public PlaceObjectsResolver(ITableState? tableState = null)
    {
        _tableState = tableState;
    }

    public Task<List<PlacedObjectEntry<T>>> Resolve(PlaceObjectsRequest<T> request)
    {
        var zone = request.DeploymentZone;
        ZoneBounds bounds = zone.Bounds;
        int total = request.ModelsToPlace.Count;

        _impassibleTerrain = _tableState?.Terrain.Objects
            .Where(t => t.TerrainType.HasFlag(ETerrainType.Impassible))
            .ToList() ?? (IReadOnlyList<ITerrain>)Array.Empty<ITerrain>();

        float minEnemyDist = request.MinDistanceFromEnemiesInches;
        var enemies = minEnemyDist > 0f ? GetEnemyPositions(request.TargetPlayerID) : new List<Position>();

        _deployCountPerPlayer.TryGetValue(request.TargetPlayerID, out int deployIndex);
        _deployCountPerPlayer[request.TargetPlayerID] = deployIndex + 1;

        float maxRadius = request.ModelsToPlace
            .Select(b => GetBaseRadius(b.GetValue()))
            .DefaultIfEmpty(0.75f)
            .Max();
        float autoSpacing = maxRadius * 2 + 0.1f;

        float zoneCz = bounds.CenterZ;
        float cz = Math.Clamp(zoneCz + deployIndex * ZRowOffset, bounds.Bottom, bounds.Top);
        float xStagger = (deployIndex % 2) * autoSpacing / 2f;

        Console.WriteLine();
        Console.WriteLine($"--- Deploy: place {total} model{(total != 1 ? "s" : "")} ---");
        Console.WriteLine($"  Zone X: {bounds.Left:F1}\" to {bounds.Right:F1}\"  |  Zone Z: {bounds.Bottom:F1}\" to {bounds.Top:F1}\"");
        Console.WriteLine("  Enter position as 'x z' (inches). Bases must not overlap.");
        if (request.MustTouchTableEdge)
            Console.WriteLine("  At least one model must touch a table edge (Aircraft redeploy).");
        Console.WriteLine();

        // Default facing: toward the table centre (a top-zone unit faces down) — matters for Aircraft,
        // whose heading IS this facing (#150).
        Float2 defaultFacing = PlacementUtilities.DefaultDeployFacing(
            bounds, GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES);

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
                    var pos = FindAutoPosition(r, autoSpacing, zone, cz, xStagger, placed, enemies, minEnemyDist,
                        request.MustTouchTableEdge);
                    Console.WriteLine($"    (EOF — auto-placing at {pos.x:F1}\", {pos.z:F1}\")");
                    placed.Add(new PlacedObjectEntry<T>(binding, pos, defaultFacing));
                    break;
                }

                string[] parts = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && float.TryParse(parts[0], out float x) && float.TryParse(parts[1], out float z))
                {
                    var newPos = new Position(x, z);
                    if (!zone.IsPointWithinZone(newPos))
                    {
                        Console.WriteLine($"    ! Outside the placement zone (bounds X {bounds.Left:F1}\"–{bounds.Right:F1}\", Z {bounds.Bottom:F1}\"–{bounds.Top:F1}\").");
                        continue;
                    }

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

                    if (TooCloseToEnemy(newPos, enemies, minEnemyDist))
                    {
                        Console.WriteLine($"    ! Too close to an enemy — must be over {minEnemyDist:F0}\" from enemy units.");
                        continue;
                    }

                    if (PlacementUtilities.OverlapsImpassibleTerrain(newPos, r, _impassibleTerrain))
                    {
                        Console.WriteLine("    ! On impassible terrain — the model's base would overlap a building or blocker.");
                        continue;
                    }

                    // #029: by the last model, at least one of the unit's bases must touch a table edge.
                    if (request.MustTouchTableEdge && i == total - 1
                        && !PlacementUtilities.TouchesZoneEdge(newPos, r, bounds)
                        && !placed.Any(p => PlacementUtilities.TouchesZoneEdge(p.Position, GetBaseRadius(p.Binding.GetValue()), bounds)))
                    {
                        Console.WriteLine("    ! At least one model must touch a table edge.");
                        continue;
                    }

                    placed.Add(new PlacedObjectEntry<T>(binding, newPos, defaultFacing));
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
    private Position FindAutoPosition(float r, float step, IBoundedZone zone, float cz,
        float xStagger, List<PlacedObjectEntry<T>> placedSoFar, List<Position> enemies, float minEnemyDist,
        bool mustTouchEdge = false)
    {
        var existing = GetTableOccupants().ToList();
        ZoneBounds b = zone.Bounds;

        // #029 edge redeploy: scan along the zone edges (bottom, top, left, right) so every auto-placed base
        // touches an edge; cohesion then strings the unit along it.
        if (mustTouchEdge)
        {
            foreach (var (isRow, fixedCoord) in new[]
                     { (true, b.Bottom + r), (true, b.Top - r), (false, b.Left + r), (false, b.Right - r) })
            {
                if (isRow)
                {
                    for (float x = b.Left + r; x <= b.Right - r; x += step)
                    {
                        var candidate = new Position(x, fixedCoord);
                        if (IsAutoCandidateFree(candidate, r, zone, placedSoFar, existing, enemies, minEnemyDist))
                            return candidate;
                    }
                }
                else
                {
                    for (float z = b.Bottom + r; z <= b.Top - r; z += step)
                    {
                        var candidate = new Position(fixedCoord, z);
                        if (IsAutoCandidateFree(candidate, r, zone, placedSoFar, existing, enemies, minEnemyDist))
                            return candidate;
                    }
                }
            }
            return new Position(Math.Clamp(b.CenterX, b.Left + r, b.Right - r), b.Bottom + r);
        }

        // With an enemy-distance constraint (Ambush), scan Z rows too, not just the center row.
        if (minEnemyDist > 0f)
        {
            for (float z = b.Bottom + r; z <= b.Top - r; z += step)
            {
                for (float x = b.Left + r; x <= b.Right - r; x += step)
                {
                    var candidate = new Position(x, z);
                    if (!zone.IsPointWithinZone(candidate)) continue; // outside the true shape (e.g. a circle's corners)
                    if (CheckOverlap(candidate, r, placedSoFar) != null) continue;
                    if (CheckOverlapWithExisting(candidate, r, existing) != null) continue;
                    if (PlacementUtilities.OverlapsImpassibleTerrain(candidate, r, _impassibleTerrain)) continue;
                    if (TooCloseToEnemy(candidate, enemies, minEnemyDist)) continue;
                    if (placedSoFar.Count > 0 && !IsInCohesion(candidate, r, placedSoFar)) continue;
                    return candidate;
                }
            }
            return new Position(Math.Clamp(b.CenterX, b.Left + r, b.Right - r),
                Math.Clamp(b.CenterZ, b.Bottom + r, b.Top - r));
        }

        float xStart = b.Left + r + xStagger;
        for (float x = xStart; x <= b.Right - r; x += step)
        {
            var candidate = new Position(x, cz);
            if (!zone.IsPointWithinZone(candidate)) continue;
            if (CheckOverlap(candidate, r, placedSoFar) != null) continue;
            if (CheckOverlapWithExisting(candidate, r, existing) != null) continue;
            if (PlacementUtilities.OverlapsImpassibleTerrain(candidate, r, _impassibleTerrain)) continue;
            if (placedSoFar.Count > 0 && !IsInCohesion(candidate, r, placedSoFar)) continue;
            return candidate;
        }

        // Fallback: best effort at zone center (shouldn't happen on a 72" table)
        return new Position(Math.Clamp(b.CenterX, b.Left + r, b.Right - r), cz);
    }

    // The auto-placement legality filter, shared by the edge scan and the row scans.
    private bool IsAutoCandidateFree(Position candidate, float r, IBoundedZone zone,
        List<PlacedObjectEntry<T>> placedSoFar, List<(Position pos, float radius)> existing,
        List<Position> enemies, float minEnemyDist)
    {
        if (!zone.IsPointWithinZone(candidate)) return false;
        if (CheckOverlap(candidate, r, placedSoFar) != null) return false;
        if (CheckOverlapWithExisting(candidate, r, existing) != null) return false;
        if (PlacementUtilities.OverlapsImpassibleTerrain(candidate, r, _impassibleTerrain)) return false;
        if (minEnemyDist > 0f && TooCloseToEnemy(candidate, enemies, minEnemyDist)) return false;
        if (placedSoFar.Count > 0 && !IsInCohesion(candidate, r, placedSoFar)) return false;
        return true;
    }

    private List<Position> GetEnemyPositions(PlayerID self)
    {
        var positions = new List<Position>();
        if (_tableState == null) return positions;
        foreach (var unit in _tableState.Units.Objects)
        {
            if (unit.PlayerID == self) continue;
            foreach (var model in unit.Models)
            {
                var pos = model.Position;
                if (!model.GetIsAlive()) continue;
                if (pos.x == 0f && pos.z == 0f) continue;
                positions.Add(pos);
            }
        }
        return positions;
    }

    private static bool TooCloseToEnemy(Position p, List<Position> enemies, float minDist)
    {
        foreach (var e in enemies)
            if (Dist(p, e) < minDist) return true;
        return false;
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
