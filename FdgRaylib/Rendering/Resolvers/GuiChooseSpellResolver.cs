using System.Numerics;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using ImGuiNET;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// #244 — GUI spell picker with the caster's self-boost built in. One panel: selectable spell rows
/// (highlight, not instant-commit) with effect subtext, disabled rows with the reason (unaffordable /
/// no target), then a boost stepper with a live "roll needed" readout and a total-spend line, and
/// Cast / Cancel. Boost is capped at the affordable remainder AND the useful maximum
/// (to the 2+ floor - a natural 1 always fails - plus one per in-range enemy hinder token); at the cap
/// the stepper says why, so a player never overspends into tokens that cannot matter (#103 hinder
/// prompts come after the caster commits, which is why hedging past the floor is real only while enemy
/// Casters hold tokens in range).
/// </summary>
public class GuiChooseSpellResolver : IStageResolver<ChooseSpellRequest, ChooseSpellReply>, IGuiResolver
{
    private static readonly Vector4 AccentRgba   = new(0.30f, 0.60f, 1.00f, 1.00f); // cast blue (matches assist)
    private static readonly Vector4 DimTextRgba  = new(0.62f, 0.66f, 0.74f, 1f);
    private static readonly Vector4 WarnTextRgba = new(1.00f, 0.60f, 0.15f, 1f);    // hedge/cap note orange
    // Friendly-support blue, matching the #103 assist banner - a relay is help from your own side.
    private static readonly Vector4 RelayTextRgba = new(0.30f, 0.60f, 1.00f, 1f);

    private readonly object _lock = new();
    private ChooseSpellRequest? _request;
    private TaskCompletionSource<ChooseSpellReply>? _tcs;

    // Draw-thread UI state, reset per request in Resolve: which row is highlighted and the boost count.
    private int _selectedIndex;
    private int _boost;

    public bool HasPendingRequest { get { lock (_lock) return _request != null; } }

    public Task<ChooseSpellReply> Resolve(ChooseSpellRequest request)
    {
        var tcs = new TaskCompletionSource<ChooseSpellReply>();
        int firstCastable = 0;
        for (int i = 0; i < request.Spells.Count; i++)
        {
            if (request.Spells[i].Castable) { firstCastable = i; break; }
        }
        lock (_lock)
        {
            _tcs = tcs;
            _request = request;
            _selectedIndex = firstCastable;
            _boost = 0;
        }
        return tcs.Task;
    }

