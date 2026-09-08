using System.IO;
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
    internal const string NoUnitsHint = "Choose a unit on both sides.";
    internal const string ArmyPrompt = "Choose an army:";
    internal const string JoinHeroLabel = "+ Join a hero";
    internal const string JoinUnitLabel = "+ Join a unit";
    internal const string RemoveJoinLabel = "Remove join";
    internal const string WhichHeroPrompt = "Which hero joins?";
    internal const string WhichUnitPrompt = "Join which unit?";
    internal const string LoadListLabel = "Load list...";
    internal const string ReadOnlyNote =
        "This army carries no book, so its units are shown as they were saved and cannot be changed here.";
    internal const string VariablesHeader = "VARIABLES";
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
        float sideWidth = avail.X * 0.27f;

        ImGui.BeginChild("##calc-a", new Vector2(sideWidth, avail.Y), ImGuiChildFlags.Borders);
        DrawSide(_attacker, _attackerPicker, "A", "a");
        ImGui.EndChild();

        ImGui.SameLine(0, spacing);
        ImGui.BeginChild("##calc-middle", new Vector2(avail.X - (sideWidth * 2f) - (spacing * 2f), avail.Y),
            ImGuiChildFlags.Borders);
        DrawMiddle();
        ImGui.EndChild();

        ImGui.SameLine(0, spacing);
        ImGui.BeginChild("##calc-b", new Vector2(0, avail.Y), ImGuiChildFlags.Borders);
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

        if (UiButton.NavigateSmall($"{ChooseUnitLabel}##pick-{id}")) picker.Open();
        ImGui.SameLine();
        ImGui.TextColored(DimText, $"UNIT {label}   {side.Points} pts");
        ImGui.Separator();

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
                if (ImGui.Selectable($"{entry.Name}##unit-{id}-{entry.Index}")) Choose(side, picker, army, entry);
                ImGui.Indent();
                ImGui.TextColored(DimText, entry.StatLine);
                ImGui.Unindent();
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

        Vector2 avail = ImGui.GetContentRegionAvail();
        float variablesHeight = avail.Y / 3f;

        ImGui.BeginChild("##calc-results", new Vector2(0, avail.Y - variablesHeight - ImGui.GetStyle().ItemSpacing.Y));
        DrawResults();
        ImGui.EndChild();

        ImGui.Separator();
        ImGui.BeginChild("##calc-variables", new Vector2(0, 0));
        DrawVariables();
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
            ImGui.TextColored(DimText, NoUnitsHint);
            return;
        }

        var view = CombatReportView.From(report);

        ImGui.TextColored(HeadText, view.Headline);
        ImGui.Text($"{CombatReportView.HitsCaption} {view.HitsValue}      " +
            $"{CombatReportView.WoundsCaption} {view.WoundsValue}");
        ImGui.TextColored(DimText, view.WoundBarText);

        foreach (string warning in view.Warnings) ImGui.TextColored(WarnText, "! " + warning);

        ImGui.Separator();

        foreach (VolleyRowView row in view.Rows) DrawVolley(row);

        if (view.Notes.Count > 0)
        {
            ImGui.Spacing();
            ImGui.Separator();
            foreach (string note in view.Notes) ImGui.TextColored(DimText, note);
        }
    }

    private static void DrawVolley(VolleyRowView row)
    {
        ImGui.Spacing();
        ImGui.TextUnformatted($"{row.CopiesPrefix}{row.Weapon.Name}");
        ImGui.SameLine();
        ImGui.TextColored(DimText, "(" + WeaponStats(row.Weapon) + ")");

        foreach (RuleHoverText.Segment rule in RuleHoverText.RuleSegments(row.Weapon))
        {
            ImGui.SameLine();
            ImGui.TextColored(RuleText, rule.Text);
            if (ImGui.IsItemHovered()) RuleHoverText.ShowTooltip(RuleHoverText.Tooltip(rule));
        }

        ImGui.Indent();
        if (!row.InRange)
        {
            ImGui.TextColored(DimText, row.OutOfRangeText);
            ImGui.Unindent();
            return;
        }

        ImGui.TextUnformatted($"Hit {row.Hit}   {Chips(row.HitChips)}");
        ImGui.SameLine();
        ImGui.TextColored(DimText, $"-> {row.Hits} hits from {row.Dice} dice");

        foreach (SaveLineView save in row.SaveLines)
            ImGui.TextUnformatted($"Save {save.SaveNeeded}+   {save.Describe()}");

        if (row.SaveChips.Count > 0) ImGui.TextColored(DimText, Chips(row.SaveChips));
        ImGui.TextUnformatted($"-> {row.Wounds} wounds");

        foreach (string note in row.Notes) ImGui.TextColored(DimText, note);
        ImGui.Unindent();
    }

    /// <summary>The weapon's numbers, in the notation the in-game shoot panel uses.</summary>
    private static string WeaponStats(IWeapon weapon) =>
        $"{CombatReportView.Inches(weapon.RangeInches)}, A{weapon.Attacks} AP{weapon.ArmorPenetration}";

    private static string Chips(IReadOnlyList<string> chips) =>
        chips.Count == 0 ? string.Empty : "[" + string.Join(", ", chips) + "]";

    private void DrawVariables()
    {
        ImGui.TextColored(DimText, VariablesHeader);
        ImGui.Separator();

        if (_situation.Mode == ECombatMode.Shooting)
        {
            float distance = _situation.DistanceInches;
            ImGui.SetNextItemWidth(160f);
            if (ImGui.InputFloat(DistanceLabel, ref distance, 0.5f, 1f, "%.1f"))
                _situation = _situation with { DistanceInches = MathF.Max(0f, distance) };

            bool cover = _situation.DefenderInCover;
            if (ImGui.Checkbox(CoverLabel, ref cover))
                _situation = _situation with { DefenderInCover = cover };

            bool moved = _situation.AttackerMoved;
            if (ImGui.Checkbox(MovedLabel, ref moved))
                _situation = _situation with { AttackerMoved = moved };
        }
        else
        {
            bool charging = _situation.AttackerCharging;
            if (ImGui.Checkbox(ChargingLabel, ref charging))
                _situation = _situation with { AttackerCharging = charging };

            bool fatigued = _situation.AttackerFatigued;
            if (ImGui.Checkbox(FatiguedLabel, ref fatigued))
                _situation = _situation with { AttackerFatigued = fatigued };
        }
    }
}
