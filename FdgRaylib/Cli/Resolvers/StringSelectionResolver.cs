using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FdgRaylib.Cli.Resolvers;

public class StringSelectionResolver : IStageResolver<StringSelectionRequest, string>
{
    public Task<string> Resolve(StringSelectionRequest request)
    {
        Console.WriteLine();
        Console.WriteLine(request.Instructions);

        for (int i = 0; i < request.ValidOptions.Count; i++)
            Console.WriteLine($"  [{i + 1}] {request.ValidOptions[i]}");

        if (request.InvalidOptions.Count > 0)
        {
            Console.WriteLine("  Unavailable:");
            foreach (var opt in request.InvalidOptions)
                Console.WriteLine($"    - {opt.Option} ({opt.Reason})");
        }

        while (true)
        {
            Console.Write("Choice: ");
            string? input = Console.ReadLine()?.Trim();

            if (int.TryParse(input, out int choice) &&
                choice >= 1 && choice <= request.ValidOptions.Count)
            {
                return Task.FromResult(request.ValidOptions[choice - 1]);
            }

            Console.WriteLine($"Enter a number between 1 and {request.ValidOptions.Count}.");
        }
    }
}
