using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using ImGuiNET;
using TinyDialogsNet;

namespace FdgRaylib.Rendering;

// #153 — the catalog army builder ("Army Forge"). Three-pane layout (roster | list | config).
//   P1: read-only book viewer.
//   P2 (this): build a list — add/remove roster units, live points via ListCompiler, Save/Load the single
//              embedded .fdgarmy (which then loads straight into the lobby's "Load Army"). Upgrade-option
//              EDITING is still read-only here; wiring options to mutate + re-cost is P3.
// The whole define/compile backend lives in the engine (FDG.ArmyBuilding); this screen is pure GUI over it.
public class ArmyForgeScreen : IAppScreen
{
    public Action? OnBack;

    private const int DefaultPointsLimit = 1000;

    private static readonly Vector4 RedText    = new(0.90f, 0.40f, 0.40f, 1f);
    private static readonly Vector4 YellowText = new(0.90f, 0.80f, 0.35f, 1f);
    private static readonly Vector4 GreenText  = new(0.45f, 0.85f, 0.45f, 1f);
    private static readonly Vector4 WhiteText  = new(1f, 1f, 1f, 1f);

    private static readonly FileFilter ArmyFilter = new(
        $"FDG Army (*{ArmyListFile.EXTENSION_WITH_PERIOD})",
        new[] { $"*{ArmyListFile.EXTENSION_WITH_PERIOD}" });

    private readonly List<BookFile> _library;
    private readonly string[] _libraryNames;
    private int _bookIndex;
    private BookFile _book;
    private BuilderList _list;
    private string? _selectedRosterId;
    private int? _selectedListIndex;
    private string? _statusHint;
    private int? _pendingBookIndex;

    public ArmyForgeScreen()
    {
        _library = LoadLibrary();
        _libraryNames = _library.Select(b => b.Name).ToArray();
        _bookIndex = 0;
        _book = _library[0];
        _list = new BuilderList { PointsLimit = DefaultPointsLimit, BookName = _book.Name };
    }

