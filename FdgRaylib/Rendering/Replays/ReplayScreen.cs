using System.Globalization;
using System.Numerics;
using FDG.Replay;
using FdgRaylib.Rendering.Resolvers;
using ImGuiNET;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Raylib_cs;

namespace FdgRaylib.Rendering.Replays;

/// <summary>
/// The replay viewer (#456, F2). The table itself is the in-game view - <c>RaylibRenderer.ShowReplay</c> wires the
/// replay's table state, log and animations in as a launch would, with no resolvers, chat or save - and this class
/// draws what replaces the game's controls: the transport bar under the table, the decision list and the current
/// step's details in the right column's top panel. It takes the viewer's keys (Space, Left / Right, Home / End;
/// Esc leaves, through the renderer's Escape arbiter). <see cref="ReplaySession"/> does the work.
/// </summary>
public sealed class ReplayScreen
{
    private const int RawReplyLines = 20;

    /// <summary>Share of the screen height the decision panel takes (a game's prompt panel takes 60%); the log keeps
    /// the rest.</summary>
    public const float PanelHeightFraction = 0.68f;

    private static readonly Vector4 TitleColor = new(0.85f, 0.85f, 0.90f, 1f);
    private static readonly Vector4 MarkerColor = new(0.55f, 0.75f, 1f, 1f);
    private static readonly Vector4 FoldedColor = new(0.55f, 0.55f, 0.55f, 1f);
    private static readonly Vector4 NoticeColor = new(1f, 0.85f, 0.4f, 1f);
    private static readonly Vector4 ErrorColor = new(1f, 0.45f, 0.45f, 1f);

    private static readonly (EReplaySpeed Speed, string Label)[] Speeds =
    {
        (EReplaySpeed.Half, "0.5x"),
        (EReplaySpeed.Normal, "1x"),
        (EReplaySpeed.Double, "2x"),
        (EReplaySpeed.Quadruple, "4x"),
        (EReplaySpeed.Instant, "Instant"),
    };

    private readonly Action _onExited;
    private readonly string _title;
    private readonly string _players;
    private readonly string _recorded;

    private IReadOnlyList<ReplayListRow>? _rows;
    private bool _rowsIncludeFolded;
    private bool _showFolded;
    private bool _showRaw;
    private int _scrolledTo = -1;
    private int _rawFor = -1;
    private string _rawText = "";

    /// <param name="onExited">Called once the renderer has torn the view down after <see cref="RequestExit"/> (the app
    /// returns to the replay browser).</param>
    public ReplayScreen(ReplaySession session, Action onExited)
    {
        Session = session;
        _onExited = onExited;

        ReplayHeader header = session.Header;
        _title = ReplayCatalog.Armies(header);
        string resumed = header.ResumedUtc.Count > 0 ? $"   resumed {ReplayLabels.Plural(header.ResumedUtc.Count, "time")}" : "";
        string recordedBy = header.RecordedBy == EReplayRecordedBy.Client && header.RecordedByPlayerId is FDG.PlayerID by
            ? $"client ({ReplayLabels.PlayerLabel(header, by)})"
            : "host";
        _players = ReplayCatalog.Players(header);
        _recorded = ReplayLabels.Ascii($"Recorded by the {recordedBy}   "
            + $"started {header.StartedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}{resumed}");
    }

    public ReplaySession Session { get; }

    /// <summary>How much of the bottom of the screen the transport bar took last frame: the renderer stands the dice
    /// stack on it.</summary>
    public int TransportHeight { get; private set; }

    /// <summary>The viewer asked to leave (Esc, or Back); the renderer tears it down at the end of the frame.</summary>
    public bool WantsExit { get; private set; }

    public void RequestExit() => WantsExit = true;

    /// <summary>The renderer has torn the view down.</summary>
    internal void Exited() => _onExited();

