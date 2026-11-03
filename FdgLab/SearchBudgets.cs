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
                // #191 2026-09-09: "iters:N" caps the search at N iterations PER WORKER and ignores the
                // clock, so a run is reproducible and a faster box buys speed rather than strength. This is
                // the scale the planned Strategist/Mastermind lobby split is tuned on; the ladder that picks
                // those two numbers benches rungs through this flag. Today's time budgets sit at roughly
                // 58 (benchmark) and 256 (interactive) iterations per worker on a 2k board post-perf.
                if (name.Trim().ToLowerInvariant() is { } iters && iters.StartsWith("iters:")
                    && int.TryParse(iters["iters:".Length..], out int n) && n > 0)
                {
                    budget = UctOptions.Benchmark with { Workers = workers, Iterations = n };
                    label = $"{n} iterations/activation per worker x {workers} workers "
                          + "(deterministic - no clock, so the same seeds replay on any machine)";
                    return true;
                }
                return false;
        }
    }

    public const string KnownNames = "benchmark, interactive, iters:N (N iterations per worker, deterministic)";
}
