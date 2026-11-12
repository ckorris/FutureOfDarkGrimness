using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FdgRaylib.Cli.Resolvers;

public class YesNoResolver : IStageResolver<YesNoRequest, bool>
{
    public Task<bool> Resolve(YesNoRequest request)
    {
        while (true)
        {
            Console.WriteLine();
            Console.WriteLine(request.QuestionText);
            Console.Write("[y/n]: ");
            string? input = Console.ReadLine()?.Trim().ToLower();
            if (input == null) return Task.FromResult(request.DefaultAnswer); // EOF: fall back to the request's declared default
            if (input == "y" || input == "yes") return Task.FromResult(true);
            if (input == "n" || input == "no")  return Task.FromResult(false);
            Console.WriteLine("Please enter y or n.");
        }
    }
}
