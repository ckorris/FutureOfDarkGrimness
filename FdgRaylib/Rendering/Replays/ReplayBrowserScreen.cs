using System.Numerics;
using FDG.Replay;
using FDG.SaveLoad;
using FdgRaylib.SaveLoad;
using ImGuiNET;
using TinyDialogsNet;

namespace FdgRaylib.Rendering.Replays;

/// <summary>
/// The replay browser (#456), behind the main menu's Replays button: every game recorded in <c>Replays/</c>, newest
/// first - when it started, the armies, the players, how far it got - read from each file's first line. Open plays
/// one (double-click does too); Open File picks any <c>.fdgreplay</c>, or a <c>.fdgsave</c> carrying a replay (#453).
/// A replay loads on a worker (a big one is described step by step up front); a file that cannot be read says why,
/// here, and never throws out of Draw.
/// </summary>
public sealed class ReplayBrowserScreen : IAppScreen
{
    private const string Title = "Replays";
    private const string BackLabel = "Back";

    private static readonly Vector4 HeadText = new(0.85f, 0.85f, 0.90f, 1f);
    private static readonly Vector4 ErrorColor = new(1f, 0.45f, 0.45f, 1f);
    private static readonly Vector4 DimText = new(0.62f, 0.62f, 0.62f, 1f);

    private static readonly FileFilter ReplayFilter = new(
        $"Replays and saves (*{ReplayFile.EXTENSION_WITH_PERIOD}, *{GameSaveFile.EXTENSION_WITH_PERIOD})",
        new[] { $"*{ReplayFile.EXTENSION_WITH_PERIOD}", $"*{GameSaveFile.EXTENSION_WITH_PERIOD}" });

    private IReadOnlyList<ReplayBrowserRow> _rows = Array.Empty<ReplayBrowserRow>();
    private int _selected = -1;
    private Task<ReplaySession>? _loading;
    private string _loadingName = "";
    private string? _error;
    private string? _confirmDelete;

    public Action? OnBack;

    /// <summary>A replay finished loading: show it. Main thread, from inside <see cref="Draw"/>.</summary>
    public Action<ReplaySession>? OnOpened;

    /// <summary>The folder listed; null is <see cref="ReplaySave.DirectoryPath"/>.</summary>
    public string? Directory { get; set; }

    public void OnShown()
    {
        _error = null;
        Refresh();
    }

    public void Refresh()
    {
        string? selectedPath = _selected >= 0 && _selected < _rows.Count ? _rows[_selected].Path : null;
        _rows = ReplayCatalog.List(Directory);
        _selected = selectedPath == null ? (_rows.Count > 0 ? 0 : -1) : IndexOf(selectedPath);
    }

    public void Draw(int screenW, int screenH)
    {
        PollLoading();

        ImGui.SetNextWindowPos(Vector2.Zero, ImGuiCond.Always);
        ImGui.SetNextWindowSize(new Vector2(screenW, screenH), ImGuiCond.Always);
        ImGui.Begin("##ReplayBrowser",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize |
            ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.NoTitleBar);

        bool busy = _loading != null;
        ImGui.BeginDisabled(busy);

        if (UiButton.Back(BackLabel, UiChrome.ButtonSize(BackLabel))) OnBack?.Invoke();
        ImGui.SameLine(0f, ImGui.GetFontSize() * 1.5f);
        UiText.Colored(HeadText, Title);
        ImGui.SameLine();
        UiText.Colored(DimText, ReplayLabels.Ascii($"   {Directory ?? ReplaySave.DirectoryPath}"));

        if (busy) UiText.Colored(HeadText, ReplayLabels.Ascii($"Loading {_loadingName}..."));
        if (_error != null) UiText.Colored(ErrorColor, _error);
        ImGui.Separator();

        float buttonsHeight = ImGui.GetFrameHeightWithSpacing() * 1.5f;
        DrawTable(new Vector2(0, Math.Max(ImGui.GetFrameHeight() * 4, ImGui.GetContentRegionAvail().Y - buttonsHeight)));
        DrawButtons();

        ImGui.EndDisabled();
        DrawDeleteConfirm();
        ImGui.End();
    }

