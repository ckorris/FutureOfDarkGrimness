using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using FDG.SaveLoad;
using FDG.ArmyBuilding;
using ImGuiNET;

namespace FdgRaylib.Rendering;

// #153 (P1) — the catalog army builder ("Army Forge"). Three-pane layout (roster | list | config), chosen by
// the user. P1 is the read-only viewer: browse a book's roster and see any unit's stats + upgrade options; no
// list-building yet (that's P2). Loads the hand-authored DemoBook until the .fdgbook library lands (P0b).
public class ArmyForgeScreen : IAppScreen
{
    public Action? OnBack;

    private const int DefaultPointsLimit = 1000;

    private BookFile _book = DemoBook.Build();
    private string? _selectedRosterId;

    // No list yet in P1, so the running total is 0. P2 makes this the compiled list's TotalPoints.
    private int _listPoints = 0;
    private int _pointsLimit = DefaultPointsLimit;

    private RosterUnit? Selected =>
        _selectedRosterId is null ? null : _book.Units.FirstOrDefault(u => u.Id == _selectedRosterId);

    public void Draw(int screenW, int screenH)
    {
        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("Army Forge",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse);

        DrawToolbar();
        ImGui.Separator();
        DrawPanes();

        ImGui.End();
    }

    private void DrawToolbar()
    {
        if (ImGui.Button("Back")) OnBack?.Invoke();
        ImGui.SameLine();
        ImGui.Text($"Army Forge  —  {_book.Name}");

        // Right-aligned points header.
        string header = PointsHeader(_listPoints, _pointsLimit);
        float headerW = ImGui.CalcTextSize(header).X;
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetWindowWidth() - ImGui.GetStyle().WindowPadding.X - headerW);
        ImGui.Text(header);
    }

    private void DrawPanes()
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
        DrawListPane();
        ImGui.EndChild();

        ImGui.SameLine(0, spacing);
        ImGui.BeginChild("##forge-config", new Vector2(0, avail.Y), ImGuiChildFlags.Borders);
        DrawConfigPane();
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
    }

    private void DrawListPane()
    {
        ImGui.TextDisabled("LIST");
        ImGui.Separator();
        // P1 has no list-building; P2 wires Add-from-roster, remove, compile, and Save here.
        ImGui.TextWrapped("Your list is empty.");
        ImGui.Spacing();
        ImGui.BeginDisabled();
        ImGui.Button("+ Add selected to list");
        ImGui.EndDisabled();
        ImGui.TextDisabled("(list building — next slice)");
    }

    private void DrawConfigPane()
    {
        RosterUnit? unit = Selected;
        if (unit is null)
        {
            ImGui.TextDisabled("Select a unit from the roster.");
            return;
        }

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

    // ── Pure formatting seams (unit-tested; ImGui itself is hand-verified) ──────────────────────────────

    internal static string PointsHeader(int total, int limit) => $"{total} / {limit} pts";

    internal static string RosterStatLine(RosterUnit u) =>
        $"{u.Name} [{u.BaseModelCount}] - Qua {u.Quality}+ Def {u.Defense}+  ({u.BasePointCost} pts)";

    internal static string OptionSummary(UpgradeOption o) =>
        o.Cost == 0 ? o.Label : $"{o.Label}  (+{o.Cost} pts)";
}
