using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FDG;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.Rules.Tokens;
using FdgRaylib.Rendering.Resolvers;
using ImGuiNET;
using Raylib_cs;

namespace FdgRaylib.Rendering;

/// <summary>
/// #329 — the in-game army list: press L to bring up every player's list the way the printed Army
/// Forge sheet reads (name [count] header, blue Quality/Defense/Tough pills, underlined hoverable
/// special rules, RNG/ATK/AP/SPE weapon table), but live — model counts, wounds, weapon counts,
/// destroyed units, activation state and status tokens all track the current game state.
///
/// <para>A true modal like the Esc menu (#246): the board dims, gameplay input is muted via
/// <see cref="EscapeRouter.MenuOpen"/> (the renderer ORs this overlay's open state into
/// <c>BeginFrame</c>), and Escape or L closes. Deliberately NOT a pause — the engine keeps running;
/// if a decision arrives for the local player while reading, a pulsing "Action needed" chip appears
/// in the header and clicking it returns to the game.</para>
///
/// <para>Read-only: draws from live <see cref="ITableState"/> on the render thread, the same
/// convention as <see cref="TableTooltipOverlay"/> (token reads snapshot per #328). ASCII only
/// (CLAUDE.md).</para>
/// </summary>
public sealed class ArmyListOverlay
{
    private const float MinCardWidth = 420f;   // roughly the printed card's proportions
    private const int   MaxColumns   = 4;

    public bool IsOpen { get; private set; }

    private ITableState? _tableState;
    private Func<PlayerID, Color> _colorForPlayer = _ => Color.White;
    private IReadOnlyList<PlayerID> _localPlayerIDs = Array.Empty<PlayerID>();
    private GuiResolverOverlay? _resolverOverlay;

    // Same core-catalog resolver the tooltip overlay holds, for resolving token display info.
    private readonly IRuleResolver _ruleResolver = CoreRuleCatalog.CreateResolver();

    // Measured card heights from the previous frame, keyed by unit — masonry packing needs heights
    // BEFORE drawing, so cards pack on last frame's measurements (self-correcting after one frame;
    // new units start on a line-count estimate).
    private readonly Dictionary<UnitID, float> _cardHeights = new();

    private static readonly Vector4 ActivatedRed  = new(1f, 0.30f, 0.30f, 1f);   // tooltip's red
    private static readonly Vector4 HeroGold      = new(1f, 0.85f, 0.3f, 1f);    // #227 hero tag
    private static readonly Vector4 WoundedAmber  = new(0.72f, 0.48f, 0.16f, 1f);
    private static readonly Vector4 PillText      = new(1f, 1f, 1f, 1f);

    public void Attach(ITableState tableState, Func<PlayerID, Color> colorForPlayer,
        IReadOnlyList<PlayerID> localPlayerIDs, GuiResolverOverlay? resolverOverlay)
    {
        _tableState      = tableState;
        _colorForPlayer  = colorForPlayer;
        _localPlayerIDs  = localPlayerIDs;
        _resolverOverlay = resolverOverlay;
        _cardHeights.Clear();
    }

    public void Open()   => IsOpen = true;
    public void Close()  => IsOpen = false;
    public void Toggle() => IsOpen = !IsOpen;

