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
/// <para><see cref="Theatre"/> (F, or the bar's button): the board alone fills the window - no decision list, no log -
/// and the transport bar shows only while the mouse moves and for a moment after, fading out like a video player's
/// controls; the cursor hides with it. Esc leaves theatre first, the viewer second.</para>
/// </summary>
public sealed class ReplayScreen
{
    private const int RawReplyLines = 20;

    /// <summary>Theatre: how long the controls stay after the mouse last moved, and how long they take to fade.</summary>
    private const float ControlsLingerSeconds = 2.5f;
    private const float ControlsFadeSeconds = 0.4f;

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
    private readonly string _recordedBy;
    private readonly string _started;

    private IReadOnlyList<ReplayListRow>? _rows;
    private float _rowsWidth;
    private bool _showRaw;
    private float _controlsHideAt;
    private Vector2 _lastMouse = new(float.NaN, float.NaN);
    private bool _cursorHidden;
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
        string resumed = header.ResumedUtc.Count > 0 ? $", resumed {ReplayLabels.Plural(header.ResumedUtc.Count, "time")}" : "";
        string recordedBy = header.RecordedBy == EReplayRecordedBy.Client && header.RecordedByPlayerId is FDG.PlayerID by
            ? $"client ({ReplayLabels.PlayerLabel(header, by)})"
            : "host";
        _players = ReplayCatalog.Players(header);
        _recordedBy = ReplayLabels.Ascii($"Recorded by the {recordedBy}");
        _started = ReplayLabels.Ascii(
            $"Started {header.StartedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)}{resumed}");
    }

    /// <summary>Full-board mode: the renderer drops the right column while this is set.</summary>
    public bool Theatre { get; private set; }

    public void ToggleTheatre()
    {
        Theatre = !Theatre;
        _controlsHideAt = (float)Raylib.GetTime() + ControlsLingerSeconds;
        if (Theatre == false) ShowCursor();
    }

    public ReplaySession Session { get; }

    /// <summary>How much of the bottom of the screen the transport bar took last frame: the renderer stands the dice
    /// stack on it.</summary>
    public int TransportHeight { get; private set; }

    /// <summary>The viewer asked to leave (Esc, or Back); the renderer tears it down at the end of the frame.</summary>
    public bool WantsExit { get; private set; }

    public void RequestExit()
    {
        WantsExit = true;
        ShowCursor();
    }

    /// <summary>Escape: out of theatre first, out of the viewer second.</summary>
    public void Escape()
    {
        if (Theatre) ToggleTheatre();
        else RequestExit();
    }

    /// <summary>The renderer has torn the view down.</summary>
    internal void Exited() => _onExited();

    /// <summary>The viewer's keys. Inside the ImGui frame, so a focused text field keeps them.</summary>
    public void HandleKeys()
    {
        if (ImGui.GetIO().WantTextInput) return;

        if (Raylib.IsKeyPressed(KeyboardKey.Space)) Session.TogglePlay();
        if (Raylib.IsKeyPressed(KeyboardKey.Right) || Raylib.IsKeyPressedRepeat(KeyboardKey.Right)) Session.StepForward();
        if (Raylib.IsKeyPressed(KeyboardKey.Left) || Raylib.IsKeyPressedRepeat(KeyboardKey.Left)) Session.StepBack();
        if (Raylib.IsKeyPressed(KeyboardKey.F)) ToggleTheatre();
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
            UiText.Wrapped(_recordedBy);
            UiText.Wrapped(_started);
            ImGui.PopStyleColor();
            ImGui.Separator();

            // The details below take four lines (name, who, summary, the Raw toggle) - plus the raw reply and its type.
            float detailsHeight = ImGui.GetTextLineHeightWithSpacing() * (_showRaw ? 5.5f + RawReplyLines : 4.5f);
            float listHeight = Math.Max(ImGui.GetTextLineHeightWithSpacing() * 4, ImGui.GetContentRegionAvail().Y - detailsHeight);
            BuildRows();
            // Wide enough for the longest line, so a long summary scrolls sideways rather than being cut.
            ImGui.SetNextWindowContentSize(new Vector2(_rowsWidth, 0));
            if (ImGui.BeginChild("##replaylist", new Vector2(0, listHeight), ImGuiChildFlags.Borders, ImGuiWindowFlags.HorizontalScrollbar))
            {
                DrawRows();
            }
            ImGui.EndChild();

            DrawDetails();
        }
        ImGui.End();
    }

    private void BuildRows()
    {
        if (_rows != null) return;
        _rows = ReplayListRows.Build(Session.Timeline);
        _rowsWidth = _rows.Max(row => ImGui.CalcTextSize(row.Text).X) + ImGui.GetStyle().FramePadding.X * 2f;
    }

    private void DrawRows()
    {
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
            // How it ended is the transport bar's line; here only that there is no step.
            UiText.Wrapped("End of the recording.");
            return;
        }

        UiText.Wrapped(ReplayLabels.Ascii(step.DisplayName));
        ImGui.PushStyleColor(ImGuiCol.Text, PlayerColor(step.PlayerId));
        UiText.Wrapped(ReplayLabels.Ascii($"Answered by {ReplayLabels.Who(Session.Header, step)}"));
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
            UiText.Wrapped(ReplayLabels.ShortType(step.RequestType));
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

        float alpha = Theatre ? ControlsAlpha() : 1f;
        if (alpha <= 0f)
        {
            HideCursor();
            return; // nothing drawn, nothing to click; TransportHeight keeps its last value so the dice stay put
        }
        ShowCursor();

        ImGui.SetNextWindowPos(new Vector2(margin, screenH - margin), ImGuiCond.Always, new Vector2(0f, 1f));
        ImGui.SetNextWindowBgAlpha(0.80f);
        ImGui.PushStyleVar(ImGuiStyleVar.Alpha, alpha);
        ImGui.Begin("##replaytransport",
            ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoCollapse |
            ImGuiWindowFlags.NoTitleBar | ImGuiWindowFlags.AlwaysAutoResize |
            ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoNav);
        // Resting the mouse on the bar keeps it up.
        if (Theatre && ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem))
            _controlsHideAt = (float)Raylib.GetTime() + ControlsLingerSeconds;

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
        string theatreLabel = Theatre ? "Exit Full Board (F)" : "Full Board (F)";
        if (UiButton.Navigate(theatreLabel, UiChrome.ButtonSize(theatreLabel))) ToggleTheatre();
        Hover("Watch the board alone; the controls show while the mouse moves");
        ImGui.SameLine();
        if (UiButton.Navigate("Army Lists (L)", UiChrome.ButtonSize("Army Lists (L)"))) toggleArmyLists();
        ImGui.SameLine();
        if (UiButton.Back("Back (Esc)", UiChrome.ButtonSize("Back (Esc)"))) RequestExit();

        UiText.Colored(PlayerColor(Session.PositionPlayer),
            ReplayLabels.Ascii(Session.PositionLabel) + (Session.ShowSeeking ? "   seeking..." : ""));

        TransportHeight = (int)ImGui.GetWindowHeight() + (int)margin;
        ImGui.End();
        ImGui.PopStyleVar();
    }

    /// <summary>Theatre: 1 while the mouse moves and for <see cref="ControlsLingerSeconds"/> after, then a
    /// <see cref="ControlsFadeSeconds"/> fade to 0.</summary>
    private float ControlsAlpha()
    {
        float now = (float)Raylib.GetTime();
        Vector2 mouse = Raylib.GetMousePosition();
        if (Raylib.IsCursorOnScreen() && mouse != _lastMouse)
        {
            if (float.IsNaN(_lastMouse.X) == false) _controlsHideAt = now + ControlsLingerSeconds;
            _lastMouse = mouse;
        }

        return Math.Clamp((_controlsHideAt - now) / ControlsFadeSeconds, 0f, 1f);
    }

    private void HideCursor()
    {
        if (_cursorHidden) return;
        Raylib.HideCursor();
        _cursorHidden = true;
    }

    private void ShowCursor()
    {
        if (_cursorHidden == false) return;
        Raylib.ShowCursor();
        _cursorHidden = false;
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
