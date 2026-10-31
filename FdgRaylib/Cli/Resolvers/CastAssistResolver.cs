using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FdgRaylib.Cli.Resolvers;

// #103 — stdin resolver for the cast-assist prompt: how many spell tokens this Caster spends to boost (+1
// each, friendly) or disrupt (-1 each, enemy) another Caster's cast. EOF / blank input defaults to 0
// (spend nothing), so piped/headless games never spend assist tokens unprompted.
public class CastAssistResolver : IStageResolver<CastAssistRequest, int>
{
    public Task<int> Resolve(CastAssistRequest request)
    {
        string assister = request.AssistingUnit.GetValue().Name;
        string caster   = request.CastingUnit.GetValue().Name;
        string verb     = request.IsFriendly ? "help" : "disrupt";
        string sign     = request.IsFriendly ? "+1" : "-1";

        Console.WriteLine();
        Console.WriteLine($"{assister} ({request.AvailableTokens} spell token" +
            $"{(request.AvailableTokens == 1 ? "" : "s")}): spend how many to {verb} {caster}'s cast of " +
            $"{request.SpellName}? ({sign} each)");
        // What the spell does, indented like the spell picker's subtext - the name alone says nothing
        // about whether the cast is worth swaying.
        if (!string.IsNullOrEmpty(request.SpellDescription))
            Console.WriteLine($"        {request.SpellDescription}");

        while (true)
        {
            Console.Write($"Tokens [0-{request.AvailableTokens}] (default 0): ");
            string? input = Console.ReadLine()?.Trim();

            if (input == null)
            {
                Console.WriteLine("(EOF - spending none)");
                return Task.FromResult(0);
            }
            if (string.IsNullOrEmpty(input)) return Task.FromResult(0);
            if (int.TryParse(input, out int n) && n >= 0 && n <= request.AvailableTokens)
                return Task.FromResult(n);

            Console.WriteLine($"Enter a number between 0 and {request.AvailableTokens}.");
        }
    }
}
