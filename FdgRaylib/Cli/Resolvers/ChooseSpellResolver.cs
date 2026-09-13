using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FdgRaylib.Cli.Resolvers;

// #244 — stdin resolver for the spell picker: choose a spell, then how many extra tokens of the caster's
// own to spend boosting the roll (+1 each, on top of the spell's cost). Boost is capped at the affordable
// remainder AND the useful maximum (to the 2+ floor - a natural 1 always fails - plus one per in-range
// enemy hinder token); past the floor extra tokens only hedge against enemy Casters' -1s, so with none in
// range the cap closes. EOF defaults: first castable spell, 0 boost (the old first-option piped behavior).
public class ChooseSpellResolver : IStageResolver<ChooseSpellRequest, ChooseSpellReply>
{
    public Task<ChooseSpellReply> Resolve(ChooseSpellRequest request)
    {
        string caster = request.CastingUnit.GetValue().Name;
        Console.WriteLine();
        Console.WriteLine($"Choose a spell to cast - {caster} has {request.AvailableTokens} spell token" +
            $"{(request.AvailableTokens == 1 ? "" : "s")}");

        // #197 P23 — a Spell Conduit in range changes what is reachable and makes the roll easier. No
        // choice to make here (the origin follows the targets), but the player should know the bonus is on
        // the table before deciding a spell is not worth casting; the target list then says which targets
        // actually get it.
        foreach (ChooseSpellRequest.RelayOption relay in request.RelaysInRange)
        {
            Console.WriteLine($"  {relay.UnitName} relays: spells cast from its position get " +
                $"+{relay.RollBonus} to the roll and measure range from it.");
        }

        // Number only the castable rows; disabled rows print with their reason, un-numbered.
        var castableIndices = new List<int>();
        for (int i = 0; i < request.Spells.Count; i++)
        {
            ChooseSpellRequest.SpellOption option = request.Spells[i];
            if (option.Castable)
            {
                castableIndices.Add(i);
                Console.WriteLine($"  [{castableIndices.Count}] {option.Label}");
            }
            else
            {
                Console.WriteLine($"      {option.Label} (unavailable: {option.UnavailableReason})");
            }
            if (!string.IsNullOrEmpty(option.Description))
                Console.WriteLine($"        {option.Description}");
        }
        Console.WriteLine($"  [{castableIndices.Count + 1}] Cancel");

        int spellIndex = PromptSpell(castableIndices);
        if (spellIndex < 0) return Task.FromResult(ChooseSpellReply.Cancel);

        int boost = PromptBoost(request, request.Spells[spellIndex].Cost);
        return Task.FromResult(new ChooseSpellReply(spellIndex, boost));
    }

    // Returns the chosen request index, or -1 for Cancel. EOF defaults to the first castable spell.
    private static int PromptSpell(IReadOnlyList<int> castableIndices)
    {
        while (true)
        {
            Console.Write("Choice: ");
            string? input = Console.ReadLine()?.Trim();

            if (input == null)
            {
                Console.WriteLine("(EOF - first spell)");
                return castableIndices[0];
            }
            if (int.TryParse(input, out int choice) && choice >= 1 && choice <= castableIndices.Count + 1)
            {
                return choice == castableIndices.Count + 1 ? -1 : castableIndices[choice - 1];
            }
            Console.WriteLine($"Enter a number between 1 and {castableIndices.Count + 1}.");
        }
    }

    // 0..cap boost prompt; EOF / blank spends nothing. Skipped entirely when no boost is possible.
    // The useful cap comes from the request: boost to the 2+ floor (a natural 1 always fails) plus one
    // per in-range enemy hinder token.
    private static int PromptBoost(ChooseSpellRequest request, int cost)
    {
        int affordable = request.AvailableTokens - cost;
        int useful = request.MaxUsefulBoost;
        int cap = Math.Min(affordable, useful);
        if (cap <= 0) return 0;

        Console.WriteLine($"Boost the roll? +1 per extra token ({affordable} affordable).");
        if (request.HinderTokensInRange > 0)
            Console.WriteLine($"  (enemy casters in range hold {request.HinderTokensInRange} token" +
                $"{(request.HinderTokensInRange == 1 ? "" : "s")} - overspending past +{request.BaseThreshold - 2} hedges their -1s)");
        else if (affordable > useful)
            Console.WriteLine($"  (capped at +{useful}: no enemy casters in range, more cannot matter - 2+ is the floor, a 1 always fails)");

        while (true)
        {
            Console.Write($"Boost tokens [0-{cap}] (default 0): ");
            string? input = Console.ReadLine()?.Trim();

            if (input == null)
            {
                Console.WriteLine("(EOF - no boost)");
                return 0;
            }
            if (string.IsNullOrEmpty(input)) return 0;
            if (int.TryParse(input, out int n) && n >= 0 && n <= cap)
                return n;

            Console.WriteLine($"Enter a number between 0 and {cap}.");
        }
    }
}
