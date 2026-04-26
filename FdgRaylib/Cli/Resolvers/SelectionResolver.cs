using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FdgRaylib.Cli.Resolvers;

public class SelectionResolver<T> : IStageResolver<SelectionRequest<T>, DataBinding<T>>
{
    public Task<DataBinding<T>> Resolve(SelectionRequest<T> request)
    {
        Console.WriteLine();
        Console.WriteLine(request.Instructions);

        for (int i = 0; i < request.ValidOptions.Count; i++)
            Console.WriteLine($"  [{i + 1}] {request.ValidOptions[i].Name}");

        if (request.InvalidOptions.Count > 0)
        {
            Console.WriteLine("  Unavailable:");
            foreach (var opt in request.InvalidOptions)
                Console.WriteLine($"    - {opt.Name} ({opt.Reason})");
        }

        while (true)
        {
            Console.Write("Choice: ");
            string? input = Console.ReadLine()?.Trim();
            if (input == null) return Task.FromResult(request.ValidOptions[0].Option); // EOF default: first option
            if (int.TryParse(input, out int choice) &&
                choice >= 1 && choice <= request.ValidOptions.Count)
            {
                return Task.FromResult(request.ValidOptions[choice - 1].Option);
            }
            Console.WriteLine($"Enter a number between 1 and {request.ValidOptions.Count}.");
        }
    }
}
