using System.Numerics;
using FDG.ArmyBuilding;
using FDG.Network.Connection.Lobby;
using FDG;
using FDG.SaveLoad;
using FdgRaylib.Config;
using FdgRaylib.Import;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// #460: the lobby roster's Import popup - an Army Forge share link straight into one slot, without the
/// trip through the Forge screen, Save As and Load Army. A slimmed-down sibling of the Forge's #241 popup
/// (<c>ArmyForgeScreen.DrawImportModal</c>): the same fetch (<see cref="ArmyForgeShareService"/>) and the same
/// issue readout, minus the Forge-only pricing reconciliation and its Open in Forge / Save As exits.
///
/// <para>Flow: paste -> the fetch starts by itself (immediately on Paste or Enter, after a short pause when
/// typed) -> preview + issues -> Import. A list that breaks the lobby settings
/// (<see cref="LaunchGate.SettingsProblems"/>, the LAUNCH gate's own check) still imports, behind a confirm -
/// the host may change the settings. The assigned army is exactly what the Forge's Save As writes (#356 shape),
/// and the optional copy is that same file.</para>
///
/// <para>Main thread only: the fetch runs on a worker task, and Draw polls it.</para>
/// </summary>
internal sealed class LobbyImportPopup
{
    private const string Title = "Import from Army Forge";
    private const string ConfirmTitle = "Import anyway?";

    /// <summary>How long typing must pause before a typed link is fetched. Paste and Enter skip the wait.</summary>
    private const double TypingPauseSeconds = 0.6;

    private static readonly Vector4 RedText    = new(0.90f, 0.40f, 0.40f, 1f);
    private static readonly Vector4 YellowText = new(0.90f, 0.80f, 0.35f, 1f);

    private readonly Action<PlayerID, ArmyListFile> _assign;

    private bool _openRequested;
    private PlayerID _target;
    private string _targetName = string.Empty;

    private string _input = string.Empty;
    private double _editedAt;
    private bool _fetchNow;

    // The list id the current (or last) fetch was for; a fresh edit only refetches when the id changes.
    private string? _fetchedId;
    private CancellationTokenSource? _cts;
    private Task<ArmyForgeShareService.ImportOutcome>? _task;
    private ArmyForgeShareService.ImportOutcome? _outcome;
    private string? _error;
    private IReadOnlyList<string> _settingsProblems = Array.Empty<string>();

    /// <param name="assign">Puts the imported army on the slot (and marks the slot hand-picked).</param>
    public LobbyImportPopup(Action<PlayerID, ArmyListFile> assign) => _assign = assign;

    /// <summary>Asks for the popup on the next <see cref="Draw"/>. Only the intent is recorded: OpenPopup
    /// has to run in the same ID scope as BeginPopupModal, which is Draw's.</summary>
    public void Open(PlayerID target, string targetName)
    {
        CancelFetch();
        _target = target;
        _targetName = targetName;
        _input = string.Empty;
        _fetchNow = false;
        _fetchedId = null;
        _outcome = null;
        _error = null;
        _openRequested = true;
    }

