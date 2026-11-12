using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;

namespace FdgRaylib.Cli.Resolvers;

/// <summary>
/// #029 — CLI resolver for the Aircraft forced move: prompts for a distance in the 30–36" band (EOF/blank →
/// the minimum). If the chosen spot would carry a base off the table, asks for confirmation (EOF → yes, since
/// the piped default must terminate); declining re-prompts the distance.
/// </summary>
public class AircraftAdvanceResolver : IStageResolver<AircraftAdvanceRequest, AircraftAdvanceResult>
{
    public Task<AircraftAdvanceResult> Resolve(AircraftAdvanceRequest request)
    {
        string unitName = request.UnitDataBinding.GetValue().Name;
        Console.WriteLine();
        Console.WriteLine($"{unitName} (Aircraft) must fly {request.MinDistanceInches:0.#}-{request.MaxDistanceInches:0.#}\" " +
            "straight ahead along its heading.");

        while (true)
        {
            Console.Write($"Distance in inches [{request.MinDistanceInches:0.#}]: ");
            string? input = Console.ReadLine()?.Trim();
            bool eof = input == null;

            float distance = request.MinDistanceInches;
            if (!eof && !string.IsNullOrEmpty(input) && float.TryParse(input, out float parsed))
                distance = Math.Clamp(parsed, request.MinDistanceInches, request.MaxDistanceInches);

            var paths = ForcedAircraftMove.BuildPaths(request.UnitDataBinding, request.HeadingNormal, distance);
            if (!ForcedAircraftMove.WouldLeaveTable(paths))
                return Task.FromResult(new AircraftAdvanceResult(distance, false));

            Console.Write("That carries it off the table edge - it leaves play and redeploys from an edge next round. Confirm? [Y/n]: ");
            string? confirm = Console.ReadLine()?.Trim().ToLower();
            if (confirm == null || confirm == "" || confirm == "y" || confirm == "yes")
                return Task.FromResult(new AircraftAdvanceResult(distance, true));
            // Declined — let them pick another distance (if the whole band is off-table, every pick re-asks).
        }
    }
}