    // The hand-authored demo book plus every .fdgbook bundled under Assets/Books/ (the imported OPR snapshots).
    private static List<BookFile> LoadLibrary()
    {
        var books = new List<BookFile> { DemoBook.Build() };
        string dir = Path.Combine(AppContext.BaseDirectory, "Assets", "Books");
        if (Directory.Exists(dir))
        {
            foreach (string path in Directory.EnumerateFiles(dir, "*" + BookFile.EXTENSION_WITH_PERIOD).OrderBy(p => p))
            {
                try
                {
                    BookFile? book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options);
                    if (book is not null) books.Add(book);
                }
                catch { /* skip a malformed book rather than crash the screen */ }
            }
        }
        return books;
    }

    private void SwitchBook(int index)
    {
        if (index < 0 || index >= _library.Count) return;
        _bookIndex = index;
        _book = _library[index];
        _list = new BuilderList { PointsLimit = _list.PointsLimit, BookName = _book.Name };
        _selectedListIndex = null;
        _selectedRosterId = null;
    }

    // A book switch would discard the current list (its units reference the old book), so confirm first.
    private void DrawSwitchBookConfirm()
    {
        bool open = true;
        if (!ImGui.BeginPopupModal("Switch book?", ref open, ImGuiWindowFlags.AlwaysAutoResize)) return;

        ImGui.TextUnformatted("Switching books will clear your current list. Continue?");
        ImGui.Spacing();
        if (ImGui.Button("Switch", new Vector2(120, 0)))
        {
            if (_pendingBookIndex is int idx) SwitchBook(idx);
            _pendingBookIndex = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel", new Vector2(120, 0)))
        {
            _pendingBookIndex = null;
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }

    // ── List-mutation seams (unit-tested without ImGui) ─────────────────────────────────────────────────

    internal BuilderList List => _list;

    internal void AddToList(string rosterId)
    {
        RosterUnit? roster = _book.Units.FirstOrDefault(u => u.Id == rosterId);
        if (roster is null) return;
        _list.Units.Add(new BuilderUnit { RosterUnitId = roster.Id, ModelCount = roster.BaseModelCount });
        _selectedListIndex = _list.Units.Count - 1;
    }

    internal void RemoveFromList(int index)
    {
        if (index < 0 || index >= _list.Units.Count) return;
        _list.Units.RemoveAt(index);
        _selectedListIndex = _list.Units.Count == 0 ? null : Math.Min(_selectedListIndex ?? 0, _list.Units.Count - 1);
    }

    internal BuiltArmyFile Compile() => ListCompiler.Compile(_book, _list);

    internal IReadOnlyList<ListIssue> Issues() => ListValidator.Validate(_book, _list, Compile());

    /// <summary>Reopen a saved army into an editable session. Succeeds only if the file carries the embedded
    /// book + selections (a Forge-authored .fdgarmy); a hand-authored army returns false (it still plays, it
    /// just isn't catalog-editable).</summary>
    internal bool AdoptLoaded(BuiltArmyFile loaded)
    {
        if (loaded.Selections is null || loaded.Book is null) return false;
        _book = loaded.Book;
        _list = loaded.Selections;
        _selectedListIndex = _list.Units.Count == 0 ? null : 0;
        _selectedRosterId = null;
        return true;
    }

    // ── Draw ────────────────────────────────────────────────────────────────────────────────────────────

    public void Draw(int screenW, int screenH)
    {
        // Recompile + revalidate every frame — cheap, and keeps points/panes/legality in sync with the list.
        BuiltArmyFile compiled = Compile();
        IReadOnlyList<ListIssue> issues = ListValidator.Validate(_book, _list, compiled);

        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("Army Forge",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse);

        DrawToolbar(compiled, issues);
        ImGui.Separator();
        DrawPanes(compiled, issues);

        ImGui.End();
    }

    private void DrawToolbar(BuiltArmyFile compiled, IReadOnlyList<ListIssue> issues)
    {
        if (ImGui.Button("Back")) OnBack?.Invoke();
        ImGui.SameLine();
        if (ImGui.Button("Save")) Save(compiled);
        ImGui.SameLine();
        if (ImGui.Button("Load")) Load();
        ImGui.SameLine();
        ImGui.Text("Army Forge  —");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(220f);
        int bi = _bookIndex;
        if (ImGui.Combo("##forge-book", ref bi, _libraryNames, _libraryNames.Length) && bi != _bookIndex)
        {
            if (_list.Units.Count == 0) SwitchBook(bi);
            else { _pendingBookIndex = bi; ImGui.OpenPopup("Switch book?"); }
        }
        DrawSwitchBookConfirm();
        if (_statusHint is not null)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(_statusHint);
        }

        // Legality badge.
        int errors = issues.Count(i => i.Severity == ListIssueSeverity.Error);
        int warnings = issues.Count(i => i.Severity == ListIssueSeverity.Warning);
        ImGui.SameLine();
        if (errors > 0) ImGui.TextColored(RedText, $"[{errors} error{(errors == 1 ? "" : "s")}]");
        else if (warnings > 0) ImGui.TextColored(YellowText, $"[{warnings} warning{(warnings == 1 ? "" : "s")}]");
        else ImGui.TextColored(GreenText, "[Legal]");

        string header = PointsHeader(compiled.TotalPoints, _list.PointsLimit);
        float headerW = ImGui.CalcTextSize(header).X;
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetWindowWidth() - ImGui.GetStyle().WindowPadding.X - headerW);
        ImGui.TextColored(compiled.TotalPoints > _list.PointsLimit ? RedText : WhiteText, header);
    }

    private void DrawPanes(BuiltArmyFile compiled, IReadOnlyList<ListIssue> issues)
    {
        Vector2 avail = ImGui.GetContentRegionAvail();
        float spacing = ImGui.GetStyle().ItemSpacing.X;
        float rosterW = avail.X * 0.24f;
        float listW = avail.X * 0.36f;

        ImGui.BeginChild("##forge-roster", new Vector2(rosterW, avail.Y), ImGuiChildFlags.Borders);
        DrawRosterPane();
        ImGui.EndChild();

        ImGui.SameLine(0, spacing);
        ImGui.BeginChild("##forge-list", new Vector2(listW, avail.Y), ImGuiChildFlags.Borders);
        DrawListPane(compiled, issues);
        ImGui.EndChild();

        ImGui.SameLine(0, spacing);
        ImGui.BeginChild("##forge-config", new Vector2(0, avail.Y), ImGuiChildFlags.Borders);
        DrawConfigPane(compiled);
        ImGui.EndChild();
    }

    private void DrawRosterPane()
    {
        ImGui.TextDisabled("ROSTER");
        ImGui.Separator();
        foreach (RosterUnit unit in _book.Units)
        {
            bool selected = unit.Id == _selectedRosterId;
            if (ImGui.Selectable($"{unit.Name}##roster-{unit.Id}", selected, ImGuiSelectableFlags.AllowDoubleClick))
            {
                _selectedRosterId = unit.Id;
                if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left)) AddToList(unit.Id);
            }
            ImGui.SameLine(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize($"{unit.BasePointCost}").X);
            ImGui.TextDisabled($"{unit.BasePointCost}");
            ImGui.Indent();
            ImGui.TextDisabled($"Qua {unit.Quality}+ Def {unit.Defense}+");
            ImGui.Unindent();
        }

        ImGui.Separator();
        ImGui.BeginDisabled(_selectedRosterId is null);
        if (ImGui.Button("+ Add to list") && _selectedRosterId is not null)
            AddToList(_selectedRosterId);
        ImGui.EndDisabled();

        if (!string.IsNullOrEmpty(_book.Source))
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.PushTextWrapPos(0f);
            ImGui.TextDisabled($"Data: {_book.Source} ({_book.License})");
            ImGui.PopTextWrapPos();
        }
    }

    private void DrawListPane(BuiltArmyFile compiled, IReadOnlyList<ListIssue> issues)
    {
        ImGui.TextDisabled("LIST");
        ImGui.Separator();
        if (_list.Units.Count == 0)
        {
            ImGui.TextWrapped("Your list is empty. Select a unit in the roster and click \"+ Add to list\".");
            return;
        }

        int removeIndex = -1;
        for (int i = 0; i < _list.Units.Count && i < compiled.Units.Count; i++)
        {
            UnitFileEntry unit = compiled.Units[i];
            if (issues.Any(x => x.UnitIndex == i && x.Severity == ListIssueSeverity.Error))
            {
                ImGui.TextColored(RedText, "!");
                ImGui.SameLine();
            }

            bool selected = _selectedListIndex == i;
            ImGui.SetNextItemAllowOverlap(); // else the full-width Selectable swallows the remove button's click
            if (ImGui.Selectable($"{unit.Name} [{unit.ModelCount}]##li{i}", selected))
                _selectedListIndex = i;

            string pts = $"{unit.PointCost}";
            ImGui.SameLine(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(pts).X - 30f);
            ImGui.TextDisabled(pts);
            ImGui.SameLine();
            if (ImGui.SmallButton($"x##rm{i}")) removeIndex = i;

            ImGui.Indent();
            ImGui.TextDisabled($"Qua {unit.Quality}+ Def {unit.Defense}+");
            foreach (WeaponFileEntry weapon in unit.Weapons)
                ImGui.TextDisabled(ArmyBuilderScreen.WeaponSummary(weapon));
            if (unit.SpecialRules.Count > 0)
                ImGui.TextDisabled(string.Join(", ", unit.SpecialRules.Select(r => r.PrintableName)));
            ImGui.Unindent();
            ImGui.Separator();
        }
        if (removeIndex >= 0) RemoveFromList(removeIndex);

        if (issues.Count > 0)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.PushTextWrapPos(0f);
            foreach (ListIssue issue in issues)
                ImGui.TextColored(issue.Severity == ListIssueSeverity.Error ? RedText : YellowText, issue.Message);
            ImGui.PopTextWrapPos();
        }
    }

    private void DrawConfigPane(BuiltArmyFile compiled)
    {
        // A selected list unit takes precedence (show its compiled stats); otherwise preview the roster pick.
        if (_selectedListIndex is int idx && idx >= 0 && idx < compiled.Units.Count)
        {
            DrawCompiledUnit(idx, compiled);
            return;
        }
        if (Selected is RosterUnit roster)
        {
            DrawRosterPreview(roster);
            return;
        }
        ImGui.TextDisabled("Select a unit from your list, or add one from the roster.");
    }

    private void DrawCompiledUnit(int idx, BuiltArmyFile compiled)
    {
        BuilderUnit bu = _list.Units[idx];
        // Recompile this unit with its wargear-item detail (names survive) for display + target availability.
        (UnitFileEntry unit, List<ItemEntry> items) = ListCompiler.CompileUnitDetailed(_book, bu);

        ImGui.TextUnformatted(ArmyBuilderScreen.UnitStatLine(unit));
        ImGui.SameLine();
        ImGui.TextDisabled($"({unit.PointCost} pts)");
        ImGui.Separator();

        ImGui.Indent();
        foreach (WeaponFileEntry weapon in unit.Weapons)
            ImGui.TextDisabled(ArmyBuilderScreen.WeaponSummary(weapon));
        foreach (ItemEntry item in items)
            ImGui.TextDisabled(ItemSummary(item));
        if (unit.SpecialRules.Count > 0)
            ImGui.TextDisabled(string.Join(", ", unit.SpecialRules.Select(r => r.PrintableName)));
        ImGui.Unindent();

        RosterUnit? roster = _book.Units.FirstOrDefault(u => u.Id == bu.RosterUnitId);
        if (roster is not null)
            DrawUpgradeEditors(bu, roster, unit, items);
    }

    // Interactive upgrade sections: mutate the BuilderUnit's choices; the per-frame recompile re-costs live.
    private static void DrawUpgradeEditors(BuilderUnit bu, RosterUnit roster, UnitFileEntry compiledUnit, List<ItemEntry> items)
    {
        if (roster.Sections.Count == 0) return;
        ImGui.Spacing();
        ImGui.TextDisabled("UPGRADES");
        ImGui.Separator();

        foreach (UpgradeSection section in roster.Sections)
        {
            bool isReplace = section.Variant == UpgradeVariant.Replace;
            int available = isReplace
                ? ListCompiler.AvailableApplications(compiledUnit.Weapons, items, section.Targets)
                : int.MaxValue;

            ImGui.TextUnformatted(section.Label);
            if (isReplace && available == 0)
            {
                ImGui.SameLine();
                ImGui.TextDisabled("(none to replace)");
            }
            ImGui.Indent();

            if (section.IsCounted) // "any"/"up to N" or add-models → a stepper
            {
                int hardBound = section.MaxApplications > 0 ? section.MaxApplications : int.MaxValue;
                foreach (UpgradeOption option in section.Options)
                {
                    int v = ChoiceCount(bu, section.Id, option.Id);
                    // `available` is measured on the FINAL compiled state (this option's picks already
                    // consumed), so the option's own count comes back into its budget.
                    int poolBound = section.Variant == UpgradeVariant.AddModels
                        ? Math.Max(0, roster.MaxModels - roster.BaseModelCount)
                        : isReplace ? available + v
                        : compiledUnit.ModelCount;
                    int max = Math.Min(hardBound, poolBound);

                    ImGui.BeginDisabled(isReplace && available == 0 && v == 0);
                    ImGui.SetNextItemWidth(90f);
                    if (ImGui.InputInt($"{OptionSummary(option)}##{section.Id}-{option.Id}", ref v, 1))
                        SetChoice(bu, section, option.Id, Math.Clamp(v, 0, max));
                    ImGui.EndDisabled();
                }
            }
            else if (section.MaxPicks <= 1 && section.Options.Count >= 2) // pick one of several → radios
            {
                bool noneChosen = !section.Options.Any(o => IsChosen(bu, section.Id, o.Id));
                if (ImGui.RadioButton($"— none —##{section.Id}-none", noneChosen))
                    SetChoice(bu, section, string.Empty, 0);
                foreach (UpgradeOption option in section.Options)
                {
                    bool chosen = IsChosen(bu, section.Id, option.Id);
                    ImGui.BeginDisabled(isReplace && available == 0 && !chosen);
                    if (ImGui.RadioButton($"{OptionSummary(option)}##{section.Id}-{option.Id}", chosen))
                        SetChoice(bu, section, option.Id, 1);
                    ImGui.EndDisabled();
                }
            }
            else // single option (binary) or multi-select → checkboxes
            {
                foreach (UpgradeOption option in section.Options)
                {
                    bool chosen = IsChosen(bu, section.Id, option.Id);
                    ImGui.BeginDisabled(isReplace && available == 0 && !chosen);
                    if (ImGui.Checkbox($"{OptionSummary(option)}##{section.Id}-{option.Id}", ref chosen))
                        SetChoice(bu, section, option.Id, chosen ? 1 : 0);
                    ImGui.EndDisabled();
                }
            }
            ImGui.Unindent();
        }
    }

    private static void DrawRosterPreview(RosterUnit unit)
    {
        ImGui.TextUnformatted(RosterStatLine(unit));
        ImGui.Separator();
        ImGui.Indent();
        foreach (WeaponFileEntry weapon in unit.Weapons)
            ImGui.TextDisabled(ArmyBuilderScreen.WeaponSummary(weapon));
        foreach (ItemEntry item in unit.Items)
            ImGui.TextDisabled(ItemSummary(item));
        if (unit.Rules.Count > 0)
            ImGui.TextDisabled(string.Join(", ", unit.Rules.Select(r => r.PrintableName)));
        ImGui.Unindent();

        if (unit.Sections.Count == 0) return;
        ImGui.Spacing();
        ImGui.TextDisabled("UPGRADES");
        ImGui.Separator();
        foreach (UpgradeSection section in unit.Sections)
        {
            ImGui.TextUnformatted(section.Label);
            ImGui.Indent();
            foreach (UpgradeOption option in section.Options)
                ImGui.TextDisabled(OptionSummary(option));
            ImGui.Unindent();
        }
    }

    private RosterUnit? Selected =>
        _selectedRosterId is null ? null : _book.Units.FirstOrDefault(u => u.Id == _selectedRosterId);

    // ── Save / Load ─────────────────────────────────────────────────────────────────────────────────────

    private void Save(BuiltArmyFile compiled)
    {
        var (canceled, path) = TinyDialogs.SaveFileDialog("Save Army", "", ArmyFilter);
        if (canceled || string.IsNullOrEmpty(path)) return;
        if (Path.GetExtension(path) != ArmyListFile.EXTENSION_WITH_PERIOD)
            path = Path.ChangeExtension(path, ArmyListFile.EXTENSION_WITH_PERIOD);

        // Serialize the DERIVED type so the embedded selections + book ride along (the engine ignores them on
        // load; the Forge reads them back to re-edit). See BuiltArmyFile.
        File.WriteAllText(path, JsonSerializer.Serialize(compiled, RuleJson.Options));
        _statusHint = $"Saved {Path.GetFileName(path)}";
    }

    private void Load()
    {
        var (canceled, paths) = TinyDialogs.OpenFileDialog("Load Army", "", false, ArmyFilter);
        if (canceled) return;
        string path = paths?.FirstOrDefault() ?? "";
        if (!File.Exists(path)) return;

        BuiltArmyFile? loaded = JsonSerializer.Deserialize<BuiltArmyFile>(File.ReadAllText(path), RuleJson.Options);
        if (loaded is null) return;
        _statusHint = AdoptLoaded(loaded)
            ? $"Loaded {Path.GetFileName(path)}"
            : "That .fdgarmy has no embedded book — open it in the Army Builder instead.";
    }

    // ── Pure formatting seams (unit-tested; ImGui itself is hand-verified) ──────────────────────────────

    internal static string PointsHeader(int total, int limit) => $"{total} / {limit} pts";

    internal static string RosterStatLine(RosterUnit u) =>
        $"{u.Name} [{u.BaseModelCount}] - Qua {u.Quality}+ Def {u.Defense}+  ({u.BasePointCost} pts)";

    internal static string OptionSummary(UpgradeOption o) =>
        o.Cost == 0 ? o.Label : $"{o.Label}  (+{o.Cost} pts)";

    // Wargear line in the same style as WeaponSummary: "5x Combat Shield (Shield Wall)".
    internal static string ItemSummary(ItemEntry i) =>
        i.Rules.Count == 0
            ? $"{i.Quantity}x {i.Name}"
            : $"{i.Quantity}x {i.Name} ({string.Join(", ", i.Rules.Select(r => r.PrintableName))})";

    // ── Choice-mutation seams (unit-tested without ImGui) ───────────────────────────────────────────────

    internal static int ChoiceCount(BuilderUnit unit, string sectionId, string optionId) =>
        unit.Choices.FirstOrDefault(c => c.SectionId == sectionId && c.OptionId == optionId)?.Count ?? 0;

    internal static bool IsChosen(BuilderUnit unit, string sectionId, string optionId) =>
        ChoiceCount(unit, sectionId, optionId) > 0;

    /// <summary>Set (count &gt; 0) or clear (count == 0) an option. A single-select section (toggle with
    /// MaxPicks ≤ 1) is mutually exclusive — choosing one clears the section's other pick. (MaxPicks &gt; 1
    /// caps are deferred — no demo/OPR section needs them yet.)</summary>
    internal static void SetChoice(BuilderUnit unit, UpgradeSection section, string optionId, int count)
    {
        bool singleSelect = !section.IsCounted && section.MaxPicks <= 1;
        if (singleSelect)
            unit.Choices.RemoveAll(c => c.SectionId == section.Id);
        else
            unit.Choices.RemoveAll(c => c.SectionId == section.Id && c.OptionId == optionId);

        if (count > 0)
            unit.Choices.Add(new UpgradeChoice { SectionId = section.Id, OptionId = optionId, Count = count });
    }
}
