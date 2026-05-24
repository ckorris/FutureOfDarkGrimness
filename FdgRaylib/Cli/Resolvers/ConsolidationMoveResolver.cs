using FDG;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;

namespace FdgRaylib.Cli.Resolvers;

public class ConsolidationMoveResolver : IStageResolver<ConsolidationMoveRequest, List<ModelMoveEntry>>
{
    public Task<List<ModelMoveEntry>> Resolve(ConsolidationMoveRequest request)
    {
        var unit = request.UnitDataBinding.GetValue();
        var aliveModels = unit.ModelBindings.Where(m => m.GetValue().GetIsAlive()).ToList();

        while (true)
        {
            Console.WriteLine();
            Console.WriteLine($"--- Consolidate: {unit.Name} ({request.Reason}, ≤ {request.MaxDistanceInches:F1}\") ---");
            Console.WriteLine($"  Enter a unit-wide offset as 'dx dz' in inches (e.g. '0 -1'), or press Enter to stay in place.");

            string? input = Console.ReadLine()?.Trim();

            if (input == null || string.IsNullOrEmpty(input))
                return Task.FromResult(StayInPlace(aliveModels));

            string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !float.TryParse(parts[0], out float dx) || !float.TryParse(parts[1], out float dz))
            {
                Console.WriteLine("    Could not parse — try again.");
                continue;
            }

            float dist = MathF.Sqrt(dx * dx + dz * dz);
            if (dist > request.MaxDistanceInches + 0.0001f)
            {
                Console.WriteLine($"    Offset is {dist:F2}\" — exceeds the {request.MaxDistanceInches:F1}\" cap. Try again.");
                continue;
            }

            var entries = aliveModels.Select(mb =>
            {
                var m = mb.GetValue();
                return new ModelMoveEntry(mb, new List<Position> { new Position(m.Position.x + dx, m.Position.z + dz) });
            }).ToList();
            return Task.FromResult(entries);
        }
    }

    private static List<ModelMoveEntry> StayInPlace(List<DataBinding<ModelData>> aliveModels) =>
        aliveModels.Select(mb => new ModelMoveEntry(mb, new List<Position>())).ToList();
}
