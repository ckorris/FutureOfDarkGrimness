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

        // #335: a cancellable selection had no CLI representation at all — the GUI's Back button simply did
        // not exist here, so "deploy normally" was unreachable next to a transport. It is [0], named by the
        // request (usually "Back"), and replies null exactly like the GUI's button does.
        if (request.AllowCancel)
            Console.WriteLine($"  [0] {request.CancelLabel}");

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
            // EOF default stays the first option, cancellable or not: piped/automated play has to make
            // FORWARD progress, and a stage that re-prompts after a cancel would spin forever on EOF.
            if (input == null) return Task.FromResult(request.ValidOptions[0].Option);
            if (int.TryParse(input, out int choice))
            {
                if (request.AllowCancel && choice == 0) return Task.FromResult<DataBinding<T>>(null!);
                if (choice >= 1 && choice <= request.ValidOptions.Count)
                    return Task.FromResult(request.ValidOptions[choice - 1].Option);
            }
            Console.WriteLine(request.AllowCancel
                ? $"Enter a number between 0 and {request.ValidOptions.Count}."
                : $"Enter a number between 1 and {request.ValidOptions.Count}.");
        }
    }
}
