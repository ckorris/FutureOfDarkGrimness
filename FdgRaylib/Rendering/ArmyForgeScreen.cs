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

/// <summary>#307: how a Save/Load status line should read. A failure must never share the success channel.</summary>
internal enum EForgeStatusKind { Info, Success, Error }

/// <summary>What a load attempt did. Anything but <see cref="Adopted"/> means the screen still holds its
/// previous list, and the caller owes the user a modal saying so.</summary>
internal enum ELoadOutcome
{
    /// <summary>The file is now the screen's list.</summary>
    Adopted,

    /// <summary>#307: unreadable, or not catalog-editable. Nothing changed.</summary>
    Rejected,

    /// <summary>#356: readable and editable, but rebuilding it for editing would change the army - the user
    /// has to accept that first. Nothing changed yet.</summary>
    NeedsDriftConfirm,
}

/// <summary>#307: why a Save needs confirming before it writes. <see cref="ESaveGuard.None"/> is the ordinary
/// path and costs the user no extra click.</summary>
internal enum ESaveGuard
{
    /// <summary>Write it - the list on screen is a real, edited list.</summary>
    None,

    /// <summary>The last load was REJECTED and nothing has changed since, so what is about to be written is
    /// whatever the screen already held - not the file the user tried to open. The reported data-loss path.</summary>
    UnchangedAfterFailedLoad,

