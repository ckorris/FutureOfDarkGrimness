using FDG;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;

namespace FdgRaylib.Cli.Resolvers;

public class DefineMovementPathResolver : IStageResolver<DefineMovementPathRequest, List<ModelMoveEntry>>
{
    private readonly ITableState? _tableState;

    public DefineMovementPathResolver(ITableState? tableState = null)
    {
        _tableState = tableState;
    }

    public Task<List<ModelMoveEntry>> Resolve(DefineMovementPathRequest request)
    {
        var unit = request.UnitDataBinding.GetValue();
        var models = unit.ModelBindings;

        while (true)
        {
            Console.WriteLine();
            Console.WriteLine($"--- Move: {unit.Name} ({models.Count} model{(models.Count != 1 ? "s" : "")}) ---");
            Console.WriteLine($"  Advance (≤ {request.MaxAdvanceDistance:F1}\"): move freely, can still shoot afterward");
            Console.WriteLine($"  Rush    (≤ {request.MaxDistanceInches:F1}\"): move farther, but cannot shoot this turn");
            if (models.Count > 1)
            {
                Console.WriteLine($"  Cohesion: each model must end within {GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES:F0}\" (base-to-base) of at least one teammate");
                Console.WriteLine($"            and within {GameWideConstants.MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES:F0}\" of every other model");
            }
            Console.WriteLine("  Enter destination as 'x z' (inches, e.g. '24 18'), or press Enter to leave in place.");
            Console.WriteLine();

            var entries = new List<ModelMoveEntry>();
            bool eof = false;

            for (int i = 0; i < models.Count; i++)
            {
                var modelBinding = models[i];
                var model = modelBinding.GetValue();
                Console.Write($"  Model {i + 1} at ({model.Position.x:F1}\", {model.Position.z:F1}\"): ");
                string? input = Console.ReadLine()?.Trim();

                if (input == null)
                {
                    eof = true;
                    break;
                }

                if (string.IsNullOrEmpty(input))
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

            if (eof)
                return Task.FromResult(AutoAdvance(request));

            if (MovementUtilities.ValidatePaths(entries, request.MaxDistanceInches, out var errors))
                return Task.FromResult(entries);

            Console.WriteLine();
            Console.WriteLine("  Movement is invalid — please re-enter all models:");
            foreach (var err in errors)
                Console.WriteLine($"    ! {MovementUtilities.ErrorReasonToString(err.ErrorReasonType)}");
        }
    }

    // Automatically advance the unit toward the nearest enemy, re-forming the living models into a
    // cohesive grid at the destination. A rigid translate would preserve any hole a casualty left in the
    // formation and be rejected for breaking cohesion (which would crash DefinePathStage), so we re-pack.
    private List<ModelMoveEntry> AutoAdvance(DefineMovementPathRequest request)
    {
        var unit = request.UnitDataBinding.GetValue();
        var living = unit.ModelBindings.Where(mb => mb.GetValue().GetIsAlive()).ToList();
        if (living.Count == 0)
            return StayInPlace(request);

        // Find live enemy model positions via ITableState.
        List<Position> enemyPositions = new();
        if (_tableState != null)
        {
            foreach (var u in _tableState.Units.Objects)
            {
                if (u.PlayerID == request.TargetPlayerID) continue;
                foreach (var m in u.Models)
                    if (m.GetIsAlive()) enemyPositions.Add(m.Position);
            }
        }

        // Compute the living models' centre.
        float cx = living.Average(mb => mb.GetValue().Position.x);
        float cz = living.Average(mb => mb.GetValue().Position.z);

        // No enemy to advance on — re-form in place (closes any casualty hole so the move is legal).
        if (enemyPositions.Count == 0)
            return CohesiveFormation.PackGrid(living, cx, cz);

        Position nearest = enemyPositions
            .OrderBy(p => (p.x - cx) * (p.x - cx) + (p.z - cz) * (p.z - cz))
            .First();

        float dx = nearest.x - cx;
        float dz = nearest.z - cz;
        float dist = MathF.Sqrt(dx * dx + dz * dz);

        if (dist < 0.01f)
            return CohesiveFormation.PackGrid(living, cx, cz);

        // Advance up to MaxAdvanceDistance (tiny margin so float rounding can't disqualify shooting),
        // clamped so the re-pack keeps every model within the movement budget.
        float step = Math.Min(request.MaxAdvanceDistance - 0.001f, Math.Max(0f, dist - 1f));
        step = CohesiveFormation.ClampRepackStep(living, cx, cz, step, request.MaxDistanceInches);
        float ndx = dx / dist;
        float ndz = dz / dist;

        Console.WriteLine($"  [auto] advancing {step:F1}\" toward nearest enemy");

        return CohesiveFormation.PackGrid(living, cx + ndx * step, cz + ndz * step);
    }

    private static List<ModelMoveEntry> StayInPlace(DefineMovementPathRequest request)
    {
        return request.UnitDataBinding.GetValue().ModelBindings
            .Select(mb => new ModelMoveEntry(mb, new List<Position> { mb.GetValue().Position }))
            .ToList();
    }
}
