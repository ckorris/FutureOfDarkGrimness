using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text.Json;
using System.Threading.Tasks;
using FDG.ArmyBuilding;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using FdgRaylib.Import;
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
    private static readonly Vector4 CyanText   = new(0.45f, 0.80f, 0.90f, 1f);

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

    // #241 Import-from-share-link modal state. The fetch runs on a worker task (HTTP must not stall the
    // ImGui thread); Draw polls for completion. Only Draw (main thread) reads/writes these fields.
    private string _importInput = string.Empty;
    private Task<ArmyForgeShareService.ImportOutcome>? _importTask;
    private ArmyForgeShareService.ImportOutcome? _importOutcome;
    private string? _importError;
    private bool _confirmOpenInForge;

    public ArmyForgeScreen()
    {
        _library = LoadLibrary();
        _libraryNames = _library.Select(b => b.Name).ToArray();
        _bookIndex = 0;
        _book = _library[0];
        _list = new BuilderList { PointsLimit = DefaultPointsLimit, BookName = _book.Name };
    }

    /// <summary>Test seam: a screen whose library is exactly the given book (the tests use DemoBook, which
    /// no longer appears in the real dropdown).</summary>
    internal ArmyForgeScreen(BookFile book)
    {
        _library = new List<BookFile> { book };
        _libraryNames = new[] { book.Name };
        _bookIndex = 0;
        _book = book;
        _list = new BuilderList { PointsLimit = DefaultPointsLimit, BookName = _book.Name };
    }

    // Every .fdgbook bundled under Assets/Books/ (the imported OPR snapshots). The hand-authored demo book
    // is deliberately NOT in the dropdown (it confused the list next to real factions — hand-verify round 2);
    // it remains only as a fallback so the screen still works in an environment with no bundled books.
    private static List<BookFile> LoadLibrary()
    {
        var books = new List<BookFile>();
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
        if (books.Count == 0) books.Add(DemoBook.Build());
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

    // ── #241 Import from an Army Forge share link ───────────────────────────────────────────────────────

    // Paste link -> fetch + import on a worker task -> preview (units, points, warnings, inert rules,
    // pricing reconciliation) -> two exits:
    //   Save As       - the verbatim import as a PLAIN ArmyListFile (Army Forge's exact points; plays
    //                   everywhere, edits in the freeform Army Builder, not re-openable here).
    //   Open in Forge - an editable session reconstructed against the bundled book, compiled by OUR
    //                   ListCompiler. Units the book doesn't know are excluded (cost disclosed so the
    //                   user can pad); every our-vs-Army-Forge points delta is shown - each one is a
    //                   real-data repro for the #218/#219 pricing bugs.
    private void DrawImportModal()
    {
        bool open = true;
        ImGui.SetNextWindowSize(new Vector2(680, 0), ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal("Import from Army Forge", ref open, ImGuiWindowFlags.AlwaysAutoResize))
            return;

        // Harvest the worker task on the ImGui thread before drawing state-dependent widgets.
        if (_importTask is { IsCompleted: true } finished)
        {
            _importTask = null;
            if (finished.IsFaulted)
                _importError = finished.Exception?.GetBaseException().Message ?? "Import failed.";
            else if (finished.IsCanceled)
                _importError = "Import was canceled.";
            else
                _importOutcome = finished.Result;
        }
        bool busy = _importTask is not null;

        ImGui.TextUnformatted("Paste an Army Forge share link (or list id):");
        ImGui.SetNextItemWidth(660f);
        bool entered = ImGui.InputText("##import-link", ref _importInput, 512, ImGuiInputTextFlags.EnterReturnsTrue);

        ImGui.BeginDisabled(busy || _importInput.Trim().Length == 0);
        bool fetch = ImGui.Button("Fetch", new Vector2(120, 0)) || (entered && !busy && _importInput.Trim().Length > 0);
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Close", new Vector2(120, 0))) ImGui.CloseCurrentPopup();

        if (fetch)
        {
            string input = _importInput;
            _importOutcome = null;
            _importError = null;
            _importTask = Task.Run(() => ArmyForgeShareService.FetchAndImportAsync(input));
        }

        if (busy) ImGui.TextDisabled("Fetching from army-forge.onepagerules.com ...");
        if (_importError is not null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, RedText);
            ImGui.TextWrapped(_importError);
            ImGui.PopStyleColor();
        }

        if (_importOutcome is { } outcome)
        {
            ArmyListFile army = outcome.Result.Army;
            ImGui.Separator();
            ImGui.TextUnformatted($"{army.Name}  -  {army.Faction}");
            ImGui.SameLine();
            ImGui.TextColored(army.PointsLimit > 0 && army.TotalPoints > army.PointsLimit ? RedText : WhiteText,
                PointsHeader(army.TotalPoints, army.PointsLimit));

            // #241 v2: the import doubles as a pricing check of OUR Forge against Army Forge's numbers.
            if (outcome.ForgeSession is { } check)
            {
                if (check.OurTotalPoints == check.TheirTotalPoints && check.ExcludedUnits.Count == 0)
                    ImGui.TextColored(GreenText, $"Points check: our Forge matches Army Forge ({check.OurTotalPoints} pts).");
                else if (check.UnpricedUpgradeCount > 0)
                    // Amber, not red: Army Forge never published a price for these, so our shortfall is
                    // expected and is NOT a defect in our compiler (#219).
                    ImGui.TextColored(YellowText, $"Points check: our Forge computes {check.OurTotalPoints} pts vs " +
                        $"Army Forge's {check.TheirTotalPoints} - {check.UnpricedUpgradeCount} selected " +
                        "upgrade(s) have no published price, so we count them as free (#219).");
                else if (check.OurTotalPoints != check.TheirTotalPoints)
                    ImGui.TextColored(RedText, $"Points check: our Forge computes {check.OurTotalPoints} pts, " +
                        $"Army Forge says {check.TheirTotalPoints} (see #218/#219).");
            }

            ImGui.BeginChild("##import-preview", new Vector2(660, 280), ImGuiChildFlags.Borders);
            ImGui.TextDisabled("UNITS");
            foreach (UnitFileEntry u in army.Units)
            {
                ImGui.TextUnformatted($"{u.Name} [{u.ModelCount}] - Qua {u.Quality}+ Def {u.Defense}+  ({u.PointCost} pts)");
                string gear = string.Join(", ", u.Weapons.Select(w => $"{w.Quantity}x {w.Name}"));
                if (gear.Length > 0) ImGui.TextDisabled("    " + gear);
            }
            if (outcome.Result.ListErrors.Count > 0)
            {
                ImGui.Spacing();
                ImGui.TextDisabled("ARMY FORGE LIST ERRORS");
                foreach (string error in outcome.Result.ListErrors) ImGui.TextColored(RedText, error);
            }
            if (outcome.Result.Warnings.Count > 0)
            {
                ImGui.Spacing();
                ImGui.TextDisabled("IMPORT WARNINGS");
                ImGui.PushStyleColor(ImGuiCol.Text, YellowText);
                foreach (string warning in outcome.Result.Warnings) ImGui.TextWrapped(warning);
                ImGui.PopStyleColor();
            }
            if (outcome.InertRules.Count > 0)
            {
                ImGui.Spacing();
                ImGui.TextDisabled("RULES NOT ENFORCED BY THE ENGINE (inert in play)");
                ImGui.PushStyleColor(ImGuiCol.Text, YellowText);
                ImGui.TextWrapped(string.Join(", ", outcome.InertRules));
                ImGui.PopStyleColor();
            }
            if (outcome.ForgeSession is { } session &&
                (session.ExcludedUnits.Count > 0 || session.UnitPointsDeltas.Count > 0 || session.Warnings.Count > 0))
            {
                ImGui.Spacing();
                ImGui.TextDisabled("FORGE RECONCILIATION (Open in Forge uses OUR pricing)");
                foreach ((string name, int pts) in session.ExcludedUnits)
                    ImGui.TextColored(RedText, $"Excluded (not in bundled book): {name} ({pts} pts)");
                foreach ((string name, int ours, int theirs) in session.UnitPointsDeltas)
                    ImGui.TextColored(YellowText, $"{name}: our Forge {ours} pts, Army Forge {theirs} pts");
                ImGui.PushStyleColor(ImGuiCol.Text, YellowText);
                foreach (string warning in session.Warnings) ImGui.TextWrapped(warning);
                ImGui.PopStyleColor();
            }
            ImGui.EndChild();

            ImGui.TextDisabled("Save As: exact Army Forge data (plays everywhere, edits in the Army Builder).\n" +
                "Open in Forge: editable here against the bundled book, priced by our Forge.");

            if (_confirmOpenInForge)
            {
                ImGui.TextColored(YellowText, "This will replace your current Forge list. Continue?");
                if (ImGui.Button("Replace list", new Vector2(140, 0)))
                {
                    _confirmOpenInForge = false;
                    AdoptImported(outcome);
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("Back", new Vector2(140, 0))) _confirmOpenInForge = false;
            }
            else
            {
                ImGui.BeginDisabled(outcome.ForgeSession is null || outcome.BundledBook is null);
                if (ImGui.Button("Open in Forge", new Vector2(140, 0)))
                {
                    if (_list.Units.Count > 0) _confirmOpenInForge = true;
                    else
                    {
                        AdoptImported(outcome);
                        ImGui.CloseCurrentPopup();
                    }
                }
                ImGui.EndDisabled();
                ImGui.SameLine();
                if (ImGui.Button("Save As...", new Vector2(140, 0)) && SaveImported(army))
                    ImGui.CloseCurrentPopup();
            }
        }

        ImGui.EndPopup();
    }

    // #241 v2: hand the reconstructed session to the normal Forge editing path. Compile gives the same
    // BuiltArmyFile shape a Load would, so AdoptLoaded's book-dropdown sync and per-frame recompile all
    // apply unchanged.
    private void AdoptImported(ArmyForgeShareService.ImportOutcome outcome)
    {
        if (outcome.ForgeSession is not { } session || outcome.BundledBook is null) return;
        if (!AdoptLoaded(ListCompiler.Compile(outcome.BundledBook, session.Selections))) return;

        string hint = $"Imported '{session.Selections.Name}' into the Forge";
        if (session.ExcludedUnits.Count > 0) hint += $" - {session.ExcludedUnits.Count} unit(s) excluded";
        if (session.UnitPointsDeltas.Count > 0) hint += $" - {session.UnitPointsDeltas.Count} points delta(s) vs Army Forge";
        _statusHint = hint;
    }

    private bool SaveImported(ArmyListFile army)
    {
        var (canceled, path) = TinyDialogs.SaveFileDialog("Save Imported Army", "", ArmyFilter);
        if (canceled || string.IsNullOrEmpty(path)) return false;
        if (Path.GetExtension(path) != ArmyListFile.EXTENSION_WITH_PERIOD)
            path = Path.ChangeExtension(path, ArmyListFile.EXTENSION_WITH_PERIOD);
        File.WriteAllText(path, JsonSerializer.Serialize(army, RuleJson.Options));
        _statusHint = $"Imported {Path.GetFileName(path)}";
        return true;
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
        string? removedId = _list.Units[index].Id;
        _list.Units.RemoveAt(index);
        // Clear any dangling link to the removed unit so a surviving combine/join partner stays a clean,
        // warning-free independent unit (e.g. removing one half of a combined pair un-combines the other).
        if (!string.IsNullOrEmpty(removedId))
            foreach (BuilderUnit u in _list.Units)
            {
                if (u.CombinedWithId == removedId) u.CombinedWithId = null;
                if (u.JoinsUnitId == removedId) u.JoinsUnitId = null;
            }
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
        int idx = Array.IndexOf(_libraryNames, _book.Name);
        if (idx >= 0) _bookIndex = idx;
        return true;
    }

    // ── Draw ────────────────────────────────────────────────────────────────────────────────────────────

    public void Draw(int screenW, int screenH)
    {
        // Recompile + revalidate every frame — cheap, and keeps points/panes/legality in sync with the list.
        // `compiled` is the play-time output (#107 combined pairs merged) for Save/points; `rows` is the
        // row-aligned unmerged view every per-row pane indexes with list positions.
        BuiltArmyFile compiled = Compile();
        List<UnitFileEntry> rows = ListCompiler.CompileRows(_book, _list);
        IReadOnlyList<ListIssue> issues = ListValidator.Validate(_book, _list, compiled);

        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("Army Forge",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse);

        DrawToolbar(compiled, issues);
        ImGui.Separator();
        DrawPanes(compiled, rows, issues);

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
        if (ImGui.Button("Import Link"))
        {
            _importInput = string.Empty;
            _importTask = null;
            _importOutcome = null;
            _importError = null;
            _confirmOpenInForge = false;
            ImGui.OpenPopup("Import from Army Forge");
        }
        ImGui.SameLine();
        ImGui.Text("Army Forge  -");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(220f);
        int bi = _bookIndex;
        if (ImGui.Combo("##forge-book", ref bi, _libraryNames, _libraryNames.Length) && bi != _bookIndex)
        {
            if (_list.Units.Count == 0) SwitchBook(bi);
            else { _pendingBookIndex = bi; ImGui.OpenPopup("Switch book?"); }
        }
        DrawSwitchBookConfirm();
        DrawImportModal();
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

        // Editable points limit (games run 1000-5000; the 1000 default was hard-coded until now).
        // Advisory like everything else here (#003): over-cap only turns the header red.
        ImGui.SameLine();
        ImGui.SetNextItemWidth(110f);
        int limit = _list.PointsLimit;
        if (ImGui.InputInt("pts limit", ref limit, 250) && limit > 0)
            _list.PointsLimit = limit;

        string header = PointsHeader(compiled.TotalPoints, _list.PointsLimit);
        float headerW = ImGui.CalcTextSize(header).X;
        ImGui.SameLine();
        ImGui.SetCursorPosX(ImGui.GetWindowWidth() - ImGui.GetStyle().WindowPadding.X - headerW);
        ImGui.TextColored(compiled.TotalPoints > _list.PointsLimit ? RedText : WhiteText, header);
    }

    private void DrawPanes(BuiltArmyFile compiled, IReadOnlyList<UnitFileEntry> rows, IReadOnlyList<ListIssue> issues)
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
        DrawListPane(rows, issues, compiled.Units.Count);
        ImGui.EndChild();

        ImGui.SameLine(0, spacing);
        ImGui.BeginChild("##forge-config", new Vector2(0, avail.Y), ImGuiChildFlags.Borders);
        DrawConfigPane(rows);
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

        if (_book.Spells.Count > 0)
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.TextDisabled("SPELLS");
            ImGui.PushTextWrapPos(0f);
            foreach (FDG.Rules.Definitions.SpellDefinition spell in _book.Spells)
                ImGui.TextDisabled($"{spell.Name} ({spell.Threshold}): {FDG.Stages.SpellText.Describe(spell)}");
            ImGui.PopTextWrapPos();
        }

        if (!string.IsNullOrEmpty(_book.Source))
        {
            ImGui.Spacing();
            ImGui.Separator();
            ImGui.PushTextWrapPos(0f);
            ImGui.TextDisabled($"Data: {_book.Source} ({_book.License})");
            ImGui.PopTextWrapPos();
        }
    }

    private void DrawListPane(IReadOnlyList<UnitFileEntry> rows, IReadOnlyList<ListIssue> issues, int unitCount)
    {
        ImGui.TextDisabled($"LIST  [{unitCount} unit{(unitCount == 1 ? "" : "s")}]");
        ImGui.Separator();
        if (_list.Units.Count == 0)
        {
            ImGui.TextWrapped("Your list is empty. Select a unit in the roster and click \"+ Add to list\".");
            return;
        }

        int removeIndex = -1;
        var rendered = new bool[_list.Units.Count];
        for (int i = 0; i < _list.Units.Count && i < rows.Count; i++)
        {
            if (rendered[i]) continue;
            int partner = CombinePartnerIndex(i);
            if (partner >= 0 && partner < rows.Count)
            {
                // Combined pair: one grouped card (link tag + summed [size]/pts), then both editable sub-rows.
                int baseIdx = string.IsNullOrEmpty(_list.Units[i].CombinedWithId) ? i : partner;
                int copyIdx = baseIdx == i ? partner : i;
                int totalModels = rows[baseIdx].ModelCount + rows[copyIdx].ModelCount;
                int totalPts = rows[baseIdx].PointCost + rows[copyIdx].PointCost;

                ImGui.TextColored(CyanText, "[Combined]");
                ImGui.SameLine();
                ImGui.TextUnformatted($"{rows[baseIdx].Name} [{totalModels}]");
                string gpts = $"{totalPts}";
                ImGui.SameLine(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize(gpts).X);
                ImGui.TextDisabled(gpts);

                ImGui.Indent();
                DrawListRow(baseIdx, rows, issues, ref removeIndex);
                DrawListRow(copyIdx, rows, issues, ref removeIndex);
                ImGui.Unindent();
                ImGui.Separator();
                rendered[baseIdx] = rendered[copyIdx] = true;
            }
            else
            {
                DrawListRow(i, rows, issues, ref removeIndex);
                rendered[i] = true;
            }
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

    // One list-pane unit block: full-row select target + name/[size]/pts/remove + stat lines. Shared by
    // standalone units and the sub-rows of a combined pair (which DrawListPane draws indented under a header).
    private void DrawListRow(int i, IReadOnlyList<UnitFileEntry> rows, IReadOnlyList<ListIssue> issues, ref int removeIndex)
    {
        UnitFileEntry unit = rows[i];
        bool selected = _selectedListIndex == i;

        // Full-row hit target: an invisible Selectable spanning the row's whole block (name + stat lines),
        // drawn FIRST with AllowOverlap so the remove button and the overlaid text on top of it still receive
        // their own clicks. Clicking anywhere else in the rectangle selects the unit.
        float lineH = ImGui.GetTextLineHeightWithSpacing();
        int lines = 2 + unit.Weapons.Count + (unit.SpecialRules.Count > 0 ? 1 : 0);
        Vector2 rowStart = ImGui.GetCursorPos();
        ImGui.SetNextItemAllowOverlap();
        if (ImGui.Selectable($"##li{i}", selected, ImGuiSelectableFlags.None, new Vector2(0, lines * lineH)))
            _selectedListIndex = i;
        ImGui.SetCursorPos(rowStart);

        if (issues.Any(x => x.UnitIndex == i && x.Severity == ListIssueSeverity.Error))
        {
            ImGui.TextColored(RedText, "!");
            ImGui.SameLine();
        }
        ImGui.TextUnformatted($"{unit.Name} [{unit.ModelCount}]");

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

    private void DrawConfigPane(IReadOnlyList<UnitFileEntry> rows)
    {
        // A selected list unit takes precedence (show its compiled stats); otherwise preview the roster pick.
        if (_selectedListIndex is int idx && idx >= 0 && idx < rows.Count)
        {
            DrawCompiledUnit(idx, rows);
            return;
        }
        if (Selected is RosterUnit roster)
        {
            DrawRosterPreview(roster);
            return;
        }
        ImGui.TextDisabled("Select a unit from your list, or add one from the roster.");
    }

    private void DrawCompiledUnit(int idx, IReadOnlyList<UnitFileEntry> rows)
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

        DrawHeroJoin(idx, unit, rows);
        DrawCombinedCheckbox(idx, unit);

        RosterUnit? roster = _book.Units.FirstOrDefault(u => u.Id == bu.RosterUnitId);
        if (roster is not null)
        {
            // When combined, the partner copy is the mirror target for whole-unit (Affects=All) upgrades.
            int partnerIdx = CombinePartnerIndex(idx);
            BuilderUnit? mirror = partnerIdx >= 0 ? _list.Units[partnerIdx] : null;
            DrawUpgradeEditors(_book, bu, roster, unit, items, mirror);
        }
    }

    // #006 hero-join, Forge-side: same semantics as the freeform builder's picker — hosts are other
    // multi-model, non-Hero units in the list; the link is by author-stable BuilderUnit.Id (generated on
    // demand); eligibility problems surface through ListValidator's issues, not silent no-ops.
    private void DrawHeroJoin(int idx, UnitFileEntry compiledUnit, IReadOnlyList<UnitFileEntry> rows)
    {
        BuilderUnit bu = _list.Units[idx];

        if (ForceOrgValidator.IsHero(compiledUnit))
        {
            List<int> hosts = HostCandidates(idx, rows);
            string[] options = new string[hosts.Count + 1];
            options[0] = "(none - deploys solo)";
            for (int i = 0; i < hosts.Count; i++)
                options[i + 1] = ListRowLabel(hosts[i], rows);

            int sel = 0;
            if (!string.IsNullOrEmpty(bu.JoinsUnitId))
                sel = hosts.FindIndex(h => _list.Units[h].Id == bu.JoinsUnitId) + 1; // -1+1 = 0 when stale

            ImGui.Spacing();
            ImGui.SetNextItemWidth(240f);
            if (ImGui.Combo($"Joins unit##join{idx}", ref sel, options, options.Length))
                bu.JoinsUnitId = sel == 0 ? null : EnsureId(_list.Units[hosts[sel - 1]]);

            if (!string.IsNullOrEmpty(bu.JoinsUnitId) && sel == 0)
                ImGui.TextColored(YellowText, "! Join target missing or ineligible (see issues) - deploys solo.");
        }
        else if (!string.IsNullOrEmpty(bu.Id))
        {
            // Host-side breadcrumb: which hero(es) picked this unit.
            List<string> joiners = _list.Units
                .Where(h => !ReferenceEquals(h, bu) && h.JoinsUnitId == bu.Id)
                .Select(h => rows[_list.Units.IndexOf(h)].Name)
                .ToList();
            if (joiners.Count > 0)
                ImGui.TextDisabled($"Joined by: {string.Join(", ", joiners)}");
        }
    }

    // #107 combined squads: the "Combined Unit" checkbox (OPR Army Forge style). Checking it SPAWNS a second
    // identical copy grouped under this unit; the pair merges into one big play-time unit (models/cost summed).
    // Only shown on a multi-model, non-Hero unit. Whole-unit ("Replace all") upgrades mirror across both
    // copies; per-model options stay independent (see DrawUpgradeEditors).
    private void DrawCombinedCheckbox(int idx, UnitFileEntry compiledUnit)
    {
        if (compiledUnit.ModelCount <= 1 || ForceOrgValidator.IsHero(compiledUnit)) return;
        ImGui.Spacing();
        bool combined = IsCombined(idx);
        if (ImGui.Checkbox($"Combined Unit##combine{idx}", ref combined))
            SetCombined(idx, combined);
    }

    /// <summary>The list index of <paramref name="idx"/>'s combine partner (a valid same-roster pair), or -1.
    /// The link is authored on the spawned copy (<see cref="BuilderUnit.CombinedWithId"/> -> base Id); either
    /// half resolves to the other. Mirrors the validity condition the compiler uses to merge.</summary>
    internal int CombinePartnerIndex(int idx)
    {
        if (idx < 0 || idx >= _list.Units.Count) return -1;
        BuilderUnit bu = _list.Units[idx];
        for (int i = 0; i < _list.Units.Count; i++)
        {
            if (i == idx) continue;
            BuilderUnit other = _list.Units[i];
            if (other.RosterUnitId != bu.RosterUnitId) continue;
            bool buLinksOther = !string.IsNullOrEmpty(bu.CombinedWithId) && bu.CombinedWithId == other.Id;
            bool otherLinksBu = !string.IsNullOrEmpty(other.CombinedWithId) && other.CombinedWithId == bu.Id;
            if (buLinksOther || otherLinksBu) return i;
        }
        return -1;
    }

    internal bool IsCombined(int idx) => CombinePartnerIndex(idx) >= 0;

    /// <summary>The "Combined Unit" toggle. ON spawns a second identical copy right after this one, linked so
    /// the compiler merges them; OFF removes the spawned copy, leaving this one a normal unit. Only acts on an
    /// eligible (multi-model, non-Hero) unit; a no-op when already in the requested state.</summary>
    internal void SetCombined(int idx, bool on)
    {
        if (idx < 0 || idx >= _list.Units.Count) return;
        int partner = CombinePartnerIndex(idx);
        if (on)
        {
            if (partner >= 0 || !CanCombine(idx)) return;
            BuilderUnit bu = _list.Units[idx];
            var copy = new BuilderUnit
            {
                RosterUnitId = bu.RosterUnitId,
                ModelCount = bu.ModelCount,
                CombinedWithId = EnsureId(bu),
            };
            SeedMirroredChoices(bu, copy); // whole-unit (Affects=All) picks start mirrored on the new copy
            _list.Units.Insert(idx + 1, copy);
            _selectedListIndex = idx; // keep viewing the base copy
        }
        else
        {
            if (partner < 0) return;
            // Remove the SPAWNED copy (the one carrying the link); the base survives as a normal unit.
            int spawned = !string.IsNullOrEmpty(_list.Units[idx].CombinedWithId) ? idx : partner;
            int survivor = spawned == idx ? partner : idx;
            RemoveFromList(spawned);
            // RemoveFromList's generic clamp doesn't know which row survived — point the selection at the
            // surviving partner explicitly (its index shifts down by one if it sat after the removed row).
            _selectedListIndex = survivor > spawned ? survivor - 1 : survivor;
        }
    }

    /// <summary>A unit may be combined only if it is multi-model and not a Hero (mirrors the OPR eligibility
    /// the compiler/validator assume).</summary>
    private bool CanCombine(int idx)
    {
        (UnitFileEntry unit, _) = ListCompiler.CompileUnitDetailed(_book, _list.Units[idx]);
        return unit.ModelCount > 1 && !ForceOrgValidator.IsHero(unit);
    }

    /// <summary>List indices of units a hero at <paramref name="heroIdx"/> may join: other multi-model,
    /// non-Hero units (mirrors the #006 eligibility the engine enforces at setup).</summary>
    internal List<int> HostCandidates(int heroIdx, IReadOnlyList<UnitFileEntry> rows)
    {
        var hosts = new List<int>();
        for (int i = 0; i < rows.Count; i++)
            if (i != heroIdx && rows[i].ModelCount > 1 && !ForceOrgValidator.IsHero(rows[i]))
                hosts.Add(i);
        return hosts;
    }

    // Disambiguates duplicate squads in combos: "Retributors [5]", "Retributors [5] #2", ...
    private string ListRowLabel(int idx, IReadOnlyList<UnitFileEntry> rows)
    {
        UnitFileEntry unit = rows[idx];
        int nth = 0;
        for (int i = 0; i <= idx; i++)
            if (rows[i].Name == unit.Name) nth++;
        return nth > 1 ? $"{unit.Name} [{unit.ModelCount}] #{nth}" : $"{unit.Name} [{unit.ModelCount}]";
    }

    internal static string EnsureId(BuilderUnit unit) =>
        unit.Id ??= Guid.NewGuid().ToString("N");

    // Interactive upgrade sections: mutate the BuilderUnit's choices; the per-frame recompile re-costs live.
    // When <paramref name="mirror"/> is non-null this unit is combined, and whole-unit (Affects=All) sections
    // are shared: any edit to one is copied to the partner copy (marked "[linked]"), so a "Replace all X" swap
    // applies to both halves and is paid on both. Per-model/one/any sections stay independent per copy.
    private static void DrawUpgradeEditors(BookFile book, BuilderUnit bu, RosterUnit roster,
        UnitFileEntry compiledUnit, List<ItemEntry> items, BuilderUnit? mirror = null)
    {
        if (roster.Sections.Count == 0) return;
        ImGui.Spacing();
        ImGui.TextDisabled("UPGRADES");
        ImGui.Separator();

        foreach (UpgradeSection section in roster.Sections)
        {
            bool isReplace = section.Variant == UpgradeVariant.Replace;
            bool linked = mirror != null && section.Affects == UpgradeAffects.All;
            int available = isReplace
                ? ListCompiler.AvailableApplications(compiledUnit.Weapons, items, section.Targets)
                : int.MaxValue;
            // Availability ignoring this section's OWN pick: a mutually-exclusive (radio) pick returns its
            // replaced target to the pool the moment you switch away, so other options must not gray out
            // just because the current pick consumed the target (hand-verify round 2). Also drives the
            // header: "(none to replace)" only when there's no target even without this section's choice.
            int switchAvailable = isReplace ? AvailableExcludingSection(book, bu, section) : int.MaxValue;

            ImGui.TextUnformatted(section.Label);
            if (linked)
            {
                ImGui.SameLine();
                ImGui.TextColored(CyanText, "[linked]");
            }
            if (isReplace && switchAvailable == 0)
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
                    DrawStepper(bu, section, option, v, max);
                }
            }
            else if (section.MaxPicks <= 1 && section.Options.Count >= 2) // pick one of several → radios
            {
                bool noneChosen = !section.Options.Any(o => IsChosen(bu, section.Id, o.Id));
                if (ImGui.RadioButton($"- none -##{section.Id}-none", noneChosen))
                    ApplyChoice(bu, mirror, section, string.Empty, 0);
                foreach (UpgradeOption option in section.Options)
                {
                    bool chosen = IsChosen(bu, section.Id, option.Id);
                    ImGui.BeginDisabled(isReplace && switchAvailable == 0 && !chosen);
                    if (ImGui.RadioButton($"{OptionSummary(option)}##{section.Id}-{option.Id}", chosen))
                        ApplyChoice(bu, mirror, section, option.Id, 1);
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
                        ApplyChoice(bu, mirror, section, option.Id, chosen ? 1 : 0);
                    ImGui.EndDisabled();
                }
            }
            ImGui.Unindent();
        }
    }

    // Counted-section control: [-] [count] [+] label. The buttons gray individually at their bound (- at 0,
    // + at max) and the type-in box has no internal step buttons (step 0), so it's wide enough for the number.
    private static void DrawStepper(BuilderUnit bu, UpgradeSection section, UpgradeOption option, int v, int max)
    {
        string id = $"{section.Id}-{option.Id}";
        float frameH = ImGui.GetFrameHeight();

        ImGui.BeginDisabled(v <= 0);
        if (ImGui.Button($"-##{id}-dec", new Vector2(frameH, frameH)))
            SetChoice(bu, section, option.Id, v - 1);
        ImGui.EndDisabled();
        ImGui.SameLine();

        int typed = v;
        ImGui.SetNextItemWidth(ImGui.GetFontSize() * 2.5f);
        ImGui.BeginDisabled(max == 0 && v == 0);
        if (ImGui.InputInt($"##{id}-val", ref typed, 0))
            SetChoice(bu, section, option.Id, Math.Clamp(typed, 0, max));
        ImGui.EndDisabled();
        ImGui.SameLine();

        ImGui.BeginDisabled(v >= max);
        if (ImGui.Button($"+##{id}-inc", new Vector2(frameH, frameH)))
            SetChoice(bu, section, option.Id, v + 1);
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.TextUnformatted(OptionSummary(option));
    }

    /// <summary>Replace-target availability computed WITHOUT this section's own choices — the pool an
    /// option could draw on if the section's current pick were released (radio switching, header text).</summary>
    internal static int AvailableExcludingSection(BookFile book, BuilderUnit bu, UpgradeSection section)
    {
        var without = new BuilderUnit
        {
            RosterUnitId = bu.RosterUnitId,
            ModelCount = bu.ModelCount,
            Choices = bu.Choices.Where(c => c.SectionId != section.Id).ToList(),
        };
        (UnitFileEntry unit, List<ItemEntry> items) = ListCompiler.CompileUnitDetailed(book, without);
        return ListCompiler.AvailableApplications(unit.Weapons, items, section.Targets);
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
            : "That .fdgarmy has no embedded book - open it in the Army Builder instead.";
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

    /// <summary>Apply a choice to <paramref name="unit"/>, then — for a shared whole-unit (Affects=All)
    /// section of a combined pair — mirror the section's whole resulting choice set onto <paramref
    /// name="mirror"/>, so both halves carry the same "Replace all X" swap (and each pays for it).</summary>
    internal static void ApplyChoice(BuilderUnit unit, BuilderUnit? mirror, UpgradeSection section, string optionId, int count)
    {
        SetChoice(unit, section, optionId, count);
        if (mirror != null && section.Affects == UpgradeAffects.All)
            MirrorSection(unit, mirror, section.Id);
    }

    /// <summary>Replace <paramref name="to"/>'s choices for one section with a clone of <paramref name="from"/>'s
    /// — a full resync (robust to multi-select and prior divergence), not a per-toggle echo.</summary>
    private static void MirrorSection(BuilderUnit from, BuilderUnit to, string sectionId)
    {
        to.Choices.RemoveAll(c => c.SectionId == sectionId);
        to.Choices.AddRange(from.Choices
            .Where(c => c.SectionId == sectionId)
            .Select(c => new UpgradeChoice { SectionId = c.SectionId, OptionId = c.OptionId, Count = c.Count }));
    }

    /// <summary>Seed a freshly spawned combined copy with the base's whole-unit (Affects=All) choices, so the
    /// pair starts already mirrored rather than only syncing on the next edit.</summary>
    private void SeedMirroredChoices(BuilderUnit from, BuilderUnit to)
    {
        RosterUnit? roster = _book.Units.FirstOrDefault(u => u.Id == from.RosterUnitId);
        if (roster is null) return;
        foreach (UpgradeSection s in roster.Sections.Where(s => s.Affects == UpgradeAffects.All))
            MirrorSection(from, to, s.Id);
    }
}
