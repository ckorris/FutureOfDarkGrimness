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
        Console.WriteLine();
        Console.WriteLine($"=== Place terrain piece {request.PiecesPlaced + 1} of {request.TotalPieces} ===");
        for (int i = 0; i < request.Pool.Count; i++)
        {
            var entry = request.Pool[i];
            Console.WriteLine($"  [{i}] {entry.TerrainType}  {DescribeShape(entry.Shape)}");
        }
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
            float rot = 0f;
            if (parts.Length >= 3) float.TryParse(parts[2], out rot);
            return Task.FromResult(new TerrainPlacementResult(idx, new Float2(x, z), rot));
        }

        Console.WriteLine("Could not parse input; using fallback placement.");
        return Task.FromResult(EofFallback(request));
    }

    private TerrainPlacementResult EofFallback(PlaceOneTerrainRequest request)
    {
        const float Step = 2f;
        var existing = _tableState.Terrain.Objects.ToList();

        for (int idx = 0; idx < request.Pool.Count; idx++)
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
