using FDG;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;

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
                return Task.FromResult(StayInPlace(aliveModels));

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

            // Enemy-check the move (move-through / standoff) against the same validator ConsolidateStage runs,
            // so an offset that crosses or stacks on an enemy is rejected here rather than throwing downstream.
            if (_tableState != null && !MovementUtilities.ValidatePaths(entries, request.MaxDistanceInches,
                    GetEnemyFootprints(request), request.CanMoveThroughEnemies, request.IgnoresDifficultTerrain,
                    request.IgnoresImpassibleTerrain, _tableState.Terrain.Objects, out var errors))
            {
                Console.WriteLine($"    Invalid: {string.Join(", ", errors.Select(e => e.ToString()))}. Try again.");
                continue;
            }

            return Task.FromResult(entries);
        }
    }

    private List<EnemyModelFootprint> GetEnemyFootprints(ConsolidationMoveRequest request)
    {
        var footprints = new List<EnemyModelFootprint>();
        if (_tableState == null) return footprints;
        int unitKey = 0;
        foreach (var u in _tableState.Units.Objects)
        {
            if (u.PlayerID == request.TargetPlayerID) continue;
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

    private static List<ModelMoveEntry> StayInPlace(List<DataBinding<ModelData>> aliveModels) =>
        aliveModels.Select(mb => new ModelMoveEntry(mb, new List<Position>())).ToList();
}