    /// <summary>The viewer's keys. Inside the ImGui frame, so a focused text field keeps them.</summary>
    public void HandleKeys()
    {
        if (ImGui.GetIO().WantTextInput) return;

        if (Raylib.IsKeyPressed(KeyboardKey.Space)) Session.TogglePlay();
        if (Raylib.IsKeyPressed(KeyboardKey.Right) || Raylib.IsKeyPressedRepeat(KeyboardKey.Right)) Session.StepForward();
        if (Raylib.IsKeyPressed(KeyboardKey.Left) || Raylib.IsKeyPressedRepeat(KeyboardKey.Left)) Session.StepBack();
        if (Raylib.IsKeyPressed(KeyboardKey.Home)) Session.Seek(0);
        if (Raylib.IsKeyPressed(KeyboardKey.End)) Session.Seek(Session.Transport.StepCount);
    }

    // --- Right column: the decision list and the current step ------------------------------------------------

    /// <summary>Fills the right column's top panel (where a game's prompts would be).</summary>
    public void DrawPanel()
    {
        if (ResolverPanelLayout.BeginDocked("##replaypanel"))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, TitleColor);
            UiText.Wrapped(_title);
            ImGui.PopStyleColor();
            ImGui.PushStyleColor(ImGuiCol.Text, FoldedColor);
            UiText.Wrapped(_players);
            UiText.Wrapped(_recorded);
            ImGui.PopStyleColor();

            UiButton.Checkbox("Show folded decisions", ref _showFolded);
            if (ImGui.IsItemHovered())
                UiText.Tooltip("Also list the requests with one option (or that failed): they happened, but nobody chose.");
            ImGui.Separator();

            float detailsHeight = ImGui.GetTextLineHeightWithSpacing() * (_showRaw ? 6 + RawReplyLines : 6);
            float listHeight = Math.Max(ImGui.GetTextLineHeightWithSpacing() * 4, ImGui.GetContentRegionAvail().Y - detailsHeight);
            if (ImGui.BeginChild("##replaylist", new Vector2(0, listHeight), ImGuiChildFlags.Borders))
            {
                DrawRows();
            }
            ImGui.EndChild();

