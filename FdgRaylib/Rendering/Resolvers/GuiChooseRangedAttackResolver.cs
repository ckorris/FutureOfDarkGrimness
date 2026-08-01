using System.Numerics;
using FDG;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
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
        // #305: a shoot action's later weapons start aimed where the last one fired, when that target is
        // still fireable - it beats the sole-target rule, which is the weaker guess of the two.
        if (!ReferenceEquals(request, _lastRequest))
        {
            _lastRequest        = request;
            _selectedWeaponIdx  = FirstFireableWeaponIndex(request.WeaponOptions);
            _selectedTargetTIdx = _selectedWeaponIdx >= 0
                ? PreferredTargetIndex(request.WeaponOptions[_selectedWeaponIdx], request.PreviousTarget) : -1;
        }

        // #248 keyboard: Left/Right cycle the weapon (among fireable ones), Up/Down + number keys pick
        // the target (among fireable ones, display order). Enter fires via the footer's existing binding.
        HandleKeyboard(request);

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
        float footerH   = rowH + pad * 2;
        float spacingY  = ImGui.GetStyle().ItemSpacing.Y;
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
            ImGui.TextUnformatted($"Qua {tu.Quality}+   Def {tu.Defense}+");
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

        // Back is only offered before the first weapon has fired this shoot action -- once you start
        // shooting, you're committed to finishing the shoot stage. De-emphasized (secondary to Fire).
        float footW   = ImGui.GetContentRegionAvail().X;
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        // #305: the ENGINE decides whether backing out is still legal (nothing fired yet this shoot
        // action). The resolver used to keep its own counter and got it wrong on repeat activations.
        bool  showBack = request.AllowCancel;
        if (showBack)
        {
            // #248: Backspace backs out too (only while Back is offered; Esc is reserved for the
            // in-game menu).
            if (ResolverButtons.Deemphasized("Back (Backspace)", new Vector2(footW * 0.36f, rowH))
                || ResolverHotkeys.IsBackPressed())
            {
                Complete(tcs, new Cancelled<RangedAttackChoice>());
                ImGui.End();
                return;
            }
            ImGui.SameLine();
        }

        // Primary: Fire! -- red accent, larger; commits on click or the Confirm key when a weapon+target is chosen.
        float fireW = showBack ? footW * 0.64f - spacing : footW;
        ImGui.PushStyleColor(ImGuiCol.Button,        new Vector4(0.65f, 0.20f, 0.20f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, new Vector4(0.78f, 0.27f, 0.27f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive,  new Vector4(0.55f, 0.16f, 0.16f, 1f));
        if (!canFire) ImGui.BeginDisabled(true);
        bool fireClicked = ImGui.Button($"Fire!  {ResolverKeybinds.Confirm.Parenthetical}##fire", new Vector2(fireW, rowH));
        if (!canFire) ImGui.EndDisabled();
        ImGui.PopStyleColor(3);
        // #240: edge-only (repeat: false) so a stuck key can't fire volleys on its own; #248: the
        // shared helper also mutes it while typing or while the in-game menu is open.
        bool fireEnter = canFire && ResolverHotkeys.IsConfirmPressed();
        if (fireClicked || fireEnter)
        {
            var wo = request.WeaponOptions[_selectedWeaponIdx];
            var ts = wo.WeaponTargetStats[_selectedTargetTIdx];
            Complete(tcs, new Selected<RangedAttackChoice>(new RangedAttackChoice(wo.Weapon, ts.TargetUnit)));
            ImGui.End();
            return;
        }

        ImGui.End();

        // Clear canvas hover so it only persists for the single frame after GetHoverLabel set it.
        // (If the mouse is still over a model next frame, GetHoverLabel will set it again before Draw.)
        _canvasHoveredOption = (-1, -1);
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
        }

        // One line per shooter — from each attacker model that can hit this unit, to its
        // nearest target model. Uses 2D distance as a proxy for "valid"; per-model LoS data
        // isn't in the request, but the unit-level modelsThatCanShoot set guarantees at
        // least one defender is reachable. Label each line at its midpoint with the 3D
        // base-to-base distance in inches (matches the engine's range-check metric).
        uint colorLabel = ImGui.ColorConvertFloat4ToU32(new Vector4(0.85f, 1.00f, 0.85f, 0.95f));
        uint colorLabelBg = ImGui.ColorConvertFloat4ToU32(new Vector4(0f, 0f, 0f, 0.55f));
        foreach (var ab in ts.modelsThatCanShoot)
        {
            var attacker = ab.GetValue();
            ModelData? nearest = NearestModel(attacker, targetUnit.ModelBindings);
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

    // Internal for tests. #158: only LIVING, placed models are candidates — a just-killed model is often
    // the nearest (you shot it last volley), and aiming the shooter line at its corpse read as
    // "shooting at a dead model".
    internal static ModelData? NearestModel(ModelData from, IReadOnlyList<DataBinding<ModelData>> candidates)
    {
        ModelData? best = null;
        float bestDist = float.PositiveInfinity;
        foreach (var mb in candidates)
        {
            var m  = mb.GetValue();
            if (!m.GetIsAlive()) continue;
            if (m.Position.x == 0f && m.Position.z == 0f) continue;
            float d = DistanceUtilities.GetBaseToBaseDistanceInches_3D(
                from.Position, m.Position, from.BaseShape, from.Facing, m.BaseShape, m.Facing);
            if (d < bestDist) { bestDist = d; best = m; }
        }
        return best;
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
    /// #305: which target a weapon should start with selected. The unit the PREVIOUS weapon of this shoot
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
