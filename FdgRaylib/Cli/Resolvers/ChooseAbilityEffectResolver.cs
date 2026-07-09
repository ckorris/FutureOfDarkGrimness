using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FdgRaylib.Cli.Resolvers;

/// <summary>
/// #197 P5a — "pick one of this rule's effects" on stdin. Mandatory: there is no back-out, because the rule
/// text is "pick one effect", not "you may pick one". EOF resolves to the first option so piped play works.
/// </summary>
public class ChooseAbilityEffectResolver : IStageResolver<ChooseAbilityEffectRequest, int>
{
    public Task<int> Resolve(ChooseAbilityEffectRequest request)
    {
        Console.WriteLine();
        Console.WriteLine($"{request.RuleName}: {request.Instructions}");

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

            Console.WriteLine($"Enter a number between 1 and {request.Options.Count}.");
        }
    }
}
