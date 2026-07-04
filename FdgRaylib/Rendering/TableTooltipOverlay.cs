using System.Numerics;
using FDG;
using FDG.Players;
using FDG.SaveLoad;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FdgRaylib.Rendering.Resolvers;
using ImGuiNET;
using Raylib_cs;
using TinyDialogsNet;

namespace FdgRaylib.Rendering;

/// <summary>
/// Draws hover tooltips and toggleable unit-name labels on the table canvas.
/// Press L (or click the top-left button) to toggle name labels.
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
    private bool _showAllTokens; // dev toggle (T): reveal Invisible bookkeeping tokens

    // Built from the core catalog so granted-rule tokens get the right valence + description. Custom
    // army-embedded rules (#059) aren't in here and fall back to Neutral/no-description.
    private readonly IRuleResolver _ruleResolver = CoreRuleCatalog.CreateResolver();

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

        if (ImGui.IsKeyPressed(ImGuiKey.T) && !ImGui.GetIO().WantCaptureKeyboard)
            _showAllTokens = !_showAllTokens;

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

        // Unit name labels + token chips. Chips show regardless of the label toggle (status at a glance);
        // only the name text is gated on _showLabels.
        DrawUnitOverlays();

        // Toolbar buttons — anchored top-left, stacked vertically.
        ImGui.SetNextWindowPos(new Vector2(8, 8), ImGuiCond.Always);
        ImGui.SetNextWindowBgAlpha(0.70f);
        ImGui.Begin("##tabletools",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize |
            ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav);

        string btnLabel = _showLabels ? "Labels: ON" : "Labels: OFF";
        if (ImGui.Button(btnLabel))
            _showLabels = !_showLabels;

        if (ImGui.Button(_showAllTokens ? "Tokens: ALL" : "Tokens: std"))
            _showAllTokens = !_showAllTokens;

        if (_saveGameToJson != null)
        {
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

    private void DrawUnitTooltip(IUnit unit, IModel model,
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

        // Transport cargo (#096): occupants ride off-table, so the on-table badge only shows "X/Y" spaces;
        // spell out who's aboard here.
        if (TransportUtilities.IsTransport(unit))
        {
            var allUnits = _tableState!.Units.Objects;
            var occupants = TransportUtilities.GetOccupants(unit, allUnits).ToList();

            ImGui.Spacing();
            ImGui.TextUnformatted(TransportBadgeRenderer.FormatAboardHeader(
                occupants.Count,
                TransportUtilities.GetOccupiedSpaces(unit, allUnits),
                TransportUtilities.GetCapacity(unit)));
            if (occupants.Count > 0)
            {
                ImGui.Indent();
                foreach (var occ in occupants)
                    ImGui.TextUnformatted(TransportBadgeRenderer.FormatOccupant(
                        occ.Name, TransportUtilities.GetUnitSpaceCost(occ)));
                ImGui.Unindent();
            }
        }

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

        // Innate special rules from the army list (the live #042 ResolvedRules). Skip the engine-internal
        // Disembark/Embark abilities that are attached to every unit — they aren't player-facing rules.
        var rules = unit.RuleDefinitions
            .Where(r => r.Definition.Name != CoreRuleCatalog.DisembarkRuleName
                     && r.Definition.Name != CoreRuleCatalog.EmbarkRuleName)
            .ToList();
        if (rules.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextUnformatted("Special Rules:");
            ImGui.Indent();
            foreach (var rule in rules)
            {
                ImGui.TextUnformatted(RuleDisplayName(rule));
                if (!string.IsNullOrEmpty(rule.Definition.Description))
                {
                    ImGui.Indent();
                    ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 300f);
                    ImGui.TextDisabled(rule.Definition.Description);
                    ImGui.PopTextWrapPos();
                    ImGui.Unindent();
                }
            }
            ImGui.Unindent();
        }

        var tokenInfos = TokenChipRenderer.ResolveVisible(unit.Tokens, _ruleResolver, false, _showAllTokens);
        if (tokenInfos.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextUnformatted("Tokens:");
            ImGui.Indent();
            foreach (var ti in tokenInfos)
            {
                ImGui.TextColored(ValenceTint(ti.Valence), ti.Name);
                if (!string.IsNullOrEmpty(ti.Description))
                {
                    ImGui.Indent();
                    ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 300f);
                    ImGui.TextWrapped(ti.Description);
                    ImGui.PopTextWrapPos();
                    ImGui.Unindent();
                }
            }
            ImGui.Unindent();
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

    private void DrawUnitOverlays()
    {
        var drawList = ImGui.GetBackgroundDrawList();
        uint shadow = ImGui.ColorConvertFloat4ToU32(new Vector4(0, 0, 0, 0.75f));
        uint white  = ImGui.ColorConvertFloat4ToU32(new Vector4(1, 1, 1, 1));
        uint cyan   = ImGui.ColorConvertFloat4ToU32(new Vector4(0.55f, 0.85f, 0.95f, 1)); // #096 occupancy badge

        foreach (var unit in _tableState!.Units.Objects)
        {
            float sumX = 0, sumY = 0;
            int   count = 0;
            float minRadiusPx = float.MaxValue;
            float minLeftX = float.MaxValue, maxRightX = float.MinValue;

            foreach (var model in unit.Models)
            {
                if (!model.GetIsAlive()) continue;
                var pos = model.Position;
                if (pos.x == 0f && pos.z == 0f) continue;

                float mx = _originX + pos.x * _scale;
                float my = _originY + (_tableH - pos.z) * _scale;
                float mr = model.BaseRadiusInches * _scale;

                sumX += mx;
                sumY += my;
                minRadiusPx = MathF.Min(minRadiusPx, mr);
                minLeftX    = MathF.Min(minLeftX, mx - mr);
                maxRightX   = MathF.Max(maxRightX, mx + mr);
                count++;

                // Model-scoped tokens sit just above each model (usually none).
                var modelChips = TokenChipRenderer.ResolveVisible(model.Tokens, _ruleResolver, true, _showAllTokens);
                if (modelChips.Count > 0)
                    TokenChipRenderer.DrawChipRow(drawList, modelChips, mx,
                        my - mr - 3f - TokenChipRenderer.RowHeight(modelChips));
            }

            if (count == 0) continue;

            float cx = sumX / count;
            float cy = sumY / count;
            float modelsTop = cy - minRadiusPx;

            // Unit-scoped tokens sit just above the unit, under its name.
            var unitChips = TokenChipRenderer.ResolveVisible(unit.Tokens, _ruleResolver, false, _showAllTokens);
            float chipH = TokenChipRenderer.RowHeight(unitChips);
            float chipTopY = modelsTop - 3f - chipH;
            if (unitChips.Count > 0)
                TokenChipRenderer.DrawChipRow(drawList, unitChips, cx, chipTopY);

            // Top of the name/chips stack — the health bar sits above it.
            float stackTopY = unitChips.Count > 0 ? chipTopY : modelsTop - 3f;

            if (_showLabels)
            {
                Vector2 textSize = ImGui.CalcTextSize(unit.Name);
                float labelX = cx - textSize.X * 0.5f;
                float labelY = stackTopY - textSize.Y - 2f;

                drawList.AddText(new Vector2(labelX + 1, labelY + 1), shadow, unit.Name);
                drawList.AddText(new Vector2(labelX,     labelY),     white,  unit.Name);

                stackTopY = labelY;
            }

            // Transport occupancy badge (#096) — "Carrying X/Y" above the name. Shown for any transport
            // (empty included, so remaining capacity always reads) regardless of the label toggle, like
            // chips/health — status at a glance. Occupants ride off-table, so this is the only on-table cue.
            if (TransportUtilities.IsTransport(unit))
            {
                string badge = TransportBadgeRenderer.FormatBadge(
                    TransportUtilities.GetOccupiedSpaces(unit, _tableState!.Units.Objects),
                    TransportUtilities.GetCapacity(unit));

                Vector2 bSize = ImGui.CalcTextSize(badge);
                float bx = cx - bSize.X * 0.5f;
                float by = stackTopY - bSize.Y - 2f;
                drawList.AddText(new Vector2(bx + 1, by + 1), shadow, badge);
                drawList.AddText(new Vector2(bx,     by),     cyan,   badge);
                stackTopY = by;
            }

            // Health bar above the name (#152) — hidden at full strength.
            var (remainingW, maxW) = HealthBarRenderer.Compute(unit);
            if (HealthBarRenderer.ShouldShow(remainingW, maxW))
                HealthBarRenderer.Draw(drawList, cx, stackTopY - 3f - HealthBarRenderer.Height,
                    maxRightX - minLeftX, remainingW, maxW);
        }
    }

    private static Vector4 ValenceTint(EValence v) => v switch
    {
        EValence.Positive => new Vector4(0.55f, 0.90f, 0.60f, 1f),
        EValence.Negative => new Vector4(0.95f, 0.55f, 0.50f, 1f),
        _                 => new Vector4(0.82f, 0.82f, 0.88f, 1f),
    };

    // The resolved RequestedName already carries any numeric args (army-load formats it "Tough(2)"), so
    // it's used as-is; re-appending Arguments would double them into "Tough(2)(2)".
    private static string RuleDisplayName(ResolvedRule rule) => rule.RequestedName;
}
