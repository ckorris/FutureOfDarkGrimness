using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using FDG;
using FDG.ArmyBuilding;
using FDG.Calculator;
using FDG.Rules.Dispatch;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using FdgRaylib.Rendering.CombatCalc;
using ImGuiNET;
using TinyDialogsNet;

namespace FdgRaylib.Rendering;

/// <summary>
/// #397: pit one unit against another and see what actually happens. Three columns - unit A, the
/// result, unit B - with each side built the way the Army Forge builds a unit, so upgrades, combined
/// squads and their prices all behave exactly as they do in a real list.
///
/// <para>
/// The numbers are not computed here. Every figure on screen comes from
/// <see cref="CombatCalculator"/>, which runs the real combat stages in a throwaway world; this screen
/// only decides what to ask and how to show the answer. A run costs milliseconds, so it re-runs
/// whenever either side or the situation changes and does nothing on the frames in between.
/// </para>
/// </summary>
public class CombatCalculatorScreen : IAppScreen
{
    public Action? OnBack;

    // Fixed user-facing labels. Held as constants so the ASCII-only rule (the font atlas bakes
    // Latin-1 only, so anything above it draws as '?') can actually be tested.
    internal const string Title = "COMBAT CALCULATOR";
    internal const string SwapLabel = "Swap A <-> B";
    internal const string ChooseUnitLabel = "Choose unit";
    internal const string AttackerBadge = "ATTACKER";
    internal const string DefenderBadge = "DEFENDER";
    internal const string NoUnitsHint = "Choose a unit on both sides.";
    internal const string NoAttackerHint = "Choose the attacking unit on the left.";
    internal const string NoDefenderHint = "Choose the defending unit on the right.";
    internal const string ArmyPrompt = "Choose an army:";
    internal const string JoinHeroLabel = "+ Join a hero";
    internal const string JoinUnitLabel = "+ Join a unit";
    internal const string RemoveJoinLabel = "Remove join";
    internal const string WhichHeroPrompt = "Which hero joins?";
    internal const string WhichUnitPrompt = "Join which unit?";
    internal const string LoadListLabel = "Load list...";
    internal const string ReadOnlyNote =
        "This army carries no book, so its units are shown as they were saved and cannot be changed here.";
    internal const string VariablesHeader = "SITUATION";
    internal const string AssumptionsLabel = "(i) what this does not account for";
    internal const string DistanceLabel = "Distance (in)";
    internal const string CoverLabel = "Defender is in cover";
    internal const string MovedLabel = "Attacker moved this activation";
    internal const string ChargingLabel = "Attacker is charging";
    internal const string FatiguedLabel = "Attacker is fatigued";

    private static readonly Vector4 DimText = new(0.62f, 0.62f, 0.62f, 1f);
    private static readonly Vector4 RuleText = new(0.45f, 0.80f, 0.90f, 1f);
    private static readonly Vector4 WarnText = new(0.90f, 0.80f, 0.35f, 1f);
    private static readonly Vector4 HeadText = new(0.85f, 0.85f, 0.90f, 1f);

    private readonly CalculatorSide _attacker = new();
    private readonly CalculatorSide _defender = new();
    private readonly UnitPicker _attackerPicker = new();
    private readonly UnitPicker _defenderPicker = new();

    private static readonly FileFilter ArmyFilter = new(
        $"FDG Army (*{ArmyListFile.EXTENSION_WITH_PERIOD})",
        new[] { $"*{ArmyListFile.EXTENSION_WITH_PERIOD}" });

    private List<ArmySource>? _armies;

    /// <summary>Armies loaded from disk this session. Shared by both columns, so a list opened for one
    /// side is one click away on the other - the whole point of remembering them.</summary>
    private readonly List<ArmySource> _loaded = new();

    private string? _loadError;
    // Charging defaults ON: in OPR a unit only ever fights in melee because it charged (or because it
    // struck back, which the calculator does not model yet), so an un-charged melee is the rare case.
    private CombatSituation _situation = new(AttackerCharging: true);
    private CombatReport? _report;
    private string _reportKey = string.Empty;

    public CombatCalculatorScreen()
    {
        // Warm the shared parse so the first frame does not stall on ~90 books.
        BookLibrary.LoadAsync();
    }

    internal CombatCalculatorScreen(List<BookFile> armies) =>
        _armies = armies.Select(ArmySource.FromBook).ToList();

