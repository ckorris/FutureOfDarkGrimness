using System.Text.Json;
using System.Text.Json.Serialization;

namespace FdgLab;

/// <summary>
/// Loads FdgLab/armies/pool.json (B+C campaign, docs/tactician-bc-campaign.md sec 5): the named
/// generalization panels (points-1k, points-3k, points-4k, shape-2v2) and the held-out pairs
/// excluded from all training data. Data only - all paths inside are repo-root-relative, same
/// convention as --a/--b/--pool.
/// </summary>
public static class Pool
{
    private const string ManifestPath = "FdgLab/armies/pool.json";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    public static IReadOnlyList<Matchup>? LoadPanel(string panelName)
    {
        PoolManifest? manifest = Load();
        if (manifest == null) return null;

        if (!manifest.Panels.TryGetValue(panelName, out List<PanelCell>? cells))
        {
            Console.Error.WriteLine($"--panel: unknown panel '{panelName}'. Known panels: " +
                string.Join(", ", manifest.Panels.Keys.OrderBy(k => k)));
            return null;
        }

        return cells.Select(c => new Matchup(c.SideA, c.SideB)).ToList();
    }

    public static IReadOnlyList<HeldOutEntry>? LoadHeldOut()
    {
        PoolManifest? manifest = Load();
        return manifest?.HeldOut;
    }

    private static PoolManifest? Load()
    {
        if (!File.Exists(ManifestPath))
        {
            Console.Error.WriteLine($"--panel: manifest not found at {ManifestPath} (run fdglab from the repo root).");
            return null;
        }
        string json = File.ReadAllText(ManifestPath);
        PoolManifest? manifest = JsonSerializer.Deserialize<PoolManifest>(json, Options);
        if (manifest == null)
        {
            Console.Error.WriteLine($"--panel: {ManifestPath} deserialized to null.");
            return null;
        }
        return manifest;
    }

    private sealed class PoolManifest
    {
        public List<HeldOutEntry> HeldOut { get; set; } = new();
        public Dictionary<string, List<PanelCell>> Panels { get; set; } = new();
    }

    private sealed class PanelCell
    {
        public List<string> SideA { get; set; } = new();
        public List<string> SideB { get; set; } = new();
    }

    public sealed class HeldOutEntry
    {
        public string Level { get; set; } = "";
        public List<string> SideA { get; set; } = new();
        public List<string> SideB { get; set; } = new();
    }
}
