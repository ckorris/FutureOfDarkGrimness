using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FdgRaylib.Cli.Resolvers;

/// <summary>
/// #197 P5a — "pick one of this rule's effects" on stdin. EOF resolves to the first option so piped play works.
/// #408: when the request allows it, [0] backs out of the activation (un-picks the unit).
/// </summary>
public class ChooseAbilityEffectResolver : IStageResolver<ChooseAbilityEffectRequest, int>
{
    public Task<int> Resolve(ChooseAbilityEffectRequest request)
    {
        Console.WriteLine();
        Console.WriteLine($"{request.RuleName}: {request.Instructions}");

        if (request.AllowCancel)
        {
            Console.WriteLine("  [0] Back");
        }

        for (int i = 0; i < request.Options.Count; i++)
        {
            ChooseAbilityEffectRequest.EffectOption option = request.Options[i];
            Console.WriteLine($"  [{i + 1}] {option.Label}");
            if (!string.IsNullOrEmpty(option.Description))
            {
                Console.WriteLine($"        {option.Description}");
            }
        }

        while (true)
        {
            Console.Write("Choice: ");
            string? input = Console.ReadLine()?.Trim();

            if (input == null) return Task.FromResult(0); // EOF default: the first effect

            if (int.TryParse(input, out int choice) && choice >= 1 && choice <= request.Options.Count)
            {
                return Task.FromResult(choice - 1);
            }

            if (request.AllowCancel && input == "0")
            {
                return Task.FromResult(ChooseAbilityEffectRequest.CANCEL);
            }

            Console.WriteLine($"Enter a number between {(request.AllowCancel ? 0 : 1)} and {request.Options.Count}.");
        }
    }
}
