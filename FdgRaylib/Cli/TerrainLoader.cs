using FDG;
using FDG.SaveLoad;
using Newtonsoft.Json;

namespace FdgRaylib.Cli;

public static class TerrainLoader
{
    private static readonly JsonSerializerSettings _settings = new JsonSerializerSettings
    {
        TypeNameHandling = TypeNameHandling.Auto,
    };

    public static TerrainLayoutFile? TryLoadFromFile(string path, out string? error)
    {
        error = null;
        if (!File.Exists(path))
        {
            error = $"File not found: {path}";
            return null;
        }

        try
        {
            string json = File.ReadAllText(path);
            var layout = JsonConvert.DeserializeObject<TerrainLayoutFile>(json, _settings);
            if (layout == null)
            {
                error = "Deserialized terrain layout was null.";
                return null;
            }
            return layout;
        }
        catch (Exception ex)
        {
            error = $"Failed to load terrain layout: {ex.Message}";
            return null;
        }
    }

    public static string SerializeToJson(TerrainLayoutFile layout)
        => JsonConvert.SerializeObject(layout, Formatting.Indented, _settings);

    /// <summary>
    /// A small built-in layout used when no .fdgterrain file is supplied. Designed to span a
    /// 72"x48" table with mixed terrain types so movement, LoS, and cover stubs have something
    /// non-trivial to query once they're implemented.
    /// </summary>
    public static TerrainLayoutFile BuildTestLayout()
    {
        return new TerrainLayoutFile
        {
            Name = "Test Layout",
            Pieces = new List<TerrainPieceEntry>
            {
                //Center building — blocking + impassible.
                new TerrainPieceEntry
                {
                    TerrainType = ETerrainType.Blocking | ETerrainType.Impassible,
                    Shape = new RectangularZone(33, 39, 22, 26),
                    HeightInches = 4f,
                },
                //Forest, left-center — cover + difficult.
                new TerrainPieceEntry
                {
                    TerrainType = ETerrainType.Cover | ETerrainType.Difficult,
                    Shape = new CircularZone(20, 24, 5),
                    HeightInches = 0f,
                },
                //Forest, right-center — cover + difficult.
                new TerrainPieceEntry
                {
                    TerrainType = ETerrainType.Cover | ETerrainType.Difficult,
                    Shape = new CircularZone(52, 24, 5),
                    HeightInches = 0f,
                },
                //Sandbags near each deployment line — cover.
                new TerrainPieceEntry
                {
                    TerrainType = ETerrainType.Cover,
                    Shape = new RectangularZone(28, 36, 12, 13),
                    HeightInches = 0f,
                },
                new TerrainPieceEntry
                {
                    TerrainType = ETerrainType.Cover,
                    Shape = new RectangularZone(36, 44, 35, 36),
                    HeightInches = 0f,
                },
                //Mine field — dangerous.
                new TerrainPieceEntry
                {
                    TerrainType = ETerrainType.Dangerous,
                    Shape = new RectangularZone(8, 14, 30, 36),
                    HeightInches = 0f,
                },
                //Rubble — difficult.
                new TerrainPieceEntry
                {
                    TerrainType = ETerrainType.Difficult,
                    Shape = new RectangularZone(58, 66, 12, 18),
                    HeightInches = 0f,
                },
            }
        };
    }
}