    /// <param name="escapeMenuOpen">True while the Esc menu owns input — the L toggle is muted so the
    /// two modals never fight over a keypress.</param>
    /// <param name="gameOver">True once the game-over card is up; the overlay closes and stays closed
    /// so the card is reachable.</param>
    public void Draw(int screenW, int screenH, bool escapeMenuOpen, bool gameOver)
    {
        if (_tableState == null) return;
        if (gameOver) { Close(); return; }

        var io = ImGui.GetIO();
        bool lPressed = !io.WantTextInput && !escapeMenuOpen
            && ImGui.IsKeyPressed(ImGuiKey.L, repeat: false);

        bool openedThisFrame = false;
        if (!IsOpen)
        {
            // Opening keeps the other view toggles' gate (a focused window or text field holds the
            // key); closing below only needs the text-input gate, since our own modal is what holds
            // keyboard capture while open.
            if (lPressed && !io.WantCaptureKeyboard)
            {
                Open();
                openedThisFrame = true;
            }
            if (!IsOpen) return;
        }

        // Same-press guard as the Esc menu: the press that opened is still "pressed" this frame.
        if (!openedThisFrame && (lPressed || ImGui.IsKeyPressed(ImGuiKey.Escape, repeat: false)))
            Close();

        if (IsOpen && !ImGui.IsPopupOpen("##armylists"))
            ImGui.OpenPopup("##armylists");

        // Near-fullscreen: the card columns want the width, and a fullscreen reading mode is the
        // point — the board is one keypress away.
        var size = new Vector2(screenW - 90f, screenH - 70f);
        ImGui.SetNextWindowPos(new Vector2(screenW * 0.5f, screenH * 0.5f), ImGuiCond.Always,
            new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSize(size, ImGuiCond.Always);
        ImGui.PushStyleColor(ImGuiCol.ModalWindowDimBg, new Vector4(0f, 0f, 0f, 0.55f));

        bool visible = true;   // NoTitleBar hides the implicit close button this ref would add
        if (ImGui.BeginPopupModal("##armylists", ref visible,
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoSavedSettings))
        {
            if (IsOpen) DrawContent();
            if (!IsOpen) ImGui.CloseCurrentPopup();
            ImGui.EndPopup();
        }

        ImGui.PopStyleColor();
    }

    // ── Modal content ───────────────────────────────────────────────────────────────────────────────

    private void DrawContent()
    {
        DrawHeaderBar();
        ImGui.Separator();

        var slots = _tableState!.Players.Objects.Where(p => p.IsFilled).ToList();
        var ordered = ArmyListLayout.OrderTabs(slots, s => _localPlayerIDs.Contains(s.PlayerID));

        if (ImGui.BeginTabBar("##armytabs"))
        {
            foreach (IPlayerSlotInfo slot in ordered)
            {
                Color c = _colorForPlayer(slot.PlayerID);
                ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(c.R / 255f, c.G / 255f, c.B / 255f, 1f));
                bool selected = ImGui.BeginTabItem($"{slot.Name}##slot{slot.SlotID}");
                ImGui.PopStyleColor();

                if (selected)
                {
                    DrawPlayerTab(slot);
                    ImGui.EndTabItem();
                }
            }
            ImGui.EndTabBar();
        }
    }

