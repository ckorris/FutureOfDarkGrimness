using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FdgRaylib.Cli.Resolvers;

public class StringSelectionResolver : IStageResolver<StringSelectionRequest, string>
{
    public Task<string> Resolve(StringSelectionRequest request)
    {
        Console.WriteLine();
        Console.WriteLine(request.Instructions);

        // #317: a companion action belongs to another option's row (melee: hold this weapon back instead
        // of attacking with it), so it is printed UNDER its owner as an "s"-keyed sub-choice rather than
        // as a peer in the numbered list - the CLI's version of the GUI's second button on the row.
        var companionOptions = new HashSet<string>(StringComparer.Ordinal);
        if (request.SecondaryActions != null)
        {
            foreach (var secondary in request.SecondaryActions.Values)
                companionOptions.Add(secondary.Option);
        }

        for (int i = 0; i < request.ValidOptions.Count; i++)
        {
            string opt = request.ValidOptions[i];
            if (companionOptions.Contains(opt)) continue;
            Console.WriteLine($"  [{i + 1}] {opt}");
            if (request.OptionDescriptions != null
                && request.OptionDescriptions.TryGetValue(opt, out string? desc))
            {
                // #298: a description can be several lines (one per weapon rule); indent each so the
                // block stays under its option rather than the second line starting at column 0.
                foreach (string line in desc.Split('\n'))
                    Console.WriteLine($"        {line}");
            }

            if (request.SecondaryActions != null
                && request.SecondaryActions.TryGetValue(opt, out var companion))
            {
                // The short label alone: the companion sits under the option it belongs to, so repeating
                // that option's full text ("Hold back: 3x Blade - A2, AP0") only stutters.
                bool available = request.ValidOptions.Contains(companion.Option);
                if (available)
                {
                    Console.WriteLine($"      [s{i + 1}] {companion.ShortLabel}");
                }
                else
                {
                    string reason = request.InvalidOptions
                        .FirstOrDefault(o => o.Option == companion.Option)?.Reason ?? "not available";
                    Console.WriteLine($"      [--] {companion.ShortLabel} ({reason})");
                }

                // The companion's own subtext (what holding back preserves) belongs under IT, not under
                // the weapon - it describes the decline, not the attack.
                if (request.OptionDescriptions != null
                    && request.OptionDescriptions.TryGetValue(companion.Option, out string? companionDesc))
                {
                    foreach (string line in companionDesc.Split('\n'))
                        Console.WriteLine($"            {line}");
                }
            }
        }

        // Companions already appeared under their owner, so they are not repeated here.
        var otherInvalid = request.InvalidOptions
            .Where(opt => !companionOptions.Contains(opt.Option)).ToList();
        if (otherInvalid.Count > 0)
        {
            Console.WriteLine("  Unavailable:");
            foreach (var opt in otherInvalid)
                Console.WriteLine($"    - {opt.Option} ({opt.Reason})");
        }

        // #248: a cancellable menu (pristine activation's action menu) offers [0] Back, replying null.
        // The EOF default stays the FIRST VALID OPTION, never the cancel — piped/automated runs must not
        // loop the turn by backing out forever.
        if (request.AllowCancel)
            Console.WriteLine("  [0] Back");

        while (true)
        {
            Console.Write("Choice: ");
            string? input = Console.ReadLine()?.Trim();

            // EOF default: first option - and never a companion, which is an opt-OUT (an automated run
            // that declined its weapons every melee would fight with nothing).
            if (input == null)
                return Task.FromResult(request.ValidOptions.First(o => !companionOptions.Contains(o)));

            // #317: "s3" takes option 3's companion action.
            if (input.StartsWith("s", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(input.AsSpan(1), out int ownerNumber)
                && ownerNumber >= 1 && ownerNumber <= request.ValidOptions.Count
                && request.SecondaryActions != null
                && request.SecondaryActions.TryGetValue(request.ValidOptions[ownerNumber - 1],
                    out var chosenCompanion)
                && request.ValidOptions.Contains(chosenCompanion.Option))
            {
                return Task.FromResult(chosenCompanion.Option);
            }

            if (int.TryParse(input, out int choice))
            {
                if (request.AllowCancel && choice == 0) return Task.FromResult<string>(null!);
                if (choice >= 1 && choice <= request.ValidOptions.Count
                    && !companionOptions.Contains(request.ValidOptions[choice - 1]))
                {
                    return Task.FromResult(request.ValidOptions[choice - 1]);
                }
            }

            string companionHint = companionOptions.Count > 0 ? ", sN for an option's second action" : "";
            Console.WriteLine(request.AllowCancel
                ? $"Enter a number between 1 and {request.ValidOptions.Count}{companionHint}, or 0 to go back."
                : $"Enter a number between 1 and {request.ValidOptions.Count}{companionHint}.");
        }
    }
}
