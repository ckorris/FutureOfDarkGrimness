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
/// <para>#457: every line that is one player's - a decision, a folded request, a round's start (its initiative), the
/// end (its sole winner) - is drawn in that player's table colour; the position line under the slider likewise.
/// Sizes are in EM so the bar and list scale with the display (<see cref="UiChrome.Em"/>).</para>
/// </summary>
public sealed class ReplayScreen
{
    private const int RawReplyLines = 20;

    /// <summary>Share of the screen height the decision panel takes (a game's prompt panel takes 60%); the log keeps
    /// the rest.</summary>
    public const float PanelHeightFraction = 0.68f;

    private static readonly Vector4 TitleColor = new(0.85f, 0.85f, 0.90f, 1f);
    private static readonly Vector4 DimColor = new(0.55f, 0.55f, 0.55f, 1f);
    private static readonly Vector4 NobodyColor = new(1f, 1f, 1f, 1f);
    private const float FoldedAlpha = 0.55f;
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
            ImGui.PushStyleColor(ImGuiCol.Text, DimColor);
            UiText.Wrapped(_players);
            UiText.Wrapped(_recorded);
            ImGui.PopStyleColor();
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
        _rows ??= ReplayListRows.Build(Session.Timeline);

        ReplayTransport transport = Session.Transport;
        for (int i = 0; i < _rows.Count; i++)
        {
            ReplayListRow row = _rows[i];
            bool current = row.Kind == EReplayRowKind.Step && row.StepIndex == transport.Position;

            Vector4 color = PlayerColor(row.Player);
            if (row.Kind == EReplayRowKind.Folded) color.W *= FoldedAlpha;
            ImGui.PushStyleColor(ImGuiCol.Text, color);
            if (ImGui.Selectable($"{row.Text}##row{i}", current) && (current == false || Session.IsSeeking))
            {
                Session.Seek(row.StepIndex);
            }
            ImGui.PopStyleColor();
            if (row.Kind == EReplayRowKind.Folded && ImGui.IsItemHovered())
                UiText.Tooltip("A request with one option (or that failed): it happened, but nobody chose.");

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

        UiText.Wrapped(ReplayLabels.Ascii(step.DisplayName));
        ImGui.PushStyleColor(ImGuiCol.Text, PlayerColor(step.PlayerId));
        UiText.Wrapped(ReplayLabels.Ascii($"Answered by {ReplayLabels.Who(Session.Header, step)}"));
        ImGui.PopStyleColor();
        ImGui.PushStyleColor(ImGuiCol.Text, DimColor);
        UiText.Wrapped(ReplayLabels.ShortType(step.RequestType));
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

            ImGui.PushStyleColor(ImGuiCol.Text, DimColor);
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

    /// <summary>The play / step / seek controls, bottom-left over the table (where a game's Menu button sits). Play is
    /// the control a viewer reaches for most, so it is the widest; the steps flank it; all in EM so they grow with
    /// the display's UI scale.</summary>
    public void DrawTransport(int areaW, int screenH, Action toggleArmyLists)
    {
        ReplayTransport transport = Session.Transport;
        int count = transport.StepCount;
        float margin = UiChrome.Em(0.5f);

        ImGui.SetNextWindowPos(new Vector2(margin, screenH - margin), ImGuiCond.Always, new Vector2(0f, 1f));
        ImGui.SetNextWindowBgAlpha(0.80f);
        ImGui.Begin("##replaytransport",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize |
            ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav);

        if (Session.Error is string error) UiText.Colored(ErrorColor, error);
        if (Session.Notice is string notice) UiText.Colored(NoticeColor, notice);

        var stepSize = new Vector2(UiChrome.Em(2.4f), ImGui.GetFrameHeight() * 1.35f);
        var playSize = new Vector2(UiChrome.Em(5f), stepSize.Y);
        bool busy = Session.IsSeeking;

        ImGui.BeginDisabled(busy || transport.Position == 0);
        if (ImGui.Button("<", stepSize)) Session.StepBack();
        Hover("Step back (Left)");
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(busy || (transport.AtEnd && transport.IsPlaying == false));
        if (ImGui.Button(transport.IsPlaying ? "Pause##play" : "Play##play", playSize)) Session.TogglePlay();
        Hover("Play / pause (Space)");
        ImGui.EndDisabled();

        ImGui.SameLine();
        ImGui.BeginDisabled(busy || transport.AtEnd);
        if (ImGui.Button(">", stepSize)) Session.StepForward();
        Hover("Step forward, with its animations (Right)");
        ImGui.EndDisabled();

        foreach ((EReplaySpeed speed, string label) in Speeds)
        {
            ImGui.SameLine(0f, speed == Speeds[0].Speed ? UiChrome.Em(1f) : -1f);
            if (ImGui.RadioButton(label, transport.Speed == speed)) transport.Speed = speed;
        }

        ImGui.SetNextItemWidth(Math.Max(UiChrome.Em(12f), areaW * 0.45f));
        int position = transport.Position;
        if (ImGui.SliderInt("##replaypos", ref position, 0, count, position >= count ? "end" : $"#{position:000}")
            && position != transport.Position)
        {
            Session.Seek(position);
        }

        ImGui.SameLine();
        if (UiButton.Navigate("Army Lists (L)", UiChrome.ButtonSize("Army Lists (L)"))) toggleArmyLists();
        ImGui.SameLine();
        if (UiButton.Back("Back (Esc)", UiChrome.ButtonSize("Back (Esc)"))) RequestExit();

        UiText.Colored(PlayerColor(Session.PositionPlayer),
            ReplayLabels.Ascii(Session.PositionLabel) + (Session.ShowSeeking ? "   seeking..." : ""));

        TransportHeight = (int)ImGui.GetWindowHeight() + (int)margin;
        ImGui.End();
    }

    /// <summary>A player's table colour as ImGui text; white for nobody.</summary>
    private Vector4 PlayerColor(FDG.PlayerID? player)
    {
        if (player is not FDG.PlayerID id) return NobodyColor;
        Color c = Session.ColorFor(id);
        return new Vector4(c.R / 255f, c.G / 255f, c.B / 255f, 1f);
    }

    private static void Hover(string text)
    {
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) UiText.Tooltip(text);
    }
}
