using FDG;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FdgRaylib.Placement;

namespace FdgRaylib.Cli.Resolvers;

public class PlaceObjectsResolver<T> : IStageResolver<PlaceObjectsRequest<T>, CancellableResult<List<PlacedObjectEntry<T>>>>
{
    private readonly ITableState? _tableState;
    // Tracks how many times each player has deployed, to stagger Z rows.
    private readonly Dictionary<PlayerID, int> _deployCountPerPlayer = new();
    private const float ZRowOffset = 2f; // inches between successive unit rows

    // Snapshot of impassible terrain for the current Resolve call; models can't be placed overlapping it.
    private IReadOnlyList<ITerrain> _impassibleTerrain = Array.Empty<ITerrain>();

    // #269 — the models this request is placing, by reference, excluded from the on-table occupancy scan.
    // A reposition placement (Teleport / Fanatic / reposition-at-activation) starts with every model still
    // at its old spot, so without this the unit blocks itself out of the ground it is about to vacate.
    // Each is still checked at its NEW position through the placed-so-far overlap loop.
    private HashSet<object> _selfModels = new(ReferenceEqualityComparer.Instance);

    public PlaceObjectsResolver(ITableState? tableState = null)
    {
        _tableState = tableState;
    }

    public Task<CancellableResult<List<PlacedObjectEntry<T>>>> Resolve(PlaceObjectsRequest<T> request)
    {
        var zone = request.DeploymentZone;
        ZoneBounds bounds = zone.Bounds;
        int total = request.ModelsToPlace.Count;

        _impassibleTerrain = _tableState?.Terrain.Objects
            .Where(t => t.TerrainType.HasFlag(ETerrainType.Impassible))
            .ToList() ?? (IReadOnlyList<ITerrain>)Array.Empty<ITerrain>();

        _selfModels = new HashSet<object>(ReferenceEqualityComparer.Instance);
        foreach (var selfBinding in request.ModelsToPlace)
        {
            if (selfBinding.GetValue() is ModelData selfModel) _selfModels.Add(selfModel);
        }

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
        if (request.AllowCancel)
            Console.WriteLine("  Type 'back' to cancel and choose a different action.");
        if (request.MustTouchTableEdge)
            Console.WriteLine("  At least one model must touch a table edge (Aircraft redeploy).");
        Console.WriteLine();

        // Default facing: toward the table centre (a top-zone unit faces down) — matters for Aircraft,
        // whose heading IS this facing (#150).
        Float2 defaultFacing = PlacementUtilities.DefaultDeployFacing(
            bounds, GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES);

        // Per-axis auto-placement steps (#150): a single (circumscribing) step over-spaces a wide rectangle's
        // short axis, forcing it into one long row that breaks the 9" all-pairs cohesion rule. Column step from
        // the widest base's X extent, row step from the tallest base's Z extent, each + 0.1" base-to-base.
        float autoStepX = 2f * 0.1f, autoStepZ = 2f * 0.1f;
        foreach (var b in request.ModelsToPlace)
        {
            var (hx, hz) = GetHalfExtents(b.GetValue(), defaultFacing);
            autoStepX = MathF.Max(autoStepX, 2f * hx + 0.1f);
            autoStepZ = MathF.Max(autoStepZ, 2f * hz + 0.1f);
        }

        // One full pass over the unit. Returns null if the player typed 'back'. A local function (rather
        // than a method) so it keeps capturing the placement locals set up above instead of taking a
        // dozen parameters; #269 calls it more than once for a reposition, see below.
        List<PlacedObjectEntry<T>>? PlaceEveryModel()
        {
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
                    // #197 reposition-at-activation: the auto-placer aims at a deployment lane and knows
                    // nothing about this model's own radius, so it would propose an illegal spot. Standing
                    // still is always inside the radius and never breaks a rule the model already satisfies,
                    // and "you MAY place" makes declining legal.
                    if (request.MaxDistanceFromStartInches > 0f)
                    {
                        Position stay = StartPositionOf(binding.GetValue());
                        Console.WriteLine($"    (EOF - staying at {stay.x:F1}\", {stay.z:F1}\")");
                        placed.Add(new PlacedObjectEntry<T>(binding, stay));
                        break;
                    }

                    var pos = FindAutoPosition(r, ShapeOf(binding.GetValue()), defaultFacing, autoStepX, autoStepZ, zone, cz, xStagger, placed, enemies, minEnemyDist,
                        total, request.MustTouchTableEdge);
                    Console.WriteLine($"    (EOF - auto-placing at {pos.x:F1}\", {pos.z:F1}\")");
                    placed.Add(new PlacedObjectEntry<T>(binding, pos, defaultFacing));
                    break;
                }

                if (request.AllowCancel && raw.Trim().Equals("back", StringComparison.OrdinalIgnoreCase))
                    return null;

                string[] parts = raw.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && float.TryParse(parts[0], out float x) && float.TryParse(parts[1], out float z))
                {
                    var newPos = new Position(x, z);
                    // The whole base (its true oriented footprint, not just its centre) must stay inside the
                    // zone — and since deployment zones sit flush with the table edge, this keeps it on the table.
                    if (!PlacementUtilities.IsBaseWithinZone(newPos, ShapeOf(binding.GetValue()), defaultFacing, zone))
                    {
                        Console.WriteLine($"    ! Base would extend outside the placement zone / off the table (keep the centre >= {r:F1}\" inside bounds X {bounds.Left:F1}\"-{bounds.Right:F1}\", Z {bounds.Bottom:F1}\"-{bounds.Top:F1}\").");
                        continue;
                    }

                    string? overlap = CheckOverlap(newPos, ShapeOf(binding.GetValue()), defaultFacing, placed)
                        ?? CheckOverlapWithExisting(newPos, ShapeOf(binding.GetValue()), defaultFacing, GetTableOccupants());
                    if (overlap != null)
                    {
                        Console.WriteLine($"    ! Bases would overlap ({overlap}).");
                        continue;
                    }

                    // #269: only DEPLOYMENT gates each entry on cohesion with the models placed so far. For a
                    // reposition the unit already exists in a formation, and rebuilding it in list order
                    // would pin every model after the first to a 1" band around model 1 - so cohesion is
                    // judged once, over the finished formation, after this loop.
                    if (request.MaxDistanceFromStartInches <= 0f && placed.Count > 0 && !IsInCohesion(newPos, r, placed))
                    {
                        Console.WriteLine($"    ! Outside cohesion - must be within {GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES}\" base-to-base of a placed model.");
                        continue;
                    }

                    if (TooCloseToEnemy(newPos, enemies, minEnemyDist))
                    {
                        Console.WriteLine($"    ! Too close to an enemy - must be over {minEnemyDist:F0}\" from enemy units.");
                        continue;
                    }

                    if (!PlacementUtilities.IsWithinStartRadius(newPos, StartPositionOf(binding.GetValue()),
                            request.MaxDistanceFromStartInches))
                    {
                        Console.WriteLine($"    ! Too far from where this model stands - it may move at most " +
                            $"{request.MaxDistanceFromStartInches:F1}\".");
                        continue;
                    }

                    if (PlacementUtilities.OverlapsImpassibleTerrain(newPos, r, _impassibleTerrain))
                    {
                        Console.WriteLine("    ! On impassible terrain - the model's base would overlap a building or blocker.");
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

                Console.WriteLine("    Could not parse - enter 'x z' (e.g. '10 5').");
            }
        }