    /// <summary>Bundled books first, then anything loaded from disk this session.</summary>
    private IEnumerable<ArmySource> AllArmies => _armies!.Concat(_loaded);

    internal IReadOnlyList<ArmySource> LoadedArmies => _loaded;

    // Test seams: the ImGui layout is hand-verified, the state underneath is not.
    internal CalculatorSide Attacker => _attacker;
    internal CalculatorSide Defender => _defender;
    internal UnitPicker AttackerPicker => _attackerPicker;
    internal UnitPicker DefenderPicker => _defenderPicker;
    internal CombatSituation Situation => _situation;
    internal CombatReport? Report => _report;

    public void Draw(int screenW, int screenH)
    {
        _armies ??= BookLibrary.Load().Select(ArmySource.FromBook).ToList();

        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("##CombatCalculator",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar);

        DrawToolbar();
        ImGui.Separator();
        RefreshReport();
        DrawPanes();

        ImGui.End();
    }

    private void DrawToolbar()
    {
        if (UiButton.Back("Back")) OnBack?.Invoke();

        ImGui.SameLine();
        ImGui.TextColored(HeadText, "   " + Title);

        ImGui.SameLine();
        float swapWidth = ImGui.CalcTextSize(SwapLabel).X + ImGui.GetStyle().FramePadding.X * 4f;
        ImGui.SetCursorPosX(ImGui.GetWindowWidth() - swapWidth - ImGui.GetStyle().WindowPadding.X);
        if (UiButton.NavigateSmall(SwapLabel)) Swap();
    }

    /// <summary>
    /// Which side is still missing, in words. "Choose a unit on both sides" is unhelpful once one side
    /// is filled - it tells the reader to do something they have half done and does not say which half.
    /// Internal so the wording is pinned without a window.
    /// </summary>
    internal static string EmptyStateHint(bool hasAttacker, bool hasDefender) =>
        (hasAttacker, hasDefender) switch
        {
            (false, false) => NoUnitsHint,
            (true, false)  => NoDefenderHint,
            (false, true)  => NoAttackerHint,
            _              => NoUnitsHint,
        };

    /// <summary>Exchanges the two columns, pickers included, so a swap keeps each side's browsing state.</summary>
    internal void Swap()
    {
        var carried = new CalculatorSide();
        carried.AdoptFrom(_attacker);
        _attacker.AdoptFrom(_defender);
        _defender.AdoptFrom(carried);

        (bool attackerOpen, bool defenderOpen) = (_attackerPicker.IsOpen, _defenderPicker.IsOpen);
        if (defenderOpen) _attackerPicker.Open(); else _attackerPicker.Close();
        if (attackerOpen) _defenderPicker.Open(); else _defenderPicker.Close();
    }

    /// <summary>Re-runs the simulation only when something it depends on has actually changed.</summary>
    private void RefreshReport()
    {
        string key = $"{_attacker.Fingerprint()}#{_defender.Fingerprint()}#{_situation}";
        if (key == _reportKey) return;

        _reportKey = key;
        _report = _attacker.HasUnit && _defender.HasUnit
            ? CombatCalculator.Run(_attacker.Compile(), _defender.Compile(), _situation)
            : null;
    }

    private void DrawPanes()
    {
        Vector2 avail = ImGui.GetContentRegionAvail();
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        // 30/40/30: the side columns hold the densest text on the screen (upgrade lines with
        // parenthesised stats and point costs) and were clipping at 27%, while the middle column
        // had the most room and the least text.
        float sideWidth = avail.X * 0.30f;

        // A horizontal scrollbar rather than wrapped upgrade labels: an ImGui checkbox/radio label is a
        // single line by construction, so wrapping one means replacing the control's own label with
        // hand-laid text - a change to the SHARED Forge detail, and so to how the Army Forge itself
        // looks. Out of scope here; this keeps every character reachable without touching that.
        ImGui.BeginChild("##calc-a", new Vector2(sideWidth, avail.Y), ImGuiChildFlags.Borders,
            ImGuiWindowFlags.HorizontalScrollbar);
        DrawSide(_attacker, _attackerPicker, "A", "a");
        ImGui.EndChild();

        ImGui.SameLine(0, spacing);
        ImGui.BeginChild("##calc-middle", new Vector2(avail.X - (sideWidth * 2f) - (spacing * 2f), avail.Y),
            ImGuiChildFlags.Borders);
        DrawMiddle();
        ImGui.EndChild();

        ImGui.SameLine(0, spacing);
        ImGui.BeginChild("##calc-b", new Vector2(0, avail.Y), ImGuiChildFlags.Borders,
            ImGuiWindowFlags.HorizontalScrollbar);
        DrawSide(_defender, _defenderPicker, "B", "b");
        ImGui.EndChild();
    }

