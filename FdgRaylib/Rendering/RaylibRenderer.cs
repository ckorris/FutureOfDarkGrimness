using System.Collections.Concurrent;
using FDG;
using Raylib_cs;

namespace FdgRaylib.Rendering;

public class RaylibRenderer
{
    private const float Scale = 10f;  // pixels per inch
    private const int Margin = 50;
    private const int TableWIn = (int)GameWideConstants.DEFAULT_TABLE_WIDTH_INCHES;
    private const int TableHIn = (int)GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;

    private static readonly Color TableColor  = new(40, 100, 40, 255);
    private static readonly Color TableBorder = new(20, 60, 20, 255);
    private static readonly Color Background  = new(30, 30, 30, 255);

    private readonly ITableState _tableState;
    private readonly Func<PlayerID, Color> _colorForPlayer;

    // Only models that have had SetPosition called at least once (i.e. deployed).
    private readonly ConcurrentDictionary<IModel, Color> _placedModels = new();

    public RaylibRenderer(ITableState tableState, Func<PlayerID, Color> colorForPlayer)
    {
        _tableState = tableState;
        _colorForPlayer = colorForPlayer;

        tableState.Models.OnObjectCreated += SubscribeToModel;
        foreach (var model in tableState.Models.Objects)
            SubscribeToModel(model);
    }

    private void SubscribeToModel(IModel model)
    {
        model.OnPositionChanged += (_, _) => OnModelPlaced(model);
    }

    private void OnModelPlaced(IModel model)
    {
        var unit = _tableState.Units.Objects.FirstOrDefault(u => u.Models.Contains(model));
        if (unit != null)
            _placedModels[model] = _colorForPlayer(unit.PlayerID);
    }

    public void Run()
    {
        int winW = (int)(TableWIn * Scale) + Margin * 2;
        int winH = (int)(TableHIn * Scale) + Margin * 2;

        Raylib.InitWindow(winW, winH, "Future of Dark Grimness");
        Raylib.SetTargetFPS(30);

        while (!Raylib.WindowShouldClose())
        {
            Raylib.BeginDrawing();
            Raylib.ClearBackground(Background);

            DrawTable();
            DrawModels();

            Raylib.EndDrawing();
        }

        Raylib.CloseWindow();
    }

    private static void DrawTable()
    {
        int tw = (int)(TableWIn * Scale);
        int th = (int)(TableHIn * Scale);
        Raylib.DrawRectangle(Margin, Margin, tw, th, TableColor);
        Raylib.DrawRectangleLines(Margin, Margin, tw, th, TableBorder);
    }

    private void DrawModels()
    {
        foreach (var (model, color) in _placedModels)
        {
            if (!model.GetIsAlive()) continue;

            var pos = model.Position;
            int cx = Margin + (int)(pos.x * Scale);
            // Z=0 is bottom of table; flip so Z=0 is at screen bottom.
            int cy = Margin + (int)((TableHIn - pos.z) * Scale);
            float radius = model.BaseRadiusInches * Scale;

            Raylib.DrawCircle(cx, cy, radius, color);
            Raylib.DrawCircleLines(cx, cy, radius, Color.Black);
        }
    }
}