        return placed;
        }

        while (true)
        {
            List<PlacedObjectEntry<T>>? attempt = PlaceEveryModel();
            if (attempt == null)
                return Task.FromResult<CancellableResult<List<PlacedObjectEntry<T>>>>(
                    new Cancelled<List<PlacedObjectEntry<T>>>());

            // #269: the reposition's one cohesion verdict, over the finished formation. Deployment is
            // unaffected (it gated per entry above and reports nothing here).
            string? issue = PlacementCohesion.Describe(FinalCohesion(request, attempt));
            if (issue == null)
                return Task.FromResult<CancellableResult<List<PlacedObjectEntry<T>>>>(
                    new Selected<List<PlacedObjectEntry<T>>>(attempt));

            // Terminating: the EOF branch stands every model still, which cannot worsen cohesion, so an
            // automated (piped / EOF) run always leaves on the first pass.
            Console.WriteLine($"    ! {issue}");
            Console.WriteLine("    Placing the unit again - keep the models within cohesion of each other.");
        }
    }

    /// <summary>
    /// #269 — cohesion of a FINISHED reposition placement, or "nothing wrong" for a deployment (which gates
    /// per entry instead). Mirrors the GUI resolver's Done gate.
    /// </summary>
    private static PlacementCohesion.Report FinalCohesion(PlaceObjectsRequest<T> request,
        List<PlacedObjectEntry<T>> placed)
    {
        if (request.MaxDistanceFromStartInches <= 0f) return new PlacementCohesion.Report(0, 0, 0f);

        var before = new List<PlacementCohesion.Footprint>(placed.Count);
        var after  = new List<PlacementCohesion.Footprint>(placed.Count);
        foreach (PlacedObjectEntry<T> entry in placed)
        {
            T value = entry.Binding.GetValue();
            IBaseShape shape = ShapeOf(value);
            Float2 startFacing = value is ModelData m ? m.Facing : new Float2(0f, 1f);
            before.Add(new PlacementCohesion.Footprint(StartPositionOf(value), shape, startFacing));
            after.Add(new PlacementCohesion.Footprint(entry.Position, shape, entry.Facing ?? startFacing));
        }

        return PlacementCohesion.Evaluate(before, after);
    }

    // Finds the next free auto-placement slot, scanning a compact grid (per-axis step so a wide rectangle
    // forms tight rows instead of one long row that breaks the 9" all-pairs cohesion rule — #150). Skips over
    // models already on the table from previous deployment requests.
    private Position FindAutoPosition(float r, IBaseShape shape, Float2 facing, float stepX, float stepZ, IBoundedZone zone, float cz,
        float xStagger, List<PlacedObjectEntry<T>> placedSoFar, List<Position> enemies, float minEnemyDist,
        int unitTotal, bool mustTouchEdge = false)
    {
        var existing = GetTableOccupants().ToList();
        ZoneBounds b = zone.Bounds;

        // Cap columns at ~√N so the unit wraps into a square-ish block instead of one long row (which would
        // break the 9" all-pairs cohesion rule on a wide zone). Matches the grid packers' column count. #150.
        int targetCols = Math.Max(1, (int)MathF.Ceiling(MathF.Sqrt(Math.Max(1, unitTotal))));

        // #029 edge redeploy: scan along the zone edges (bottom, top, left, right) so every auto-placed base
        // touches an edge; cohesion then strings the unit along it.
        if (mustTouchEdge)
        {
            foreach (var (isRow, fixedCoord) in new[]
                     { (true, b.Bottom + r), (true, b.Top - r), (false, b.Left + r), (false, b.Right - r) })
            {
                if (isRow)
                {
                    for (float x = b.Left + r; x <= b.Right - r; x += stepX)
                    {
                        var candidate = new Position(x, fixedCoord);
                        if (IsAutoCandidateFree(candidate, r, shape, facing, zone, placedSoFar, existing, enemies, minEnemyDist))
                            return candidate;
                    }
                }
                else
                {
                    for (float z = b.Bottom + r; z <= b.Top - r; z += stepZ)
                    {
                        var candidate = new Position(fixedCoord, z);
                        if (IsAutoCandidateFree(candidate, r, shape, facing, zone, placedSoFar, existing, enemies, minEnemyDist))
                            return candidate;
                    }
                }
            }
            return CohesiveFallback(placedSoFar, b, r, b.Bottom + r);
        }

        // With an enemy-distance constraint (Ambush), scan the whole zone bottom-to-top, but keep each unit's
        // block ~targetCols wide (anchored at the first placed model) so it stays inside the 9" all-pairs rule.
        if (minEnemyDist > 0f)
        {
            float ambushXStart = placedSoFar.Count > 0 ? placedSoFar[0].Position.x : b.Left + r;
            float ambushXEnd = MathF.Min(b.Right - r, ambushXStart + (targetCols - 1) * stepX + 0.001f);
            for (float z = b.Bottom + r; z <= b.Top - r; z += stepZ)
                for (float x = b.Left + r; x <= b.Right - r; x += stepX)
                {
                    if (placedSoFar.Count > 0 && x > ambushXEnd) break; // wrap to the next row
                    var candidate = new Position(x, z);
                    if (IsAutoCandidateFree(candidate, r, shape, facing, zone, placedSoFar, existing, enemies, minEnemyDist))
                        return candidate;
                }
            return CohesiveFallback(placedSoFar, b, r, b.CenterZ);
        }

        // Normal deploy: fill a compact block — scan Z rows outward from cz (per-axis step), and X within each
        // row, returning the first free, cohesive slot. Per-axis rows keep a wide unit inside the 9" all-pairs
        // rule instead of stringing it along one over-long row.
        // Anchor the block at the first placed model so successive models fill the SAME ~targetCols-wide block,
        // wrapping to a new row once the row is full — keeping the unit compact (inside the 9" all-pairs rule).
        float xStart = placedSoFar.Count > 0 ? placedSoFar[0].Position.x : b.Left + r + xStagger;
        float rowXEnd = MathF.Min(b.Right - r, xStart + (targetCols - 1) * stepX + 0.001f);
        int maxRows = (int)((b.Top - b.Bottom) / stepZ) + 1;
        for (int rowOffset = 0; rowOffset <= maxRows; rowOffset++)
        {
            int signCount = rowOffset == 0 ? 1 : 2;
            for (int s = 0; s < signCount; s++)
            {
                float z = cz + (s == 0 ? rowOffset : -rowOffset) * stepZ;
                if (z < b.Bottom + r || z > b.Top - r) continue;
                for (float x = xStart; x <= rowXEnd; x += stepX)
                {
                    var candidate = new Position(x, z);
                    if (IsAutoCandidateFree(candidate, r, shape, facing, zone, placedSoFar, existing, enemies, minEnemyDist))
                        return candidate;
                }
            }
        }

        // Block full (terrain / a cramped or split zone): fall back to ANY free, cohesive, in-bounds spot in the
        // whole zone — the block-column cap is dropped so the unit stays connected even in an awkward zone.
        for (float z = b.Bottom + r; z <= b.Top - r; z += stepZ)
            for (float x = b.Left + r; x <= b.Right - r; x += stepX)
            {
                var candidate = new Position(x, z);
                if (IsAutoCandidateFree(candidate, r, shape, facing, zone, placedSoFar, existing, enemies, minEnemyDist))
                    return candidate;
            }

        return CohesiveFallback(placedSoFar, b, r, cz);
    }

    // Last-resort auto-placement: sit on the last placed model (base-to-base 0 → always within cohesion) so EOF
    // auto-play never emits an out-of-cohesion deploy that would crash the unit's first move; movement then
    // re-packs them apart. With nothing placed yet, best-effort at the zone centre.
    private static Position CohesiveFallback(List<PlacedObjectEntry<T>> placedSoFar, ZoneBounds b, float r, float cz) =>
        placedSoFar.Count > 0
            ? placedSoFar[placedSoFar.Count - 1].Position
            : new Position(Math.Clamp(b.CenterX, b.Left + r, b.Right - r), Math.Clamp(cz, b.Bottom + r, b.Top - r));

    // The auto-placement legality filter, shared by the edge scan and the row scans.
    private bool IsAutoCandidateFree(Position candidate, float r, IBaseShape shape, Float2 facing, IBoundedZone zone,
        List<PlacedObjectEntry<T>> placedSoFar, List<(Position pos, IBaseShape shape, Float2 facing)> existing,
        List<Position> enemies, float minEnemyDist)
    {
        if (!PlacementUtilities.IsBaseWithinZone(candidate, shape, facing, zone)) return false; // whole base in zone / on table
        if (CheckOverlap(candidate, shape, facing, placedSoFar) != null) return false;
        if (CheckOverlapWithExisting(candidate, shape, facing, existing) != null) return false;
        if (PlacementUtilities.OverlapsImpassibleTerrain(candidate, shape, facing, _impassibleTerrain)) return false;
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

    private IEnumerable<(Position pos, IBaseShape shape, Float2 facing)> GetTableOccupants()
    {
        if (_tableState == null) yield break;
        foreach (var model in _tableState.Models.Objects)
        {
            var pos = model.Position;
            // Default-constructed Position is (0,0,0); models there haven't been placed yet.
            if (pos.x == 0f && pos.z == 0f) continue;
            // #269: a model this request is repositioning doesn't block its own placement.
            if (_selfModels.Contains(model)) continue;
            yield return (pos, model.BaseShape, model.Facing);
        }
    }

    // True-shape overlap (#150): two bases overlap iff their real oriented footprints intersect — NOT their
    // circumscribing circles, which would falsely reject a tight, legal packing of elongated rectangles (two
    // 1×3 bases 0.1" apart on the long edge read as "overlapping" by a 1.58" bounding circle).
    private static string? CheckOverlap(Position newPos, IBaseShape newShape, Float2 newFacing, List<PlacedObjectEntry<T>> placed)
    {
        foreach (var entry in placed)
        {
            IBaseShape es = ShapeOf(entry.Binding.GetValue());
            Float2 ef = entry.Facing ?? new Float2(0f, 1f);
            if (BaseShapeGeometry.AreColliding(newShape, newPos, newFacing, es, entry.Position, ef))
                return $"bases overlap (gap {BaseShapeGeometry.SurfaceGap2D(newShape, newPos, newFacing, es, entry.Position, ef):F2}\")";
        }
        return null;
    }

    private static string? CheckOverlapWithExisting(Position newPos, IBaseShape newShape, Float2 newFacing,
        IEnumerable<(Position pos, IBaseShape shape, Float2 facing)> existing)
    {
        foreach (var (pos, shape, facing) in existing)
            if (BaseShapeGeometry.AreColliding(newShape, newPos, newFacing, shape, pos, facing))
                return $"bases overlap (gap {BaseShapeGeometry.SurfaceGap2D(newShape, newPos, newFacing, shape, pos, facing):F2}\")";
        return null;
    }

    private static float Dist(Position a, Position b)
    {
        float dx = a.x - b.x, dz = a.z - b.z;
        return (float)Math.Sqrt(dx * dx + dz * dz);
    }

    // Circumscribing radius so rectangular bases don't overlap when auto-placed and so they stay in the zone
    // for any facing — a conservative layout bound matching the AI and GUI deploy resolvers (#150). The
    // inscribed BaseRadiusInches under-bounded a rectangle and let adjacent bases overlap.
    // The model's live position, i.e. where a reposition placement measures its radius from. Read before any
    // placement is committed, which PlacedObjectEntry's deferred-write shape guarantees.
    private static Position StartPositionOf(T value) =>
        value is ModelData model ? model.Position : new Position();

    private static float GetBaseRadius(T value) =>
        value is ModelData m ? m.BaseShape.CircumscribedRadiusInches : 0.75f;

    // Per-axis footprint half-extents at the deploy facing, for the auto-placement grid step (#150) — a wide
    // rectangle packs tight rows on its short axis instead of one long row that breaks the 9" cohesion rule.
    private static (float hx, float hz) GetHalfExtents(T value, Float2 facing) =>
        value is ModelData m ? BaseShapeGeometry.FootprintHalfExtents(m.BaseShape, facing) : (0.75f, 0.75f);

    private static IBaseShape ShapeOf(T value) => value is ModelData m ? m.BaseShape : new CircleBase(0.75f);

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
