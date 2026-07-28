using FDG;
using FDG.SaveLoad;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;

namespace FdgRaylib.Cli.Resolvers;

/// <summary>
/// CLI terrain placement: lists the pool, parses <c>&lt;template_index&gt; &lt;x&gt;,&lt;z&gt;</c>
/// from stdin. EOF defaults to a deterministic grid search for the first legal
/// position of the first template, so piped headless runs don't deadlock.
/// </summary>
public class PlaceOneTerrainResolver : IStageResolver<PlaceOneTerrainRequest, TerrainPlacementResult>
{
    private readonly ITableState _tableState;

    public PlaceOneTerrainResolver(ITableState tableState)
    {
        _tableState = tableState;
    }

    public Task<TerrainPlacementResult> Resolve(PlaceOneTerrainRequest request)
    {
        TerrainPointsBudget? budget = request.PointsBudget;

        Console.WriteLine();
        if (budget != null)
        {
            // #299 Alternating: Points - the header carries the pre-dealt personal total and this
            // turn's remaining spend; the copy comes from the budget so it matches the GUI exactly.
            Console.WriteLine($"=== Place terrain: {budget.PointsSummaryLine} ===");
            Console.WriteLine($"    {budget.TurnSummaryLine}");
            if (budget.DebtNoticeLine is string debtLine)
                Console.WriteLine($"    WARNING: {debtLine}");
        }
        else
        {
            Console.WriteLine($"=== Place terrain piece {request.PiecesPlaced + 1} of {request.TotalPieces} ===");
        }

        for (int i = 0; i < request.Pool.Count; i++)
        {
            var entry = request.Pool[i];
            // #268: lead with the piece's name when it has one; unnamed layouts read as before.
            string label = string.IsNullOrWhiteSpace(entry.Name)
                ? $"{entry.TerrainType}"
                : $"{entry.Name} - {entry.TerrainType}";

            string pointsSuffix = "";
            if (budget != null)
            {
                int cost = TerrainPointsBudget.CostOf(entry);
                var verdict = budget.Evaluate(cost);
                pointsSuffix = $"  [{TerrainPointsBudget.Pts(cost)}]";
                if (!verdict.Playable)
                    pointsSuffix += $"  (unavailable: {verdict.BlockedReason})";
                else if (verdict.WarningText is string warning)
                    pointsSuffix += $"  (warning: {warning})";
            }

            Console.WriteLine($"  [{i}] {label}  {DescribeShape(entry.Shape)}{pointsSuffix}");
        }

        while (true)
        {
            Console.Write("Enter <template_index> <x>,<z> [rotation_deg]: ");

            string? input = Console.ReadLine();
            if (input == null)
            {
                // EOF: deterministic fallback so piped tests progress.
                return Task.FromResult(EofFallback(request));
            }

            var parts = input.Trim().Split(' ', 3, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2
                && int.TryParse(parts[0], out int idx)
                && idx >= 0 && idx < request.Pool.Count
                && TryParseXZ(parts[1], out float x, out float z))
            {
                if (budget != null)
                {
                    var verdict = budget.Evaluate(TerrainPointsBudget.CostOf(request.Pool[idx]));
                    if (!verdict.Playable)
                    {
                        // Unlike a parse failure (fallback), an unaffordable pick re-prompts: silently
                        // placing a piece the player did not choose would be worse than asking again.
                        Console.WriteLine($"Cannot place that piece: {verdict.BlockedReason}");
                        continue;
                    }
                }

                float rot = 0f;
                if (parts.Length >= 3) float.TryParse(parts[2], out rot);
                return Task.FromResult(new TerrainPlacementResult(idx, new Float2(x, z), rot));
            }

            Console.WriteLine("Could not parse input; using fallback placement.");
            return Task.FromResult(EofFallback(request));
        }
    }

    /// <summary>
    /// #299 - template indices the fallback may pick: with a points budget, playable entries with
    /// debt-free ones first (matching the AI resolver's preference); otherwise the whole pool.
    /// </summary>
    private static IReadOnlyList<int> FallbackCandidates(PlaceOneTerrainRequest request)
    {
        var all = Enumerable.Range(0, request.Pool.Count).ToList();
        if (request.PointsBudget is not TerrainPointsBudget budget) return all;

        var debtFree = new List<int>();
        var withDebt = new List<int>();
        foreach (int i in all)
        {
            var verdict = budget.Evaluate(TerrainPointsBudget.CostOf(request.Pool[i]));
            if (!verdict.Playable) continue;
            (verdict.DebtIncurred == 0 ? debtFree : withDebt).Add(i);
        }

        var candidates = debtFree.Concat(withDebt).ToList();
        return candidates.Count > 0 ? candidates : all;
    }

    private TerrainPlacementResult EofFallback(PlaceOneTerrainRequest request)
    {
        const float Step = 2f;
        var existing = _tableState.Terrain.Objects.ToList();

        foreach (int idx in FallbackCandidates(request))
        {
            IZone template = request.Pool[idx].Shape;
            (float halfW, float halfH) = GetHalfExtents(template);
            for (float x = halfW; x <= request.TableWidthInches - halfW; x += Step)
            {
                for (float y = halfH; y <= request.TableHeightInches - halfH; y += Step)
                {
                    var center = new Float2(x, y);
                    var candidate = TerrainTemplateUtilities.TranslateToCenter(template, center);
                    var validity = TerrainPlacementValidator.Check(
                        candidate, request.TableWidthInches, request.TableHeightInches, existing);
                    if (validity == TerrainPlacementValidity.Valid)
                        return new TerrainPlacementResult(idx, center);
                }
            }
        }

        // No legal placement found — return template 0 at table center; the engine's
        // re-prompt loop will catch it and bring us back here, which is fine.
        return new TerrainPlacementResult(0,
            new Float2(request.TableWidthInches * 0.5f, request.TableHeightInches * 0.5f));
    }

    private static (float halfW, float halfH) GetHalfExtents(IZone zone)
    {
        (float lx, float hx, float ly, float hy) = zone.GetAABB();
        return ((hx - lx) * 0.5f, (hy - ly) * 0.5f);
    }

    private static string DescribeShape(IZone shape)
    {
        if (shape is CircularZone c) return $"circle r={c.Radius:F1}\"";
        (float lx, float hx, float ly, float hy) = shape.GetAABB();
        return $"{hx - lx:F1}\"x{hy - ly:F1}\"";
    }

    private static bool TryParseXZ(string text, out float x, out float z)
    {
        x = z = 0f;
        var split = text.Split(',', 2);
        return split.Length == 2
            && float.TryParse(split[0].Trim(), out x)
            && float.TryParse(split[1].Trim(), out z);
    }
}