    private void DrawTable(Vector2 size)
    {
        if (_rows.Count == 0)
        {
            UiText.Colored(DimText, "No replays yet. Every game played writes one here when it ends.");
            ImGui.Dummy(size with { Y = Math.Max(0, size.Y - ImGui.GetTextLineHeightWithSpacing()) });
            return;
        }

        if (ImGui.BeginTable("##replays", 7,
                ImGuiTableFlags.Borders | ImGuiTableFlags.RowBg | ImGuiTableFlags.ScrollY | ImGuiTableFlags.SizingStretchProp,
                size))
        {
            ImGui.TableSetupScrollFreeze(0, 1);
            ImGui.TableSetupColumn("Started", ImGuiTableColumnFlags.WidthStretch, 0.21f);
            ImGui.TableSetupColumn("Armies", ImGuiTableColumnFlags.WidthStretch, 0.22f);
            ImGui.TableSetupColumn("Players", ImGuiTableColumnFlags.WidthStretch, 0.20f);
            ImGui.TableSetupColumn("Rounds", ImGuiTableColumnFlags.WidthStretch, 0.06f);
            ImGui.TableSetupColumn("Steps", ImGuiTableColumnFlags.WidthStretch, 0.06f);
            ImGui.TableSetupColumn("Outcome", ImGuiTableColumnFlags.WidthStretch, 0.17f);
            ImGui.TableSetupColumn("Size", ImGuiTableColumnFlags.WidthStretch, 0.08f);
            ImGui.TableHeadersRow();

            for (int i = 0; i < _rows.Count; i++)
            {
                ReplayBrowserRow row = _rows[i];
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                if (ImGui.Selectable($"{(row.Error == null ? row.Started : "(unreadable)")}##replay{i}", _selected == i,
                        ImGuiSelectableFlags.SpanAllColumns | ImGuiSelectableFlags.AllowDoubleClick))
                {
                    _selected = i;
                    if (ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && row.Error == null) Open(row.Path);
                }
                if (ImGui.IsItemHovered()) UiText.Tooltip(ReplayLabels.Ascii(row.FileName));

                ImGui.TableNextColumn();
                UiText.Wrapped(row.Armies);
                if (row.Error != null)
                {
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.TableNextColumn();
                    ImGui.PushStyleColor(ImGuiCol.Text, ErrorColor);
                    UiText.Wrapped(row.Error);
                    ImGui.PopStyleColor();
                }
                else
                {
                    ImGui.TableNextColumn();
                    UiText.Wrapped(row.Players);
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(row.Rounds.ToString());
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(row.Steps.ToString());
                    ImGui.TableNextColumn();
                    ImGui.TextUnformatted(row.Outcome);
                }
                ImGui.TableNextColumn();
                ImGui.TextUnformatted(row.Size);
            }

            ImGui.EndTable();
        }
    }

    private void DrawButtons()
    {
        ReplayBrowserRow? selected = _selected >= 0 && _selected < _rows.Count ? _rows[_selected] : null;

        ImGui.BeginDisabled(selected == null || selected.Error != null);
        if (UiButton.Confirm("Open")) Open(selected!.Path);
        ImGui.EndDisabled();

        ImGui.SameLine();
        if (UiButton.Navigate("Open File...")) OpenFile();

        ImGui.SameLine();
        if (UiButton.Navigate("Refresh")) Refresh();

        ImGui.SameLine(0f, ImGui.GetFontSize() * 3f);
        ImGui.BeginDisabled(selected == null);
        if (UiButton.Back("Delete")) _confirmDelete = selected!.Path;
        ImGui.EndDisabled();
    }

    private void DrawDeleteConfirm()
    {
        if (_confirmDelete == null) return;

        ImGui.OpenPopup("Delete replay?##confirm");
        if (ImGui.BeginPopupModal("Delete replay?##confirm", ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.TextUnformatted(ReplayLabels.Ascii($"Delete {Path.GetFileName(_confirmDelete)}? This cannot be undone."));
            if (UiButton.Confirm("Delete")) Delete(_confirmDelete);
            ImGui.SameLine();
            if (UiButton.Back("Keep it")) _confirmDelete = null;
            ImGui.EndPopup();
        }
    }

    private void Delete(string path)
    {
        _confirmDelete = null;
        try
        {
            File.Delete(path);
            _error = null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            _error = ReplayLabels.Ascii($"Could not delete {Path.GetFileName(path)}: {exception.Message}");
        }

        _selected = Math.Min(_selected, _rows.Count - 2);
        Refresh();
    }

    private void OpenFile()
    {
        string folder = Directory ?? ReplaySave.DirectoryPath;
        var (canceled, paths) = TinyDialogs.OpenFileDialog("Open Replay",
            System.IO.Directory.Exists(folder) ? folder + Path.DirectorySeparatorChar : "", false, ReplayFilter);
        if (canceled) return;

        string path = paths?.FirstOrDefault() ?? "";
        if (path.Length > 0) Open(path);
    }

    private void Open(string path)
    {
        if (_loading != null) return;

        _error = null;
        _loadingName = Path.GetFileName(path);
        _loading = Task.Run(() => ReplaySession.Load(path));
    }

    private void PollLoading()
    {
        if (_loading is not { IsCompleted: true } loading) return;

        _loading = null;
        if (loading.IsFaulted)
        {
            Exception reason = loading.Exception!.GetBaseException();
            _error = ReplayLabels.Ascii($"Could not open {_loadingName}: {reason.Message}");
            return;
        }

        OnOpened?.Invoke(loading.Result);
    }

    private int IndexOf(string path)
    {
        for (int i = 0; i < _rows.Count; i++)
        {
            if (_rows[i].Path == path) return i;
        }

        return _rows.Count > 0 ? 0 : -1;
    }
}