    // ---- the two unit columns ----------------------------------------------------------------------

    private void DrawSide(CalculatorSide side, UnitPicker picker, string label, string id)
    {
        if (picker.IsOpen || !side.HasUnit)
        {
            DrawPicker(side, picker, label, id);
            return;
        }

        DrawSideHeader(side, picker, label, id);

        if (!side.IsEditable)
        {
            foreach (UnitFileEntry saved in side.SavedRows)
            {
                ForgeUnitDetail.DrawReadOnly(saved, side.Glossary);
                ImGui.Spacing();
            }
            ImGui.TextColored(DimText, ReadOnlyNote);
            return;
        }

        // A joined pair splits the column into two rows, hero first.
        IReadOnlyList<BuilderUnit> rows = side.Rows();
        for (int row = 0; row < rows.Count; row++)
        {
            if (row > 0)
            {
                ImGui.Spacing();
                ImGui.Separator();
            }
            DrawUnitRow(side, rows[row], id, row);
        }

        DrawJoinControl(side, picker, id);
    }

    /// <summary>
    /// The column's identity line: which side of the fight this is, what it is called, what it costs.
    /// The role badge (not just the letter A/B) is what makes Swap legible - after a swap the badge
    /// moves, so there is never a question of which unit is doing the shooting.
    /// </summary>
    private void DrawSideHeader(CalculatorSide side, UnitPicker picker, string label, string id)
    {
        bool isAttacker = ReferenceEquals(side, _attacker);
        ImGui.TextColored(isAttacker ? ImGuiTheme.HeaderAccent : DimText,
            isAttacker ? AttackerBadge : DefenderBadge);

        ImGui.SameLine();
        ImGui.TextColored(DimText, $"UNIT {label}");

        // Points right-aligned, so the two columns' costs line up against the screen edges and can be
        // compared at a glance rather than read out of the middle of a sentence.
        string points = $"{side.Points} pts";
        float pointsWidth = ImGui.CalcTextSize(points).X;
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - pointsWidth + ImGui.GetCursorPosX()
            - ImGui.GetStyle().ItemSpacing.X);
        ImGui.TextUnformatted(points);

