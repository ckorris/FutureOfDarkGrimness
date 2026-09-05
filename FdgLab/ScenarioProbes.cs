using System.Text.Json;
using FDG;
using FDG.Ai.Tactician.Search;
using FDG.Data;
using FDG.SaveLoad;
using FDG.Simulation;

namespace FdgLab;

/// <summary>
/// Scenario-based decision probes (#191 campaign step 10, plan sec 6.2). The harness the
/// 2026-07-11 handoff item 1 specced and never built - step 10 builds it because two probes
/// (last-round-steal, charge-vs-shoot) gate the B-merge. Each probe is a <see cref="ScenarioCompiler"/>
/// JSON (Scenarios/README.md format) under the probes directory, plus a sidecar
/// "&lt;name&gt;.expect.json" naming the unit and the top-level action ("Move"/"Charge"/"Shoot"/
/// "Cast"/"Pass" - <c>ChooseActionStage</c>'s vocabulary, or "*" when only the UNIT matters, e.g.
/// the last-round tempo probes where the expected pick is "the irrelevant unit, doing anything")
/// expected for that unit's very next activation. The scenario compiles to exactly the boundary <c>StrategistActivationResolver</c>
/// snapshots at (<c>DeterminePlayerTurnStage.Enter</c>), so this drives the real B5 search
/// (<see cref="UctSearch"/>) on the compiled position, not a plain-policy shortcut.
/// </summary>
public static class ScenarioProbes
{
    private sealed record Expectation(string UnitNameContains, string Action, string? Note = null);

    public static async Task<int> RunAsync(string dir, UctOptions options)
    {
        if (!Directory.Exists(dir))
        {
            Console.WriteLine($"0 probe(s) found - no directory at {dir}.");
            return 0;
        }
        string[] scenarioFiles = Directory.GetFiles(dir, "*.json")
            .Where(p => !p.EndsWith(".expect.json", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToArray();
        if (scenarioFiles.Length == 0)
        {
            Console.WriteLine($"0 probe(s) found in {dir}.");
            return 0;
        }

        var evaluator = new HandWeightedEvaluator();
        int failed = 0;
        Console.WriteLine($"Scenario probes: {scenarioFiles.Length} found in {dir}.");
        foreach (string scenarioPath in scenarioFiles)
        {
            string name = Path.GetFileNameWithoutExtension(scenarioPath);
            string expectPath = Path.Combine(dir, name + ".expect.json");
            if (!File.Exists(expectPath))
            {
                Console.WriteLine($"  SKIP  {name}: no {name}.expect.json");
                continue;
            }

            Expectation? expect;
            try
            {
                expect = JsonSerializer.Deserialize<Expectation>(File.ReadAllText(expectPath),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  FAIL  {name}: {name}.expect.json - {ex.Message}");
                failed++;
                continue;
            }
            if (expect == null)
            {
                Console.WriteLine($"  FAIL  {name}: {name}.expect.json parsed to null");
                failed++;
                continue;
            }

            GameDataStore store;
            try
            {
                store = ScenarioCompiler.CompileFromFile(scenarioPath);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"  FAIL  {name}: scenario compile error - {ex.Message}");
                failed++;
                continue;
            }

            string snapshot = SimulationService.Snapshot(store);
            SearchResult result = await UctSearch.RunAsync(snapshot, options, evaluator);
            if (result.Choice is not { } choice || choice.Prescription.Unit is not { } unitRef)
            {
                Console.WriteLine($"  FAIL  {name}: search returned no choice ({result.Note})");
                failed++;
                continue;
            }

            string unitName = store.GetDataBinding<UnitData>(unitRef).GetValue().Name;
            bool unitOk = unitName.Contains(expect.UnitNameContains, StringComparison.OrdinalIgnoreCase);
            bool actionOk = expect.Action == "*"
                || string.Equals(choice.Prescription.Action, expect.Action, StringComparison.Ordinal);
            bool pass = unitOk && actionOk;
            if (!pass) failed++;
            Console.WriteLine($"  {(pass ? "PASS" : "FAIL")}  {name}: expected [{expect.UnitNameContains}] -> " +
                $"{expect.Action}, got [{unitName}] -> {choice.Prescription.Action} " +
                $"({choice.Visits} visits, {result.Iterations} iterations)" +
                (expect.Note != null ? $" | {expect.Note}" : ""));
            if (!pass)
            {
                foreach (RootEdgeStat edge in result.Root.OrderByDescending(e => e.Visits).Take(6))
                    Console.WriteLine($"        {edge.Visits,6} visits  value={edge.Value:F3}  prior={edge.Prior:F3}  {edge.Label}");
            }
        }

        Console.WriteLine($"{scenarioFiles.Length - failed}/{scenarioFiles.Length} probes passed.");
        return failed == 0 ? 0 : 1;
    }
}