    /// <param name="uiScale">The lobby's window-to-monitor scale, so the popup shrinks with the window the
    /// way the rest of the lobby does.</param>
    public void Draw(ILobbyViewModel viewModel, float uiScale)
    {
        if (_openRequested)
        {
            _openRequested = false;
            ImGui.OpenPopup(Title);
        }

        // Re-centred every frame: the popup grows when the preview arrives, and growing down from where it
        // first opened would push Import off the bottom of a short window.
        ImGuiViewportPtr viewport = ImGui.GetMainViewport();
        float margin = ImGui.GetStyle().WindowPadding.X * 2f;
        ImGui.SetNextWindowPos(viewport.GetCenter(), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        ImGui.SetNextWindowSizeConstraints(Vector2.Zero, viewport.WorkSize - new Vector2(margin, margin));

        bool open = true;
        if (!ImGui.BeginPopupModal(Title, ref open, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
        {
            if (_task is not null) CancelFetch(); // closed by its title-bar X
            return;
        }
        ImGui.SetWindowFontScale(uiScale);

        // Everything is sized in em (after the font scale) and capped by the viewport, so it holds its shape
        // from a 4K fullscreen down to a small window.
        float width = MathF.Min(UiChrome.Em(36f), viewport.WorkSize.X - margin * 3f);
        // Text wraps at this explicit width rather than at the window edge (UiText.Wrapped): an
        // auto-resizing popup has no edge yet on its first frame, and would open one word wide.
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);

        Harvest();
        UpdateFetch();
        bool busy = _task is not null;

        ImGui.PushStyleColor(ImGuiCol.Text, ImGuiTheme.HeaderAccent);
        ImGui.TextUnformatted($"For: {_targetName}");
        ImGui.PopStyleColor();
        ImGui.TextUnformatted(LobbyArmyImport.Instructions);
        ImGui.Spacing();

        DrawLinkField(width);

        if (busy) UiText.Disabled("Fetching from army-forge.onepagerules.com ...");
        else if (ShowNotALinkHint())
        {
            ImGui.PushStyleColor(ImGuiCol.Text, YellowText);
            ImGui.TextUnformatted(LobbyArmyImport.NotALinkHint);
            ImGui.PopStyleColor();
        }
        if (_error is not null)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, RedText);
            ImGui.TextUnformatted(_error);
            ImGui.PopStyleColor();
        }

        if (_outcome is { } outcome && !busy) DrawPreview(outcome, viewModel, width, viewport.WorkSize.Y);

        ImGui.Spacing();
        DrawSaveCopyToggle(_outcome?.Result.Army);
        ImGui.Spacing();

        ImGui.BeginDisabled(busy || _outcome is null);
        bool import = UiButton.Confirm("Import", UiChrome.ButtonSize("Import"));
        ImGui.EndDisabled();
        ImGui.SameLine();
        // #240: edge-only Esc (stuck-key safe), and not while the confirm on top of this owns it.
        bool cancel = UiButton.Back("Cancel", UiChrome.ButtonSize("Cancel"))
            || (!ImGui.IsPopupOpen(ConfirmTitle) && ImGui.IsKeyPressed(ImGuiKey.Escape, repeat: false));

        if (import && _outcome is not null)
        {
            _settingsProblems = LaunchGate.SettingsProblems(_outcome.Result.Army, viewModel.ArmyPoints,
                viewModel.AllowedGameSystems);
            if (_settingsProblems.Count > 0) ImGui.OpenPopup(ConfirmTitle);
            else if (Commit(viewModel)) ImGui.CloseCurrentPopup();
        }
        else if (cancel)
        {
            CancelFetch();
            ImGui.CloseCurrentPopup();
        }

        DrawSettingsConfirm(viewModel, uiScale, width);

        ImGui.PopTextWrapPos();
        ImGui.EndPopup();
    }

    private void DrawLinkField(float width)
    {
        // #403's Paste button, kept: the link always arrives on the clipboard, and an ImGui field has no
        // right-click menu to paste from. A paste fetches at once - there is no typing to wait out.
        float pasteW = UiChrome.ButtonSize("Paste").X;
        ImGui.SetNextItemWidth(MathF.Max(UiChrome.Em(6f), width - pasteW - ImGui.GetStyle().ItemSpacing.X));
        if (ImGui.InputText("##lobby-import-link", ref _input, 512)) _editedAt = ImGui.GetTime();
        // Enter: fetch now, and again even when it is the same link (the retry after a failed fetch).
        if (ImGui.IsItemDeactivatedAfterEdit() || (ImGui.IsItemFocused() && ImGui.IsKeyPressed(ImGuiKey.Enter)))
            _fetchNow = true;
        ImGui.SameLine();
        if (UiButton.Navigate("Paste", new Vector2(pasteW, ImGui.GetFrameHeight())))
        {
            string clipboard = (ImGui.GetClipboardText() ?? string.Empty).Trim();
            if (clipboard.Length > 0) _input = clipboard;
            _fetchNow = true;
            _fetchedId = null; // pasting the same link again retries it
        }
    }

    private bool ShowNotALinkHint()
    {
        string trimmed = _input.Trim();
        return trimmed.Length > 0
            && !ArmyForgeShareService.TryParseShareId(trimmed, out _)
            && (_fetchNow || ImGui.GetTime() - _editedAt >= TypingPauseSeconds);
    }

    // Collects a finished fetch. A superseded one was canceled and its result is dropped silently.
    private void Harvest()
    {
        if (_task is not { IsCompleted: true } finished) return;
        _task = null;
        if (finished.IsCanceled) return;
        if (finished.IsFaulted)
            _error = (finished.Exception?.GetBaseException().Message ?? "Import failed.")
                + " Paste the link again to retry.";
        else
            _outcome = finished.Result;
    }

