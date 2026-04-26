using FDG;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FdgRaylib.Cli.Resolvers;

/// <summary>
/// CLI movement resolver. Prompts the user for a destination (x, z) per model.
/// Accepts coordinates in inches; the table is 72" wide x 48" deep by default.
/// </summary>
public class DefineMovementPathResolver : IStageResolver<DefineMovementPathRequest, List<ModelMoveEntry>>
{
    public Task<List<ModelMoveEntry>> Resolve(DefineMovementPathRequest request)
    {
        var unit = request.UnitDataBinding.GetValue();
        Console.WriteLine();
        Console.WriteLine($"Move unit '{unit.Name}' (max advance {request.MaxAdvanceDistance:F1}\", max charge {request.MaxChargeDistance:F1}\")");
        Console.WriteLine("Enter destination for each model as: x z  (inches, e.g. '24 18')");
        Console.WriteLine("Press Enter with no input to leave a model in place.");

        var entries = new List<ModelMoveEntry>();
        foreach (var modelBinding in request.UnitDataBinding.GetValue().ModelBindings)
        {
            var model = modelBinding.GetValue();
            Console.Write($"  Model at ({model.Position.x:F1}\", {model.Position.z:F1}\"): ");
            string? input = Console.ReadLine()?.Trim();

            if (string.IsNullOrEmpty(input))
            {
                entries.Add(new ModelMoveEntry(modelBinding, new List<Position> { model.Position }));
                continue;
            }

            string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2 &&
                float.TryParse(parts[0], out float x) &&
                float.TryParse(parts[1], out float z))
            {
                entries.Add(new ModelMoveEntry(modelBinding, new List<Position> { new Position(x, z) }));
            }
            else
            {
                Console.WriteLine("  Could not parse — leaving in place.");
                entries.Add(new ModelMoveEntry(modelBinding, new List<Position> { model.Position }));
            }
        }

        return Task.FromResult(entries);
    }
}
