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
        {
            string opt = request.ValidOptions[i];
            Console.WriteLine($"  [{i + 1}] {opt}");
            if (request.OptionDescriptions != null
                && request.OptionDescriptions.TryGetValue(opt, out string? desc))
            {
                Console.WriteLine($"        {desc}");
            }
        }

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

            if (input == null) return Task.FromResult(request.ValidOptions[0]); // EOF default: first option

            if (int.TryParse(input, out int choice) &&
                choice >= 1 && choice <= request.ValidOptions.Count)
            {
                return Task.FromResult(request.ValidOptions[choice - 1]);
            }

            Console.WriteLine($"Enter a number between 1 and {request.ValidOptions.Count}.");
        }
    }
}
