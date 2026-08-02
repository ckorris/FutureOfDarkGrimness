using System.Numerics;
using FDG;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FdgRaylib.Rendering.Presentation;
using FdgRaylib.Rendering.Resolvers;
using ImGuiNET;
using Raylib_cs;

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

    // Same animated-position source the model bases and spotlight use, so name labels glide with the
    // models during a move instead of teleporting to the destination (which is the logical model.Position).
    private PresentationPlayer? _presentationPlayer;

    private float _scale;
    private int   _originX;
    private int   _originY;
    private float _tableH;

    // Built from the core catalog so granted-rule tokens get the right valence + description. Custom
    // army-embedded rules (#059) aren't in here and fall back to Neutral/no-description.
    private readonly IRuleResolver _ruleResolver = CoreRuleCatalog.CreateResolver();

    // Capabilities (transport, casting, ...) are answered by the rule graph rather than by testing for a
    // named rule, so the tooltip needs an evaluator to ask. Read-only and non-logging: the dice roller is
    // never reached, since a capability entry's effect rolls nothing.
    private readonly RuleEvaluator _capabilityEvaluator =
        new RuleEvaluator(new ProbabilisticDiceRoller(), ruleResolver: CoreRuleCatalog.CreateResolver());

    public void Attach(ITableState tableState, Func<PlayerID, Color> colorForPlayer,
        PresentationPlayer? presentationPlayer = null)
    {
        _tableState = tableState;
        _presentationPlayer = presentationPlayer;
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

        // Hotkeys are muted while the in-game menu owns input (#246). Toggles live in ViewSettings, shared
        // with the menu's Options panel.
        bool wantKeys = !ImGui.GetIO().WantCaptureKeyboard && !EscapeRouter.MenuOpen;
        if (wantKeys && ImGui.IsKeyPressed(ImGuiKey.L))
            ViewSettings.ShowLabels = !ViewSettings.ShowLabels;

        if (wantKeys && ImGui.IsKeyPressed(ImGuiKey.T))
            ViewSettings.ShowAllTokens = !ViewSettings.ShowAllTokens;

        var hoveredUnit    = hitTester.HoveredUnit;
        var hoveredModel   = hitTester.HoveredModel;
        var hoveredTerrain = hitTester.HoveredTerrain;

        // Route canvas clicks to the active resolver (suppressed while the in-game menu is open, #246).
        if (!EscapeRouter.MenuOpen && hitTester.Clicked && hoveredUnit != null && hoveredModel != null)
            interactionHandler?.HandleClick(hoveredUnit, hoveredModel);

        // Draw tooltip
        if (hoveredUnit != null && hoveredModel != null)
            DrawUnitTooltip(hoveredUnit, hoveredModel, interactionHandler);
        else if (hoveredTerrain != null)
            DrawTerrainTooltip(hoveredTerrain);

        // Range / threat rings for the hovered unit (cyan shoot rings) were REPLACED by the #162
        // team-colored opportunity field, which shows the same weapon-range info in the selected unit's
        // team color while planning a move. Disabled to avoid two overlapping range visualizations.
        // Re-enable this call to bring the passive hover rings back.
        // if (hoveredUnit != null)
        //     DrawRangeRings(hoveredUnit, hoveredModel);

        // Unit name labels + token chips. Chips show regardless of the label toggle (status at a glance);
        // only the name text is gated on ViewSettings.ShowLabels.
        DrawUnitOverlays();
    }

    private void DrawUnitTooltip(IUnit unit, IModel model,
        ICanvasInteractionHandler? interactionHandler)
    {
        ImGui.BeginTooltip();

        // Activation state up top, in red, so a spent unit reads before anything else.
        if (HasActivated(unit))
        {
            ImGui.TextColored(new Vector4(1f, 0.30f, 0.30f, 1f), "Already Activated.");
            ImGui.Separator();
        }

        // Model section first — it sits nearest the cursor, so the hovered model's own weapon(s), its
        // model-specific special rules, and (if Tough) its remaining wounds read before the whole-unit stats.
        DrawModelSection(model);

        // Joined-Hero tag (#227): if the hovered model is the unit's joined hero, call it out with the hero's
        // OWN Quality / Defense (which diverge from the host unit's). The stats live on HeroAttachment, off
        // the concrete UnitData; fall back to a bare "Hero" for any other IUnit impl.
        if (HeroMarkerRenderer.IsHeroModel(unit, model))
        {
            string tag = unit is UnitData { HeroAttachment: { } ha }
                ? HeroMarkerRenderer.FormatHeroTag(ha.Quality, ha.Defense)
                : "Hero";
            ImGui.TextColored(new Vector4(1f, 0.85f, 0.3f, 1f), tag);
        }

        ImGui.Separator();

        ImGui.PushFont(RaylibRenderer.LargeFont);
        ImGui.TextUnformatted(unit.Name);
        ImGui.PopFont();

        ImGui.Separator();
        ImGui.TextUnformatted($"Qua {unit.Quality}+   Def {unit.Defense}+");

        // #287: rounded to hundredths - under the probabilistic roller RemainingWounds is a float chain
        // and bare interpolation printed "Wounds: 8.666667/12".
        ImGui.TextUnformatted($"Wounds: {WoundFormat.Fraction(unit.RemainingWounds, unit.MaxWounds)}");

        if (unit.GetMobility(out float advance, out float charge))
            ImGui.TextUnformatted($"Advance {advance}\"   Charge {charge}\"");

        // Transport cargo (#096): occupants ride off-table, so the on-table badge only shows "X/Y" spaces;
        // spell out who's aboard here.
        if (TransportUtilities.IsTransport(unit, _capabilityEvaluator))
        {
            var allUnits = _tableState!.Units.Objects;
            var occupants = TransportUtilities.GetOccupants(unit, allUnits).ToList();

            ImGui.Spacing();
            ImGui.TextUnformatted(TransportBadgeRenderer.FormatAboardHeader(
                occupants.Count,
                TransportUtilities.GetOccupiedSpaces(unit, allUnits),
                TransportUtilities.GetCapacity(unit, _capabilityEvaluator)));
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
            // Total count of each weapon across the whole unit (e.g. "10x Razor Claws"), so the tooltip
            // shows how many of each the unit fields, not just which distinct types it has.
            foreach (var grp in weapons.GroupBy(w => w.Name))
            {
                var w = grp.First();
                string count = grp.Count() > 1 ? $"{grp.Count()}x " : "";
                string range = w.RangeInches > 0 ? $"{w.RangeInches}\"" : "Melee";
                string ap    = w.ArmorPenetration > 0 ? $" AP{w.ArmorPenetration}" : "";
                string wRules = WeaponStatFormatter.RuleList(w);
                string wRuleSuffix = wRules.Length > 0 ? $"  {wRules}" : "";
                ImGui.TextUnformatted($"{count}{w.Name}  A{w.Attacks}  {range}{ap}{wRuleSuffix}");
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

        var tokenInfos = TokenChipRenderer.ResolveVisible(unit.Tokens, _ruleResolver, false, ViewSettings.ShowAllTokens, unit);
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

    // The section for the specific model under the cursor: the weapon(s) IT carries (matters in mixed units
    // like a joined hero), any rule scoped to just this model (per-model RuleDefinitions — unit-wide rules
    // show in the unit section below), and, if it is Tough, its own remaining wounds.
    private void DrawModelSection(IModel model)
    {
        ImGui.TextDisabled("This model");

        foreach (var grp in model.Weapons.GroupBy(w => w.Name))
        {
            var w = grp.First();
            string count = grp.Count() > 1 ? $"{grp.Count()}x " : "";
            string range = w.RangeInches > 0 ? $"{w.RangeInches}\"" : "Melee";
            string ap    = w.ArmorPenetration > 0 ? $" AP{w.ArmorPenetration}" : "";
            string rules = WeaponStatFormatter.RuleList(w);
            string ruleSuffix = rules.Length > 0 ? $"  {rules}" : "";
            ImGui.TextUnformatted($"{count}{w.Name}  A{w.Attacks}  {range}{ap}{ruleSuffix}");
        }

        var modelRules = model.RuleDefinitions
            .Where(r => r.Definition.Name != CoreRuleCatalog.DisembarkRuleName
                     && r.Definition.Name != CoreRuleCatalog.EmbarkRuleName)
            .ToList();
        foreach (var rule in modelRules)
            ImGui.TextUnformatted(RuleDisplayName(rule));

        // Tough (multi-wound) models show their own remaining wounds; single-wound models need no counter.
        if (model.TotalWounds > 1f)
            ImGui.TextUnformatted(
                $"Wounds: {WoundFormat.Fraction(model.TotalWounds - model.WoundsDealt, model.TotalWounds)}");
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
        // Named pieces (palette entries like "Boulder") lead with the name, matching the placement picker.
        if (!string.IsNullOrWhiteSpace(terrain.Name))
            ImGui.TextUnformatted(terrain.Name);
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
        uint red    = ImGui.ColorConvertFloat4ToU32(new Vector4(0.85f, 0.14f, 0.14f, 1)); // "activated" marker

        foreach (var unit in _tableState!.Units.Objects)
        {
            float sumX = 0, sumY = 0;
            int   count = 0;
            float minRadiusPx = float.MaxValue;
            float minLeftX = float.MaxValue, maxRightX = float.MinValue;

            foreach (var model in unit.Models)
            {
                if (!model.GetIsAlive()) continue;
                // Follow the animated draw position (glide during a move) rather than the logical
                // destination, matching DrawModels / DrawActiveUnitSpotlight. Falls back to the logical
                // position when there is no presentation layer (e.g. tests) or no active override.
                var pos = _presentationPlayer?.GetModelDrawState(model).Position ?? model.Position;
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
                var modelChips = TokenChipRenderer.ResolveVisible(model.Tokens, _ruleResolver, true, ViewSettings.ShowAllTokens, unit);
                if (modelChips.Count > 0)
                    TokenChipRenderer.DrawChipRow(drawList, modelChips, mx,
                        my - mr - 3f - TokenChipRenderer.RowHeight(modelChips));
            }

            if (count == 0) continue;

            float cx = sumX / count;
            float cy = sumY / count;
            float modelsTop = cy - minRadiusPx;

            // Unit-scoped tokens sit just above the unit, under its name.
            var unitChips = TokenChipRenderer.ResolveVisible(unit.Tokens, _ruleResolver, false, ViewSettings.ShowAllTokens, unit);
            float chipH = TokenChipRenderer.RowHeight(unitChips);
            float chipTopY = modelsTop - 3f - chipH;
            if (unitChips.Count > 0)
                TokenChipRenderer.DrawChipRow(drawList, unitChips, cx, chipTopY);

            // Top of the name/chips stack — the health bar sits above it.
            float stackTopY = unitChips.Count > 0 ? chipTopY : modelsTop - 3f;

            if (ViewSettings.ShowLabels)
            {
                Vector2 textSize = ImGui.CalcTextSize(unit.Name);
                float labelX = cx - textSize.X * 0.5f;
                float labelY = stackTopY - textSize.Y - 2f;

                // Activation marker: a red disc with a white "A" just left of the name, so a spent unit
                // reads at a glance. The currently-activating unit already has its own spotlight halo, so
                // it is excluded (see HasActivated) and does not get the marker until its turn ends.
                if (HasActivated(unit))
                {
                    float   radius = textSize.Y * 0.6f;
                    Vector2 center = new Vector2(labelX - 5f - radius, labelY + textSize.Y * 0.5f);

                    drawList.AddCircleFilled(center, radius, red);
                    drawList.AddCircle(center, radius, shadow, 0, 1.5f); // thin outline

                    // "A" from line segments, so it sits geometrically centered in the disc (the text
                    // glyph has uneven side bearings and never quite centers). Apex at top, two legs to
                    // the base corners, and a crossbar a little below middle.
                    float   h    = radius * 1.15f;               // glyph height
                    float   w    = radius * 0.95f;               // glyph width
                    Vector2 apex = new Vector2(center.X, center.Y - h * 0.5f);
                    Vector2 botL = new Vector2(center.X - w * 0.5f, center.Y + h * 0.5f);
                    Vector2 botR = new Vector2(center.X + w * 0.5f, center.Y + h * 0.5f);
                    Vector2 barL = apex + (botL - apex) * 0.62f;
                    Vector2 barR = apex + (botR - apex) * 0.62f;
                    float   thick = MathF.Max(1.4f, radius * 0.2f);
                    drawList.AddLine(apex, botL, white, thick);
                    drawList.AddLine(apex, botR, white, thick);
                    drawList.AddLine(barL, barR, white, thick);
                }

                drawList.AddText(new Vector2(labelX + 1, labelY + 1), shadow, unit.Name);
                drawList.AddText(new Vector2(labelX,     labelY),     white,  unit.Name);

                stackTopY = labelY;
            }

            // Transport occupancy badge (#096) — "Carrying X/Y" above the name. Shown for any transport
            // (empty included, so remaining capacity always reads) regardless of the label toggle, like
            // chips/health — status at a glance. Occupants ride off-table, so this is the only on-table cue.
            if (TransportUtilities.IsTransport(unit, _capabilityEvaluator))
            {
                string badge = TransportBadgeRenderer.FormatBadge(
                    TransportUtilities.GetOccupiedSpaces(unit, _tableState!.Units.Objects),
                    TransportUtilities.GetCapacity(unit, _capabilityEvaluator));

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

    // A unit has spent its activation this round once the main phase is under way (RoundCount != null)
    // and it is no longer in the unactivated pool. The unit currently taking its turn stays in the pool
    // until the turn ends (SingleTurnStage.MarkUnitAsActivated), and is excluded here too so a unit
    // mid-activation does not prematurely read as done. Outside the main phase nothing is activated.
    private bool HasActivated(IUnit unit)
    {
        var progress = _tableState!.Progress;
        if (progress.RoundCount == null) return false;
        if (progress.ActivatingUnit?.ID.Equals(unit.ID) == true) return false;

        foreach (var u in progress.UnactivatedUnits)
            if (u.ID.Equals(unit.ID)) return false;
        return true;
    }

    // Per-model weapon-range and charge-reach rings for the hovered unit, drawn in world space centred on
    // each model — a unit's true reach is the union of its models' circles, not a single centroid sphere.
    // The model under the cursor draws solid + thicker + labelled; the others draw dotted + dimmer so you can
    // read what a specific model can hit while still seeing the whole unit's footprint. Reads only live unit
    // state (weapon ranges, mobility) -- no beats, no request context.
    private static readonly uint ShootRingColor     = U32(0.55f, 0.85f, 0.95f, 0.95f); // cyan  -- shooting threat (hovered)
    private static readonly uint ShootRingColorDim  = U32(0.55f, 0.85f, 0.95f, 0.40f); //         other models (dotted)
    private static readonly uint ChargeRingColor    = U32(0.90f, 0.62f, 0.24f, 0.95f); // amber -- charge reach (hovered)
    private static readonly uint ChargeRingColorDim = U32(0.90f, 0.62f, 0.24f, 0.40f); //         other models (dotted)
    private static readonly uint RingShadow         = U32(0f, 0f, 0f, 0.65f);

    private void DrawRangeRings(IUnit unit, IModel? hoveredModel)
    {
        var dl = ImGui.GetBackgroundDrawList();

        // Unit-level charge distance; each model's charge circle is drawn from its own centre (a joined
        // model with its own budget is a later refinement -- today the unit shares one charge distance).
        float charge = unit.GetMobility(out float _, out float ch) ? ch : 0f;

        foreach (IModel m in unit.Models)
        {
            if (!m.GetIsAlive()) continue;
            Position p = m.Position;
            if (p.x == 0f && p.z == 0f) continue; // unplaced model sits at the origin

            bool hovered = ReferenceEquals(m, hoveredModel);
            float cx = _originX + p.x * _scale;
            float cy = _originY + (_tableH - p.z) * _scale;

            // Attack radii: one circle per distinct weapon range this model carries (melee weapons have no
            // shooting radius -- the charge circle covers that reach).
            foreach (var grp in m.Weapons.Where(w => w.RangeInches > 0f)
                                         .GroupBy(w => w.RangeInches)
                                         .OrderBy(g => g.Key))
            {
                string names = string.Join(" / ", grp.Select(w => w.Name).Distinct());
                DrawModelRing(dl, cx, cy, grp.Key * _scale, ShootRingColor, ShootRingColorDim, hovered,
                    $"{names} {grp.Key:0.#}\"");
            }

            if (charge > 0f)
                DrawModelRing(dl, cx, cy, charge * _scale, ChargeRingColor, ChargeRingColorDim, hovered,
                    $"Charge {charge:0.#}\"");
        }
    }

    private static void DrawModelRing(ImDrawListPtr dl, float cx, float cy, float radiusPx,
        uint solidColor, uint dottedColor, bool hovered, string label)
    {
        if (radiusPx < 2f) return;

        if (!hovered)
        {
            AddDottedCircle(dl, cx, cy, radiusPx, dottedColor, 1.5f);
            return;
        }

        dl.AddCircle(new Vector2(cx, cy), radiusPx, solidColor, 64, 2.5f);

        // Label rides the top of the hovered model's ring (dotted rings stay unlabelled to avoid clutter).
        Vector2 size = ImGui.CalcTextSize(label);
        var at = new Vector2(cx - size.X * 0.5f, cy - radiusPx - size.Y - 2f);
        dl.AddText(at + new Vector2(1, 1), RingShadow, label);
        dl.AddText(at, solidColor, label);
    }

    // ImGui has no native dotted circle -- draw dashes as short line segments around the circumference,
    // rendering every other segment so the gaps read as a dotted outline.
    private static void AddDottedCircle(ImDrawListPtr dl, float cx, float cy, float radiusPx, uint color, float thickness)
    {
        const int segments = 72; // even, so dash/gap pairs stay uniform
        for (int i = 0; i < segments; i += 2)
        {
            float a0 = (i       / (float)segments) * MathF.PI * 2f;
            float a1 = ((i + 1) / (float)segments) * MathF.PI * 2f;
            var p0 = new Vector2(cx + MathF.Cos(a0) * radiusPx, cy + MathF.Sin(a0) * radiusPx);
            var p1 = new Vector2(cx + MathF.Cos(a1) * radiusPx, cy + MathF.Sin(a1) * radiusPx);
            dl.AddLine(p0, p1, color, thickness);
        }
    }

    private static uint U32(float r, float g, float b, float a) =>
        ImGui.ColorConvertFloat4ToU32(new Vector4(r, g, b, a));

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
