using System.Numerics;
using FDG;
using FDG.Players;
using FDG.SaveLoad;
using FdgRaylib.Rendering.Resolvers;
using ImGuiNET;
using Raylib_cs;
using TinyDialogsNet;

namespace FdgRaylib.Rendering;

/// <summary>
/// Draws hover tooltips and toggleable unit-name labels on the table canvas.
/// Press L (or click the bottom-left button) to toggle name labels.
///
/// Reads hit-test results from TableHitTester (shared with resolvers) and checks
/// the active resolver's ICanvasInteractionHandler to append contextual hover text
/// and route canvas clicks.
/// </summary>
public class TableTooltipOverlay
{
    private ITableState? _tableState;

    private float _scale;
    private int   _originX;
    private int   _originY;
    private float _tableH;

    private bool _showLabels = true;

    // Non-null only on the host (work item #054 will add client-initiated saving); returns the
    // serialized game to write to a .fdgsave file.
    private Func<string?>? _saveGameToJson;

    private static readonly FileFilter SaveFilter = new(
        $"Saved Game (*{GameSaveFile.EXTENSION_WITH_PERIOD})",
        new[] { $"*{GameSaveFile.EXTENSION_WITH_PERIOD}" });

    public void Attach(ITableState tableState, Func<PlayerID, Color> colorForPlayer,
        Func<string?>? saveGameToJson = null)
    {
        _tableState = tableState;
        _saveGameToJson = saveGameToJson;
    }

    public void UpdateLayout(float scale, int originX, int originY, float tableH)
    {
        _scale   = scale;
        _originX = originX;
        _originY = originY;
        _tableH  = tableH;
    }

    public void Draw(int screenW, int screenH,
        TableHitTester hitTester, ICanvasInteractionHandler? interactionHandler)
    {
        if (_tableState == null) return;

        if (ImGui.IsKeyPressed(ImGuiKey.L) && !ImGui.GetIO().WantCaptureKeyboard)
            _showLabels = !_showLabels;

        var hoveredUnit    = hitTester.HoveredUnit;
        var hoveredModel   = hitTester.HoveredModel;
        var hoveredTerrain = hitTester.HoveredTerrain;

        // Route canvas clicks to the active resolver
        if (hitTester.Clicked && hoveredUnit != null && hoveredModel != null)
            interactionHandler?.HandleClick(hoveredUnit, hoveredModel);

        // Draw tooltip
        if (hoveredUnit != null && hoveredModel != null)
            DrawUnitTooltip(hoveredUnit, hoveredModel, interactionHandler);
        else if (hoveredTerrain != null)
            DrawTerrainTooltip(hoveredTerrain);

        // Draw unit name labels
        if (_showLabels)
            DrawUnitLabels();

        // Toolbar button — anchored to bottom-left
        float btnH   = ImGui.GetFontSize() + ImGui.GetStyle().FramePadding.Y * 2;
        float winPad = ImGui.GetStyle().WindowPadding.Y;
        ImGui.SetNextWindowPos(new Vector2(8, screenH - btnH - winPad * 2 - 8), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.70f);
        ImGui.Begin("##tabletools",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize |
            ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav);

        string btnLabel = _showLabels ? "Labels: ON" : "Labels: OFF";
        if (ImGui.Button(btnLabel))
            _showLabels = !_showLabels;

        if (_saveGameToJson != null)
        {
            ImGui.SameLine();
            if (ImGui.Button("Save Game"))
                HandleSaveGame();
        }

        ImGui.End();
    }

    private void HandleSaveGame()
    {
        string? json = _saveGameToJson?.Invoke();
        if (json == null) return;

        var (canceled, path) = TinyDialogs.SaveFileDialog("Save Game", "", SaveFilter);
        if (canceled || string.IsNullOrWhiteSpace(path)) return;

        if (!path.EndsWith(GameSaveFile.EXTENSION_WITH_PERIOD, StringComparison.OrdinalIgnoreCase))
            path += GameSaveFile.EXTENSION_WITH_PERIOD;

        File.WriteAllText(path, json);
    }