    // Starts a fetch when the field holds a share link we have not fetched yet - at once after Paste or
    // Enter, otherwise once typing pauses - and drops the preview when the field stops holding one.
    private void UpdateFetch()
    {
        bool now = _fetchNow;
        _fetchNow = false;

        if (!ArmyForgeShareService.TryParseShareId(_input, out string id))
        {
            if (_fetchedId is not null || _outcome is not null || _error is not null)
            {
                CancelFetch();
                _fetchedId = null;
                _outcome = null;
                _error = null;
            }
            return;
        }

        if (id == _fetchedId && !now) return;
        if (id == _fetchedId && (_task is not null || _outcome is not null)) return; // Enter on a good fetch
        if (!now && ImGui.GetTime() - _editedAt < TypingPauseSeconds) return;

        CancelFetch();
        _fetchedId = id;
        _outcome = null;
        _error = null;
        var cts = new CancellationTokenSource();
        _cts = cts;
        _task = Task.Run(() => ArmyForgeShareService.FetchAndImportAsync(id, cts.Token), cts.Token);
    }

    private void CancelFetch()
    {
        _cts?.Cancel();
        _cts = null;
        _task = null;
    }

    private static void DrawPreview(ArmyForgeShareService.ImportOutcome outcome, ILobbyViewModel viewModel,
        float width, float viewportH)
    {
        ArmyListFile army = outcome.Result.Army;
        IReadOnlyList<string> settings = LaunchGate.SettingsProblems(army, viewModel.ArmyPoints,
            viewModel.AllowedGameSystems);

        ImGui.Separator();
        ImGui.TextUnformatted($"{army.Name}  -  {army.Faction}");
        UiText.Colored(army.TotalPoints > viewModel.ArmyPoints ? RedText : ImGui.GetStyle().Colors[(int)ImGuiCol.Text],
            $"{army.TotalPoints} pts (lobby limit {viewModel.ArmyPoints})");

        // Outside the scroll region: these are what the Import confirm will be about, so never scrolled away.
        if (settings.Count > 0)
        {
            ImGui.PushStyleColor(ImGuiCol.Text, RedText);
            foreach (string problem in settings) ImGui.TextUnformatted(LobbyArmyImport.ProblemLine(problem));
            ImGui.PopStyleColor();
        }

        // A third of the window tall, within sane em bounds: tall enough to read a list on a big screen,
        // short enough that Import stays on a small one.
        float previewH = Math.Clamp(viewportH * 0.33f, UiChrome.Em(5f), UiChrome.Em(16f));
        ImGui.BeginChild("##lobby-import-preview", new Vector2(width, previewH), ImGuiChildFlags.Borders);
        ImGui.PushTextWrapPos(0f);
        UiText.Disabled("UNITS");
        foreach (UnitFileEntry u in army.Units)
        {
            ImGui.TextUnformatted($"{u.Name} [{u.ModelCount}] - Qua {u.Quality}+ Def {u.Defense}+  ({u.PointCost} pts)");
            string gear = string.Join(", ", u.Weapons.Select(w => $"{w.Quantity}x {w.Name}"));
            if (gear.Length > 0) UiText.Disabled("    " + gear);
        }
        if (outcome.Result.ListErrors.Count > 0)
        {
            ImGui.Spacing();
            UiText.Disabled("ARMY FORGE LIST ERRORS");
            ImGui.PushStyleColor(ImGuiCol.Text, RedText);
            foreach (string error in outcome.Result.ListErrors) ImGui.TextUnformatted(error);
            ImGui.PopStyleColor();
        }
        if (outcome.Result.Warnings.Count > 0)
        {
            ImGui.Spacing();
            UiText.Disabled("IMPORT WARNINGS");
            ImGui.PushStyleColor(ImGuiCol.Text, YellowText);
            foreach (string warning in outcome.Result.Warnings) ImGui.TextUnformatted(warning);
            ImGui.PopStyleColor();
        }
        if (outcome.InertRules.Count > 0)
        {
            ImGui.Spacing();
            UiText.Disabled("RULES NOT ENFORCED BY THE ENGINE (inert in play)");
            ImGui.PushStyleColor(ImGuiCol.Text, YellowText);
            ImGui.TextUnformatted(string.Join(", ", outcome.InertRules));
            ImGui.PopStyleColor();
        }
        ImGui.PopTextWrapPos();
        ImGui.EndChild();
    }