    // Title on the left; "Action needed" pulse + Close on the right.
    private void DrawHeaderBar()
    {
        ImGui.PushFont(RaylibRenderer.LargeFont);
        ImGui.TextUnformatted("Army Lists");
        ImGui.PopFont();

        float closeW = 110f;
        bool pending = _resolverOverlay?.HasAnyPending ?? false;
        float chipW = pending
            ? ImGui.CalcTextSize("Action needed - return to game").X
              + 2f * ImGui.GetStyle().FramePadding.X + ImGui.GetStyle().ItemSpacing.X
            : 0f;

        ImGui.SameLine(ImGui.GetWindowWidth() - ImGui.GetStyle().WindowPadding.X - closeW - chipW);

        if (pending)
        {
            // The engine is waiting on YOU while this is open (it never pauses); pulse so the chip
            // reads over a wall of cards, and clicking it drops straight back to the prompt.
            float pulse = 0.5f + 0.5f * MathF.Sin((float)ImGui.GetTime() * 5f);
            var hot = new Vector4(0.95f, 0.65f, 0.15f, 1f);
            var dim = new Vector4(0.62f, 0.42f, 0.10f, 1f);
            ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Lerp(dim, hot, pulse));
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.05f, 0.05f, 0.05f, 1f));
            if (ImGui.Button("Action needed - return to game")) Close();
            ImGui.PopStyleColor(2);
            ImGui.SameLine();
        }

        if (ImGui.Button("Close (L)", new Vector2(closeW, 0f))) Close();
    }

    private void DrawPlayerTab(IPlayerSlotInfo slot)
    {
        var units = UnitsFor(slot.PlayerID);

        int standing = units.Count(u => u.Models.Any(m => m.GetIsAlive()));
        ImGui.TextDisabled(standing == units.Count
            ? $"{units.Count} unit{(units.Count == 1 ? "" : "s")}"
            : $"{standing}/{units.Count} units standing");
        const string hint = "Hover an underlined rule for its description";
        ImGui.SameLine(ImGui.GetWindowWidth() - ImGui.GetStyle().WindowPadding.X
            - ImGui.CalcTextSize(hint).X);
        ImGui.TextDisabled(hint);

        // One scroll region per player (stable ID), so each tab keeps its own scroll position.
        ImGui.BeginChild($"##list{slot.SlotID}");

        int columns = ArmyListLayout.ColumnCount(ImGui.GetContentRegionAvail().X, MinCardWidth, MaxColumns);
        float estimate = ImGui.GetTextLineHeightWithSpacing();
        var heights = units
            .Select(u => _cardHeights.TryGetValue(u.ID, out float h) ? h : estimate * 10f)
            .ToList();
        int[] columnOf = ArmyListLayout.PackColumns(heights, columns);

        if (ImGui.BeginTable("##cards", columns, ImGuiTableFlags.SizingStretchSame))
        {
            ImGui.TableNextRow();
            for (int c = 0; c < columns; c++)
            {
                ImGui.TableSetColumnIndex(c);
                for (int i = 0; i < units.Count; i++)
                    if (columnOf[i] == c)
                        DrawCard(units[i]);
            }
            ImGui.EndTable();
        }

        ImGui.EndChild();
    }

    // A player's units in army order (the Armies store preserves the list file's order, dead
    // included); armies are synced to every client, but fall back to the flat unit store just in
    // case an IArmy was never registered for this player.
    private List<IUnit> UnitsFor(PlayerID playerID)
    {
        var fromArmies = _tableState!.Armies.Objects
            .Where(a => a.PlayerID.Equals(playerID))
            .SelectMany(a => a.Units)
            .ToList();
        if (fromArmies.Count > 0) return fromArmies;

        return _tableState.Units.Objects.Where(u => u.PlayerID.Equals(playerID)).ToList();
    }

    // ── One unit card ───────────────────────────────────────────────────────────────────────────────

    private void DrawCard(IUnit unit)
    {
        int live  = unit.Models.Count(m => m.GetIsAlive());
        int total = unit.Models.Count;
        bool destroyed = live == 0;

        ImGui.PushID(unit.ID.GetHashCode());

        // A destroyed unit stays on the list — "what is left of their army" is half the point — but
        // reads as spent: everything dims, then a full-strength DESTROYED stamp goes over the card.
        if (destroyed)
            ImGui.PushStyleVar(ImGuiStyleVar.Alpha, ImGui.GetStyle().Alpha * 0.45f);

        ImGui.BeginChild("card", new Vector2(0f, 0f),
            ImGuiChildFlags.Borders | ImGuiChildFlags.AutoResizeY | ImGuiChildFlags.AlwaysUseWindowPadding);

        DrawCardHeader(unit, live, total, destroyed);
        DrawPillRow(unit, live, destroyed);

        if (!destroyed && live > 0 && unit.RemainingWounds < unit.MaxWounds)
        {
            string wounds = $"Wounds {WoundFormat.Fraction(unit.RemainingWounds, unit.MaxWounds)}";
            CenterNextText(wounds);
            ImGui.TextColored(WithAlpha(WoundedAmber, 1f), wounds);
        }

        DrawRuleLine(unit);
        DrawWeaponTable(unit);
        DrawHeroBlock(unit);
        DrawTokenChips(unit);

        ImGui.EndChild();

        if (destroyed) ImGui.PopStyleVar();

        // Cache the drawn height (plus spacing) for next frame's masonry packing.
        Vector2 cardMin = ImGui.GetItemRectMin();
        Vector2 cardMax = ImGui.GetItemRectMax();
        _cardHeights[unit.ID] = (cardMax.Y - cardMin.Y) + ImGui.GetStyle().ItemSpacing.Y;

        if (destroyed)
        {
            // Stamped over the dimmed card at full alpha, centered.
            const string stamp = "DESTROYED";
            var dl = ImGui.GetWindowDrawList();
            float stampSize = 24f;
            Vector2 textSize = RaylibRenderer.LargeFont.CalcTextSizeA(stampSize, float.MaxValue, 0f, stamp);
            var pos = new Vector2(
                cardMin.X + (cardMax.X - cardMin.X - textSize.X) * 0.5f,
                cardMin.Y + (cardMax.Y - cardMin.Y - textSize.Y) * 0.5f);
            dl.AddText(RaylibRenderer.LargeFont, stampSize, pos + new Vector2(1f, 1f),
                ImGui.GetColorU32(new Vector4(0f, 0f, 0f, 0.8f)), stamp);
            dl.AddText(RaylibRenderer.LargeFont, stampSize, pos,
                ImGui.GetColorU32(new Vector4(0.95f, 0.25f, 0.25f, 0.95f)), stamp);
        }

        ImGui.PopID();
        ImGui.Spacing();
    }

    private void DrawCardHeader(IUnit unit, int live, int total, bool destroyed)
    {
        // A pristine unit reads exactly like the printout ("[20]"); casualties turn it into the
        // live fraction ("[12/20]").
        string header = live == total ? $"{unit.Name} [{total}]" : $"{unit.Name} [{live}/{total}]";

        ImGui.SetWindowFontScale(1.12f);
        CenterNextText(header);
        ImGui.TextUnformatted(header);
        ImGui.SetWindowFontScale(1f);

        if (!destroyed && UnitActivation.HasActivated(_tableState!.Progress, unit))
        {
            ImGui.SameLine(ImGui.GetWindowWidth() - ImGui.GetStyle().WindowPadding.X
                - ImGui.CalcTextSize("Activated").X);
            ImGui.TextColored(ActivatedRed, "Activated");
        }
    }

    // The blue stat pills: Quality / Defense always, Tough when the unit's models carry more than
    // one wound each. A wounded single-model Tough unit shows "remaining/max" in amber — the pill
    // itself is the wound counter, never color alone (the fraction text carries the fact).
    private void DrawPillRow(IUnit unit, int live, bool destroyed)
    {
        var pills = new List<(string label, string value, Vector4 bg)>
        {
            ("Quality", $"{unit.Quality}+", ImGuiTheme.AccentBlue),
            ("Defense", $"{unit.Defense}+", ImGuiTheme.AccentBlue),
        };

        // Representative per-model wounds: prefer a living rank-and-file model, fall back to any.
        IModel? tough = unit.Models
            .Where(m => m.TotalWounds > 1f)
            .OrderByDescending(m => m.GetIsAlive())
            .FirstOrDefault();
        if (tough != null)
        {
            bool single = unit.Models.Count == 1;
            float remaining = tough.TotalWounds - tough.WoundsDealt;
            bool wounded = single && remaining < tough.TotalWounds && !destroyed;
            string value = wounded
                ? WoundFormat.Fraction(remaining, tough.TotalWounds)
                : WoundFormat.Format(tough.TotalWounds);
            pills.Add(("Tough", value, wounded ? WoundedAmber : ImGuiTheme.AccentBlue));
        }

        // Center the row like the printout.
        float totalW = 0f;
        foreach (var p in pills) totalW += PillWidth(p.label, p.value) + 8f;
        totalW -= 8f;
        ImGui.SetCursorPosX(Math.Max(ImGui.GetCursorPosX(),
            ImGui.GetCursorPosX() + (ImGui.GetContentRegionAvail().X - totalW) * 0.5f));

        for (int i = 0; i < pills.Count; i++)
        {
            if (i > 0) ImGui.SameLine(0f, 8f);
            DrawPill(pills[i].label, pills[i].value, pills[i].bg);
        }
        ImGui.Spacing();
    }

    private static float PillWidth(string label, string value) =>
        ImGui.CalcTextSize(label).X + ImGui.CalcTextSize(value).X + 4f * PillPad;

    private const float PillPad = 7f;

    // Two-tone pill: label on the accent field, value on a dark well, one rounded outline — the
    // printed list's stat badge.
    private static void DrawPill(string label, string value, Vector4 labelBg)
    {
        var dl = ImGui.GetWindowDrawList();
        Vector2 pos = ImGui.GetCursorScreenPos();
        float h = ImGui.GetTextLineHeight() + 8f;
        float labelW = ImGui.CalcTextSize(label).X + 2f * PillPad;
        float valueW = ImGui.CalcTextSize(value).X + 2f * PillPad;
        const float rounding = 5f;

        dl.AddRectFilled(pos, pos + new Vector2(labelW + valueW, h),
            ImGui.GetColorU32(ImGuiTheme.InkWell), rounding);
        dl.AddRectFilled(pos, pos + new Vector2(labelW, h),
            ImGui.GetColorU32(labelBg), rounding, ImDrawFlags.RoundCornersLeft);
        dl.AddText(pos + new Vector2(PillPad, 4f), ImGui.GetColorU32(PillText), label);
        dl.AddText(pos + new Vector2(labelW + PillPad, 4f), ImGui.GetColorU32(PillText), value);

        ImGui.Dummy(new Vector2(labelW + valueW, h));
    }

    // The unit's special rules as one comma-joined line of underlined hover targets, wrapped at rule
    // boundaries — the #292 convention (solid underline = documented, faded = inert in play).
    private void DrawRuleLine(IUnit unit)
    {
        var rules = PlayerFacingRules(unit.RuleDefinitions);
        if (rules.Count == 0) return;

        var segments = new List<RuleHoverText.Segment>();
        foreach (ResolvedRule rule in rules)
        {
            if (segments.Count > 0) segments.Add(new RuleHoverText.Segment(", ", null, null));
            segments.Add(new RuleHoverText.Segment(rule.RequestedName, rule.RequestedName,
                rule.Definition.Description));
        }

        DrawWrappedSegments(segments, ImGui.GetContentRegionAvail().X);
        ImGui.Spacing();
    }

    private void DrawWeaponTable(IUnit unit)
    {
        var weapons = unit.AllWeapons();   // living models only — counts shrink with casualties
        if (weapons.Count == 0) return;

        if (!ImGui.BeginTable("weapons", 5,
            ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp)) return;

        ImGui.TableSetupColumn("Weapon", ImGuiTableColumnFlags.WidthStretch, 3f);
        ImGui.TableSetupColumn("RNG", ImGuiTableColumnFlags.WidthFixed, 46f);
        ImGui.TableSetupColumn("ATK", ImGuiTableColumnFlags.WidthFixed, 40f);
        ImGui.TableSetupColumn("AP", ImGuiTableColumnFlags.WidthFixed, 32f);
        ImGui.TableSetupColumn("SPE", ImGuiTableColumnFlags.WidthStretch, 2f);
        ImGui.TableHeadersRow();

        foreach (var group in weapons.GroupBy(w => w.Name))
        {
            IWeapon w = group.First();
            ImGui.TableNextRow();

            ImGui.TableSetColumnIndex(0);
            ImGui.TextUnformatted(ArmyListLayout.CountedName(group.Count(), w.Name));
            ImGui.TableSetColumnIndex(1);
            ImGui.TextUnformatted(ArmyListLayout.RangeText(w.RangeInches));
            ImGui.TableSetColumnIndex(2);
            ImGui.TextUnformatted(ArmyListLayout.AttacksText(w.Attacks));
            ImGui.TableSetColumnIndex(3);
            ImGui.TextUnformatted(ArmyListLayout.ApText(w.ArmorPenetration));

            ImGui.TableSetColumnIndex(4);
            var ruleSegments = RuleHoverText.RuleSegments(w);
            if (ruleSegments.Count == 0)
                ImGui.TextDisabled("-");
            else
                DrawWrappedSegments(ruleSegments, ImGui.GetContentRegionAvail().X);
        }

        ImGui.EndTable();
    }

    // Joined hero (#227/#006): a sub-block inside the host unit's card — the in-game truth after the
    // merge, unlike the printout's separate pre-merge entry. The hero's own Qua/Def (they diverge
    // from the host's), its wound counter when Tough, and its OWN rules (a joined hero does not
    // share the unit's).
    private void DrawHeroBlock(IUnit unit)
    {
        if (unit.JoinedHeroModelId is not { } heroId) return;
        IModel? hero = unit.Models.FirstOrDefault(m => m.ID.Equals(heroId));
        if (hero == null) return;

        ImGui.Separator();

        string tag = unit is UnitData { HeroAttachment: { } ha }
            ? HeroMarkerRenderer.FormatHeroTag(ha.Quality, ha.Defense)
            : "Hero";
        if (!hero.GetIsAlive()) tag += "  -  dead";
        else if (hero.TotalWounds > 1f)
            tag += $"  Wounds {WoundFormat.Fraction(hero.TotalWounds - hero.WoundsDealt, hero.TotalWounds)}";
        ImGui.TextColored(HeroGold, tag);

        var heroRules = PlayerFacingRules(hero.RuleDefinitions);
        if (heroRules.Count > 0)
        {
            var segments = new List<RuleHoverText.Segment>();
            foreach (ResolvedRule rule in heroRules)
            {
                if (segments.Count > 0) segments.Add(new RuleHoverText.Segment(", ", null, null));
                segments.Add(new RuleHoverText.Segment(rule.RequestedName, rule.RequestedName,
                    rule.Definition.Description));
            }
            DrawWrappedSegments(segments, ImGui.GetContentRegionAvail().X);
        }
    }

    // Status tokens as colored text chips ("[Shaken]"), hover for the engine's description — same
    // resolve + color source as the canvas chips (#151/#328), text form because a card has room to
    // say the name.
    private void DrawTokenChips(IUnit unit)
    {
        var tokens = TokenChipRenderer.ResolveVisible(unit.Tokens, _ruleResolver, false,
            ViewSettings.ShowAllTokens, unit);
        if (tokens.Count == 0) return;

        ImGui.Spacing();
        float avail = ImGui.GetContentRegionAvail().X;
        float x = 0f;
        for (int i = 0; i < tokens.Count; i++)
        {
            TokenDisplayInfo token = tokens[i];
            string chip = token.Count > 1 ? $"[{token.Name} x{token.Count}]" : $"[{token.Name}]";
            float w = ImGui.CalcTextSize(chip).X;
            if (i > 0)
            {
                if (x + w > avail) x = 0f;         // wrap
                else ImGui.SameLine();
            }
            x += w + ImGui.GetStyle().ItemSpacing.X;

            ImGui.TextColored(ImGui.ColorConvertU32ToFloat4(TokenChipRenderer.ColorFor(token)), chip);
            if (ImGui.IsItemHovered() && !string.IsNullOrEmpty(token.Description))
                RuleHoverText.ShowTooltip($"{token.Name}\n{token.Description}");
        }
    }

    // ── Shared bits ─────────────────────────────────────────────────────────────────────────────────

    /// <summary>Rules minus the engine-internal Disembark/Embark abilities attached to every unit —
    /// the same filter as the canvas tooltip and deploy panel.</summary>
    private static List<ResolvedRule> PlayerFacingRules(IReadOnlyList<ResolvedRule> rules) =>
        rules.Where(r => r.Definition.Name != CoreRuleCatalog.DisembarkRuleName
                      && r.Definition.Name != CoreRuleCatalog.EmbarkRuleName)
             .ToList();

    // Wraps at segment boundaries (ArmyListLayout.WrapSegments), draws each line via
    // RuleHoverText.DrawInline on the window draw list, and raises the hovered rule's tooltip.
    private static void DrawWrappedSegments(IReadOnlyList<RuleHoverText.Segment> segments, float width)
    {
        var lines = ArmyListLayout.WrapSegments(segments, s => ImGui.CalcTextSize(s).X, width);
        var dl = ImGui.GetWindowDrawList();
        uint textColor = ImGui.GetColorU32(ImGuiCol.Text);
        float lineHeight = ImGui.GetTextLineHeightWithSpacing();
        bool hoverEnabled = ImGui.IsWindowHovered(ImGuiHoveredFlags.RootAndChildWindows);

        string? tooltip = null;
        foreach (var line in lines)
        {
            Vector2 origin = ImGui.GetCursorScreenPos();
            tooltip ??= RuleHoverText.DrawInline(dl, origin, line, textColor, textColor, hoverEnabled);
            ImGui.Dummy(new Vector2(width, lineHeight));
        }

        if (tooltip != null) RuleHoverText.ShowTooltip(tooltip);
    }

    private static void CenterNextText(string text)
    {
        float indent = (ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(text).X) * 0.5f;
        if (indent > 0f) ImGui.SetCursorPosX(ImGui.GetCursorPosX() + indent);
    }

    private static Vector4 WithAlpha(Vector4 color, float alpha) =>
        new(color.X, color.Y, color.Z, alpha);
}