    public void Draw(int screenW, int screenH)
    {
        ChooseSpellRequest? request;
        TaskCompletionSource<ChooseSpellReply>? tcs;
        lock (_lock) { request = _request; tcs = _tcs; }
        if (request == null || tcs == null) return;

        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("##ChooseSpellBackdrop",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoInputs |
            ImGuiWindowFlags.NoBackground);

        const float pad = 14f;
        const float rowH = 28f;
        const float gap = 4f;
        const float descScale = 0.82f;
        const float descIndent = 10f;

        float dw = ResolverPanelLayout.W;   // dock into the right-column resolver panel
        float dh = ResolverPanelLayout.H;
        float dx = ResolverPanelLayout.X;
        float dy = ResolverPanelLayout.Y;
        float btnW = dw - pad * 2;

        ImGui.SetCursorPos(new Vector2(dx, dy));
        ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.15f, 0.15f, 0.20f, 0.97f));
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f);
        ImGui.BeginChild("##ChooseSpellDialog", new Vector2(dw, dh), ImGuiChildFlags.Borders,
            ImGuiWindowFlags.None);
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();

        // #248 keyboard: numbers/arrows move the highlight among CASTABLE rows (not instant cast — the
        // boost stepper sits between select and commit in this panel by design, #244), Left/Right step
        // the boost, Enter casts, Esc cancels (buttons below).
        var castable = new List<int>();
        for (int i = 0; i < request.Spells.Count; i++)
            if (request.Spells[i].Castable) castable.Add(i);
        if (castable.Count > 0)
        {
            int number = ResolverHotkeys.PressedNumberIndex(castable.Count);
            if (number >= 0) { _selectedIndex = castable[number]; ClampBoost(request); }

            int ud = ResolverHotkeys.ArrowDelta();
            if (ud != 0)
            {
                int pos = castable.IndexOf(_selectedIndex);
                pos = pos < 0 ? (ud > 0 ? 0 : castable.Count - 1)
                              : (pos + ud + castable.Count) % castable.Count;
                _selectedIndex = castable[pos];
                ClampBoost(request);
            }
        }
        int lr = ResolverHotkeys.HorizontalArrowDelta();
        if (lr != 0) { _boost += lr; if (_boost < 0) _boost = 0; ClampBoost(request); }

        string casterName = request.CastingUnit.GetValue().Name;

        // ── Header ──────────────────────────────────────────────────────────────
        ImGui.SetCursorPos(new Vector2(pad, pad));
        ImGui.PushStyleColor(ImGuiCol.Text, AccentRgba);
        ImGui.TextUnformatted($"Choose a spell - {casterName}");
        ImGui.PopStyleColor();
        ImGui.SetCursorPos(new Vector2(pad, pad + 20f));
        ImGui.PushStyleColor(ImGuiCol.Text, DimTextRgba);
        ImGui.TextUnformatted($"{request.AvailableTokens} spell token{(request.AvailableTokens == 1 ? "" : "s")} available");
        ImGui.PopStyleColor();

        float y = pad + 44f;

        // ── #197 P23 relay note ────────────────────────────────────────────────
        // A Spell Conduit in range extends what is reachable and eases the roll. Nothing to choose here -
        // the origin follows the targets - but the bonus must be visible where the player decides whether
        // a spell is worth casting. The target list then names the origin per target.
        foreach (ChooseSpellRequest.RelayOption relay in request.RelaysInRange)
        {
            ImGui.SetCursorPos(new Vector2(pad, y));
            ImGui.PushStyleColor(ImGuiCol.Text, RelayTextRgba);
            ImGui.TextUnformatted($"{relay.UnitName} relays: cast from its position for +{relay.RollBonus} " +
                "and measure range from it.");
            ImGui.PopStyleColor();
            y += 20f;
        }

        if (request.RelaysInRange.Count > 0) y += 6f;

        // ── Spell rows: click to highlight; disabled rows show why ──────────────
        float descWrapMeasure = (btnW - descIndent) / descScale;
        int castableRowsSeen = 0;   // #248: numbers the castable rows top-down, matching the key handler
        for (int i = 0; i < request.Spells.Count; i++)
        {
            ChooseSpellRequest.SpellOption option = request.Spells[i];
            bool selected = i == _selectedIndex;

            ImGui.SetCursorPos(new Vector2(pad, y));
            if (option.Castable)
            {
                string numPrefix = ResolverHotkeys.NumberPrefix(castableRowsSeen++);
                if (selected)
                {
                    ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(AccentRgba.X * 0.5f, AccentRgba.Y * 0.5f, AccentRgba.Z * 0.5f, 1f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, AccentRgba);
                }
                if (ImGui.Button($"{(selected ? "> " : "")}{numPrefix}{option.Label}##spell{i}", new Vector2(btnW, rowH)))
                {
                    _selectedIndex = i;
                    ClampBoost(request);
                }
                if (selected) ImGui.PopStyleColor(2);
            }
            else
            {
                ImGui.BeginDisabled(true);
                ImGui.Button($"{option.Label} ({option.UnavailableReason})##spell{i}", new Vector2(btnW, rowH));
                ImGui.EndDisabled();
            }
            y += rowH + 2f;

            if (!string.IsNullOrEmpty(option.Description))
            {
                ImGui.SetCursorPos(new Vector2(pad + descIndent, y));
                ImGui.PushStyleColor(ImGuiCol.Text, DimTextRgba);
                ImGui.SetWindowFontScale(descScale);
                ImGui.PushTextWrapPos(dw - pad);
                ImGui.TextUnformatted(option.Description);
                ImGui.PopTextWrapPos();
                float descH = ImGui.CalcTextSize(option.Description, false, descWrapMeasure).Y * descScale;
                ImGui.SetWindowFontScale(1f);
                ImGui.PopStyleColor();
                y += descH + gap + 2f;
            }
            else
            {
                y += gap;
            }
        }

        // ── Boost stepper + live odds ───────────────────────────────────────────
        ChooseSpellRequest.SpellOption chosen = request.Spells[_selectedIndex];
        int affordable = System.Math.Max(0, request.AvailableTokens - chosen.Cost);
        int useful = request.MaxUsefulBoost; // to the 2+ floor (+ hedge vs in-range hinder tokens)
        int cap = System.Math.Min(affordable, useful);

        y += 6f;
        ImGui.SetCursorPos(new Vector2(pad, y));
        ImGui.Separator();
        y += 8f;

        const float stepBtn = 26f;
        ImGui.SetCursorPos(new Vector2(pad, y));
        ImGui.TextUnformatted("Boost roll:");
        ImGui.SameLine();
        ImGui.BeginDisabled(_boost <= 0);
        if (ImGui.Button("-##boostDown", new Vector2(stepBtn, stepBtn - 4f))) _boost--;
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.TextUnformatted($" {_boost} token{(_boost == 1 ? "" : "s")} ");
        ImGui.SameLine();
        ImGui.BeginDisabled(_boost >= cap);
        if (ImGui.Button("+##boostUp", new Vector2(stepBtn, stepBtn - 4f))) _boost++;
        ImGui.EndDisabled();
        y += stepBtn + 4f;

        // At (or past) the useful cap, say why the + goes no further. Orange so it reads as advice.
        if (_boost >= cap && affordable > cap)
        {
            ImGui.SetCursorPos(new Vector2(pad, y));
            ImGui.PushStyleColor(ImGuiCol.Text, WarnTextRgba);
            ImGui.SetWindowFontScale(descScale);
            string capNote = request.HinderTokensInRange == 0
                ? "Max useful boost - no enemy casters in range (2+ is the floor, a 1 always fails)."
                : $"Max useful boost vs the {request.HinderTokensInRange} enemy token{(request.HinderTokensInRange == 1 ? "" : "s")} in range.";
            ImGui.TextUnformatted(capNote);
            ImGui.SetWindowFontScale(1f);
            ImGui.PopStyleColor();
            y += 18f;
        }
        else if (_boost > (request.BaseThreshold - 2) && request.HinderTokensInRange > 0)
        {
            ImGui.SetCursorPos(new Vector2(pad, y));
            ImGui.PushStyleColor(ImGuiCol.Text, WarnTextRgba);
            ImGui.SetWindowFontScale(descScale);
            ImGui.TextUnformatted($"Hedging vs up to -{request.HinderTokensInRange} from enemy casters in range.");
            ImGui.SetWindowFontScale(1f);
            ImGui.PopStyleColor();
            y += 18f;
        }

        int needed = System.Math.Max(2, request.BaseThreshold - _boost); // 2+ floor: a natural 1 always fails
        ImGui.SetCursorPos(new Vector2(pad, y));
        ImGui.TextUnformatted(_boost > 0
            ? $"Roll needed: {needed}+ (base {request.BaseThreshold}+, self +{_boost})"
            : $"Roll needed: {request.BaseThreshold}+");
        y += 20f;

        ImGui.SetCursorPos(new Vector2(pad, y));
        ImGui.PushStyleColor(ImGuiCol.Text, DimTextRgba);
        ImGui.TextUnformatted(_boost > 0
            ? $"Total spend: {chosen.Cost} cost + {_boost} boost = {chosen.Cost + _boost} of {request.AvailableTokens}"
            : $"Total spend: {chosen.Cost} of {request.AvailableTokens}");
        ImGui.PopStyleColor();
        y += 28f;

        // ── Commit / cancel ─────────────────────────────────────────────────────
        // #248: Enter casts, Backspace cancels (Esc is reserved for the in-game menu). Applied once
        // after drawing so same-frame click + key can't double-resolve the TCS.
        float half = (btnW - 8f) / 2f;
        ImGui.SetCursorPos(new Vector2(pad, y));
        ImGui.PushStyleColor(ImGuiCol.Button, new Vector4(AccentRgba.X * 0.5f, AccentRgba.Y * 0.5f, AccentRgba.Z * 0.5f, 1f));
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, AccentRgba);
        bool castNow = ImGui.Button($"Cast (Enter)##commit", new Vector2(half, rowH + 4f));
        ImGui.PopStyleColor(2);
        ImGui.SameLine();
        ImGui.SetCursorPosX(pad + half + 8f);
        bool cancelNow = ImGui.Button("Cancel (Backspace)##cancel", new Vector2(half, rowH + 4f));

        castNow   |= ResolverHotkeys.IsEnterPressed();
        cancelNow |= ResolverHotkeys.IsBackPressed();

        ImGui.EndChild();
        ImGui.End();

        if (castNow) Complete(tcs, new ChooseSpellReply(_selectedIndex, _boost));
        else if (cancelNow) Complete(tcs, ChooseSpellReply.Cancel);
    }

    // Re-clamp the boost when the selected spell changes (a pricier spell leaves fewer boost tokens).
    private void ClampBoost(ChooseSpellRequest request)
    {
        ChooseSpellRequest.SpellOption chosen = request.Spells[_selectedIndex];
        int affordable = System.Math.Max(0, request.AvailableTokens - chosen.Cost);
        _boost = System.Math.Clamp(_boost, 0, System.Math.Min(affordable, request.MaxUsefulBoost));
    }

    private void Complete(TaskCompletionSource<ChooseSpellReply> tcs, ChooseSpellReply reply)
    {
        lock (_lock) { _request = null; _tcs = null; }
        tcs.SetResult(reply);
    }
}
