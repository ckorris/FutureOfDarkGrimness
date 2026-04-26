using FDG;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;

namespace FdgRaylib.Cli.Resolvers;

public class DefineMovementPathResolver : IStageResolver<DefineMovementPathRequest, List<ModelMoveEntry>>
{
    public Task<List<ModelMoveEntry>> Resolve(DefineMovementPathRequest request)
    {
        var unit = request.UnitDataBinding.GetValue();
        var models = unit.ModelBindings;

        while (true)
        {
            Console.WriteLine();
            Console.WriteLine($"--- Move: {unit.Name} ({models.Count} model{(models.Count != 1 ? "s" : "")}) ---");
            Console.WriteLine($"  Advance (≤ {request.MaxAdvanceDistance:F1}\"): move freely, can still shoot afterward");
            Console.WriteLine($"  Rush    (≤ {request.MaxChargeDistance:F1}\"): move farther, but cannot shoot this turn");
            if (models.Count > 1)
            {
                Console.WriteLine($"  Cohesion: each model must end within {GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES:F0}\" (base-to-base) of at least one teammate");
                Console.WriteLine($"            and within {GameWideConstants.MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES:F0}\" of every other model");
            }
            Console.WriteLine("  Enter destination as 'x z' (inches, e.g. '24 18'), or press Enter to leave in place.");
            Console.WriteLine();

            var entries = new List<ModelMoveEntry>();
            for (int i = 0; i < models.Count; i++)
            {
                var modelBinding = models[i];
                var model = modelBinding.GetValue();
                Console.Write($"  Model {i + 1} at ({model.Position.x:F1}\", {model.Position.z:F1}\"): ");
                string? input = Console.ReadLine()?.Trim();

                if (input == null || string.IsNullOrEmpty(input))
                {
                    entries.Add(new ModelMoveEntry(modelBinding, new List<Position> { model.Position }));
                    continue;
                }

                string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && float.TryParse(parts[0], out float x) && float.TryParse(parts[1], out float z))
                    entries.Add(new ModelMoveEntry(modelBinding, new List<Position> { new Position(x, z) }));
                else
                {
                    Console.WriteLine("    Could not parse — leaving in place.");
                    entries.Add(new ModelMoveEntry(modelBinding, new List<Position> { model.Position }));
                }
            }

            if (MovementUtilities.ValidatePaths(entries, request.MaxChargeDistance, out var errors))
                return Task.FromResult(entries);

            Console.WriteLine();
            Console.WriteLine("  Movement is invalid — please re-enter all models:");
            foreach (var err in errors)
                Console.WriteLine($"    ! {MovementUtilities.ErrorReasonToString(err.ErrorReasonType)}");
        }
    }
}
