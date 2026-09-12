using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using FDG;
using FDG.ArmyBuilding;
using FdgRaylib.Config;
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
    internal const string HeroTag = "HERO";
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
    internal const string BackLabel = "Back";
    internal const string CancelLabel = "Cancel";
    internal const string DistanceLabel = "Distance (in)";
    internal const string CoverLabel = "Defender is in cover";
    internal const string MovedLabel = "Attacker moved this activation";
    internal const string ChargingLabel = "Attacker is charging";
    internal const string FatiguedLabel = "Attacker is fatigued";

    private static readonly Vector4 DimText = new(0.62f, 0.62f, 0.62f, 1f);
    private static readonly Vector4 BrightText = new(1f, 1f, 1f, 1f);
    private static readonly Vector4 WarnText = new(0.90f, 0.80f, 0.35f, 1f);
    private static readonly Vector4 HeadText = new(0.85f, 0.85f, 0.90f, 1f);
    private static readonly Vector4 Transparent = Vector4.Zero;

    private readonly SideTabs _attackerTabs = new();
    private readonly SideTabs _defenderTabs = new();
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
    // Taken with the report, not per frame: the points must be the DEFENDER'S as the report saw them,
    // and pricing a list costs a compile, which is not a thing to do sixty times a second for a number
    // that only moves when the fingerprint does.
    private int _reportDefenderPoints;

    public CombatCalculatorScreen()
    {
        // Warm the shared parse so the first frame does not stall on ~90 books.
        BookLibrary.LoadAsync();
        RestoreSystems(UserConfig.Current.CalculatorSystemA, UserConfig.Current.CalculatorSystemB);
    }

    /// <summary>Puts each side back on the system it was last left browsing. An unrecognised slug (a
    /// hand-edited config, a system this build no longer ships) falls back to the default rather than
    /// leaving a side pointed at an empty list.</summary>
    internal void RestoreSystems(string? a, string? b)
    {
        _attackerPicker.GameSystem = KnownSystem(a);
        _defenderPicker.GameSystem = KnownSystem(b);
    }

    internal static string KnownSystem(string? slug) =>
        Systems.Any(sys => GameSystems.SameSystem(sys.Slug, slug))
            ? GameSystems.Normalize(slug)
            : GameSystems.GrimdarkFuture;

    internal CombatCalculatorScreen(List<BookFile> armies) =>
        _armies = armies.Select(ArmySource.FromBook).ToList();

    /// <summary>Bundled books first, then anything loaded from disk this session.</summary>
    private IEnumerable<ArmySource> AllArmies => _armies!.Concat(_loaded);

    internal IReadOnlyList<ArmySource> LoadedArmies => _loaded;

    // Test seams: the ImGui layout is hand-verified, the state underneath is not.
    internal CalculatorSide Attacker => _attackerTabs.Current;
    internal CalculatorSide Defender => _defenderTabs.Current;
    internal SideTabs AttackerTabs => _attackerTabs;
    internal SideTabs DefenderTabs => _defenderTabs;
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
        if (UiButton.Back(BackLabel, UiChrome.ButtonSize(BackLabel))) OnBack?.Invoke();

        ImGui.SameLine(0f, ImGui.GetFontSize() * 1.5f);
        UiText.Colored(HeadText, Title);

        ImGui.SameLine();
        Vector2 swap = UiChrome.ButtonSize(SwapLabel);
        ImGui.SetCursorPosX(ImGui.GetWindowWidth() - swap.X - ImGui.GetStyle().WindowPadding.X);
        if (UiButton.Navigate(SwapLabel, swap)) Swap();
    }

    /// <summary>A back/cancel button at the shared chrome size - big enough to aim at without looking,
    /// and sized from the font so it is the same button on a laptop and on a 4K display. The label is
    /// measured, the "##id" suffix is not.</summary>
    private static bool BackButton(string label, string id) =>
        UiButton.Back($"{label}##{id}", UiChrome.ButtonSize(label));

    /// <inheritdoc cref="BackButton"/>
    private static bool NavButton(string label, string id) =>
        UiButton.Navigate($"{label}##{id}", UiChrome.ButtonSize(label));

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

    /// <summary>Exchanges the two columns - every tab, not just the selected one - pickers included, so
    /// a swap keeps each side's browsing state.</summary>
    internal void Swap()
    {
        _attackerTabs.SwapWith(_defenderTabs);

        (bool attackerOpen, bool defenderOpen) = (_attackerPicker.IsOpen, _defenderPicker.IsOpen);
        if (defenderOpen) _attackerPicker.Open(); else _attackerPicker.Close();
        if (attackerOpen) _defenderPicker.Open(); else _defenderPicker.Close();
    }

    /// <summary>Re-runs the simulation only when something it depends on has actually changed.</summary>
    private void RefreshReport()
    {
        string key = $"{Attacker.Fingerprint()}#{Defender.Fingerprint()}#{_situation}";
        if (key == _reportKey) return;

        _reportKey = key;
        _report = Attacker.HasUnit && Defender.HasUnit
            ? CombatCalculator.Run(Attacker.Compile(), Defender.Compile(), _situation)
            : null;
        _reportDefenderPoints = _report is null ? 0 : Defender.Points;
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
        // No horizontal scrollbar: the upgrade labels wrap now (#398), so there is nothing to scroll
        // sideways to - and a horizontally scrollable child reports a content width that the wrap would
        // have had to fight.
        ImGui.BeginChild("##calc-a", new Vector2(sideWidth, avail.Y), ImGuiChildFlags.Borders);
        DrawSide(_attackerTabs, _attackerPicker, "A", "a", isAttacker: true);
        ImGui.EndChild();

        ImGui.SameLine(0, spacing);
        ImGui.BeginChild("##calc-middle", new Vector2(avail.X - (sideWidth * 2f) - (spacing * 2f), avail.Y),
            ImGuiChildFlags.Borders);
        DrawMiddle();
        ImGui.EndChild();

        ImGui.SameLine(0, spacing);
        ImGui.BeginChild("##calc-b", new Vector2(0, avail.Y), ImGuiChildFlags.Borders);
        DrawSide(_defenderTabs, _defenderPicker, "B", "b", isAttacker: false);
        ImGui.EndChild();
    }

    // ---- the two unit columns ----------------------------------------------------------------------

    private void DrawSide(SideTabs tabs, UnitPicker picker, string label, string id, bool isAttacker)
    {
        DrawTabStrip(tabs, picker, id);
        CalculatorSide side = tabs.Current;

        if (picker.IsOpen || !side.HasUnit)
        {
            DrawPicker(side, picker, label, id);
            return;
        }

        DrawSideHeader(side, picker, label, id, isAttacker);

        if (!side.IsEditable)
        {
            foreach (UnitFileEntry saved in side.SavedRows)
            {
                ForgeUnitDetail.DrawReadOnly(saved, side.Glossary);
                ImGui.Spacing();
            }
            UiText.Colored(DimText, ReadOnlyNote);
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
    /// The column's tabs: one per candidate unit, plus a "+" that copies the one in hand. Only the
    /// selected tab fights, so this is the cheapest way to ask "and what if it were this instead".
    ///
    /// <para>The tab bar owns the selection - whichever tab ImGui reports as open is the one adopted,
    /// rather than driving ImGui from our own index and having two sources of truth disagree on the
    /// frame a tab is added or closed.</para>
    ///
    /// <para>Closing: an "x" on the tab, absent while only one tab remains (nothing can live in a
    /// column with no tabs). Middle-click closing is turned OFF deliberately - it is the standard
    /// browser gesture, but it is also invisible, and there is no undo here: a stray middle-click would
    /// silently throw away a unit someone had spent a minute configuring. The "x" is deliberate, so it
    /// needs no confirm.</para>
    /// </summary>
    private static void DrawTabStrip(SideTabs tabs, UnitPicker picker, string id)
    {
        float em = ImGui.GetFontSize();
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(em * 0.6f, em * 0.3f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing, new Vector2(em * 0.5f, em * 0.25f));
        // The tab in play takes the accent. Selected tabs and buttons used to be the same raised grey,
        // which left the reader working out from context which of two grey blocks they were looking at.
        ImGui.PushStyleColor(ImGuiCol.Tab, ImGuiTheme.TabIdle);
        ImGui.PushStyleColor(ImGuiCol.TabHovered, ImGuiTheme.ButtonGoHovered);
        ImGui.PushStyleColor(ImGuiCol.TabSelected, ImGuiTheme.AccentBlue);

        bool barOpen = ImGui.BeginTabBar($"##tabs-{id}",
            ImGuiTabBarFlags.AutoSelectNewTabs | ImGuiTabBarFlags.NoCloseWithMiddleMouseButton);

        if (!barOpen)
        {
            ImGui.PopStyleColor(3);
            ImGui.PopStyleVar(2);
            return;
        }

        int closing = -1;
        for (int i = 0; i < tabs.Slots.Count; i++)
        {
            SideTabs.Slot slot = tabs.Slots[i];
            string label = $"{SideTabs.TabLabel(slot.Side)}##{id}-tab-{slot.Key}";
            bool open = true;

            bool selected = tabs.Slots.Count > 1
                ? ImGui.BeginTabItem(label, ref open)
                : ImGui.BeginTabItem(label);

            if (selected)
            {
                // Switching tabs is navigation: a picker left half-open on the way out would otherwise
                // greet the next unit with a browser it never asked for.
                if (tabs.Active != i) picker.Close();
                tabs.Select(i);
                ImGui.EndTabItem();
            }
            if (!open) closing = i;
        }

        if (ImGui.TabItemButton($"+##{id}-add", ImGuiTabItemFlags.Trailing | ImGuiTabItemFlags.NoTooltip))
            tabs.Duplicate();

        ImGui.EndTabBar();
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar(2);
        ImGui.Spacing();

        if (closing >= 0) tabs.Close(closing);
    }

    /// <summary>
    /// The column's identity line: which side of the fight this is, what it is called, what it costs.
    /// The role badge (not just the letter A/B) is what makes Swap legible - after a swap the badge
    /// moves, so there is never a question of which unit is doing the shooting.
    /// </summary>
    private void DrawSideHeader(CalculatorSide side, UnitPicker picker, string label, string id,
        bool isAttacker)
    {
        UiText.Colored(isAttacker ? ImGuiTheme.HeaderAccent : DimText,
            isAttacker ? AttackerBadge : DefenderBadge);

        ImGui.SameLine();
        UiText.Colored(DimText, $"UNIT {label}");

        // Points right-aligned, so the two columns' costs line up against the screen edges and can be
        // compared at a glance rather than read out of the middle of a sentence.
        string points = $"{side.Points} pts";
        float pointsWidth = ImGui.CalcTextSize(points).X;
        ImGui.SameLine(ImGui.GetContentRegionAvail().X - pointsWidth + ImGui.GetCursorPosX()
            - ImGui.GetStyle().ItemSpacing.X);
        ImGui.TextUnformatted(points);

        ImGui.Spacing();
        if (NavButton(ChooseUnitLabel, $"pick-{id}")) picker.Open();
        ImGui.Spacing();
        ImGui.Separator();
    }

    private static void DrawUnitRow(CalculatorSide side, BuilderUnit unit, string id, int row)
    {
        (UnitFileEntry compiled, List<ItemEntry> items) = side.DetailOf(unit);
        bool isMain = ReferenceEquals(unit, side.List.Units[CalculatorSide.MainIndex]);

        // Rows() puts the hero first whenever a pair is joined, so row 0 of a joined column IS the hero.
        // Tagged in #227's gold, the same colour the printed army list marks a hero with.
        if (side.Joined is not null && row == 0)
            UiText.Colored(ImGuiTheme.HeroGold, HeroTag);

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
            if (BackButton(RemoveJoinLabel, $"unjoin-{id}")) side.RemoveJoin();
            return;
        }

        bool mainIsHero = side.MainIsHero;
        if (NavButton(mainIsHero ? JoinUnitLabel : JoinHeroLabel, $"join-{id}"))
        {
            picker.OpenForJoin(ArmySource.FromBook(side.Book!),
                mainIsHero ? UnitPicker.ERoles.HostsOnly : UnitPicker.ERoles.HeroesOnly);
        }
    }

    private void DrawPicker(CalculatorSide side, UnitPicker picker, string label, string id)
    {
        UiText.Colored(DimText, $"UNIT {label}");
        ImGui.Separator();

        if (picker.Level == UnitPicker.ELevel.Units && picker.Army is { } army)
        {
            if (picker.JoinMode)
            {
                // A join stays inside the unit's own army, so there is no army level to step back to.
                if (BackButton(CancelLabel, $"joincancel-{id}")) picker.Close();
                ImGui.SameLine(0f, ImGui.GetFontSize());
                ImGui.TextUnformatted(side.MainIsHero ? WhichUnitPrompt : WhichHeroPrompt);
            }
            else
            {
                if (BackButton(BackLabel, $"armies-{id}")) picker.BackToArmies();
                ImGui.SameLine(0f, ImGui.GetFontSize());
                ImGui.TextUnformatted(army.Name);
            }
            ImGui.Spacing();
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

                // #227's gold, the colour a hero is marked in on the printed list and in the unit
                // columns - so the tag teaches nothing new, it just applies here too.
                if (entry.IsHero)
                    dl.AddText(
                        rowMin + new Vector2(ImGui.CalcTextSize(entry.Name).X + ImGui.GetFontSize() * 0.6f, 0f),
                        ImGui.GetColorU32(ImGuiTheme.HeroGold), HeroTag);
            }
            return;
        }

        if (side.HasUnit && BackButton(CancelLabel, $"cancel-{id}")) picker.Close();
        ImGui.Spacing();
        UiText.Colored(DimText, ArmyPrompt);

        if (NavButton(LoadListLabel, $"load-{id}")) LoadArmyFromDisk();
        if (_loadError is not null) UiText.Colored(WarnText, _loadError);

        DrawSystemToggle(picker, id);
        DrawFilter(picker, id);

        foreach (ArmySource candidate in UnitPicker.MatchingArmies(AllArmies, picker.Filter, picker.GameSystem))
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

    /// <summary>
    /// #398: Grimdark Future or Age of Fantasy, one or the other. Two exclusive buttons rather than a
    /// dropdown - there are exactly two, so a combo would cost a click to show what a pair of buttons
    /// shows for free. The choice is per side and is written straight back to the user config, so it is
    /// still there next launch.
    /// </summary>
    private void DrawSystemToggle(UnitPicker picker, string id)
    {
        for (int i = 0; i < Systems.Length; i++)
        {
            if (i > 0) ImGui.SameLine(0f, ImGui.GetFontSize() * 0.5f);
            (string label, string slug) = Systems[i];
            bool active = GameSystems.SameSystem(picker.GameSystem, slug);

            if (active) ImGui.PushStyleColor(ImGuiCol.Button, ImGuiTheme.AccentBlue);
            if (ImGui.Button($"{label}##sys-{id}")) SetSystem(picker, slug);
            if (active) ImGui.PopStyleColor();
        }
    }

    private void SetSystem(UnitPicker picker, string slug)
    {
        if (GameSystems.SameSystem(picker.GameSystem, slug)) return;

        picker.GameSystem = slug;
        picker.BackToArmies();

        if (ReferenceEquals(picker, _attackerPicker)) UserConfig.Current.CalculatorSystemA = slug;
        else UserConfig.Current.CalculatorSystemB = slug;
        UserConfig.Save();
    }

    /// <summary>The two OPR systems, labelled as the Army Forge labels them.</summary>
    private static readonly (string Label, string Slug)[] Systems =
    {
        ("Grimdark Future", GameSystems.GrimdarkFuture),
        ("Age of Fantasy", GameSystems.AgeOfFantasy),
    };

    private static void DrawFilter(UnitPicker picker, string id)
    {
        ImGui.SetNextItemWidth(-1f);
        string filter = picker.Filter;
        if (ImGui.InputTextWithHint($"##filter-{id}", "Search...", ref filter, 64)) picker.Filter = filter;
    }

    // ---- the middle column -------------------------------------------------------------------------

    private void DrawMiddle()
    {
        DrawModeTabs();

        // The inputs sit directly above the output they change. They used to live in a fixed bottom
        // third of the column, which put a screen-height of empty space between a checkbox and the
        // number it moves - the reader had to remember what they had just toggled.
        DrawSituationBar();
        ImGui.Separator();

        ImGui.BeginChild("##calc-results", Vector2.Zero);
        DrawResults();
        ImGui.EndChild();
    }

    /// <summary>
    /// Shooting or Melee - the one control that changes what every other number on the screen means, so
    /// it is drawn at the size that says so: the large font, real padding, and the accent on the tab in
    /// play. At body size in the default tab colours it was the quietest thing in the column.
    /// </summary>
    private void DrawModeTabs()
    {
        // Measured in the BODY font, before the large one is pushed - padding sized in 32px ems would
        // swallow the column.
        float em = ImGui.GetFontSize();

        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(em * 1.1f, em * 0.45f));
        ImGui.PushStyleVar(ImGuiStyleVar.ItemInnerSpacing, new Vector2(em * 0.9f, em * 0.25f));
        ImGui.PushStyleColor(ImGuiCol.Tab, ImGuiTheme.TabIdle);
        ImGui.PushStyleColor(ImGuiCol.TabHovered, ImGuiTheme.ButtonGoHovered);
        ImGui.PushStyleColor(ImGuiCol.TabSelected, ImGuiTheme.AccentBlue);
        ImGui.PushFont(RaylibRenderer.LargeFont);

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

        ImGui.PopFont();
        ImGui.PopStyleColor(3);
        ImGui.PopStyleVar(2);
        ImGui.Spacing();
    }

    private void SetMode(ECombatMode mode)
    {
        if (_situation.Mode != mode) _situation = _situation with { Mode = mode };
    }

    private void DrawResults()
    {
        if (_report is not { } report)
        {
            UiText.Colored(DimText, EmptyStateHint(Attacker.HasUnit, Defender.HasUnit));
            return;
        }

        var view = CombatReportView.From(report, _reportDefenderPoints);

        DrawHeadline(view);

        foreach (string warning in view.Warnings) UiText.Colored(WarnText, "! " + warning);

        ImGui.Separator();

        DrawVolleyTable(view);
    }

    /// <summary>
    /// The answer, before the working. Everything else in this pane explains these two numbers, so they
    /// are the only thing set in the large font and the only thing in the accent colour - the eye lands
    /// here first and can stop here. The wound bar underneath answers the question the numbers do not:
    /// whether that damage actually matters to this defender.
    /// </summary>
    private void DrawHeadline(CombatReportView view)
    {
        UiText.Colored(HeadText, view.Headline);
        ImGui.Spacing();

        // Two rows of two rather than one row of four: four large numbers and their captions do not fit
        // across this column at a laptop width, and the pairing is meaningful anyway - what the dice did
        // on top, what it cost the defender underneath.
        if (ImGui.BeginTable("##calc-headline", 2, ImGuiTableFlags.SizingStretchSame))
        {
            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            DrawBigNumber(view.HitsValue, CombatReportView.HitsCaption);

            ImGui.TableNextColumn();
            // Amber for wounds, blue for hits: the two headline numbers are different KINDS of thing
            // (dice that landed vs damage that stuck), and the colour ties the wounds figure to the
            // amber slice of the meter below and to the WOUNDS column in the table.
            DrawBigNumber(view.WoundsValue, CombatReportView.WoundsCaption, ImGuiTheme.DamageAmber);

            ImGui.TableNextRow();

            ImGui.TableNextColumn();
            DrawBigNumber(view.HealthValue, CombatReportView.HealthCaption, ImGuiTheme.DamageAmber);

            ImGui.TableNextColumn();
            DrawBigNumber(view.PointsValue, CombatReportView.PointsCaption, ImGuiTheme.DamageAmber);

            ImGui.EndTable();
        }

        ImGui.Spacing();
        UiChrome.DrawMeter(view.WoundFractionRemaining, ImGui.GetContentRegionAvail().X,
            ImGuiTheme.AccentBlue, ImGuiTheme.DamageAmber);
        UiText.Colored(DimText, view.WoundBarText);
    }

    private static void DrawBigNumber(string value, string caption, Vector4? color = null)
    {
        ImGui.PushFont(RaylibRenderer.LargeFont);
        UiText.Colored(color ?? ImGuiTheme.HeaderAccent, value);
        ImGui.PopFont();
        UiText.Colored(DimText, caption);
    }

    /// <summary>
    /// One row per weapon, in the order the rules resolve it: dice -> hit -> hits -> save -> wounds.
    /// The old pane nested those as an indented tree with arrows, which made the reader reconstruct the
    /// sequence and made two weapons impossible to compare; as columns the same numbers line up and a
    /// six-weapon unit still fits.
    /// </summary>
    private void DrawVolleyTable(CombatReportView view)
    {
        if (!ImGui.BeginTable("##calc-volleys", 8,
            ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerH | ImGuiTableFlags.SizingStretchProp))
            return;

        float num = ImGui.CalcTextSize("00.00").X * 1.6f;
        ImGui.TableSetupColumn("WEAPON", ImGuiTableColumnFlags.WidthStretch, 3f);
        ImGui.TableSetupColumn("DICE", ImGuiTableColumnFlags.WidthFixed, num);
        ImGui.TableSetupColumn("HIT", ImGuiTableColumnFlags.WidthFixed, num * 0.7f);
        ImGui.TableSetupColumn("HITS", ImGuiTableColumnFlags.WidthFixed, num);
        ImGui.TableSetupColumn("SAVE", ImGuiTableColumnFlags.WidthFixed, num);
        ImGui.TableSetupColumn("WOUNDS", ImGuiTableColumnFlags.WidthFixed, num);
        // The two derived columns, abbreviated here and spelled out under the headline numbers they
        // total up to ("Health removed", "Points of damage"), which is where the reader meets them first.
        ImGui.TableSetupColumn("%HP", ImGuiTableColumnFlags.WidthFixed, num);
        ImGui.TableSetupColumn("PTS", ImGuiTableColumnFlags.WidthFixed, num);
        ImGui.TableHeadersRow();

        // Every row is drawn, and the FIRST hovered rule wins the frame's tooltip. Written out longhand
        // because the compact form - `tooltip ??= DrawVolleyRow(row)` - short-circuits: once a rule was
        // hovered the call itself stopped being evaluated, so hovering a rule on the second of three
        // weapons made the third weapon vanish for as long as the tooltip was up.
        string? tooltip = null;
        foreach (VolleyRowView row in view.Rows)
        {
            string? hovered = DrawVolleyRow(row);
            tooltip ??= hovered;
        }

        ImGui.EndTable();
        if (tooltip != null) RuleHoverText.ShowTooltip(tooltip);
    }

    /// <summary>Returns the tooltip body for a hovered rule name, or null.</summary>
    private static string? DrawVolleyRow(VolleyRowView row)
    {
        ImGui.TableNextRow();

        // A weapon that cannot reach is switched off, and says so three ways at once: a well behind the
        // row, the disabled alpha over everything in it, and the darker rule blue. Greyed text alone was
        // too close to the ordinary dim text this pane is full of. The alpha reaches the hand-painted
        // subline too - GetColorU32 multiplies by style.Alpha - so the whole row recedes together.
        if (!row.InRange)
        {
            ImGui.TableSetBgColor(ImGuiTableBgTarget.RowBg0, ImGui.GetColorU32(ImGuiTheme.DisabledRowBg));
            ImGui.BeginDisabled();
        }

        ImGui.TableNextColumn();
        // One colour for the name in both states: the disabled alpha above is what dims an out-of-range
        // row, and dimming the colour as WELL left the weapon's own name barely legible - which is not
        // "greyed out", it is "gone".
        UiText.Colored(BrightText, $"{row.CopiesPrefix}{row.Weapon.Name}");

        // The stat subline in the in-game shoot panel's own notation, each rule underlined and hoverable
        // (#292). Drawn on the draw list because a table cell gives no wrapping for SameLine runs.
        uint sub = ImGui.GetColorU32(DimText);
        uint ruleCol = ImGui.GetColorU32(row.InRange ? ImGuiTheme.RuleBlue : ImGuiTheme.RuleBlueDim);
        float indent = ImGui.GetTextLineHeight();
        string? hovered = RuleHoverText.DrawInline(ImGui.GetWindowDrawList(),
            ImGui.GetCursorScreenPos() + new Vector2(indent, 0f),
            RuleHoverText.WeaponStatLine(row.Weapon), sub, ruleCol, ImGui.IsWindowHovered());
        ImGui.Dummy(new Vector2(indent, ImGui.GetTextLineHeight()));

        if (!row.InRange)
        {
            // The reason goes where the numbers would have been, not on a third line under the weapon:
            // the stat columns are empty precisely BECAUSE of it, so that is where the reader is looking.
            DrawSpanningNote(row.OutOfRangeText);
            ImGui.EndDisabled();
            return hovered;
        }

        // The working stays INSIDE the weapon cell, under the subline it belongs to. As its own table
        // row it detached from the weapon it explained - a horizontal rule landed between them and it
        // read as a separate entry.
        DrawVolleyDetail(row);

        Cell(row.Dice);
        Cell(row.Hit);
        Cell(row.Hits);
        Cell(row.Save);
        Cell(row.Wounds, ImGuiTheme.DamageAmber);
        Cell(row.Health, ImGuiTheme.DamageAmber);
        Cell(row.Points, ImGuiTheme.DamageAmber);

        return hovered;
    }

    /// <summary>
    /// One line of text starting in the first stat column and running across the rest of them. A table
    /// cell clips to its own column, so this paints on the draw list under a clip rect widened to the
    /// table's right edge - the same trick the stat subline uses to escape its cell.
    /// </summary>
    private static void DrawSpanningNote(string text)
    {
        ImGui.TableNextColumn();

        Vector2 at = ImGui.GetCursorScreenPos();
        float right = ImGui.GetWindowPos().X + ImGui.GetWindowWidth() - ImGui.GetStyle().WindowPadding.X;
        ImDrawListPtr dl = ImGui.GetWindowDrawList();

        dl.PushClipRect(at, new Vector2(MathF.Max(right, at.X + 1f), at.Y + ImGui.GetTextLineHeight()), false);
        // Full-strength colour: this note is the row's explanation, and it is already being dimmed by
        // the disabled alpha its caller pushed.
        dl.AddText(at, ImGui.GetColorU32(BrightText), text);
        dl.PopClipRect();

        ImGui.Dummy(new Vector2(ImGui.GetTextLineHeight(), ImGui.GetTextLineHeight()));
    }

    private static void Cell(string text, Vector4? color = null)
    {
        ImGui.TableNextColumn();
        if (color is { } c) UiText.Colored(c, text); else ImGui.TextUnformatted(text);
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

        ImGui.Indent();

        if (row.HitChips.Count > 0) DrawChipLine("To hit", row.HitChips, row.Hit);
        if (row.SaveChips.Count > 0) DrawChipLine("Save", row.SaveChips, row.Save);

        if (splitSaves)
            foreach (SaveLineView save in row.SaveLines)
                UiText.Colored(DimText, $"   {save.SaveNeeded}+  {save.Describe()}");

        foreach (string note in row.Notes) UiText.Colored(DimText, note);

        ImGui.Unindent();
    }

    private static void DrawChipLine(string label, IReadOnlyList<string> chips, string result)
    {
        UiText.Colored(DimText, label);
        foreach (string chip in chips)
        {
            ImGui.SameLine();
            UiChrome.DrawChip(chip);
        }
        ImGui.SameLine();
        UiText.Colored(DimText, "->");
        ImGui.SameLine();
        ImGui.TextUnformatted(result);
    }


    private void DrawSituationBar()
    {
        UiText.Colored(DimText, VariablesHeader);

        if (_situation.Mode == ECombatMode.Shooting) DrawShootingSituation();
        else DrawMeleeSituation();
    }

    private void DrawShootingSituation()
    {
        DrawDistanceTrack();
        DrawDistanceSteppers();

        // Third line. The shooting inputs did not fit across one row at this column width and the last
        // checkbox was being cut off by the column edge.
        bool cover = _situation.DefenderInCover;
        if (ImGui.Checkbox(CoverLabel, ref cover)) _situation = _situation with { DefenderInCover = cover };

        ImGui.SameLine(0f, ImGui.GetTextLineHeight());
        bool moved = _situation.AttackerMoved;
        if (ImGui.Checkbox(MovedLabel, ref moved)) _situation = _situation with { AttackerMoved = moved };
    }

    /// <summary>
    /// The distance slider, full width, painted over three bands: green where every weapon in the fight
    /// reaches, yellow where only some do, red where none do. Dragging it walks the whole table through
    /// its thresholds at once, and the bands say in advance where the next row is going to drop out -
    /// which is the question a range slider is asked, and which the ticks alone answered only if you
    /// already knew what they meant.
    ///
    /// <para>The bands are painted BEFORE the slider, at the rect the slider is about to occupy, and the
    /// slider's own frame background is pushed transparent so the bands show through. The grab still
    /// draws on top, so the reader keeps a normal slider - the track behind it is what changed.</para>
    ///
    /// <para>The slider carries no number of its own: the field below it is the number, and printing it
    /// twice (once inside the track, once in the box) read as two different controls.</para>
    /// </summary>
    private void DrawDistanceTrack()
    {
        float width = MathF.Max(ImGui.GetFontSize() * 6f, ImGui.GetContentRegionAvail().X);
        // No fight yet, no bands - and then the slider keeps its own frame, or the track would simply
        // not be there.
        bool banded = DrawRangeBands(ImGui.GetCursorScreenPos(), new Vector2(width, ImGui.GetFrameHeight()));

        if (banded)
        {
            ImGui.PushStyleColor(ImGuiCol.FrameBg, Transparent);
            ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Transparent);
            ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Transparent);
        }

        float distance = _situation.DistanceInches;
        ImGui.SetNextItemWidth(width);
        if (ImGui.SliderFloat("##calc-distance", ref distance, 0f, MaxDistanceInches, string.Empty))
            SetDistance(distance);

        if (banded) ImGui.PopStyleColor(3);
        DrawRangeTicks();
    }

    /// <summary>
    /// The exact number, with a coarse and a fine pair of steppers either side of it: 3in is a typical
    /// short move, 6in an advance, which makes "what if I close the gap" one click rather than a drag
    /// aimed at a 12px target. Sizes come from the frame height, so the row grows with the UI scale.
    /// </summary>
    private void DrawDistanceSteppers()
    {
        Vector2 step = new(ImGui.GetFrameHeight() * 1.5f, ImGui.GetFrameHeight());

        if (ImGui.Button("--##calc-dist-dec2", step)) StepDistance(-BigStepInches);
        ImGui.SameLine();
        if (ImGui.Button("-##calc-dist-dec", step)) StepDistance(-StepInches);

        ImGui.SameLine();
        ImGui.SetNextItemWidth(ImGui.GetFontSize() * 3.2f);
        float typed = _situation.DistanceInches;
        // Step 0 removes InputFloat's own arrows: two sets of steppers are enough, and a third pair
        // sitting between them with a different increment would be a puzzle rather than a control.
        if (ImGui.InputFloat("##calc-dist-typed", ref typed, 0f, 0f, "%.0f")) SetDistance(typed);

        ImGui.SameLine();
        if (ImGui.Button("+##calc-dist-inc", step)) StepDistance(StepInches);
        ImGui.SameLine();
        if (ImGui.Button("++##calc-dist-inc2", step)) StepDistance(BigStepInches);

        ImGui.SameLine();
        UiText.Colored(DimText, DistanceLabel);
    }

    private void StepDistance(float step) => SetDistance(Stepped(_situation.DistanceInches, step));

    /// <summary>
    /// The next multiple of the step in the direction pressed, not the current value plus the step: from
    /// 14in, "+" lands on 15 and then 18 rather than trailing 17, 20, 23 forever. A stepper that keeps
    /// an arbitrary offset alive is a stepper you have to do arithmetic with - and the multiples of 3 and
    /// 6 ARE the interesting distances, being the moves a unit makes. Internal so the walk is pinned by
    /// tests.
    /// </summary>
    internal static float Stepped(float current, float step)
    {
        float size = MathF.Abs(step);
        if (size <= 0f) return SnapDistance(current);

        float next = step > 0f
            ? (MathF.Floor(current / size) + 1f) * size
            : (MathF.Ceiling(current / size) - 1f) * size;
        return SnapDistance(next);
    }

    private void SetDistance(float inches)
    {
        float snapped = SnapDistance(inches);
        if (snapped != _situation.DistanceInches) _situation = _situation with { DistanceInches = snapped };
    }

    /// <summary>
    /// Distances land on whole inches. A tabletop is measured with a tape to the inch and every
    /// range-gated rule in the corpus is written in whole inches, so the halves the slider used to
    /// produce ("13.47in") were noise in the report key - each one a fresh simulation - and never a
    /// distinction a player could act on. Internal so the rounding is pinned without a window.
    /// </summary>
    internal static float SnapDistance(float inches) =>
        Math.Clamp(MathF.Round(inches), 0f, MaxDistanceInches);

    /// <summary>How far a click of the fine and coarse steppers moves the distance.</summary>
    internal const float StepInches = 3f;
    internal const float BigStepInches = 6f;

    /// <summary>
    /// Where the distance track changes colour: every weapon in the fight reaches <c>All</c>, at least
    /// one reaches <c>Some</c>, nothing reaches past it. Both clamped into the slider's span, and (0, 0)
    /// when there is nothing to measure - no report, or a melee-only unit - which paints no bands at all
    /// rather than a screen of red. Internal so the arithmetic is tested without a window.
    /// </summary>
    internal static (float All, float Some) RangeZones(CombatReport? report)
    {
        if (report is null) return (0f, 0f);

        List<float> ranges = report.Volleys
            .Select(volley => volley.EffectiveRangeInches)
            .Where(range => range > 0f)
            .ToList();

        return ranges.Count == 0
            ? (0f, 0f)
            : (MathF.Min(ranges.Min(), MaxDistanceInches), MathF.Min(ranges.Max(), MaxDistanceInches));
    }

    /// <summary>Paints the bands, or reports that there was nothing to paint.</summary>
    private bool DrawRangeBands(Vector2 at, Vector2 size)
    {
        (float all, float some) = RangeZones(_report);
        if (some <= 0f) return false;

        ImDrawListPtr dl = ImGui.GetWindowDrawList();
        float rounding = ImGui.GetStyle().FrameRounding;
        float allX = at.X + (all / MaxDistanceInches * size.X);
        float someX = at.X + (some / MaxDistanceInches * size.X);
        float endX = at.X + size.X;

        Band(dl, at.X, allX, ImGuiTheme.RangeAllZone, ImDrawFlags.RoundCornersLeft);
        Band(dl, allX, someX, ImGuiTheme.RangeSomeZone, ImDrawFlags.RoundCornersNone);
        Band(dl, someX, endX, ImGuiTheme.RangeNoneZone, ImDrawFlags.RoundCornersRight);
        return true;

        void Band(ImDrawListPtr list, float x0, float x1, Vector4 color, ImDrawFlags corners)
        {
            if (x1 - x0 <= 0.5f) return;
            list.AddRectFilled(new Vector2(x0, at.Y), new Vector2(x1, at.Y + size.Y),
                ImGui.GetColorU32(color), rounding, corners);
        }
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