    private static void DrawUnitTooltip(IUnit unit, IModel model,
        ICanvasInteractionHandler? interactionHandler)
    {
        ImGui.BeginTooltip();

        ImGui.PushFont(RaylibRenderer.LargeFont);
        ImGui.TextUnformatted(unit.Name);
        ImGui.PopFont();

        ImGui.Separator();
        ImGui.TextUnformatted($"Qua {unit.Quality}+   Def {unit.Defense}+");

        float wounds = unit.RemainingWounds;
        float maxW   = unit.MaxWounds;
        ImGui.TextUnformatted($"Wounds: {wounds}/{maxW}");

        if (unit.GetMobility(out float advance, out float charge))
            ImGui.TextUnformatted($"Advance {advance}\"   Charge {charge}\"");

        var weapons = unit.AllWeapons();
        if (weapons.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextUnformatted("Weapons:");
            ImGui.Indent();
            foreach (var w in weapons.DistinctBy(w => w.Name))
            {
                string range = w.RangeInches > 0 ? $"{w.RangeInches}\"" : "Melee";
                string ap    = w.ArmorPenetration > 0 ? $" AP{w.ArmorPenetration}" : "";
                ImGui.TextUnformatted($"{w.Name}  A{w.Attacks}  {range}{ap}");
            }
            ImGui.Unindent();
        }

        var rules = unit.SpecialRules;
        if (rules.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextUnformatted("Special: " + string.Join(", ", rules.Select(r => r.GetType().Name)));
        }

        // Contextual line from the active resolver
        string? hoverLabel = interactionHandler?.GetHoverLabel(unit, model);
        if (hoverLabel != null)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextUnformatted(hoverLabel);
        }

        ImGui.EndTooltip();
    }

    private static void DrawTerrainTooltip(ITerrain terrain)
    {
        ImGui.BeginTooltip();

        var flags = new List<string>();
        foreach (ETerrainType flag in Enum.GetValues<ETerrainType>())
        {
            if (flag == ETerrainType.None) continue;
            if (terrain.TerrainType.HasFlag(flag))
                flags.Add(flag.ToString());
        }

        string typeLine = flags.Count > 0 ? string.Join(", ", flags) : "None";
        ImGui.TextUnformatted($"Terrain: {typeLine}");
        if (terrain.HeightInches > 0f)
            ImGui.TextUnformatted($"Height: {terrain.HeightInches}\"");

        ImGui.EndTooltip();
    }

    private void DrawUnitLabels()
    {
        var drawList = ImGui.GetBackgroundDrawList();
        uint shadow = ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0.75f));
        uint white  = ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 1));

        foreach (var unit in _tableState!.Units.Objects)
        {
            float sumX = 0, sumY = 0;
            int   count = 0;
            float minRadius = float.MaxValue;

            foreach (var model in unit.Models)
            {
                if (!model.GetIsAlive()) continue;
                var pos = model.Position;
                if (pos.x == 0f && pos.z == 0f) continue;

                sumX += _originX + pos.x * _scale;
                sumY += _originY + (_tableH - pos.z) * _scale;
                minRadius = MathF.Min(minRadius, model.BaseRadiusInches * _scale);
                count++;
            }

            if (count == 0) continue;

            float cx = sumX / count;
            float cy = sumY / count;

            Vector2 textSize = ImGui.CalcTextSize(unit.Name);
            float labelX = cx - textSize.X * 0.5f;
            float labelY = cy - minRadius - textSize.Y - 4f;

            drawList.AddText(new Vector2(labelX + 1, labelY + 1), shadow, unit.Name);
            drawList.AddText(new Vector2(labelX,     labelY),     white,  unit.Name);
        }
    }
}
