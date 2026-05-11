using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FdgRaylib.Cli.Resolvers;

public class CancellableSelectionResolver<T>
    : IStageResolver<CancellableSelectionRequest<T>, CancellableResult<DataBinding<T>>>
{
    public Task<CancellableResult<DataBinding<T>>> Resolve(CancellableSelectionRequest<T> request)
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

        Console.WriteLine("  [0] Back");

        while (true)
        {
            Console.Write("Choice: ");
            string? input = Console.ReadLine()?.Trim();
            // EOF default: first option (keeps piped-input scripts working).
            if (input == null)
                return Task.FromResult<CancellableResult<DataBinding<T>>>(
                    new Selected<DataBinding<T>>(request.ValidOptions[0].Option));

            if (int.TryParse(input, out int choice))
            {
                if (choice == 0)
                    return Task.FromResult<CancellableResult<DataBinding<T>>>(new Cancelled<DataBinding<T>>());
                if (choice >= 1 && choice <= request.ValidOptions.Count)
                    return Task.FromResult<CancellableResult<DataBinding<T>>>(
                        new Selected<DataBinding<T>>(request.ValidOptions[choice - 1].Option));
            }
            Console.WriteLine($"Enter 0 (Back) or a number between 1 and {request.ValidOptions.Count}.");
        }
    }
}
