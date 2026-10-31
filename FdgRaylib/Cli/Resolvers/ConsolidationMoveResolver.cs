using FDG;
using FDG.Ai.Tactician;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FdgRaylib.Placement;

namespace FdgRaylib.Cli.Resolvers;

public class ConsolidationMoveResolver : IStageResolver<ConsolidationMoveRequest, List<ModelMoveEntry>>
{
    private readonly ITableState? _tableState;

    // tableState lets the resolver enemy-check the entered offset (#090) so it never returns a move the
    // authoritative ConsolidateStage would reject. Null-safe: with no table state it falls back to the
    // distance-cap-only check.
    public ConsolidationMoveResolver(ITableState? tableState = null) => _tableState = tableState;

    public Task<List<ModelMoveEntry>> Resolve(ConsolidationMoveRequest request)
    {
        var unit = request.UnitDataBinding.GetValue();
        var aliveModels = unit.ModelBindings.Where(m => m.GetValue().GetIsAlive()).ToList();

        while (true)
        {
            Console.WriteLine();
            Console.WriteLine($"--- Consolidate: {unit.Name} ({request.Reason}, <= {request.MaxDistanceInches:F1}\") ---");
            Console.WriteLine($"  Enter a unit-wide offset as 'dx dz' in inches (e.g. '0 -1'), or press Enter to stay in place.");

            string? input = Console.ReadLine()?.Trim();

            if (input == null || string.IsNullOrEmpty(input))
                return Task.FromResult(AutoConsolidate(request, aliveModels));

            string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !float.TryParse(parts[0], out float dx) || !float.TryParse(parts[1], out float dz))
            {
                Console.WriteLine("    Could not parse - try again.");
                continue;
            }

            float dist = MathF.Sqrt(dx * dx + dz * dz);
            if (dist > request.MaxDistanceInches + 0.0001f)
            {
                Console.WriteLine($"    Offset is {dist:F2}\" - exceeds the {request.MaxDistanceInches:F1}\" cap. Try again.");
                continue;
            }

            var entries = aliveModels.Select(mb =>
            {
                var m = mb.GetValue();
                return new ModelMoveEntry(mb, new List<Position> { new Position(m.Position.x + dx, m.Position.z + dz) });
            }).ToList();

            // Enemy-check the move (move-through / standoff) against the same lenient validator ConsolidateStage
            // runs (#159), so an offset that crosses or stacks on an enemy is rejected here rather than throwing
            // downstream — while a hold / re-form of an already-broken unit isn't wrongly blocked.
            if (_tableState != null && !MovementUtilities.ValidateConsolidationPaths(entries, request.MaxDistanceInches,
                    GetEnemyFootprints(request), request.CanMoveThroughEnemies, request.IgnoresDifficultTerrain,
                    request.IgnoresImpassibleTerrain, _tableState.Terrain.Objects, out var errors,
                    GetFriendlyFootprints(request)))
            {
                Console.WriteLine($"    Invalid: {string.Join(", ", errors.Select(e => e.ToString()))}. Try again.");
                continue;
            }

            return Task.FromResult(entries);
        }
    }

    // EOF / "stay in place": if the unit is out of coherency (a mid-unit casualty left a hole), re-form the
    // survivors toward their centroid within the cap so the auto-consolidation still pulls them back together
    // (#159); otherwise just hold. Validated against the same lenient check ConsolidateStage runs, falling back
    // to a plain hold (always valid) when there's no table state or the re-form is blocked.
    private List<ModelMoveEntry> AutoConsolidate(ConsolidationMoveRequest request, List<DataBinding<ModelData>> aliveModels)
    {
        if (aliveModels.Count <= 1 || CohesiveFormation.IsCohesive(aliveModels))
            return StayInPlace(aliveModels);

        float cx = aliveModels.Average(mb => mb.GetValue().Position.x);
        float cz = aliveModels.Average(mb => mb.GetValue().Position.z);
        var reform = CohesiveFormation.ReformTowardWithinCap(aliveModels, cx, cz, request.MaxDistanceInches - 0.001f);

        if (_tableState == null || MovementUtilities.ValidateConsolidationPaths(reform, request.MaxDistanceInches,
                GetEnemyFootprints(request), request.CanMoveThroughEnemies, request.IgnoresDifficultTerrain,
                request.IgnoresImpassibleTerrain, _tableState.Terrain.Objects, out _,
                GetFriendlyFootprints(request)))
            return reform;

        return StayInPlace(aliveModels);
    }

    private List<EnemyModelFootprint> GetEnemyFootprints(ConsolidationMoveRequest request)
    {
        var footprints = new List<EnemyModelFootprint>();
        if (_tableState == null) return footprints;
        int unitKey = 0;
        foreach (var u in _tableState.Units.Objects)
        {
            if (!TeamAwareness.IsEnemyUnit(_tableState, request.TargetPlayerID, u)) continue;
            bool uncontactable = FDG.Rules.Dispatch.AircraftRules.IsAircraft(u); // #029
            bool anyLiving = false;
            foreach (var m in u.Models)
                if (m.GetIsAlive())
                {
                    footprints.Add(new EnemyModelFootprint(m.Position, m.BaseRadiusInches, unitKey, uncontactable, m.BaseShape, m.Facing));
                    anyLiving = true;
                }
            if (anyLiving) unitKey++;
        }
        return footprints;
    }

    // #205: friendly footprints (same team, excluding the consolidating unit) it may not end stacked on.
    private List<EnemyModelFootprint> GetFriendlyFootprints(ConsolidationMoveRequest request)
        => _tableState == null
            ? new List<EnemyModelFootprint>()
            : MovementPlanner.LiveFriendlyFootprints(_tableState, request.TargetPlayerID,
                request.UnitDataBinding.GetValue().ID);

    private static List<ModelMoveEntry> StayInPlace(List<DataBinding<ModelData>> aliveModels) =>
        aliveModels.Select(mb => new ModelMoveEntry(mb, new List<Position>())).ToList();
}
