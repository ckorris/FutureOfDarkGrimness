using FDG.Ai.Tactician.Search;

namespace FdgLab;

/// <summary>
/// The lab's named search budgets (#191 step 10's --search-budget, step 12b's per-mix-entry
/// searchBudget). One parser so a bench flag and a mix file cannot drift into meaning different
/// things by the same name. Worker count is a separate knob because root parallelism is an ensemble
/// over determinizations (design sec 6): changing it benchmarks a different bot, so callers name it
/// explicitly and the default matches lobby play (4).
/// </summary>
public static class SearchBudgets
{
    public const int DefaultWorkers = 4;

    public static bool TryParse(string name, int workers, out UctOptions? budget, out string? label)
    {
        budget = null;
        label = null;
        switch (name.Trim().ToLowerInvariant())
        {
            case "benchmark":
                budget = UctOptions.Benchmark with { Workers = workers };
                label = "benchmark (1-2s/activation)";
                return true;
            case "interactive":
                budget = UctOptions.Interactive with { Workers = workers };
                label = "interactive (5-10s/activation - the budget that ships to players)";
                return true;
            default:
                return false;
        }
    }

    public const string KnownNames = "benchmark, interactive";
}
