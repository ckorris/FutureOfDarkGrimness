using System.Numerics;
using FDG;
using FDG.Players;
using ImGuiNET;
using Raylib_cs;

namespace FdgRaylib.Rendering;

/// <summary>
/// Draws hover tooltips and toggleable unit-name labels on the table canvas.
/// Press L to toggle name labels.
/// </summary>
public class TableTooltipOverlay
{
    private ITableState? _tableState;
    private Func<PlayerID, Color>? _colorForPlayer;

    private float _scale;
    private int   _originX;
    private int   _originY;
    private float _tableH;

    private bool _showLabels = false;

    public void Attach(ITableState tableState, Func<PlayerID, Color> colorForPlayer)
    {
        _tableState      = tableState;
        _colorForPlayer  = colorForPlayer;
    }

    public void UpdateLayout(float scale, int originX, int originY, float tableH)
    {
        _scale   = scale;
        _originX = originX;
        _originY = originY;
        _tableH  = tableH;
    }

    public void Draw(int screenW, int screenH)
    {
        if (_tableState == null) return;

        if (ImGui.IsKeyPressed(ImGuiKey.L) && !ImGui.GetIO().WantCaptureKeyboard)
            _showLabels = !_showLabels;

        var mousePos  = ImGui.GetIO().MousePos;
        bool mouseOwned = ImGui.GetIO().WantCaptureMouse;

        // Convert mouse position to table inches
        float mouseTableX = (mousePos.X - _originX) / _scale;
        float mouseTableZ = _tableH - (mousePos.Y - _originY) / _scale;

        IUnit?    hoveredUnit    = null;
        IModel?   hoveredModel   = null;
        ITerrain? hoveredTerrain = null;

        if (!mouseOwned)
        {
            // Hit-test models (closest wins)
            float bestDist = float.MaxValue;
            foreach (var unit in _tableState.Units.Objects)
            {
                foreach (var model in unit.Models)
                {
                    if (!model.GetIsAlive()) continue;
                    var pos = model.Position;
                    if (pos.x == 0f && pos.z == 0f) continue; // not yet placed

                    float dx   = mouseTableX - pos.x;
                    float dz   = mouseTableZ - pos.z;
                    float dist = MathF.Sqrt(dx * dx + dz * dz);
                    if (dist <= model.BaseRadiusInches && dist < bestDist)
                    {
                        bestDist     = dist;
                        hoveredModel = model;
                        hoveredUnit  = unit;
                    }
                }
            }

            // Hit-test terrain if no model hit
            if (hoveredUnit == null)
            {
                foreach (var terrain in _tableState.Terrain.Objects)
                {
                    if (terrain.IsPointWithinZone(new Float2(mouseTableX, mouseTableZ)))
                    {
                        hoveredTerrain = terrain;
                        break;
                    }
                }
            }
        }

        // Draw tooltip
        if (hoveredUnit != null && hoveredModel != null)
            DrawUnitTooltip(hoveredUnit, hoveredModel);
        else if (hoveredTerrain != null)
            DrawTerrainTooltip(hoveredTerrain);

        // Draw unit name labels
        if (_showLabels)
            DrawUnitLabels();
    }

    private void DrawUnitTooltip(IUnit unit, IModel model)
    {
        ImGui.BeginTooltip();

        // Unit name + stats header
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

        // Weapons (deduplicated by name)
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

        // Special rules
        var rules = unit.SpecialRules;
        if (rules.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextUnformatted("Special: " + string.Join(", ", rules.Select(r => r.GetType().Name)));
        }

        ImGui.EndTooltip();
    }

    private static void DrawTerrainTooltip(ITerrain terrain)
    {
        ImGui.BeginTooltip();

        // Build a readable flag list
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
        // Shadow colour (semi-transparent black) and label colour (white)
        uint shadow = ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0.75f));
        uint white  = ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 1));

        foreach (var unit in _tableState!.Units.Objects)
        {
            // Average screen position of placed, living models
            float sumX = 0, sumY = 0;
            int count = 0;
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

            // Place label just above the topmost model circle
            Vector2 textSize = ImGui.CalcTextSize(unit.Name);
            float labelX = cx - textSize.X * 0.5f;
            float labelY = cy - minRadius - textSize.Y - 4f;

            // Drop shadow then white text
            drawList.AddText(new Vector2(labelX + 1, labelY + 1), shadow, unit.Name);
            drawList.AddText(new Vector2(labelX,     labelY),     white,  unit.Name);
        }
    }
}
