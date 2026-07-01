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

    private static readonly FileFilter ArmyFilter = new(
        $"FDG Army (*{ArmyListFile.EXTENSION_WITH_PERIOD})",
        new[] { $"*{ArmyListFile.EXTENSION_WITH_PERIOD}" });

    private BookFile _book = DemoBook.Build();
    private BuilderList _list = new() { PointsLimit = DefaultPointsLimit };
    private string? _selectedRosterId;
    private int? _selectedListIndex;
    private string? _statusHint;

    public ArmyForgeScreen()
    {
        _list.BookName = _book.Name;
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
        // Recompile every frame — cheap, and keeps points/stat panes always in sync with the list.
        BuiltArmyFile compiled = Compile();

        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("Army Forge",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse);

        DrawToolbar(compiled);
        ImGui.Separator();
        DrawPanes(compiled);

        ImGui.End();
    }

    private void DrawToolbar(BuiltArmyFile compiled)
    {
        if (ImGui.Button("Back")) OnBack?.Invoke();
        ImGui.SameLine();
        if (ImGui.Button("Save")) Save(compiled);
        ImGui.SameLine();
        if (ImGui.Button("Load")) Load();
        ImGui.SameLine();
        ImGui.Text($"Army Forge  —  {_book.Name}");
        if (_statusHint is not null)
        {
            ImGui.SameLine();
            ImGui.TextDisabled(_statusHint);
        }

        string header = PointsHeader(compiled.TotalPoints, _list.PointsLimit);
        float headerW = ImGui.CalcTextSize(header).X;
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetWindowWidth() - ImGui.GetStyle().WindowPadding.X - headerW);
        bool over = compiled.TotalPoints > _list.PointsLimit;
        ImGui.TextColored(over ? new Vector4(0.90f, 0.40f, 0.40f, 1f) : new Vector4(1f, 1f, 1f, 1f), header);
    }

    private void DrawPanes(BuiltArmyFile compiled)
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
        DrawListPane(compiled);
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
            if (ImGui.Selectable($"{unit.Name}##roster-{unit.Id}", selected))
                _selectedRosterId = unit.Id;
            ImGui.SameLine(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize($"{unit.BasePointCost}").X);
            ImGui.TextDisabled($"{unit.BasePointCost}");
        }

        ImGui.Separator();
        ImGui.BeginDisabled(_selectedRosterId is null);
        if (ImGui.Button("+ Add to list") && _selectedRosterId is not null)
            AddToList(_selectedRosterId);
        ImGui.EndDisabled();
    }

    private void DrawListPane(BuiltArmyFile compiled)
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
            bool selected = _selectedListIndex == i;
            if (ImGui.Selectable($"{unit.Name} [{unit.ModelCount}]##li{i}", selected))
                _selectedListIndex = i;

            string pts = $"{unit.PointCost}";
            ImGui.SameLine(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(pts).X - 30f);
            ImGui.TextDisabled(pts);
            ImGui.SameLine();
            if (ImGui.SmallButton($"x##rm{i}")) removeIndex = i;
        }
        if (removeIndex >= 0) RemoveFromList(removeIndex);
    }

    private void DrawConfigPane(BuiltArmyFile compiled)
    {
        // A selected list unit takes precedence (show its compiled stats); otherwise preview the roster pick.
        if (_selectedListIndex is int idx && idx >= 0 && idx < compiled.Units.Count)
        {
            DrawCompiledUnit(compiled.Units[idx], _list.Units[idx].RosterUnitId);
            return;
        }
        if (Selected is RosterUnit roster)
        {
            DrawRosterPreview(roster);
            return;
        }
        ImGui.TextDisabled("Select a unit from your list, or add one from the roster.");
    }

    private void DrawCompiledUnit(UnitFileEntry unit, string rosterId)
    {
        ImGui.TextUnformatted(ArmyBuilderScreen.UnitStatLine(unit));
        ImGui.SameLine();
        ImGui.TextDisabled($"({unit.PointCost} pts)");
        ImGui.Separator();

        ImGui.Indent();
        foreach (WeaponFileEntry weapon in unit.Weapons)
            ImGui.TextDisabled(ArmyBuilderScreen.WeaponSummary(weapon));
        if (unit.SpecialRules.Count > 0)
            ImGui.TextDisabled(string.Join(", ", unit.SpecialRules.Select(r => r.PrintableName)));
        ImGui.Unindent();

        // The unit's upgrade options — read-only until P3 wires selection.
        RosterUnit? roster = _book.Units.FirstOrDefault(u => u.Id == rosterId);
        if (roster is null || roster.Sections.Count == 0) return;
        ImGui.Spacing();
        ImGui.TextDisabled("UPGRADES  (editing coming next slice)");
        ImGui.Separator();
        foreach (UpgradeSection section in roster.Sections)
        {
            ImGui.TextUnformatted(section.Label);
            ImGui.Indent();
            foreach (UpgradeOption option in section.Options)
                ImGui.TextDisabled(OptionSummary(option));
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
}