    private static void DrawSaveCopyToggle(ArmyListFile? army)
    {
        string? folder = ArmyPaths.FolderPath;
        bool saveCopy = UserConfig.Current.ImportSavesCopy;

        ImGui.BeginDisabled(folder is null);
        if (UiButton.Checkbox("Save a copy to my armies folder", ref saveCopy))
        {
            UserConfig.Current.ImportSavesCopy = saveCopy;
            UserConfig.Save();
        }
        ImGui.EndDisabled();
        if (folder is null)
        {
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
                UiText.Tooltip("No armies folder was found next to the game, so there is nowhere to save a copy.");
            return;
        }

        // Where it will land, before you press Import - nothing reports it afterwards.
        if (saveCopy && army is not null)
            ImGui.TextUnformatted(DisplayPath(SavePath(army, folder), folder));
    }

    private static string SavePath(ArmyListFile army, string armiesFolder)
    {
        string dir = LobbyArmyImport.SaveFolder(armiesFolder, army.GameSystem, Directory.Exists)!;
        return LobbyArmyImport.UniquePath(dir, LobbyArmyImport.SaveFileName(army), File.Exists);
    }

    // "Saves as armies/GDF/2k - Orks.fdgarmy": relative to the armies folder's parent, so it reads as the
    // folder the player knows rather than a full machine path.
    private static string DisplayPath(string path, string armiesFolder)
    {
        string parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(armiesFolder)) ?? armiesFolder;
        return "Saves as " + Path.GetRelativePath(parent, path).Replace('\\', '/');
    }

    // The "breaks the lobby settings" confirm, stacked on the import popup. Cancel first and focused - the
    // safe default - matching the lobby's "Launch anyway?".
    private void DrawSettingsConfirm(ILobbyViewModel viewModel, float uiScale, float width)
    {
        bool open = true;
        if (!ImGui.BeginPopupModal(ConfirmTitle, ref open, ImGuiWindowFlags.AlwaysAutoResize | ImGuiWindowFlags.NoSavedSettings))
            return;
        ImGui.SetWindowFontScale(uiScale);
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + width);

        ImGui.TextUnformatted(LobbyArmyImport.SettingsConfirmLead);
        ImGui.PushStyleColor(ImGuiCol.Text, RedText);
        foreach (string problem in _settingsProblems) ImGui.TextUnformatted("  " + LobbyArmyImport.ProblemLine(problem));
        ImGui.PopStyleColor();
        ImGui.Spacing();
        ImGui.TextUnformatted(LobbyArmyImport.SettingsConfirmBody);
        ImGui.Spacing();

        bool committed = false;
        if (UiButton.Back("Cancel", UiChrome.ButtonSize("Cancel")) || ImGui.IsKeyPressed(ImGuiKey.Escape, repeat: false))
            ImGui.CloseCurrentPopup();
        ImGui.SetItemDefaultFocus();
        ImGui.SameLine();
        if (UiButton.Confirm("Import anyway", UiChrome.ButtonSize("Import anyway")))
        {
            ImGui.CloseCurrentPopup();
            committed = Commit(viewModel);
        }

        ImGui.PopTextWrapPos();
        ImGui.EndPopup();

        // Closing the import popup too, now that the confirm is ended and the import popup is current again.
        if (committed) ImGui.CloseCurrentPopup();
    }

    // Saves the copy (when ticked) and assigns. Save first: a copy the player asked for that silently did not
    // happen would be the #307 lie, so a failed write leaves the slot alone and says why.
    private bool Commit(ILobbyViewModel viewModel)
    {
        if (_outcome is not { } outcome) return false;
        if (!viewModel.CheckCanModifyPlayerIDInfo(_target))
        {
            _error = $"{_targetName}'s slot can no longer be changed from here.";
            return false;
        }

        ArmyListFile army = ArmyForgeScreen.ImportedFileToWrite(
            outcome.Result.Army, outcome.ForgeSession?.Selections, outcome.BundledBook);

        if (UserConfig.Current.ImportSavesCopy && ArmyPaths.FolderPath is { } folder)
        {
            try
            {
                File.WriteAllText(SavePath(army, folder), ArmyForgeScreen.SerializeArmy(army));
            }
            catch (Exception ex)
            {
                _error = $"Could not save the copy ({ex.Message}). Untick the box to import without saving.";
                return false;
            }
        }

        _assign(_target, army);
        return true;
    }
}