        if (UiButton.NavigateSmall($"{ChooseUnitLabel}##pick-{id}")) picker.Open();
        ImGui.Separator();
    }

    private static void DrawUnitRow(CalculatorSide side, BuilderUnit unit, string id, int row)
    {
        (UnitFileEntry compiled, List<ItemEntry> items) = side.DetailOf(unit);
        bool isMain = ReferenceEquals(unit, side.List.Units[CalculatorSide.MainIndex]);

        ForgeUnitDetail.DrawHeader(compiled);
        ForgeUnitDetail.DrawGear(compiled, items, side.Glossary);

        // Only the main unit can be doubled up; a joined hero is a single model by definition.
        if (isMain && (side.CanCombine || side.IsCombined))
        {
            ImGui.Spacing();
            bool combined = side.IsCombined;
            if (ImGui.Checkbox($"Combined Unit##combine-{id}", ref combined)) side.SetCombined(combined);
        }

        if (side.RosterOf(unit) is { } roster)
        {
            ForgeUnitDetail.DrawUpgrades(side.Book!, side.Glossary, unit, roster, compiled, items,
                isMain ? side.Mirror : null);
        }
    }

    private static void DrawJoinControl(CalculatorSide side, UnitPicker picker, string id)
    {
        if (!side.IsEditable) return;

        ImGui.Spacing();

        if (side.Joined is not null)
        {
            if (UiButton.Back($"{RemoveJoinLabel}##unjoin-{id}")) side.RemoveJoin();
            return;
        }

        bool mainIsHero = side.MainIsHero;
        if (UiButton.NavigateSmall($"{(mainIsHero ? JoinUnitLabel : JoinHeroLabel)}##join-{id}"))
        {
            picker.OpenForJoin(ArmySource.FromBook(side.Book!),
                mainIsHero ? UnitPicker.ERoles.HostsOnly : UnitPicker.ERoles.HeroesOnly);
        }
    }

    private void DrawPicker(CalculatorSide side, UnitPicker picker, string label, string id)
    {
        ImGui.TextColored(DimText, $"UNIT {label}");
        ImGui.Separator();

        if (picker.Level == UnitPicker.ELevel.Units && picker.Army is { } army)
        {
            if (picker.JoinMode)
            {
                // A join stays inside the unit's own army, so there is no army level to step back to.
                if (UiButton.Back($"Cancel##joincancel-{id}")) picker.Close();
                ImGui.SameLine();
                ImGui.TextUnformatted(side.MainIsHero ? WhichUnitPrompt : WhichHeroPrompt);
            }
            else
            {
                if (UiButton.Back($"Back##armies-{id}")) picker.BackToArmies();
                ImGui.SameLine();
                ImGui.TextUnformatted(army.Name);
            }
            ImGui.Separator();

            DrawFilter(picker, id);
            foreach (UnitPicker.Entry entry in UnitPicker.MatchingUnits(army, picker.Filter, picker.Roles))
            {
                // One Selectable covering BOTH lines. Drawing the name as the only hit target left the
                // stat line under it dead - and the stat line is the half carrying what you are choosing
                // on, so clicking it is the natural move and it silently did nothing.
                if (ImGui.Selectable($"##unit-{id}-{entry.Index}", false, ImGuiSelectableFlags.None,
                        new Vector2(0f, ImGui.GetTextLineHeight() * 2f)))
                    Choose(side, picker, army, entry);

                Vector2 rowMin = ImGui.GetItemRectMin();
                ImDrawListPtr dl = ImGui.GetWindowDrawList();
                dl.AddText(rowMin, ImGui.GetColorU32(ImGuiCol.Text), entry.Name);
                dl.AddText(rowMin + new Vector2(ImGui.GetTextLineHeight(), ImGui.GetTextLineHeight()),
                    ImGui.GetColorU32(DimText), entry.StatLine);
            }
            return;
        }

        if (side.HasUnit && UiButton.Back($"Cancel##cancel-{id}")) picker.Close();
        ImGui.TextColored(DimText, ArmyPrompt);

        if (UiButton.NavigateSmall($"{LoadListLabel}##load-{id}")) LoadArmyFromDisk();
        if (_loadError is not null) ImGui.TextColored(WarnText, _loadError);

        DrawFilter(picker, id);

        foreach (ArmySource candidate in UnitPicker.MatchingArmies(AllArmies, picker.Filter))
        {
            string tag = candidate.Path is null ? string.Empty
                : candidate.IsEditable ? "  [saved list]" : "  [saved list, read-only]";
            if (ImGui.Selectable($"{candidate.Name}{tag}##army-{id}-{candidate.Name}"))
                picker.ChooseArmy(candidate);
        }
    }

    /// <summary>Adopt the picked unit, from whichever kind of army it came out of.</summary>
    private static void Choose(CalculatorSide side, UnitPicker picker, ArmySource army, UnitPicker.Entry entry)
    {
        if (army.Book is { } book)
        {
            string rosterId = book.Units[entry.Index].Id;
            if (picker.JoinMode) side.SetJoin(rosterId);
            else side.SetUnit(book, rosterId);
        }
        else if (army.Saved is { } saved)
        {
            // A saved unit brings its own joined hero, so there is no join step to offer.
            side.SetSavedUnit(saved, saved.Units[entry.Index]);
        }

        picker.Close();
    }

    /// <summary>
    /// Open a .fdgarmy and remember it for the rest of the session, so picking a second unit out of it
    /// is a click rather than another trip through the file dialog. A file that cannot be read says so
    /// and changes nothing - it never throws out of Draw.
    /// </summary>
    internal void LoadArmyFromDisk()
    {
        var (canceled, paths) = TinyDialogs.OpenFileDialog("Load Army", ArmyPaths.DefaultDialogPath,
            false, ArmyFilter);
        if (canceled) return;

        string path = paths?.FirstOrDefault() ?? string.Empty;
        if (string.IsNullOrEmpty(path)) return;

        AdoptArmyFile(path);
    }

    /// <summary>The testable half of the load: everything after a path has been chosen.</summary>
    internal bool AdoptArmyFile(string path)
    {
        _loadError = null;

        if (_loaded.Any(army => army.Path == path)) return true; // already listed; nothing to do

        try
        {
            if (!File.Exists(path))
            {
                _loadError = "That file no longer exists.";
                return false;
            }

            // Read as the Forge's derived type so an army it built keeps its selections and book, and
            // can therefore be edited here; a plain list simply leaves those null.
            ArmyListFile? file = JsonSerializer.Deserialize<BuiltArmyFile>(File.ReadAllText(path), RuleJson.Options);
            if (file is null || file.Units.Count == 0)
            {
                _loadError = "That file is empty, or is not an army list.";
                return false;
            }

            _loaded.Add(ArmySource.FromSaved(path, file));
            return true;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            _loadError = "That file could not be read: " + ex.Message;
            return false;
        }
    }

    internal string? LoadError => _loadError;

    private static void DrawFilter(UnitPicker picker, string id)
    {
        ImGui.SetNextItemWidth(-1f);
        string filter = picker.Filter;
        if (ImGui.InputTextWithHint($"##filter-{id}", "Search...", ref filter, 64)) picker.Filter = filter;
    }

    // ---- the middle column -------------------------------------------------------------------------

    private void DrawMiddle()
    {
        if (ImGui.BeginTabBar("##calc-modes"))
        {
            if (ImGui.BeginTabItem("Shooting"))
            {
                SetMode(ECombatMode.Shooting);
                ImGui.EndTabItem();
            }
            if (ImGui.BeginTabItem("Melee"))
            {
                SetMode(ECombatMode.Melee);
                ImGui.EndTabItem();
            }
            ImGui.EndTabBar();
        }

        // The inputs sit directly above the output they change. They used to live in a fixed bottom
        // third of the column, which put a screen-height of empty space between a checkbox and the
        // number it moves - the reader had to remember what they had just toggled.
        DrawSituationBar();
        ImGui.Separator();

        ImGui.BeginChild("##calc-results", Vector2.Zero);
        DrawResults();
        ImGui.EndChild();
    }

    private void SetMode(ECombatMode mode)
    {
        if (_situation.Mode != mode) _situation = _situation with { Mode = mode };
    }

    private void DrawResults()
    {
        if (_report is not { } report)
        {
            ImGui.TextColored(DimText, EmptyStateHint(_attacker.HasUnit, _defender.HasUnit));
            return;
        }

        var view = CombatReportView.From(report);

        DrawHeadline(view);

        foreach (string warning in view.Warnings) ImGui.TextColored(WarnText, "! " + warning);

        ImGui.Separator();

        DrawVolleyTable(view);

        if (view.Notes.Count > 0)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextColored(DimText, AssumptionsLabel);
            if (ImGui.IsItemHovered())
                RuleHoverText.ShowTooltip(string.Join("\n", view.Notes));
        }
    }

    /// <summary>
    /// The answer, before the working. Everything else in this pane explains these two numbers, so they
    /// are the only thing set in the large font and the only thing in the accent colour - the eye lands
    /// here first and can stop here. The wound bar underneath answers the question the numbers do not:
    /// whether that damage actually matters to this defender.
    /// </summary>
    private void DrawHeadline(CombatReportView view)
    {
        ImGui.TextColored(HeadText, view.Headline);
        ImGui.Spacing();

        if (ImGui.BeginTable("##calc-headline", 2, ImGuiTableFlags.SizingStretchSame))
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            DrawBigNumber(view.HitsValue, CombatReportView.HitsCaption);

            ImGui.TableNextColumn();
            DrawBigNumber(view.WoundsValue, CombatReportView.WoundsCaption);

            ImGui.EndTable();
        }

        ImGui.Spacing();
        UiChrome.DrawMeter(view.WoundFractionRemaining, ImGui.GetContentRegionAvail().X, ImGuiTheme.AccentBlue);
        ImGui.TextColored(DimText, view.WoundBarText);
    }

    private static void DrawBigNumber(string value, string caption)
    {
        ImGui.PushFont(RaylibRenderer.LargeFont);
        ImGui.TextColored(ImGuiTheme.HeaderAccent, value);
        ImGui.PopFont();
        ImGui.TextColored(DimText, caption);
    }

    /// <summary>
    /// One row per weapon, in the order the rules resolve it: dice -> hit -> hits -> save -> wounds.
    /// The old pane nested those as an indented tree with arrows, which made the reader reconstruct the
    /// sequence and made two weapons impossible to compare; as columns the same numbers line up and a
    /// six-weapon unit still fits.
    /// </summary>
    private void DrawVolleyTable(CombatReportView view)
    {
        if (!ImGui.BeginTable("##calc-volleys", 6,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp))
            return;

        float num = ImGui.CalcTextSize("00.00").X * 1.6f;
        ImGui.TableSetupColumn("WEAPON", ImGuiTableColumnFlags.WidthStretch, 3f);
        ImGui.TableSetupColumn("DICE", ImGuiTableColumnFlags.WidthFixed, num);
        ImGui.TableSetupColumn("HIT", ImGuiTableColumnFlags.WidthFixed, num * 0.7f);
        ImGui.TableSetupColumn("HITS", ImGuiTableColumnFlags.WidthFixed, num);
        ImGui.TableSetupColumn("SAVE", ImGuiTableColumnFlags.WidthFixed, num);
        ImGui.TableSetupColumn("WOUNDS", ImGuiTableColumnFlags.WidthFixed, num);
        ImGui.TableHeadersRow();

        string? tooltip = null;
        foreach (VolleyRowView row in view.Rows) tooltip ??= DrawVolleyRow(row);

        ImGui.EndTable();
        if (tooltip != null) RuleHoverText.ShowTooltip(tooltip);
    }

    /// <summary>Returns the tooltip body for a hovered rule name, or null.</summary>
    private static string? DrawVolleyRow(VolleyRowView row)
    {
        ImGui.TableNextRow();
        ImGui.TableNextColumn();

        Vector4 nameColor = row.InRange ? new Vector4(1f, 1f, 1f, 1f) : DimText;
        ImGui.TextColored(nameColor, $"{row.CopiesPrefix}{row.Weapon.Name}");

        // The stat subline in the in-game shoot panel's own notation, each rule underlined and hoverable
        // (#292). Drawn on the draw list because a table cell gives no wrapping for SameLine runs.
        uint sub = ImGui.GetColorU32(DimText);
        uint ruleCol = ImGui.GetColorU32(row.InRange ? RuleText : DimText);
        string? hovered = RuleHoverText.DrawInline(ImGui.GetWindowDrawList(), ImGui.GetCursorScreenPos(),
            RuleHoverText.WeaponStatLine(row.Weapon), sub, ruleCol, ImGui.IsWindowHovered());
        ImGui.Dummy(new Vector2(0f, ImGui.GetTextLineHeight()));

        if (!row.InRange)
        {
            ImGui.TableNextColumn();
            ImGui.TextColored(DimText, row.OutOfRangeText);
            return hovered;
        }

        Cell(row.Dice);
        Cell(row.Hit);
        Cell(row.Hits);
        Cell(row.Save);
        Cell(row.Wounds, ImGuiTheme.HeaderAccent);

        DrawVolleyDetail(row);
        return hovered;
    }

    private static void Cell(string text, Vector4? color = null)
    {
        ImGui.TableNextColumn();
        if (color is { } c) ImGui.TextColored(c, text); else ImGui.TextUnformatted(text);
    }

    /// <summary>
    /// The working, under the row it explains: the modifier chips that produced each threshold, and the
    /// save split when a rule really did make one. Chip wording arrives verbatim from the engine's own
    /// composers, so it matches the dice beats the player sees in a live game.
    /// </summary>
    private static void DrawVolleyDetail(VolleyRowView row)
    {
        bool splitSaves = row.SaveLines.Count > 1 || row.SaveLines.Any(s => s.Sources.Count > 0);
        if (row.HitChips.Count == 0 && row.SaveChips.Count == 0 && !splitSaves && row.Notes.Count == 0)
            return;

        ImGui.TableNextRow();
        ImGui.TableNextColumn();
        ImGui.Indent();

        if (row.HitChips.Count > 0) DrawChipLine("to hit", row.HitChips, row.Hit);
        if (row.SaveChips.Count > 0) DrawChipLine("save", row.SaveChips, row.Save);

        if (splitSaves)
            foreach (SaveLineView save in row.SaveLines)
                ImGui.TextColored(DimText, $"   {save.SaveNeeded}+  {save.Describe()}");

        foreach (string note in row.Notes) ImGui.TextColored(DimText, note);

        ImGui.Unindent();
    }

    private static void DrawChipLine(string label, IReadOnlyList<string> chips, string result)
    {
        ImGui.TextColored(DimText, label);
        foreach (string chip in chips)
        {
            ImGui.SameLine();
            UiChrome.DrawChip(chip);
        }
        ImGui.SameLine();
        ImGui.TextColored(DimText, "->");
        ImGui.SameLine();
        ImGui.TextUnformatted(result);
    }


    private void DrawSituationBar()
    {
        ImGui.AlignTextToFramePadding();
        ImGui.TextColored(DimText, VariablesHeader);
        ImGui.SameLine();

        if (_situation.Mode == ECombatMode.Shooting) DrawShootingSituation();
        else DrawMeleeSituation();
    }

    private void DrawShootingSituation()
    {
        float distance = _situation.DistanceInches;

        // The slider is the point: dragging it walks the whole table through its thresholds at once, so
        // a range-gated rule (Stealth at 9in, a weapon running out of reach) shows itself instead of
        // waiting to be guessed. Ticks mark where this fight's weapons stop reaching.
        float sliderWidth = MathF.Max(160f, ImGui.GetContentRegionAvail().X * 0.35f);
        ImGui.SetNextItemWidth(sliderWidth);
        if (ImGui.SliderFloat("##calc-distance", ref distance, 0f, MaxDistanceInches, "%.1f"))
            _situation = _situation with { DistanceInches = Math.Clamp(distance, 0f, MaxDistanceInches) };
        DrawRangeTicks();

        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGui.CalcTextSize("00.0").X * 3.2f);
        float typed = _situation.DistanceInches;
        if (ImGui.InputFloat(DistanceLabel, ref typed, 0.5f, 1f, "%.1f"))
            _situation = _situation with { DistanceInches = Math.Clamp(typed, 0f, MaxDistanceInches) };

        ImGui.SameLine(0f, ImGui.GetTextLineHeight() * 1.5f);
        bool cover = _situation.DefenderInCover;
        if (ImGui.Checkbox(CoverLabel, ref cover)) _situation = _situation with { DefenderInCover = cover };

        ImGui.SameLine(0f, ImGui.GetTextLineHeight());
        bool moved = _situation.AttackerMoved;
        if (ImGui.Checkbox(MovedLabel, ref moved)) _situation = _situation with { AttackerMoved = moved };
    }

    private void DrawMeleeSituation()
    {
        bool charging = _situation.AttackerCharging;
        if (ImGui.Checkbox(ChargingLabel, ref charging))
            _situation = _situation with { AttackerCharging = charging };

        ImGui.SameLine(0f, ImGui.GetTextLineHeight());
        bool fatigued = _situation.AttackerFatigued;
        if (ImGui.Checkbox(FatiguedLabel, ref fatigued))
            _situation = _situation with { AttackerFatigued = fatigued };
    }

    /// <summary>
    /// A tick on the distance slider at every range this fight's weapons actually reach, so the player
    /// can see where the next row drops out before dragging past it.
    /// </summary>
    private void DrawRangeTicks()
    {
        if (_report is not { } report) return;

        Vector2 min = ImGui.GetItemRectMin();
        Vector2 max = ImGui.GetItemRectMax();
        float span = max.X - min.X;
        if (span <= 0f) return;

        ImDrawListPtr dl = ImGui.GetWindowDrawList();
        uint tick = ImGui.GetColorU32(DimText);

        foreach (float range in RangeTicks(report))
        {
            float x = min.X + (range / MaxDistanceInches * span);
            dl.AddLine(new Vector2(x, min.Y), new Vector2(x, min.Y + (max.Y - min.Y) * 0.28f), tick);
        }
    }

    /// <summary>The distinct weapon reaches worth marking. Internal so the arithmetic can be tested
    /// without a window.</summary>
    internal static IReadOnlyList<float> RangeTicks(CombatReport report) =>
        report.Volleys
            .Select(v => v.EffectiveRangeInches)
            .Where(r => r > 0f && r <= MaxDistanceInches)
            .Distinct()
            .OrderBy(r => r)
            .ToList();

    /// <summary>The longest range the slider offers - the long edge of a standard table, past which no
    /// weapon in the corpus reaches and the answer is always "out of range".</summary>
    internal const float MaxDistanceInches = 48f;
}
