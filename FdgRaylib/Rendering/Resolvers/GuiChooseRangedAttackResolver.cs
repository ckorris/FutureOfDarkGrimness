using System.Numerics;
using FDG;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using ImGuiNET;
using static FDG.StageResolution.Requests.ChooseRangedAttackRequest;

namespace FdgRaylib.Rendering.Resolvers;

public class GuiChooseRangedAttackResolver
    : IStageResolver<ChooseRangedAttackRequest, CancellableResult<RangedAttackChoice>>, IGuiResolver, IGuiCanvasOverlay,
      ICanvasInteractionHandler
{
    private readonly ITableState _tableState;
    private readonly object _lock = new();
    private ChooseRangedAttackRequest? _request;
    private TaskCompletionSource<CancellableResult<RangedAttackChoice>>? _tcs;

    // Layout — main-thread only
    private float _scale   = 10f;
    private float _tableH  = GameWideConstants.DEFAULT_TABLE_HEIGHT_INCHES;
    private int   _originX, _originY;

    // Selection state — main-thread only
    private ChooseRangedAttackRequest? _lastRequest;
    private int _selectedWeaponIdx  = -1;
    private int _selectedTargetTIdx = -1;

    // Hover state — main-thread only
    // _hoveredOption: set by button hover inside the dialog this frame
    // _canvasHoveredOption: set by GetHoverLabel (called before Draw) this frame
    private (int wIdx, int tIdx) _hoveredOption       = (-1, -1);
    private (int wIdx, int tIdx) _canvasHoveredOption = (-1, -1);

    // #319: "Done shooting" ends the action with weapons still loaded, so it asks first (user sign-off).
    // Main-thread only, cleared with the request in Complete.
    private const string DonePopupTitle = "End the shoot action?";
    private bool _donePopupOpen;

    public GuiChooseRangedAttackResolver(ITableState tableState) => _tableState = tableState;

    public void UpdateLayout(float scale, int originX, int originY, float tableH)
    {
        _scale   = scale;
        _originX = originX;
        _originY = originY;
        _tableH  = tableH;
    }

    public bool HasPendingRequest { get { lock (_lock) return _request != null; } }

    public Task<CancellableResult<RangedAttackChoice>> Resolve(ChooseRangedAttackRequest request)
    {
        var tcs = new TaskCompletionSource<CancellableResult<RangedAttackChoice>>();
        lock (_lock) { _tcs = tcs; _request = request; }
        return tcs.Task;
    }

    // ── ICanvasInteractionHandler ─────────────────────────────────────────────

    public string? GetHoverLabel(IUnit unit, IModel model)
    {
        ChooseRangedAttackRequest? request;
        lock (_lock) { request = _request; }
        if (request == null || _selectedWeaponIdx < 0) { _canvasHoveredOption = (-1, -1); return null; }

        int tIdx = FindTargetInWeapon(unit, _selectedWeaponIdx, request);
        if (tIdx < 0) { _canvasHoveredOption = (-1, -1); return null; }

        _canvasHoveredOption = (_selectedWeaponIdx, tIdx);
        var ts = request.WeaponOptions[_selectedWeaponIdx].WeaponTargetStats[tIdx];
        if (ts.UnselectableReason != null) return ts.UnselectableReason;
        int canShoot = ts.modelsThatCanShoot.Count;
        if (canShoot == 0) return "Out of range";
        return $"Click to select  ({canShoot} model{(canShoot != 1 ? "s" : "")} in range)";
    }

    public void HandleClick(IUnit unit, IModel model)
    {
        ChooseRangedAttackRequest? request;
        lock (_lock) { request = _request; }
        if (request == null || _selectedWeaponIdx < 0) return;

        int tIdx = FindTargetInWeapon(unit, _selectedWeaponIdx, request);
        if (tIdx < 0) return;
        var ts = request.WeaponOptions[_selectedWeaponIdx].WeaponTargetStats[tIdx];
        if (ts.UnselectableReason != null) return;
        if (ts.modelsThatCanShoot.Count == 0) return;

        _selectedTargetTIdx = tIdx;
    }

    // ── Draw ──────────────────────────────────────────────────────────────────

    public void Draw(int screenW, int screenH)
    {
        ChooseRangedAttackRequest? request;
        TaskCompletionSource<CancellableResult<RangedAttackChoice>>? tcs;
        lock (_lock) { request = _request; tcs = _tcs; }
        if (request == null || tcs == null) return;

        // Auto-select the first selectable weapon on a new request.
        // #237: when that weapon has exactly one fireable target, pre-select it too - the player only
        // has to press Fire. Never auto-fires: the commit stays a deliberate click/Enter.
        // #308: a shoot action's later weapons start aimed where the last one fired, when that target is
        // still fireable - it beats the sole-target rule, which is the weaker guess of the two.
        if (!ReferenceEquals(request, _lastRequest))
        {
            _lastRequest        = request;
            _selectedWeaponIdx  = FirstFireableWeaponIndex(request.WeaponOptions);
            _selectedTargetTIdx = _selectedWeaponIdx >= 0
                ? PreferredTargetIndex(request.WeaponOptions[_selectedWeaponIdx], request.PreviousTarget) : -1;
            _donePopupOpen      = false;
        }

        // #248 keyboard: Left/Right cycle the weapon (among fireable ones), Up/Down + number keys pick
        // the target (among fireable ones, display order). Enter fires via the footer's existing binding.
        // #319: the Done confirmation owns the keyboard while it is up.
        if (!_donePopupOpen) HandleKeyboard(request);

        DrawHoverLines(request);

        // Invisible non-interactive backdrop to anchor z-order
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("##RangedBackdrop",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar |
            ImGuiWindowFlags.NoInputs | ImGuiWindowFlags.NoBackground);
        ImGui.End();

        // Docked into the right-column resolver panel. The narrow column can't fit three side-by-side
        // sub-panels, so Weapon / Target / Details stack top-to-bottom, each an independently scrolling third.
        ImGui.SetNextWindowPos(new Vector2(ResolverPanelLayout.X, ResolverPanelLayout.Y), ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(ResolverPanelLayout.W, ResolverPanelLayout.H), ImGuiCond.Always);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0.13f, 0.13f, 0.18f, 0.97f));
        ImGui.PushStyleVar(ImGuiStyleVar.WindowRounding, 6f);
        string attackerName = request.AttackingUnit.GetValue().Name;
        ImGui.Begin($"Shoot: {attackerName}##RangedDialog",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoBringToFrontOnFocus | ImGuiWindowFlags.NoScrollbar);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();

        float pad       = 8f;
        float rowH      = ResolverPanelLayout.OptionRowHeight();
        float spacingY  = ImGui.GetStyle().ItemSpacing.Y;
        // #288 sizing rule: cost the footer first. #319 made it two rows - Fire on top, the
        // Back/Done + Hold fire pair under it - so the sections must give up a row's worth of height.
        float footerH   = rowH * 2 + spacingY + pad * 2;
        // Three stacked sections share the vertical space above the footer (two gaps between them).
        float sectionH  = (ImGui.GetContentRegionAvail().Y - footerH - spacingY * 2f) / 3f;

        (int wIdx, int tIdx) newHovered = (-1, -1);

        // ── Section 1: Weapons ────────────────────────────────────────────────
        ImGui.BeginChild("##WeaponCol", new Vector2(0, sectionH), ImGuiChildFlags.Borders);
        ImGui.TextUnformatted("Weapon");
        ImGui.SameLine();
        ImGui.TextDisabled("(Left/Right)");
        ImGui.Separator();
        // #292: a rule name under the cursor claims the frame's tooltip. Collected across the loop and
        // raised after it, because ImGui allows one tooltip per frame and the rows are drawn by hand.
        bool weaponColHovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows);
        string? ruleTooltip = null;
        for (int wi = 0; wi < request.WeaponOptions.Count; wi++)
        {
            var wo            = request.WeaponOptions[wi];
            bool sel          = _selectedWeaponIdx == wi;
            bool selectableW  = HasAnyFireableTarget(wo);
            float itemH       = ResolverPanelLayout.OptionRowHeight();

            if (!selectableW) ImGui.BeginDisabled(true);

            if (ImGui.Selectable($"##{wi}", sel, ImGuiSelectableFlags.None, new Vector2(0, itemH)))
            {
                if (selectableW)
                {
                    // #237: switching weapons re-applies the sole-target pre-select.
                    if (_selectedWeaponIdx != wi)
                        _selectedTargetTIdx = PreferredTargetIndex(wo, request.PreviousTarget);
                    _selectedWeaponIdx = wi;
                }
            }
            if (ImGui.IsItemHovered())
            {
                int firstTIdx = FindFirstValidTargetInWeapon(wi, request);
                newHovered = firstTIdx >= 0 ? (wi, firstTIdx) : (-1, -1);
            }

            if (!selectableW) ImGui.EndDisabled();

            // BeginDisabled suppresses IsItemHovered, so re-query with AllowWhenDisabled to
            // surface a tooltip explaining why the weapon is unavailable.
            bool rowHovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);
            string? unavailabilityTooltip =
                !selectableW && rowHovered ? DescribeWeaponUnavailability(wo) : null;

            // Overlay text via draw list — no cursor manipulation, no boundary extension.
            var rMin    = ImGui.GetItemRectMin();
            var dl      = ImGui.GetWindowDrawList();
            uint colTxt = selectableW
                ? ImGui.GetColorU32(ImGuiCol.Text)
                : ImGui.ColorConvertFloat4ToU32(new Vector4(0.55f, 0.55f, 0.55f, 1f));
            uint colSub = selectableW
                ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.65f, 0.65f, 0.70f, 1f))
                : ImGui.ColorConvertFloat4ToU32(new Vector4(0.50f, 0.50f, 0.50f, 1f));
            dl.AddText(rMin + new Vector2(4, 2), colTxt, wo.Weapon.Name);
            // #319: a once-per-game weapon says so on its row, in both states - "ONCE PER GAME" while it
            // still has its shot (firing it is irreversible, and that has to be visible BEFORE the click),
            // "SPENT" once it is gone. Amber for the live one, gray for the used one.
            if (wo.LimitedRule != null)
            {
                string badge = wo.LimitedAlreadyFired ? "SPENT" : "ONCE PER GAME";
                uint colBadge = wo.LimitedAlreadyFired
                    ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.55f, 0.55f, 0.55f, 1f))
                    : ImGui.ColorConvertFloat4ToU32(new Vector4(0.95f, 0.72f, 0.25f, 1f));
                dl.AddText(rMin + new Vector2(4 + ImGui.CalcTextSize(wo.Weapon.Name + "  ").X, 2),
                    colBadge, badge);
            }
            // #292: the stat subline is unchanged text, but each special-rule name is now its own
            // underlined, hoverable run explaining what the rule does (the Army Forge treatment). Rule
            // names are tinted brighter than the rest of the subline so they read as "there is more here".
            uint colRule = selectableW
                ? ImGui.ColorConvertFloat4ToU32(new Vector4(0.82f, 0.86f, 0.95f, 1f))
                : colSub;
            string? hoveredRule = RuleHoverText.DrawInline(dl,
                rMin + new Vector2(4, ImGui.GetTextLineHeight() + 4),
                RuleHoverText.WeaponStatLine(wo.Weapon), colSub, colRule, weaponColHovered);

            // A hovered rule name outranks the row's own "why is this grayed out" tooltip: it is the more
            // specific thing the cursor is on, and the unavailability reason is one mouse-move away.
            ruleTooltip ??= hoveredRule ?? unavailabilityTooltip;
        }
        if (ruleTooltip != null) RuleHoverText.ShowTooltip(ruleTooltip);
        ImGui.EndChild();

        // ── Section 2: Targets ────────────────────────────────────────────────
        ImGui.BeginChild("##TargetCol", new Vector2(0, sectionH), ImGuiChildFlags.Borders);
        ImGui.TextUnformatted("Target");
        ImGui.SameLine();
        ImGui.TextDisabled("(Up/Down, 1-9)");
        ImGui.Separator();
        int fireableRowsSeen = 0;   // #248: numbers the fireable rows top-down, matching HandleKeyboard
        if (_selectedWeaponIdx >= 0)
        {
            var wo = request.WeaponOptions[_selectedWeaponIdx];
            // Float the fireable (in-range) targets to the top and sink the out-of-range ones (already
            // grayed) to the bottom, so the player doesn't hunt past unreachable rows. OrderBy is stable,
            // so each group keeps its original order. Everything below still indexes WeaponTargetStats by
            // the ORIGINAL index ti (selection, the Fire button, and canvas hover all key on it) - only the
            // draw order changes.
            var displayOrder = Enumerable.Range(0, wo.WeaponTargetStats.Count)
                .OrderBy(i => wo.WeaponTargetStats[i].modelsThatCanShoot.Count > 0 ? 0 : 1)
                .ToList();
            foreach (int ti in displayOrder)
            {
                var ts             = wo.WeaponTargetStats[ti];
                bool inRange       = ts.modelsThatCanShoot.Count > 0;
                bool ruleBlocked   = ts.UnselectableReason != null;
                bool selectableT   = inRange && !ruleBlocked;
                bool sel           = _selectedTargetTIdx == ti;
                string name        = ts.TargetUnit.GetValue().Name;
                float itemH        = ResolverPanelLayout.OptionRowHeight();

                if (!selectableT) ImGui.BeginDisabled(true);

                if (ImGui.Selectable($"##{ti}", sel, ImGuiSelectableFlags.None, new Vector2(0, itemH)))
                {
                    if (selectableT) _selectedTargetTIdx = ti;
                }
                if (ImGui.IsItemHovered())
                    newHovered = (_selectedWeaponIdx, ti);

                if (!selectableT) ImGui.EndDisabled();

                // Rule-blocked rows still show a tooltip explaining why (BeginDisabled suppresses IsItemHovered,
                // so use AllowWhenDisabled to keep the hover query alive).
                if (ruleBlocked && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                    ImGui.SetTooltip(ts.UnselectableReason);

                // Overlay text via draw list — no cursor manipulation, no boundary extension.
                var rMin    = ImGui.GetItemRectMin();
                var dl      = ImGui.GetWindowDrawList();
                uint colTxt = selectableT
                    ? ImGui.GetColorU32(ImGuiCol.Text)
                    : ImGui.ColorConvertFloat4ToU32(new Vector4(0.55f, 0.55f, 0.55f, 1f));
                // #248: fireable rows advertise their number key ("[1] Warriors").
                string numPrefix = selectableT ? ResolverHotkeys.NumberPrefix(fireableRowsSeen++) : "";
                dl.AddText(rMin + new Vector2(4, 2), colTxt, $"{numPrefix}{name}");

                // #325: the numbers the dice will actually use, right-aligned on the name line so the
                // rows read as a comparable column. Effective values - AP, cover and rule modifiers
                // already folded by the engine's forecast; the Details pane holds the arithmetic.
                // Drawn in the row's own text color, so a grayed row's numbers gray with it.
                if (ts.Forecast != null)
                {
                    string nums = $"Hit {ts.Forecast.HitRollNeeded}+ / Sv {ts.Forecast.SaveRollNeeded}+";
                    var numSize  = ImGui.CalcTextSize(nums);
                    var rMax     = ImGui.GetItemRectMax();
                    float numsX  = rMax.X - numSize.X - 4;
                    // A very long unit name wins the collision; the numbers are one selection away
                    // in the Details pane, an overdraw here is unreadable either way.
                    if (numsX > rMin.X + 4 + ImGui.CalcTextSize($"{numPrefix}{name}").X + 12)
                        dl.AddText(new Vector2(numsX, rMin.Y + 2), colTxt, nums);
                }

                string sub;
                uint colSub;
                if (ruleBlocked)
                {
                    sub    = ts.UnselectableReason!;
                    colSub = ImGui.ColorConvertFloat4ToU32(new Vector4(0.70f, 0.55f, 0.30f, 1f));
                }
                else if (!inRange)
                {
                    sub    = "Out of range";
                    colSub = ImGui.ColorConvertFloat4ToU32(new Vector4(0.70f, 0.35f, 0.35f, 1f));
                }
                else
                {
                    // #158: the denominator is the target's LIVING models — dead ones aren't shootable.
                    int livingTargets = ts.TargetUnit.GetValue().ModelBindings.Count(mb => mb.GetValue().GetIsAlive());
                    sub = $"{ts.modelsThatCanShoot.Count}/{livingTargets} in range";
                    // #042 Blast/Indirect/Takedown: a weapon that ignores cover negates the +1.
                    if (ts.HasCover) sub += wo.IgnoresCover ? ", Cover (ignored)" : ", Cover (+1 Def)";
                    if (wo.IgnoresTerrain) sub += $", ignores LoS ({wo.LineOfSightIgnoreRule})";
                    colSub = ImGui.ColorConvertFloat4ToU32(new Vector4(0.40f, 0.85f, 0.40f, 1f));
                }
                dl.AddText(rMin + new Vector2(4, ImGui.GetTextLineHeight() + 4), colSub, sub);
            }
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.55f, 0.55f, 0.55f, 1f));
            ImGui.TextUnformatted("Select a weapon.");
            ImGui.PopStyleColor();
        }
        ImGui.EndChild();

        // ── Section 3: Details ────────────────────────────────────────────────
        ImGui.BeginChild("##DetailCol", new Vector2(0, sectionH), ImGuiChildFlags.Borders);
        ImGui.TextUnformatted("Details");
        ImGui.Separator();
        if (_selectedWeaponIdx >= 0 && _selectedTargetTIdx >= 0)
        {
            var wo = request.WeaponOptions[_selectedWeaponIdx];
            var ts = wo.WeaponTargetStats[_selectedTargetTIdx];
            var tu = ts.TargetUnit.GetValue();

            ImGui.TextUnformatted(wo.Weapon.GetWeaponNameAndStats());

            // #319: the consequence, not just the rule name. Firing a once-per-game weapon is the most
            // irreversible thing this panel can do, and the player can still walk away from it (Hold fire),
            // so the trade is spelled out at the moment of the decision.
            if (wo.LimitedRule != null)
            {
                ImGui.Spacing();
                ImGui.PushStyleColor(ImGuiCol.Text, wo.LimitedAlreadyFired
                    ? new Vector4(0.70f, 0.70f, 0.70f, 1f) : new Vector4(0.95f, 0.72f, 0.25f, 1f));
                ImGui.TextWrapped(wo.LimitedAlreadyFired
                    ? $"{wo.LimitedRule}: already fired this game - it cannot fire again."
                    : $"{wo.LimitedRule}: firing spends this weapon for the REST OF THE GAME. " +
                      "Hold fire to keep it.");
                ImGui.PopStyleColor();
            }

            // #292: the weapon's rules spelled out, so the player can read what Rending/Deadly actually do
            // without hovering the narrow weapon row. Same descriptions the hover tooltips carry.
            IReadOnlyList<RuleHoverText.Segment> weaponRules = RuleHoverText.RuleSegments(wo.Weapon);
            if (weaponRules.Count > 0)
            {
                ImGui.Spacing();
                ImGui.TextUnformatted("Rules:");
                ImGui.Indent();
                foreach (RuleHoverText.Segment rule in weaponRules)
                {
                    ImGui.TextUnformatted(rule.RuleName!);
                    ImGui.Indent();
                    ImGui.PushTextWrapPos(0f);   // wrap at the pane's right edge
                    ImGui.TextDisabled(rule.IsDocumented ? rule.Description : RuleHoverText.UnknownRuleText);
                    ImGui.PopTextWrapPos();
                    ImGui.Unindent();
                }
                ImGui.Unindent();
            }

            ImGui.Spacing();
            ImGui.Separator();
            ImGui.Spacing();
            ImGui.TextUnformatted($"Target:  {tu.Name}");
            // #325: the arithmetic behind the row's numbers, chip-for-chip identical to what the dice
            // beats will show ("Quality 4+ | Stealth -1 -> 5+"), so the preview teaches the player to
            // read the roll. The target's QUALITY is deliberately gone from this pane: it plays no part
            // in being shot at, and showing it here invited exactly that misreading.
            if (ts.Forecast != null)
            {
                DrawForecastLine("To hit", ts.Forecast.HitTags, ts.Forecast.HitRollNeeded);
                DrawForecastLine("Save", ts.Forecast.SaveTags, ts.Forecast.SaveRollNeeded);
                if (ts.Forecast.Notes != null)
                {
                    ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.85f, 0.75f, 0.30f, 1f));
                    foreach (string note in ts.Forecast.Notes)
                    {
                        ImGui.TextWrapped($"* {note}");
                    }
                    ImGui.PopStyleColor();
                }
            }
            else
            {
                ImGui.TextUnformatted($"Def {tu.Defense}+");
            }
            ImGui.Spacing();

            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.40f, 0.85f, 0.40f, 1f));
            ImGui.TextUnformatted($"{ts.modelsThatCanShoot.Count} model{(ts.modelsThatCanShoot.Count != 1 ? "s" : "")} in range");
            ImGui.PopStyleColor();

            if (ts.modelsWithWeaponThatCannotShoot.Count > 0)
            {
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.80f, 0.45f, 0.45f, 1f));
                ImGui.TextUnformatted($"{ts.modelsWithWeaponThatCannotShoot.Count} out of range");
                ImGui.PopStyleColor();
            }
            if (ts.HasCover)
            {
                // #042/#052 Blast/Indirect/Takedown: show cover as negated, attributed to the rule.
                ImGui.PushStyleColor(ImGuiCol.Text, wo.IgnoresCover
                    ? new Vector4(0.45f, 0.80f, 0.45f, 1f) : new Vector4(0.85f, 0.75f, 0.30f, 1f));
                ImGui.TextWrapped(wo.IgnoresCover
                    ? $"Cover ignored ({wo.CoverIgnoreRule})" : "Cover  +1 to defense roll");
                ImGui.PopStyleColor();
            }
            if (wo.IgnoresTerrain)
            {
                // #042/#052 Indirect/Takedown: this weapon can fire at targets out of line of sight.
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.45f, 0.80f, 0.45f, 1f));
                ImGui.TextWrapped($"Ignores line of sight ({wo.LineOfSightIgnoreRule})");
                ImGui.PopStyleColor();
            }
        }
        else
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.55f, 0.55f, 0.55f, 1f));
            ImGui.TextUnformatted("Select a weapon\nand target.");
            ImGui.PopStyleColor();
        }
        ImGui.EndChild();

        _hoveredOption = newHovered;

        // ── Footer ────────────────────────────────────────────────────────────
        ImGui.SetCursorPosY(ImGui.GetCursorPosY() + pad);

        bool canFire = _selectedWeaponIdx >= 0 && _selectedTargetTIdx >= 0
                    && request.WeaponOptions[_selectedWeaponIdx]
                              .WeaponTargetStats[_selectedTargetTIdx]
                              .UnselectableReason == null
                    && request.WeaponOptions[_selectedWeaponIdx]
                              .WeaponTargetStats[_selectedTargetTIdx]
                              .modelsThatCanShoot.Count > 0;

        float footW   = ImGui.GetContentRegionAvail().X;
        float spacing = ImGui.GetStyle().ItemSpacing.X;

        // Primary: Fire! -- red accent, full width; commits on click or the Confirm key when a
        // weapon+target is chosen. Muted while the Done confirmation is up, so the same Enter press
        // cannot both answer the popup and fire the volley behind it.
        ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(0.65f, 0.20f, 0.20f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.78f, 0.27f, 0.27f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive,  new Vector4(0.55f, 0.16f, 0.16f, 1f));
        if (!canFire) ImGui.BeginDisabled(true);
        bool fireClicked = ImGui.Button($"Fire!  {ResolverKeybinds.Confirm.Parenthetical}##fire", new Vector2(footW, rowH));
        if (!canFire) ImGui.EndDisabled();
        ImGui.PopStyleColor(3);
        // #240: edge-only (repeat: false) so a stuck key can't fire volleys on its own; #248: the
        // shared helper also mutes it while typing or while the in-game menu is open.
        bool fireEnter = canFire && !_donePopupOpen && ResolverHotkeys.IsConfirmPressed();
        if (fireClicked || fireEnter)
        {
            var wo = request.WeaponOptions[_selectedWeaponIdx];
            var ts = wo.WeaponTargetStats[_selectedTargetTIdx];
            Complete(tcs, new Selected<RangedAttackChoice>(new RangedAttackChoice(wo.Weapon, ts.TargetUnit)));
            ImGui.End();
            return;
        }

        // Second row: the exit on the left, Hold fire on the right. #308: the ENGINE decides which exit
        // is legal (the resolver used to keep its own counter and got it wrong on repeat activations);
        // #319 turned the "no exit at all" case into the honestly-labelled "Done shooting".
        float halfW = (footW - spacing) * 0.5f;
        if (request.AllowCancel)
        {
            // Nothing has fired: backing out costs the player nothing, so it needs no confirmation.
            // #248: Backspace backs out too (Esc is reserved for the in-game menu).
            if (ResolverButtons.Deemphasized("Back (Backspace)", new Vector2(halfW, rowH))
                || (!_donePopupOpen && ResolverHotkeys.IsBackPressed()))
            {
                Complete(tcs, new Cancelled<RangedAttackChoice>());
                ImGui.End();
                return;
            }
        }
        else if (request.AllowStopShooting)
        {
            // #319: a weapon has fired, so this ENDS the action - shots the unit still has go unfired.
            // That is the point (a Limited weapon you would rather keep), but it is also irreversible,
            // so it asks first.
            if (ResolverButtons.Deemphasized("Done shooting", new Vector2(halfW, rowH)))
            {
                _donePopupOpen = true;
                ImGui.OpenPopup(DonePopupTitle);
            }
        }
        else
        {
            ImGui.Dummy(new Vector2(halfW, rowH));
        }

        ImGui.SameLine();

        // #319: Hold fire - decline just this weapon. It leaves the shoot action unfired (a Limited
        // weapon keeps its once-per-game shot, a Deadly one stops gating the rest), and the remaining
        // weapons are offered again.
        bool canHoldFire = _selectedWeaponIdx >= 0 && !_donePopupOpen;
        if (!canHoldFire) ImGui.BeginDisabled(true);
        bool holdClicked = ImGui.Button("Hold fire (H)##holdfire", new Vector2(halfW, rowH));
        if (!canHoldFire) ImGui.EndDisabled();
        if (canHoldFire && ImGui.IsItemHovered())
        {
            var wo = request.WeaponOptions[_selectedWeaponIdx];
            ImGui.SetTooltip(wo.LimitedRule != null
                ? $"Do not fire {wo.Weapon.Name} this action - it keeps its {wo.LimitedRule} shot."
                : $"Do not fire {wo.Weapon.Name} this action.");
        }
        if (holdClicked || (canHoldFire && ResolverHotkeys.IsLetterPressed('H')))
        {
            var wo = request.WeaponOptions[_selectedWeaponIdx];
            Complete(tcs, new Selected<RangedAttackChoice>(RangedAttackChoice.HoldFire(wo.Weapon)));
            ImGui.End();
            return;
        }

        if (DrawDoneConfirmation(request, tcs))
        {
            ImGui.End();
            return;
        }

        ImGui.End();

        // Clear canvas hover so it only persists for the single frame after GetHoverLabel set it.
        // (If the mouse is still over a model next frame, GetHoverLabel will set it again before Draw.)
        _canvasHoveredOption = (-1, -1);
    }

    /// <summary>
    /// #319: the "Done shooting" confirmation. Ending the action here gives up every shot the unit has
    /// left this turn, so the popup names them rather than asking an abstract "are you sure?" - and calls
    /// out that a once-per-game weapon is the one thing this KEEPS, since that is usually why the player
    /// is here. Returns true when the resolver has completed (the caller must stop drawing the panel).
    /// </summary>
    private bool DrawDoneConfirmation(ChooseRangedAttackRequest request,
        TaskCompletionSource<CancellableResult<RangedAttackChoice>> tcs)
    {
        if (!_donePopupOpen) return false;

        // Keep the modal request alive across frames (OpenPopup is consumed by the first BeginPopupModal).
        if (!ImGui.IsPopupOpen(DonePopupTitle)) ImGui.OpenPopup(DonePopupTitle);
        var center = ImGui.GetMainViewport().GetCenter();
        ImGui.SetNextWindowPos(center, ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        if (!ImGui.BeginPopupModal(DonePopupTitle, ImGuiWindowFlags.AlwaysAutoResize)) return false;

        var giveUp = WeaponsGivenUpByStopping(request.WeaponOptions);
        var limited = giveUp.Where(wo => wo.LimitedRule != null).ToList();

        ImGui.TextWrapped(giveUp.Count > 0
            ? $"{request.AttackingUnit.GetValue().Name} still has {giveUp.Count} weapon" +
              $"{(giveUp.Count != 1 ? "s" : "")} that can fire this action:"
            : $"{request.AttackingUnit.GetValue().Name} has nothing left that can fire.");
        foreach (var wo in giveUp)
        {
            ImGui.BulletText(wo.LimitedRule != null
                ? $"{wo.Weapon.Name}  ({wo.LimitedRule})"
                : wo.Weapon.Name);
        }

        ImGui.Spacing();
        ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.85f, 0.75f, 0.30f, 1f));
        ImGui.TextWrapped("Ending the shoot action now gives up those shots for this turn.");
        ImGui.PopStyleColor();
        if (limited.Count > 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.45f, 0.80f, 0.45f, 1f));
            ImGui.TextWrapped(limited.Count == 1
                ? $"{limited[0].Weapon.Name} keeps its once-per-game shot for a later turn."
                : "The once-per-game weapons above keep their shots for a later turn.");
            ImGui.PopStyleColor();
        }

        ImGui.Spacing();
        float confirmH = ResolverPanelLayout.OptionRowHeight();
        if (ImGui.Button("End the shoot", new Vector2(150f, confirmH)))
        {
            ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
            Complete(tcs, new Cancelled<RangedAttackChoice>());
            return true;
        }
        ImGui.SameLine();
        if (ImGui.Button("Keep shooting", new Vector2(150f, confirmH)))
        {
            ImGui.CloseCurrentPopup();
            _donePopupOpen = false;
        }
        ImGui.EndPopup();
        return false;
    }

    // #248: keyboard selection. Weapon cycling mirrors a weapon-row click (sole-target pre-select
    // re-applied); target keys move among FIREABLE targets in the same fireable-first display order
    // the rows use, so "[2]" on screen is always number key 2.
    private void HandleKeyboard(ChooseRangedAttackRequest request)
    {
        var fireableWeapons = new List<int>();
        for (int wi = 0; wi < request.WeaponOptions.Count; wi++)
            if (HasAnyFireableTarget(request.WeaponOptions[wi])) fireableWeapons.Add(wi);

        int lr = ResolverHotkeys.HorizontalArrowDelta();
        if (lr != 0 && fireableWeapons.Count > 0)
        {
            int pos = fireableWeapons.IndexOf(_selectedWeaponIdx);
            pos = pos < 0 ? 0 : (pos + lr + fireableWeapons.Count) % fireableWeapons.Count;
            int newW = fireableWeapons[pos];
            if (newW != _selectedWeaponIdx)
            {
                _selectedTargetTIdx = PreferredTargetIndex(request.WeaponOptions[newW], request.PreviousTarget);
                _selectedWeaponIdx  = newW;
            }
        }

        if (_selectedWeaponIdx < 0) return;
        List<int> fireableTargets = FireableTargetsInDisplayOrder(request.WeaponOptions[_selectedWeaponIdx]);
        if (fireableTargets.Count == 0) return;

        int number = ResolverHotkeys.PressedNumberIndex(fireableTargets.Count);
        if (number >= 0) { _selectedTargetTIdx = fireableTargets[number]; return; }

        int ud = ResolverHotkeys.ArrowDelta();
        if (ud != 0)
        {
            int pos = fireableTargets.IndexOf(_selectedTargetTIdx);
            pos = pos < 0 ? (ud > 0 ? 0 : fireableTargets.Count - 1)
                          : (pos + ud + fireableTargets.Count) % fireableTargets.Count;
            _selectedTargetTIdx = fireableTargets[pos];
        }
    }

    // The fireable targets of a weapon, in the same fireable-first stable order the target rows render
    // in (fireable rows float to the top, so this is just "the displayed prefix").
    private static List<int> FireableTargetsInDisplayOrder(WeaponOption wo)
    {
        var result = new List<int>();
        for (int ti = 0; ti < wo.WeaponTargetStats.Count; ti++)
        {
            var ts = wo.WeaponTargetStats[ti];
            if (ts.UnselectableReason == null && ts.modelsThatCanShoot.Count > 0) result.Add(ti);
        }
        return result;
    }

    // #325: one ledger line of the Details pane - the base stat and every modifier as chips, then the
    // effective threshold, or just the number when nothing modifies it ("To hit:  4+"). The chip strings
    // arrive verbatim from the engine's forecast (the roll stages' own composers), never re-derived here.
    private static void DrawForecastLine(string label, List<string>? tags, int threshold)
    {
        if (tags == null)
        {
            ImGui.TextUnformatted($"{label}:  {threshold}+");
            return;
        }
        ImGui.TextWrapped($"{label}:  {string.Join(" | ", tags)}  ->  {threshold}+");
    }

    // ── Canvas line drawing ───────────────────────────────────────────────────

    private void DrawHoverLines(ChooseRangedAttackRequest request)
    {
        // Priority: button hover > canvas hover > current selection
        (int wIdx, int tIdx) effective =
            _hoveredOption.wIdx >= 0       ? _hoveredOption :
            _canvasHoveredOption.wIdx >= 0 ? _canvasHoveredOption :
            (_selectedWeaponIdx, _selectedTargetTIdx);

        if (effective.wIdx < 0 || effective.wIdx >= request.WeaponOptions.Count) return;
        var wo = request.WeaponOptions[effective.wIdx];
        if (effective.tIdx < 0 || effective.tIdx >= wo.WeaponTargetStats.Count) return;

        var ts         = wo.WeaponTargetStats[effective.tIdx];
        var targetUnit = ts.TargetUnit.GetValue();
        var dl         = ImGui.GetBackgroundDrawList();

        uint colorCan    = ImGui.ColorConvertFloat4ToU32(new Vector4(0.20f, 1.00f, 0.20f, 0.80f));
        uint colorTarget = ImGui.ColorConvertFloat4ToU32(new Vector4(1.00f, 0.85f, 0.10f, 0.65f));

        float ringSumX = 0f, ringMinY = float.MaxValue;
        int ringCount = 0;
        foreach (var mb in targetUnit.ModelBindings)
        {
            var m = mb.GetValue();
            // #158: no target rings on corpses (they'd sit at the model's death position) or on
            // never-placed models (they'd ring the table origin).
            if (!m.GetIsAlive()) continue;
            if (m.Position.x == 0f && m.Position.z == 0f) continue;
            var (tx, ty) = InchesToPixel(m.Position.x, m.Position.z);
            // #250: the ring follows the model's true base shape — it used to be a circle while the
            // base-to-base distance label below reads from the real shape, so the two disagreed.
            ModelBaseRenderer.DrawOutlineImGui(dl, m.BaseShape, new Vector2(tx, ty), _scale, colorTarget,
                thickness: 2f, inflateInches: 3f / _scale, facing: m.Facing);
            ringSumX += tx;
            ringMinY = MathF.Min(ringMinY, ty);
            ringCount++;
        }

        // #325: the effective numbers as a badge over the ringed unit, so the player comparing targets
        // by looking at the TABLE (the #286 hover gesture) gets the same glance the row gives. One badge,
        // on the single hovered/selected pairing only - never one per target, which is the noise case.
        if (ts.Forecast != null && ringCount > 0)
        {
            string badge = $"Hit {ts.Forecast.HitRollNeeded}+ / Sv {ts.Forecast.SaveRollNeeded}+";
            var badgeSize = ImGui.CalcTextSize(badge);
            var badgePos = new Vector2(ringSumX / ringCount - badgeSize.X * 0.5f,
                ringMinY - badgeSize.Y - 14f);
            uint badgeBg = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.65f));
            dl.AddRectFilled(badgePos - new Vector2(4, 2), badgePos + badgeSize + new Vector2(4, 2),
                badgeBg, 3f);
            dl.AddText(badgePos, ImGui.ColorConvertFloat4ToU32(new Vector4(1f, 0.92f, 0.70f, 1f)), badge);
        }

        // One line per shooter — from each attacker model that can hit this unit, to the nearest
        // defender it can actually SEE (#313). Aiming at the nearest model outright drew fire lines
        // straight through blocking terrain whenever the closest defender was the blocked one, while
        // the volley itself resolved against a model the shooter could see. The sight test is the
        // engine's own (ShotEligibility, shared with the attack animation's endpoints), so the preview
        // and the shot cannot disagree.
        //
        // No range check is needed here: modelsThatCanShoot already says this shooter can hit the unit,
        // and the nearest VISIBLE defender is necessarily the in-range one (every other visible defender
        // is farther away). A weapon that ignores line of sight (Indirect/Takedown) passes null blockers
        // and so aims at the nearest model, blocked or not — which is exactly what it shoots.
        IReadOnlyList<ITerrain>? blockers = wo.IgnoresTerrain
            ? null
            : ShotEligibility.BuildBlockers(_tableState, request.AttackingUnit.GetValue(), targetUnit);

        uint colorLabel = ImGui.ColorConvertFloat4ToU32(new Vector4(0.85f, 1.00f, 0.85f, 0.95f));
        uint colorLabelBg = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.55f));
        foreach (var ab in ts.modelsThatCanShoot)
        {
            var attacker = ab.GetValue();
            ModelData? nearest = NearestVisibleModel(attacker, targetUnit.ModelBindings, blockers);
            if (nearest == null) continue;
            var (ax, ay) = InchesToPixel(attacker.Position.x, attacker.Position.z);
            var (tx, ty) = InchesToPixel(nearest.Position.x, nearest.Position.z);
            dl.AddLine(new Vector2(ax, ay), new Vector2(tx, ty), colorCan, 1.5f);

            float distInches = DistanceUtilities.GetBaseToBaseDistanceInches_3D(
                attacker.Position, nearest.Position, attacker.BaseShape, attacker.Facing, nearest.BaseShape, nearest.Facing);
            string distText = $"{distInches:F1}\"";
            var textSize = ImGui.CalcTextSize(distText);
            var mid = new Vector2((ax + tx) * 0.5f - textSize.X * 0.5f,
                                  (ay + ty) * 0.5f - textSize.Y * 0.5f);
            dl.AddRectFilled(mid - new Vector2(3, 1), mid + textSize + new Vector2(3, 1),
                colorLabelBg, 2f);
            dl.AddText(mid, colorLabel, distText);
        }
    }

    // Internal for tests. The nearest candidate this shooter can SEE (#313) — pass null blockers for a
    // weapon that ignores line of sight, which reduces it to plain nearest. #158: only LIVING, placed
    // models are candidates — a just-killed model is often the nearest (you shot it last volley), and
    // aiming the shooter line at its corpse read as "shooting at a dead model".
    //
    // A thin adapter over the engine's ShotEligibility, not a second implementation: the shot animation
    // asks the same function, so a line the panel draws is a line the volley would fire.
    internal static ModelData? NearestVisibleModel(ModelData from,
        IReadOnlyList<DataBinding<ModelData>> candidates, IReadOnlyList<ITerrain>? blockers)
    {
        var models = new List<IModel>(candidates.Count);
        foreach (var mb in candidates) models.Add(mb.GetValue());
        return ShotEligibility.NearestVisibleModel(from.Position, from.BaseShape, from.Facing,
            models, blockers) as ModelData;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static int FindTargetInWeapon(IUnit unit, int wIdx, ChooseRangedAttackRequest request)
    {
        var stats = request.WeaponOptions[wIdx].WeaponTargetStats;
        for (int ti = 0; ti < stats.Count; ti++)
            if (stats[ti].TargetUnit.GetValue() == unit) return ti;
        return -1;
    }

    private static int FindFirstValidTargetInWeapon(int wIdx, ChooseRangedAttackRequest request)
    {
        var stats = request.WeaponOptions[wIdx].WeaponTargetStats;
        for (int ti = 0; ti < stats.Count; ti++)
            if (stats[ti].UnselectableReason == null && stats[ti].modelsThatCanShoot.Count > 0) return ti;
        return -1;
    }

    // #237: the first weapon that can actually fire at something, so the auto-selected weapon is never
    // a grayed row. Falls back to 0 (the old behavior) when nothing is fireable, so the panel still
    // shows the first weapon's rows rather than nothing; -1 only when there are no weapons at all.
    // Internal for tests.
    internal static int FirstFireableWeaponIndex(IReadOnlyList<WeaponOption> weaponOptions)
    {
        for (int wi = 0; wi < weaponOptions.Count; wi++)
            if (HasAnyFireableTarget(weaponOptions[wi])) return wi;
        return weaponOptions.Count > 0 ? 0 : -1;
    }

    /// <summary>
    /// #308: which target a weapon should start with selected. The unit the PREVIOUS weapon of this shoot
    /// action fired at wins whenever this weapon can still legally fire at it — a volley is normally aimed
    /// at one unit, and re-picking it for every weapon was pure clicking. Otherwise fall back to #237's
    /// sole-fireable-target rule, and to "nothing selected" when even that is ambiguous.
    /// <para>Ranked, not merged: the previous target is EVIDENCE of intent, while a sole target is only
    /// the absence of alternatives — so when both apply the evidence wins. Internal for tests.</para>
    /// </summary>
    internal static int PreferredTargetIndex(WeaponOption wo, DataBinding<UnitData>? previousTarget)
    {
        if (previousTarget != null)
        {
            for (int ti = 0; ti < wo.WeaponTargetStats.Count; ti++)
            {
                var ts = wo.WeaponTargetStats[ti];
                if (ts.UnselectableReason != null || ts.modelsThatCanShoot.Count == 0) continue;
                if (ts.TargetUnit.Reference.Equals(previousTarget.Reference)) return ti;
            }
        }

        return SoleFireableTargetIndex(wo);
    }

    // #237: the index of the weapon's ONLY fireable target, or -1 when it has zero or several - the
    // pre-select must never guess between real alternatives. Internal for tests.
    internal static int SoleFireableTargetIndex(WeaponOption wo)
    {
        int sole = -1;
        for (int ti = 0; ti < wo.WeaponTargetStats.Count; ti++)
        {
            var ts = wo.WeaponTargetStats[ti];
            if (ts.UnselectableReason != null || ts.modelsThatCanShoot.Count == 0) continue;
            if (sole >= 0) return -1;
            sole = ti;
        }
        return sole;
    }

    /// <summary>
    /// #319: what ending the shoot action here actually costs — the weapons that could still fire at
    /// something. A weapon with nothing in range loses nothing by stopping now, and naming it in the
    /// confirmation would be a false warning. Internal for tests.
    /// </summary>
    internal static List<WeaponOption> WeaponsGivenUpByStopping(IReadOnlyList<WeaponOption> weaponOptions)
    {
        var giveUp = new List<WeaponOption>();
        foreach (var wo in weaponOptions)
            if (HasAnyFireableTarget(wo)) giveUp.Add(wo);
        return giveUp;
    }

    private static bool HasAnyFireableTarget(WeaponOption wo)
    {
        foreach (var ts in wo.WeaponTargetStats)
            if (ts.UnselectableReason == null && ts.modelsThatCanShoot.Count > 0) return true;
        return false;
    }

    // Picks a tooltip explaining why a weapon row is grayed out. If every target's only barrier is
    // an explicit UnselectableReason (e.g. the 2-target-per-shoot-action limit), surface that;
    // otherwise fall back to the in-range diagnostic.
    private static string DescribeWeaponUnavailability(WeaponOption wo)
    {
        string? ruleReason = null;
        bool anyTargetExists = false;
        foreach (var ts in wo.WeaponTargetStats)
        {
            anyTargetExists = true;
            if (ts.UnselectableReason == null) return "No models with this weapon are in range or have line of sight.";
            ruleReason ??= ts.UnselectableReason;
        }
        if (!anyTargetExists) return "No enemy units to target.";
        return ruleReason!;
    }

    private void Complete(TaskCompletionSource<CancellableResult<RangedAttackChoice>> tcs, CancellableResult<RangedAttackChoice> choice)
    {
        lock (_lock) { _request = null; _tcs = null; }
        _lastRequest        = null;
        _selectedWeaponIdx  = -1;
        _selectedTargetTIdx = -1;
        _donePopupOpen      = false;
        _hoveredOption       = (-1, -1);
        _canvasHoveredOption = (-1, -1);
        tcs.SetResult(choice);
    }

    private (float px, float py) InchesToPixel(float x, float z)
    {
        float px = _originX + x * _scale;
        float py = _originY + (_tableH - z) * _scale;
        return (px, py);
    }
}
