using System.Globalization;
using FDG.Ai.Tactician.Search;

namespace FdgLab;

/// <summary>
/// The lab's search SHAPE knobs (#191 2026-09-09), the breadth/depth parameters as opposed to the
/// budget in <see cref="SearchBudgets"/>. Four fields, all tuned at B4 on a 20-ITERATION
/// measurement and never revisited since:
/// <list type="bullet">
/// <item><c>c</c>, <c>alpha</c> - progressive widening k(N) = ceil(C * N^alpha). Alpha is the
/// budget-sensitivity exponent: at 0.5 a node's allowed breadth grows as sqrt(visits), which is why
/// the 2026-09-09 iteration sweep found max depth PINNED at 6 from 64 iterations to 512 on both a
/// 3-unit and a 7-unit root - past ~64 iterations the extra budget goes sideways, not down.</item>
/// <item><c>exploration</c> - PUCT weight (UctOptions, not the tree). Lower concentrates visits on
/// the principal variation instead of spreading them; independent of widening.</item>
/// <item><c>continuation</c> - natural activations played after an edge before the child boundary.
/// Raising it buys lookahead in GAME time at the same node count, the only one of the four that
/// deepens the horizon without needing more iterations.</item>
/// </list>
/// Spec syntax mirrors --weights: "c=0.25;alpha=0.3;exploration=0.8;continuation=1", any subset,
/// case-insensitive. An omitted field keeps the shipped default.
/// </summary>
public sealed record SearchShape(float? WideningC = null, float? WideningAlpha = null,
    float? ExplorationC = null, int? Continuation = null)
{
    public static bool TryParse(string spec, out SearchShape? shape, out string? error)
    {
        shape = null;
        error = null;
        var parsed = new SearchShape();
        foreach (string part in spec.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string[] kv = part.Split('=', 2, StringSplitOptions.TrimEntries);
            if (kv.Length != 2)
            {
                error = $"'{part}' is not Key=Value";
                return false;
            }

            string key = kv[0].ToLowerInvariant();
            if (key is "continuation")
            {
                if (!int.TryParse(kv[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out int n) || n < 0)
                {
                    error = $"continuation needs a non-negative integer, got '{kv[1]}'";
                    return false;
                }
                parsed = parsed with { Continuation = n };
                continue;
            }

            if (!float.TryParse(kv[1], NumberStyles.Float, CultureInfo.InvariantCulture, out float v) || v <= 0f)
            {
                error = $"{key} needs a positive number, got '{kv[1]}'";
                return false;
            }

            parsed = key switch
            {
                "c" or "wideningc" => parsed with { WideningC = v },
                "alpha" or "wideningalpha" => parsed with { WideningAlpha = v },
                "exploration" or "explorationc" => parsed with { ExplorationC = v },
                _ => parsed,
            };
            if (key is not ("c" or "wideningc" or "alpha" or "wideningalpha" or "exploration" or "explorationc"))
            {
                error = $"unknown shape key '{kv[0]}' (known: c, alpha, exploration, continuation)";
                return false;
            }
        }

        shape = parsed;
        return true;
    }

    /// <summary>Tree-level knobs. Every UctOptions in a run passes the same shaped SearchOptions.</summary>
    public SearchOptions ApplyTo(SearchOptions tree) => tree with
    {
        WideningC = WideningC ?? tree.WideningC,
        WideningAlpha = WideningAlpha ?? tree.WideningAlpha,
        Continuation = Continuation ?? tree.Continuation,
    };

    /// <summary>Search-level knobs (ExplorationC) plus the tree ones, for a whole UctOptions.</summary>
    public UctOptions ApplyTo(UctOptions options) => options with
    {
        ExplorationC = ExplorationC ?? options.ExplorationC,
        Tree = ApplyTo(options.Tree),
    };

    /// <summary>What the report header prints. Only the fields this shape actually overrides.</summary>
    public string Label
    {
        get
        {
            var parts = new List<string>();
            if (WideningC != null) parts.Add($"C={WideningC.Value.ToString("0.###", CultureInfo.InvariantCulture)}");
            if (WideningAlpha != null) parts.Add($"alpha={WideningAlpha.Value.ToString("0.###", CultureInfo.InvariantCulture)}");
            if (ExplorationC != null) parts.Add($"exploration={ExplorationC.Value.ToString("0.###", CultureInfo.InvariantCulture)}");
            if (Continuation != null) parts.Add($"continuation={Continuation.Value}");
            return parts.Count == 0 ? "shipped defaults" : string.Join(" ", parts);
        }
    }

    public const string Syntax = "c=F;alpha=F;exploration=F;continuation=N (any subset)";
}