    /// <summary>The list has no units. Saving writes an empty army, which is almost never the intent when the
    /// target is an existing file.</summary>
    EmptyList,
}

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

    // The public ctor loads the library on a worker task: parsing all 47 bundled books (~9 MB of JSON)
    // took ~0.5s on the startup path, before the window even existed. Every member that reads the
    // books/list joins the task via EnsureLibrary() first; the test-seam ctor fills these synchronously.
    private Task<List<BookFile>>? _libraryTask;
    private List<BookFile> _library = null!;
    private string[] _libraryNames = null!;
    private int _bookIndex;
    private BookFile _book = null!; // always set through UseBook() before any read (both ctors call it)

    // #259 rule tooltips: name -> description for the CURRENT book (core catalog + the book's embedded
    // definitions). Rebuilt whenever _book changes - a switch, a load, or a share-link import - since a
    // faction's own rules only exist in its own book.
    private RuleGlossary _glossary = RuleGlossary.Empty;
    private BuilderList _list = null!; // set with the library (ctor test seam / EnsureLibrary)
    private string? _selectedRosterId;
    private int? _selectedListIndex;
    private string? _statusHint;
    private int? _pendingBookIndex;

    // #307: the status line is TYPED. It used to print every outcome through ImGui.TextDisabled, so
    // "that army has no embedded book" (a REJECTED load, screen contents unchanged) was typographically
    // identical to "Saved X.fdgarmy" - which is how a user walked from a failed load straight into Save
    // and wrote the pristine startup default over a path they meant to hold a real army.
    private EForgeStatusKind _statusKind = EForgeStatusKind.Info;

    // #307: set when a load is REJECTED, cleared when one succeeds. Holds the modal text plus a fingerprint
    // of the list as it stood at the moment of the failure, so Save can tell "you have not touched anything
    // since that load failed" (the data-loss path) from a deliberate save of an edited list.
    private string? _loadFailureMessage;
    private string? _failedLoadFingerprint;

    // #307: a Save held between the file dialog and the guard modal - the path is only known after the
    // dialog returns, and a popup can only be raised from inside Draw.
    private string? _pendingSavePath;
    private ESaveGuard _pendingSaveGuard;

    // #356: a load held while the user decides whether to accept the rebuild. NOT adopted yet - the screen
    // still holds its previous list until "Open for editing" is pressed.
    private BuiltArmyFile? _pendingReopen;
    private string? _pendingReopenFileName;
    private EditableSessionDrift? _pendingReopenDrift;

    // #241 Import-from-share-link modal state. The fetch runs on a worker task (HTTP must not stall the
    // ImGui thread); Draw polls for completion. Only Draw (main thread) reads/writes these fields.
    private string _importInput = string.Empty;
    private Task<ArmyForgeShareService.ImportOutcome>? _importTask;
    private ArmyForgeShareService.ImportOutcome? _importOutcome;
    private string? _importError;
    private bool _confirmOpenInForge;

    public ArmyForgeScreen()
    {
        _libraryTask = Task.Run(LoadLibrary);
    }

    /// <summary>Joins the background library load and adopts the first book as current. No-op once the
    /// library is present (always, for a screen built through the test-seam ctor).</summary>
    private void EnsureLibrary()
    {
        if (_library is not null) return;
        _library = _libraryTask!.GetAwaiter().GetResult();
        _libraryTask = null;
        _libraryNames = _library.Select(b => b.Name).ToArray();
        _bookIndex = 0;
        UseBook(_library[0]);
        _list = new BuilderList { PointsLimit = DefaultPointsLimit, BookName = _book.Name };
    }

    /// <summary>Test seam: a screen whose library is exactly the given book (the tests use DemoBook, which
    /// no longer appears in the real dropdown).</summary>
    internal ArmyForgeScreen(BookFile book)
    {
        _library = new List<BookFile> { book };
        _libraryNames = new[] { book.Name };
        _bookIndex = 0;
        UseBook(book);
        _list = new BuilderList { PointsLimit = DefaultPointsLimit, BookName = _book.Name };
    }

    /// <summary>Adopt a book as the one being edited against, rebuilding the rule glossary the #259 hover
    /// tooltips read. Every assignment to <c>_book</c> goes through here so the two can never drift.</summary>
    private void UseBook(BookFile book)
    {
        _book = book;
        _glossary = RuleGlossary.Build(book);
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
        UseBook(_library[index]);
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

    // #307: a rejected load is a MODAL, not a status line. The report this item was filed from is a user who
    // read a greyed-out rejection as a success and pressed Save; the one thing the dialog has to land is
    // "the screen still holds what it held before", because that is what Save will write.
    private void DrawLoadFailedModal()
    {
        bool open = true;
        ImGui.SetNextWindowSize(new Vector2(560, 0), ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal(LoadFailedTitle, ref open, ImGuiWindowFlags.AlwaysAutoResize)) return;

        WrappedText(_loadFailureMessage ?? "The army could not be loaded.", RedText, 540f);
        ImGui.Spacing();
        if (ImGui.Button("OK", ButtonSize("OK", 120f))) ImGui.CloseCurrentPopup();
        ImGui.EndPopup();
    }

    // #307: the last gate before a write that would not be the army the user thinks it is. Confirm, never
    // block - the whole screen is advisory (#003).
    private void DrawSaveGuardModal()
    {
        bool open = true;
        ImGui.SetNextWindowSize(new Vector2(560, 0), ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal(SaveGuardTitle, ref open, ImGuiWindowFlags.AlwaysAutoResize)) return;

        WrappedText(
            SaveGuardMessage(_pendingSaveGuard, Path.GetFileName(_pendingSavePath ?? ""), _book.Name, _list.Units.Count),
            YellowText, 540f);
        ImGui.Spacing();
        if (ImGui.Button("Save anyway", ButtonSize("Save anyway", 140f)))
        {
            if (_pendingSavePath is string path) WriteArmy(path, Compile());
            // Confirming IS the acknowledgement that the screen holds what it holds - do not re-warn about
            // the same failed load on every subsequent save.
            _failedLoadFingerprint = null;
            ClearPendingSave();
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel", ButtonSize("Cancel", 140f)))
        {
            SetStatus(EForgeStatusKind.Info, "Save canceled - nothing was written.");
            ClearPendingSave();
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }

    // #356: reopening an imported army rebuilds it against the bundled book. Where that changes the army,
    // say exactly what changes and let the user decline - the alternative is a silent edit of an army they
    // already played, which is the harm #307 closed on the save side.
    private void DrawReopenDriftModal()
    {
        bool open = true;
        ImGui.SetNextWindowSize(new Vector2(560, 0), ImGuiCond.Appearing);
        if (!ImGui.BeginPopupModal(ReopenDriftTitle, ref open, ImGuiWindowFlags.AlwaysAutoResize)) return;

        string fileName = _pendingReopenFileName ?? "That army";
        if (_pendingReopenDrift is { } drift) WrappedText(ReopenDriftMessage(fileName, drift), YellowText, 540f);
        ImGui.Spacing();
        if (ImGui.Button("Open for editing", ButtonSize("Open for editing", 160f)))
        {
            if (_pendingReopen is { } file) Adopt(file, fileName);
            ClearPendingReopen();
            ImGui.CloseCurrentPopup();
        }
        ImGui.SameLine();
        if (ImGui.Button("Cancel", ButtonSize("Cancel", 160f)))
        {
            DeclineReopen(fileName);
            ClearPendingReopen();
            ImGui.CloseCurrentPopup();
        }
        ImGui.EndPopup();
    }

    private void ClearPendingSave()
    {
        _pendingSavePath = null;
        _pendingSaveGuard = ESaveGuard.None;
    }

    private void ClearPendingReopen()
    {
        _pendingReopen = null;
        _pendingReopenFileName = null;
        _pendingReopenDrift = null;
    }

    private static Vector4 StatusColor(EForgeStatusKind kind) => kind switch
    {
        EForgeStatusKind.Error => RedText,
        EForgeStatusKind.Success => GreenText,
        _ => ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled],
    };

    private static void WrappedText(string text, Vector4 color, float wrapWidth)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, color);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + wrapWidth);
        ImGui.TextUnformatted(text);
        ImGui.PopTextWrapPos();
        ImGui.PopStyleColor();
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
        bool fetch = ImGui.Button("Fetch", ButtonSize("Fetch", 120f)) || (entered && !busy && _importInput.Trim().Length > 0);
        ImGui.EndDisabled();
        ImGui.SameLine();
        // The link always arrives via the clipboard (it is copied out of the Army Forge share dialog), and
        // an ImGui text field has no context menu to paste from - so the paste has to be a button.
        ImGui.BeginDisabled(busy);
        if (ImGui.Button("Paste", ButtonSize("Paste", 120f)))
        {
            string clipboard = ImGui.GetClipboardText() ?? string.Empty;
            if (clipboard.Trim().Length > 0) _importInput = clipboard.Trim();
        }
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Close", ButtonSize("Close", 120f))) ImGui.CloseCurrentPopup();

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

            // #356: Save As is no longer a dead end - it carries the editable session too, so the same file
            // both plays with Army Forge's numbers and reopens here (with the difference disclosed on reopen).
            ImGui.TextDisabled(outcome.ForgeSession is not null
                ? "Save As: exact Army Forge data, and reopens here for editing.\n" +
                  "Open in Forge: edit it now, against the bundled book, priced by our Forge."
                : "Save As: exact Army Forge data (plays everywhere, edits in the Army Builder).\n" +
                  "No bundled book matched this faction, so this list cannot be reopened for editing here.");

            if (_confirmOpenInForge)
            {
                ImGui.TextColored(YellowText, "This will replace your current Forge list. Continue?");
                if (ImGui.Button("Replace list", ButtonSize("Replace list")))
                {
                    _confirmOpenInForge = false;
                    AdoptImported(outcome);
                    ImGui.CloseCurrentPopup();
                }
                ImGui.SameLine();
                if (ImGui.Button("Back", ButtonSize("Back"))) _confirmOpenInForge = false;
            }
            else
            {
                ImGui.BeginDisabled(outcome.ForgeSession is null || outcome.BundledBook is null);
                if (ImGui.Button("Open in Forge", ButtonSize("Open in Forge")))
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
                if (ImGui.Button("Save As...", ButtonSize("Save As...")) && SaveImported(outcome))
                    ImGui.CloseCurrentPopup();
            }
        }

        ImGui.EndPopup();
    }

    /// <summary>
    /// A button size that always fits its own label. The fixed widths this screen used clipped once the
    /// 18px UI font is scaled up ("Open in Forge" overflowed 140px); <paramref name="minWidth"/> keeps a
    /// row of short labels looking uniform rather than each shrinking to its text.
    /// </summary>
    private static Vector2 ButtonSize(string label, float minWidth = 140f) =>
        new(MathF.Max(minWidth, ImGui.CalcTextSize(label).X + ImGui.GetStyle().FramePadding.X * 2f), 0f);

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
        SetStatus(EForgeStatusKind.Success, hint);

        // #307: an import IS a successful adopt - the screen now holds this list, so a later Save is
        // legitimate and must not inherit an earlier load failure's guard.
        _loadFailureMessage = null;
        _failedLoadFingerprint = null;
    }

    /// <summary>#356: what "Save As" writes. The playable half is always Army Forge's verbatim import - their
    /// units, their authoritative points - but when the reconstruction against the bundled book succeeded, the
    /// editable session rides along so the file can be reopened here instead of being a dead end. The two
    /// halves can disagree (excluded units, unmatched choices, #218/#219 pricing); that is measured and
    /// disclosed at reopen, not silently applied.</summary>
    internal static ArmyListFile ImportedFileToWrite(ArmyListFile army, BuilderList? selections, BookFile? book) =>
        selections is not null && book is not null ? EditableSession.Attach(army, selections, book) : army;

    private bool SaveImported(ArmyForgeShareService.ImportOutcome outcome)
    {
        var (canceled, path) = TinyDialogs.SaveFileDialog("Save Imported Army", ArmyPaths.DefaultDialogPath, ArmyFilter);
        if (canceled || string.IsNullOrEmpty(path)) return false;
        if (Path.GetExtension(path) != ArmyListFile.EXTENSION_WITH_PERIOD)
            path = Path.ChangeExtension(path, ArmyListFile.EXTENSION_WITH_PERIOD);

        ArmyListFile army = ImportedFileToWrite(
            outcome.Result.Army, outcome.ForgeSession?.Selections, outcome.BundledBook);
        try
        {
            File.WriteAllText(path, SerializeArmy(army));
        }
        catch (Exception ex) // #307: never report a write that did not happen as a success
        {
            SetStatus(EForgeStatusKind.Error, $"SAVE FAILED - {ex.Message}");
            return false;
        }
        SetStatus(EForgeStatusKind.Success, $"Imported {Path.GetFileName(path)}");
        return true;
    }

    // ── List-mutation seams (unit-tested without ImGui) ─────────────────────────────────────────────────

    internal BuilderList List { get { EnsureLibrary(); return _list; } }

    internal void AddToList(string rosterId)
    {
        EnsureLibrary();
        RosterUnit? roster = _book.Units.FirstOrDefault(u => u.Id == rosterId);
        if (roster is null) return;
        _list.Units.Add(new BuilderUnit { RosterUnitId = roster.Id, ModelCount = roster.BaseModelCount });
        _selectedListIndex = _list.Units.Count - 1;
        _selectedRosterId = null; // list + roster selection are mutually exclusive (the config pane shows one)
    }

    internal void RemoveFromList(int index)
    {
        EnsureLibrary();
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

    internal BuiltArmyFile Compile()
    {
        EnsureLibrary();
        return ListCompiler.Compile(_book, _list);
    }

    internal IReadOnlyList<ListIssue> Issues()
    {
        EnsureLibrary();
        return ListValidator.Validate(_book, _list, Compile());
    }

    /// <summary>Reopen a saved army into an editable session. Succeeds only if the file carries the embedded
    /// book + selections (a Forge-authored .fdgarmy); a hand-authored army returns false (it still plays, it
    /// just isn't catalog-editable).</summary>
    internal bool AdoptLoaded(BuiltArmyFile loaded)
    {
        EnsureLibrary();
        if (loaded.Selections is null || loaded.Book is null) return false;
        UseBook(loaded.Book);
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
        EnsureLibrary();
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
        DrawLoadFailedModal();
        DrawSaveGuardModal();
        DrawReopenDriftModal();
        if (_statusHint is not null)
        {
            ImGui.SameLine();
            // TextUnformatted, not TextColored/Text: the line can carry a file name or an exception message,
            // and a stray '%' in either would be eaten as a printf directive.
            ImGui.PushStyleColor(ImGuiCol.Text, StatusColor(_statusKind));
            ImGui.TextUnformatted(_statusHint);
            ImGui.PopStyleColor();
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
                // Selecting a roster ("available") unit shows its read-only preview in the config pane; clear
                // any list selection so the preview isn't masked by a still-selected list unit (list takes
                // precedence in DrawConfigPane).
                _selectedRosterId = unit.Id;
                _selectedListIndex = null;
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
        // The stat lines wrap (#259 segments them per rule), so their height is measured, not assumed: an
        // under-sized rectangle would leave the bottom of a wrapped row unclickable.
        float lineH = ImGui.GetTextLineHeightWithSpacing();
        float statWrapW = MathF.Max(1f, ImGui.GetContentRegionAvail().X - ImGui.GetStyle().IndentSpacing);
        int lines = 2; // name + "Qua X+ Def Y+"
        foreach (WeaponFileEntry weapon in unit.Weapons)
            lines += RuleTextFlow.MeasureLines(RuleTextFlow.WeaponLine(weapon), statWrapW);
        if (unit.SpecialRules.Count > 0)
            lines += RuleTextFlow.MeasureLines(RuleTextFlow.RuleList(unit.SpecialRules), statWrapW);
        Vector2 rowStart = ImGui.GetCursorPos();
        ImGui.SetNextItemAllowOverlap();
        if (ImGui.Selectable($"##li{i}", selected, ImGuiSelectableFlags.None, new Vector2(0, lines * lineH)))
        {
            _selectedListIndex = i;
            _selectedRosterId = null; // mutually exclusive with the roster preview
        }
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
            RuleTextFlow.Draw(RuleTextFlow.WeaponLine(weapon), _glossary, ImGuiCol.TextDisabled);
        if (unit.SpecialRules.Count > 0)
            RuleTextFlow.Draw(RuleTextFlow.RuleList(unit.SpecialRules), _glossary, ImGuiCol.TextDisabled);
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
            RuleTextFlow.Draw(RuleTextFlow.WeaponLine(weapon), _glossary, ImGuiCol.TextDisabled);
        foreach (ItemEntry item in items)
            RuleTextFlow.Draw(RuleTextFlow.ItemLine(item), _glossary, ImGuiCol.TextDisabled);
        if (unit.SpecialRules.Count > 0)
            RuleTextFlow.Draw(RuleTextFlow.RuleList(unit.SpecialRules), _glossary, ImGuiCol.TextDisabled);
        ImGui.Unindent();

        DrawHeroJoin(idx, unit, rows);
        DrawCombinedCheckbox(idx, unit);

        RosterUnit? roster = _book.Units.FirstOrDefault(u => u.Id == bu.RosterUnitId);
        if (roster is not null)
        {
            // When combined, the partner copy is the mirror target for whole-unit (Affects=All) upgrades.
            int partnerIdx = CombinePartnerIndex(idx);
            BuilderUnit? mirror = partnerIdx >= 0 ? _list.Units[partnerIdx] : null;
            DrawUpgradeEditors(_book, _glossary, bu, roster, unit, items, mirror);
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
        EnsureLibrary();
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
        EnsureLibrary();
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
    private static void DrawUpgradeEditors(BookFile book, RuleGlossary glossary, BuilderUnit bu, RosterUnit roster,
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
            // #324: when a single-target all-swap above this section has been taken, the compiler now leaves
            // this section its copies rather than eating the pool - so availability must be measured against
            // that same reservation, or the Forge would gray out a swap the compiler would honour. Only pay
            // for the extra compile when such a rival is actually selected.
            int available = !isReplace ? int.MaxValue
                : YieldingAllSwapChosen(bu, roster, section)
                    ? ReplacePool(book, bu, roster, section, excludeOwn: false)
                    : ListCompiler.AvailableApplications(compiledUnit.Weapons, items, section.Targets);
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
                foreach (UpgradeOption option in section.Options)
                {
                    int v = ChoiceCount(bu, section.Id, option.Id);
                    DrawStepper(bu, glossary, section, option, v,
                        StepperMax(section, roster, compiledUnit, available, v));
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
                    // Inside the disabled scope so the underline picks up the same dimmed text color.
                    RuleTextFlow.DecorateControlLabel(
                        RuleTextFlow.OptionLabel(option, OptionSummary(option)), glossary);
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
                    RuleTextFlow.DecorateControlLabel(
                        RuleTextFlow.OptionLabel(option, OptionSummary(option)), glossary);
                    ImGui.EndDisabled();
                }
            }
            ImGui.Unindent();
        }
    }

    /// <summary>
    /// Upper bound for one option's stepper in a counted section: the section's own hard cap ("up to N"),
    /// against the pool it draws from - models the unit may still gain (AddModels), targets it can still
    /// replace (Replace), or one per model (a per-model upgrade).
    /// </summary>
    /// <param name="available">Replace-target availability on the FINAL compiled state, where this option's
    /// picks are already consumed - so <paramref name="current"/> comes back into its own budget.</param>
    /// <param name="current">Applications this option currently holds.</param>
    internal static int StepperMax(UpgradeSection section, RosterUnit roster, UnitFileEntry compiledUnit,
        int available, int current)
    {
        int hardBound = section.MaxApplications > 0 ? section.MaxApplications : int.MaxValue;
        int poolBound = section.Variant switch
        {
            UpgradeVariant.AddModels => Math.Max(0, roster.MaxModels - roster.BaseModelCount),
            UpgradeVariant.Replace => available + current,
            _ => compiledUnit.ModelCount,
        };
        return Math.Min(hardBound, poolBound);
    }

    // Counted-section control: [-] [count] [+] label. The buttons gray individually at their bound (- at 0,
    // + at max) and the type-in box has no internal step buttons (step 0), so it's wide enough for the number.
    private static void DrawStepper(BuilderUnit bu, RuleGlossary glossary, UpgradeSection section,
        UpgradeOption option, int v, int max)
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
        RuleTextFlow.Draw(RuleTextFlow.OptionLabel(option, OptionSummary(option)), glossary, ImGuiCol.Text);
    }

    /// <summary>Replace-target availability computed WITHOUT this section's own choices — the pool an
    /// option could draw on if the section's current pick were released (radio switching, header text).</summary>
    internal static int AvailableExcludingSection(BookFile book, BuilderUnit bu, UpgradeSection section)
    {
        RosterUnit? roster = book.Units.FirstOrDefault(u => u.Id == bu.RosterUnitId);
        return roster is null ? 0 : ReplacePool(book, bu, roster, section, excludeOwn: true);
    }

    /// <summary>Whether a single-target all-swap that must YIELD to <paramref name="section"/> (#324) is
    /// currently selected — the only case where availability has to be re-measured against the reservation
    /// rather than read off the already-compiled unit.</summary>
    private static bool YieldingAllSwapChosen(BuilderUnit bu, RosterUnit roster, UpgradeSection section) =>
        bu.Choices.Any(c => YieldsTo(roster, c.SectionId, section));

    // The all-swap at `sectionId` leaves copies for `claimant` when it is a single-target all-swap authored
    // ABOVE the claimant and they compete for the same weapon — mirroring ListCompiler's reservation rule.
    private static bool YieldsTo(RosterUnit roster, string sectionId, UpgradeSection claimant)
    {
        int index = roster.Sections.FindIndex(s => s.Id == sectionId);
        if (index < 0 || index >= roster.Sections.FindIndex(s => s.Id == claimant.Id)) return false;

        string? target = ListCompiler.SingleAllSwapTarget(roster.Sections[index]);
        return target is not null && ListCompiler.CompetesForTarget(claimant, target);
    }

    /// <summary>
    /// The pool a Replace section may draw on, measured on a compile where every all-swap that yields to it
    /// (#324) is dropped — those copies are reserved for this section, so the Forge must offer them even
    /// though the finished unit no longer shows them. <paramref name="excludeOwn"/> additionally releases
    /// this section's own picks (the radio-switching pool).
    /// </summary>
    internal static int ReplacePool(BookFile book, BuilderUnit bu, RosterUnit roster, UpgradeSection section,
        bool excludeOwn)
    {
        var view = new BuilderUnit
        {
            RosterUnitId = bu.RosterUnitId,
            ModelCount = bu.ModelCount,
            Choices = bu.Choices
                .Where(c => !(excludeOwn && c.SectionId == section.Id) && !YieldsTo(roster, c.SectionId, section))
                .ToList(),
        };
        (UnitFileEntry unit, List<ItemEntry> items) = ListCompiler.CompileUnitDetailed(book, view);
        return ListCompiler.AvailableApplications(unit.Weapons, items, section.Targets);
    }

    // Read-only profile of an available (roster) unit, mirroring what the real Army Forge shows in its config
    // column when you select a unit on the left: stats, default gear, rules, base, the caster spell list (for
    // caster units), and every upgrade section with all its options - all non-interactive. The upgrade option
    // labels already bake in the full gained profile (e.g. "Energy Spear (A2, AP(4))"), so OptionSummary alone
    // shows the gear each option grants; no need to re-list WeaponsGained/RulesGained.
    private void DrawRosterPreview(RosterUnit unit)
    {
        ImGui.TextUnformatted(RosterStatLine(unit));
        ImGui.Separator();
        ImGui.Indent();
        foreach (WeaponFileEntry weapon in unit.Weapons)
            RuleTextFlow.Draw(RuleTextFlow.WeaponLine(weapon), _glossary, ImGuiCol.TextDisabled);
        foreach (ItemEntry item in unit.Items)
            RuleTextFlow.Draw(RuleTextFlow.ItemLine(item), _glossary, ImGuiCol.TextDisabled);
        if (unit.Rules.Count > 0)
            RuleTextFlow.Draw(RuleTextFlow.RuleList(unit.Rules), _glossary, ImGuiCol.TextDisabled);
        ImGui.TextDisabled($"Base: {BaseSummary(unit.Base)}");
        ImGui.Unindent();

        // Caster units draw from the army-wide spell list; show it here (the roster pane lists it once for the
        // whole book, but the real Army Forge surfaces it per caster unit).
        if (IsCaster(unit) && _book.Spells.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextDisabled("SPELLS");
            ImGui.Separator();
            ImGui.PushTextWrapPos(0f);
            foreach (FDG.Rules.Definitions.SpellDefinition spell in _book.Spells)
                ImGui.TextDisabled($"{spell.Name} ({spell.Threshold}): {FDG.Stages.SpellText.Describe(spell)}");
            ImGui.PopTextWrapPos();
        }

        if (unit.Sections.Count == 0) return;
        ImGui.Spacing();
        ImGui.TextDisabled("UPGRADES");
        ImGui.Separator();
        foreach (UpgradeSection section in unit.Sections)
        {
            ImGui.TextUnformatted(section.Label);
            ImGui.Indent();
            foreach (UpgradeOption option in section.Options)
                RuleTextFlow.Draw(RuleTextFlow.OptionLabel(option, OptionSummary(option)), _glossary,
                    ImGuiCol.TextDisabled);
            ImGui.Unindent();
        }
    }

    private RosterUnit? Selected =>
        _selectedRosterId is null ? null : _book.Units.FirstOrDefault(u => u.Id == _selectedRosterId);

    // ── Save / Load ─────────────────────────────────────────────────────────────────────────────────────

    private void Save(BuiltArmyFile compiled)
    {
        var (canceled, path) = TinyDialogs.SaveFileDialog("Save Army", ArmyPaths.DefaultDialogPath, ArmyFilter);
        if (canceled || string.IsNullOrEmpty(path)) return;
        if (Path.GetExtension(path) != ArmyListFile.EXTENSION_WITH_PERIOD)
            path = Path.ChangeExtension(path, ArmyListFile.EXTENSION_WITH_PERIOD);

        // #307: a save that would write something other than an army the user built gets one confirm naming
        // the target file. The ordinary path (a real list, deliberately saved) is untouched - no extra click.
        ESaveGuard guard = PendingSaveGuard();
        if (guard == ESaveGuard.None) { WriteArmy(path, compiled); return; }

        _pendingSavePath = path;
        _pendingSaveGuard = guard;
        ImGui.OpenPopup(SaveGuardTitle);
    }

    /// <summary>Serialize at the army's RUNTIME type. Serializing through a base-typed reference writes only
    /// the base properties, which would silently drop the embedded selections + book (the engine ignores them
    /// on load; the Forge reads them back to re-edit). See BuiltArmyFile.</summary>
    private static string SerializeArmy(ArmyListFile army) =>
        JsonSerializer.Serialize(army, army.GetType(), RuleJson.Options);

    private void WriteArmy(string path, BuiltArmyFile compiled)
    {
        try
        {
            File.WriteAllText(path, SerializeArmy(compiled));
            SetStatus(EForgeStatusKind.Success, $"Saved {Path.GetFileName(path)}");
        }
        catch (Exception ex) // read-only target, vanished directory, permissions - never leave it looking saved
        {
            SetStatus(EForgeStatusKind.Error, $"SAVE FAILED - {ex.Message}");
        }
    }

    private void Load()
    {
        var (canceled, paths) = TinyDialogs.OpenFileDialog("Load Army", ArmyPaths.DefaultDialogPath, false, ArmyFilter);
        if (canceled) return;
        string path = paths?.FirstOrDefault() ?? "";
        if (string.IsNullOrEmpty(path)) return; // no file picked (incl. #285's silent no-op with no dialog helper)

        string fileName = Path.GetFileName(path);
        BuiltArmyFile? loaded = null;
        string? readError = null;
        if (!File.Exists(path))
        {
            readError = "That file no longer exists.";
        }
        else
        {
            // #307: a malformed .fdgarmy used to throw straight out of Draw (JsonException) and take the
            // renderer with it; a missing file and a null deserialize both returned in silence.
            try { loaded = JsonSerializer.Deserialize<BuiltArmyFile>(File.ReadAllText(path), RuleJson.Options); }
            catch (Exception ex) { readError = "That file could not be read:\n\n" + ex.Message; }
        }

        switch (TryAdopt(loaded, fileName, readError))
        {
            case ELoadOutcome.Rejected: ImGui.OpenPopup(LoadFailedTitle); break;
            case ELoadOutcome.NeedsDriftConfirm: ImGui.OpenPopup(ReopenDriftTitle); break;
        }
    }

    // ── #307: load/save outcome state (ImGui-free, so the whole decision chain is testable) ─────────────

    internal const string LoadFailedTitle = "Load failed";
    internal const string SaveGuardTitle = "Save this list?";
    internal const string ReopenDriftTitle = "Open for editing?";

    /// <summary>#356: what reopening an imported army for editing would change, or empty when it changes
    /// nothing. The file on disk is untouched either way - this describes the screen, not the file.</summary>
    internal static string ReopenDriftMessage(string fileName, EditableSessionDrift drift)
    {
        var lines = new List<string>();
        if (drift.SavedPoints != drift.RebuiltPoints)
            lines.Add($"    Points: {drift.SavedPoints} -> {drift.RebuiltPoints}");
        if (drift.SavedUnitCount != drift.RebuiltUnitCount)
            lines.Add($"    Units: {drift.SavedUnitCount} -> {drift.RebuiltUnitCount}");
        if (drift.DroppedUnits.Count > 0)
            lines.Add("    Dropped: " + string.Join(", ", drift.DroppedUnits));

        return $"{fileName} came from Army Forge. It PLAYS with Army Forge's own numbers, but the Forge edits "
            + "it by rebuilding it against the bundled book - and the two do not match.\n\n"
            + "Opening it for editing would change it:\n"
            + string.Join("\n", lines)
            + "\n\nThe file on disk is not touched until you save. Open it for editing, or cancel and leave "
            + "the list on screen as it is.";
    }

    internal const string NoEmbeddedBookReason =
        "That army carries no embedded book, so the Forge cannot reopen it for editing. It was almost "
        + "certainly written by the Army Builder - open it there instead. The army itself is fine and still "
        + "plays normally.";

    /// <summary>Adopt a file that has just been read, recording the outcome: status line, modal text, and the
    /// list fingerprint the Save guard compares against. A result other than <see cref="ELoadOutcome.Adopted"/>
    /// is the caller's cue to raise the matching modal.</summary>
    internal ELoadOutcome TryAdopt(BuiltArmyFile? loaded, string fileName, string? readError)
    {
        EnsureLibrary();
        string? reason = null;
        if (readError is not null) reason = readError;
        else if (loaded is null) reason = "That file is empty, or is not an army list.";
        else if (loaded.Selections is null || loaded.Book is null) reason = NoEmbeddedBookReason;

        if (reason is not null)
        {
            RecordLoadFailure(fileName, reason);
            return ELoadOutcome.Rejected;
        }

        // #356: an imported army carries Army Forge's units alongside OUR reconstruction of them, and the two
        // need not agree. Reopening recompiles from the reconstruction, so anything it would change has to be
        // shown BEFORE the screen adopts it - a Forge-authored file measures no drift and adopts silently.
        EditableSessionDrift? drift = EditableSession.Measure(loaded!);
        if (drift is { Differs: true })
        {
            _pendingReopen = loaded;
            _pendingReopenFileName = fileName;
            _pendingReopenDrift = drift;
            return ELoadOutcome.NeedsDriftConfirm;
        }

        return Adopt(loaded!, fileName) ? ELoadOutcome.Adopted : ELoadOutcome.Rejected;
    }

    /// <summary>Take the file as the screen's list and clear any armed load failure.</summary>
    private bool Adopt(BuiltArmyFile loaded, string fileName)
    {
        if (!AdoptLoaded(loaded)) // defensive: the caller already checked the embedded block is present
        {
            RecordLoadFailure(fileName, NoEmbeddedBookReason);
            return false;
        }
        _loadFailureMessage = null;
        _failedLoadFingerprint = null;
        SetStatus(EForgeStatusKind.Success, $"Loaded {fileName}");
        return true;
    }

    private void RecordLoadFailure(string fileName, string reason)
    {
        _loadFailureMessage = LoadFailureMessage(fileName, reason);
        _failedLoadFingerprint = ListFingerprint(_list);
        SetStatus(EForgeStatusKind.Error, $"LOAD FAILED - {fileName} was not opened");
    }

    /// <summary>The user declined the #356 rebuild. Nothing was opened, so the Save guard arms exactly as it
    /// does after a rejection - but this was a deliberate choice, not a failure, so it is not reported as one.</summary>
    private void DeclineReopen(string fileName)
    {
        _failedLoadFingerprint = ListFingerprint(_list);
        SetStatus(EForgeStatusKind.Info, $"{fileName} was not opened - the list on screen is unchanged.");
    }

    /// <summary>Which confirm (if any) the next Save needs, from the screen's current state.</summary>
    internal ESaveGuard PendingSaveGuard()
    {
        EnsureLibrary();
        bool failedLoadPending = _failedLoadFingerprint is not null;
        bool untouchedSince = failedLoadPending && _failedLoadFingerprint == ListFingerprint(_list);
        return EvaluateSaveGuard(_list.Units.Count, failedLoadPending, untouchedSince);
    }

    internal static ESaveGuard EvaluateSaveGuard(int unitCount, bool failedLoadPending, bool untouchedSince)
    {
        // The failed-load reason is the more specific story, so it outranks a bare empty list.
        if (failedLoadPending && untouchedSince) return ESaveGuard.UnchangedAfterFailedLoad;
        return unitCount == 0 ? ESaveGuard.EmptyList : ESaveGuard.None;
    }

    /// <summary>Structural fingerprint of the editable list - equal lists give equal strings. Used only to
    /// answer "has anything changed since that load failed", so any faithful serialization will do.</summary>
    internal static string ListFingerprint(BuilderList list) => JsonSerializer.Serialize(list, RuleJson.Options);

    internal static string LoadFailureMessage(string fileName, string reason) =>
        $"{fileName} was NOT loaded.\n\n{reason}\n\n"
        + "The list on screen has not changed - it is still whatever was here before. Saving now would write "
        + "THAT list, not the file you just picked.";

    internal static string SaveGuardMessage(ESaveGuard guard, string fileName, string bookName, int unitCount)
    {
        string writing = unitCount == 0
            ? $"an EMPTY {bookName} list"
            : $"the {bookName} list on screen ({unitCount} unit{(unitCount == 1 ? "" : "s")})";
        string lead = guard == ESaveGuard.UnchangedAfterFailedLoad
            ? "Your last load did not take effect, and nothing has changed on screen since."
            : "This list has no units in it.";
        return $"{lead}\n\nSaving now writes {writing} to:\n    {fileName}\n\n"
            + "If that file already holds an army you want to keep, this will overwrite it. Cancel unless "
            + "writing this list is what you meant to do.";
    }

    private void SetStatus(EForgeStatusKind kind, string text)
    {
        _statusKind = kind;
        _statusHint = text;
    }

    internal (EForgeStatusKind Kind, string? Text) Status => (_statusKind, _statusHint);

    // ── Pure formatting seams (unit-tested; ImGui itself is hand-verified) ──────────────────────────────

    internal static string PointsHeader(int total, int limit) => $"{total} / {limit} pts";

    internal static string RosterStatLine(RosterUnit u) =>
        $"{u.Name} [{u.BaseModelCount}] - Qua {u.Quality}+ Def {u.Defense}+  ({u.BasePointCost} pts)";

    internal static string OptionSummary(UpgradeOption o) =>
        o.Cost == 0 ? o.Label : $"{o.Label}  (+{o.Cost} pts)";

    private const float MmPerInch = 25.4f;

    // Base footprint in mm, the unit modellers author in: "25mm" for a circle, "25 x 50mm" for a rectangle.
    // Bases persist in inches (BaseFileEntry); round to the nearest mm so 0.984252" reads back as the 25mm it
    // was authored as rather than 24.99...mm.
    internal static string BaseSummary(BaseFileEntry b) =>
        b.Shape == EBaseShapeKind.Rectangle
            ? $"{Mm(b.WidthInches)} x {Mm(b.HeightInches)}mm"
            : $"{Mm(b.DiameterInches)}mm";

    private static int Mm(float inches) => (int)MathF.Round(inches * MmPerInch);

    // A unit can cast if it carries any Caster rule - "Caster(X)" or the squad-wide "Caster Group" (#033);
    // both drive the per-unit spell list in the roster preview, matching the real Army Forge.
    internal static bool IsCaster(RosterUnit unit) =>
        unit.Rules.Any(r => r.PrintableName.StartsWith("Caster", StringComparison.Ordinal));

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