            DrawDetails();
        }
        ImGui.End();
    }

    private void DrawRows()
    {
        if (_rows == null || _rowsIncludeFolded != _showFolded)
        {
            _rows = ReplayListRows.Build(Session.Timeline, _showFolded);
            _rowsIncludeFolded = _showFolded;
            _scrolledTo = -1;
        }

        ReplayTransport transport = Session.Transport;
        for (int i = 0; i < _rows.Count; i++)
        {
            ReplayListRow row = _rows[i];
            bool current = row.Kind == EReplayRowKind.Step && row.StepIndex == transport.Position;

            if (row.Kind != EReplayRowKind.Step) ImGui.PushStyleColor(ImGuiCol.Text, row.Kind == EReplayRowKind.Marker ? MarkerColor : FoldedColor);
            if (ImGui.Selectable($"{row.Text}##row{i}", current) && (current == false || Session.IsSeeking))
            {
                Session.Seek(row.StepIndex);
            }
            if (row.Kind != EReplayRowKind.Step) ImGui.PopStyleColor();

            // Keep the current step in view as it moves (not every frame - the list stays scrollable by hand).
            if (current && _scrolledTo != transport.Position)
            {
                ImGui.SetScrollHereY(0.35f);
                _scrolledTo = transport.Position;
            }
        }

        if (transport.AtEnd && _scrolledTo != transport.Position)
        {
            ImGui.SetScrollHereY(1f);
            _scrolledTo = transport.Position;
        }
    }

    private void DrawDetails()
    {
        ReplayTransport transport = Session.Transport;
        ReplayStep? step = transport.AtEnd ? null : Session.Player.Steps[transport.Position];
        if (step == null)
        {
            UiText.Wrapped(Session.Notice ?? "End of the recording.");
            return;
        }

        UiText.Wrapped(ReplayLabels.Ascii($"#{step.Index:000}  {step.DisplayName}"));
        ImGui.PushStyleColor(ImGuiCol.Text, FoldedColor);
        UiText.Wrapped(ReplayLabels.Ascii($"Answered by {ReplayLabels.Who(Session.Header, step)}   {ReplayLabels.ShortType(step.RequestType)}"));
        ImGui.PopStyleColor();
        UiText.Wrapped(ReplayLabels.Ascii(string.IsNullOrEmpty(step.Summary) ? "(no summary)" : step.Summary));

        UiButton.Checkbox("Raw reply", ref _showRaw);
        if (_showRaw)
        {
            if (_rawFor != step.Index)
            {
                _rawFor = step.Index;
                _rawText = RawReply(step);
            }

            ImGui.PushStyleColor(ImGuiCol.Text, FoldedColor);
            ImGui.TextUnformatted(_rawText);
            ImGui.PopStyleColor();
        }
    }

    /// <summary>The reply as recorded, indented, cut to <see cref="RawReplyLines"/> lines.</summary>
    internal static string RawReply(ReplayStep step)
    {
        string json = step.Decision.Reply?.ReplyJson ?? "(no reply)";
        try
        {
            json = JToken.Parse(json).ToString(Formatting.Indented);
        }
        catch (JsonException)
        {
            // Shown as recorded.
        }

        string[] lines = json.Replace("\r", "").Split('\n');
        string shown = string.Join("\n", lines.Take(RawReplyLines));
        if (lines.Length > RawReplyLines) shown += $"\n... ({ReplayLabels.Plural(lines.Length - RawReplyLines, "more line")})";
        return ReplayText.Ascii(shown); // keeps the newlines
    }

    // --- Bottom: the transport bar -----------------------------------------------------------------------------

    /// <summary>The play / step / seek controls, bottom-left over the table (where a game's Menu button sits).</summary>
    public void DrawTransport(int areaW, int screenH, Action toggleArmyLists)
    {
        ReplayTransport transport = Session.Transport;
        int count = transport.StepCount;

        ImGui.SetNextWindowPos(new Vector2(8, screenH - 8), ImGuiCond.Always, new Vector2(0f, 1f));
        ImGui.SetNextWindowBgAlpha(0.80f);
        ImGui.Begin("##replaytransport",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize |
            ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav);

        if (Session.Error is string error) UiText.Colored(ErrorColor, error);
        if (Session.Notice is string notice) UiText.Colored(NoticeColor, notice);

        bool busy = Session.IsSeeking;
        ImGui.BeginDisabled(busy || transport.Position == 0);
        if (ImGui.Button("|<")) Session.Seek(0);
        Hover("First step (Home)");
        ImGui.SameLine();
        if (ImGui.Button("<")) Session.StepBack();
        Hover("Step back (Left)");
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(busy || (transport.AtEnd && transport.IsPlaying == false));
        if (ImGui.Button(transport.IsPlaying ? "Pause##play" : "Play##play")) Session.TogglePlay();
        Hover("Play / pause (Space)");
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(busy || transport.AtEnd);
        if (ImGui.Button(">")) Session.StepForward();
        Hover("Step forward, with its animations (Right)");
        ImGui.SameLine();
        if (ImGui.Button(">|")) Session.Seek(count);
        Hover("The end (End)");
        ImGui.EndDisabled();

        foreach ((EReplaySpeed speed, string label) in Speeds)
        {
            ImGui.SameLine();
            if (ImGui.RadioButton(label, transport.Speed == speed)) transport.Speed = speed;
        }

        ImGui.SetNextItemWidth(Math.Max(160f, areaW * 0.45f));
        int position = transport.Position;
        if (ImGui.SliderInt("##replaypos", ref position, 0, count, position >= count ? "end" : $"#{position:000}")
            && position != transport.Position)
        {
            Session.Seek(position);
        }

        ImGui.SameLine();
        if (ImGui.Button("Army Lists (L)")) toggleArmyLists();
        ImGui.SameLine();
        if (UiButton.Back("Back (Esc)")) RequestExit();

        ImGui.TextUnformatted(ReplayLabels.Ascii(Session.PositionLabel) + (Session.ShowSeeking ? "   seeking..." : ""));

        TransportHeight = (int)ImGui.GetWindowHeight() + 8;
        ImGui.End();
    }

    private static void Hover(string text)
    {
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) UiText.Tooltip(text);
    }
}
