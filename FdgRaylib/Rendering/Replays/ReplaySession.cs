using FDG;
using FDG.Replay;
using FdgRaylib.Rendering.Presentation;
using Raylib_cs;

namespace FdgRaylib.Rendering.Replays;

/// <summary>What the replay session needs from whatever draws the table (the renderer, or a test's stand-in).</summary>
public interface IReplayCanvas
{
    /// <summary>The player rebuilt its store: draw this table state from now on (main thread).</summary>
    void ReplaceTableState(ITableState tableState);

    /// <summary>The log was rebuilt for a jump: show its end.</summary>
    void ScrollConsoleToEnd();
}

/// <summary>
/// A replay being watched (#456): the <see cref="ReplayPlayer"/> (#454) and everything the table view needs from it -
/// the log, the animations, the play / pause / step state - with no ImGui in it, so it can be driven by a test.
/// <see cref="ReplayScreen"/> draws its controls; the renderer draws <see cref="ReplayPlayer.TableState"/>.
/// <para>
/// <b>Threads.</b> Everything here runs on the main thread except a seek. A step forward applies the step's deltas
/// in place and queues its beats in the same frame, so a model never jumps to where its move ends before the move
/// animates (the presentation player holds it at the start from the moment the beat is queued). A seek - a jump, a
/// step back - rebuilds a new store from a snapshot, which can take tens of milliseconds on a big game, so it runs on
/// a worker; the old store is left untouched, the renderer keeps drawing it, and the new one is handed over when the
/// worker is done (<see cref="Update"/>). Nothing else touches the player while a seek runs: a jump asked for
/// meanwhile is remembered, the latest one wins.
/// </para>
/// </summary>
public sealed class ReplaySession
{
    /// <summary>A seek still running after this long says so ("seeking...").</summary>
    public const float SeekNoticeSeconds = 0.1f;

    private static readonly TextColor LogColor = new(225, 225, 225, 255);
    private static readonly TextColor ProblemColor = new(255, 180, 90, 255);

    private readonly Dictionary<PlayerID, Color> _colors = new();
    private IReplayCanvas? _canvas;

    private Task<PlaybackSpan>? _seek;
    private int _seekTarget;
    private int? _pendingSeek;
    private float _seekSeconds;

    /// <summary>
    /// Reads and indexes a replay - a .fdgreplay, or the one a .fdgsave carries - and builds the player at its first
    /// step. Slow on a big game (every step is described up front), so callers run it off the main thread. Throws
    /// <see cref="ReplayFileException"/>, <see cref="ReplayFormatException"/> or <see cref="ReplayPlaybackException"/>.
    /// </summary>
    public static ReplaySession Load(string path)
    {
        ReplayDocument document = ReplayFiles.Read(path);
        return new ReplaySession(path, new ReplayPlayer(new ReplayTimeline(document)));
    }

    public ReplaySession(string path, ReplayPlayer player)
    {
        Path = path;
        Player = player;
        Transport = new ReplayTransport(player.Steps.Count);
        Transport.Arrived(player.IsAtEnd ? player.Steps.Count : 0, crossedResume: false);

        // The palette in slot order, as a launch with no colour picks gets it - a replay does not record the picks.
        List<ReplaySlot> slots = Header.Slots.OrderBy(slot => slot.SlotId).ToList();
        int[] palette = PlayerColorOptions.ResolveIndices(new int?[slots.Count]);
        for (int i = 0; i < slots.Count; i++)
        {
            _colors[slots[i].PlayerId] = PlayerColorOptions.Options[palette[i]].Color;
        }

        Background = Header.Settings?.TableBackground ?? ETableBackground.Forest;
        RebuildLog(player.CurrentStep?.RequestSeq ?? long.MaxValue);
    }

    public string Path { get; }

    public ReplayPlayer Player { get; }

    public ReplayTimeline Timeline => Player.Timeline;

    public ReplayHeader Header => Timeline.Header;

    public ReplayTransport Transport { get; }

    /// <summary>The game log up to the current position.</summary>
    public GameLog Log { get; } = new GameLog();

    /// <summary>The animations of the step just taken (none after a seek).</summary>
    public PresentationPlayer Presentation { get; } = new PresentationPlayer();

    public ETableBackground Background { get; }

    public bool IsSeeking => _seek != null;

    /// <summary>A seek has been running long enough to say so.</summary>
    public bool ShowSeeking => IsSeeking && _seekSeconds >= SeekNoticeSeconds;

    /// <summary>The last playback failure (a corrupt replay), shown until the next successful move.</summary>
    public string? Error { get; private set; }

    /// <summary>How fast animations run.</summary>
    public float TimeScale => Transport.TimeScale;

    public Color ColorFor(PlayerID player) => _colors.GetValueOrDefault(player, Color.White);

    /// <summary>Hands the session the canvas that draws it, before the first <see cref="Update"/>.</summary>
    public void Attach(IReplayCanvas canvas) => _canvas = canvas;

    /// <summary>Once a frame, before the table is drawn: finishes a seek, or lets autoplay take its next step.</summary>
    public void Update(float seconds)
    {
        if (_seek != null)
        {
            _seekSeconds += seconds;
            if (_seek.IsCompleted)
            {
                FinishSeek();
            }

            return;
        }

        if (Transport.Tick(seconds, Presentation.IsAnimating == false))
        {
            Advance(manual: false);
        }
    }

    public void TogglePlay()
    {
        if (IsSeeking == false) Transport.TogglePlay();
    }

    /// <summary>The next step, with its animations - cutting short any still playing from the last.</summary>
    public void StepForward()
    {
        if (IsSeeking || Transport.AtEnd) return;
        Transport.Pause();
        Advance(manual: true);
    }

    /// <summary>The step before (from the end, the last step), rebuilt.</summary>
    public void StepBack()
    {
        int position = _pendingSeek ?? (IsSeeking ? _seekTarget : Transport.Position);
        if (position > 0) Seek(position - 1);
    }

    /// <summary>Jumps to step <paramref name="position"/> (the step count = the end), without animations.</summary>
    public void Seek(int position)
    {
        position = Math.Clamp(position, 0, Transport.StepCount);
        Transport.Pause();
        if (_seek != null)
        {
            _pendingSeek = position;
            return;
        }

        StartSeek(position);
    }

    /// <summary>"Round 2 - Activation 5 - P1 (AI) - step #012 of 341": numbered as the decision list and the
    /// <c>--replay</c> CLI number steps, so the number is the one <c>--at STEP --make-save</c> takes.</summary>
    public string PositionLabel
    {
        get
        {
            int count = Transport.StepCount;
            ReplayStep? step = Transport.AtEnd ? null : Player.Steps[Transport.Position];
            if (step == null)
            {
                return $"End of the recording - {ReplayLabels.Plural(count, "step")}";
            }

            string when = step.Round is int round
                ? $"Round {round} - " + (step.Activation is int activation ? $"Activation {activation} - " : "")
                : "Setup - ";
            return $"{when}{ReplayLabels.Who(Header, step)} - step #{step.Index:000} of {count}";
        }
    }

    /// <summary>Whose line <see cref="PositionLabel"/> is (#457 colours): the deciding player; at the end, the sole
    /// winner, else the player with the initiative in the last round; null for nobody.</summary>
    public PlayerID? PositionPlayer
    {
        get
        {
            if (Transport.AtEnd == false) return Player.Steps[Transport.Position].PlayerId;
            _initiative ??= ReplayListRows.InitiativeByRound(Timeline);
            int? lastRound = _initiative.Count > 0 ? _initiative.Keys.Max() : null;
            return Timeline.GameEndedMarker is MarkerEvent ended
                ? ReplayListRows.MarkerPlayer(ended, lastRound, _initiative)
                : lastRound is int round ? _initiative[round] : null;
        }
    }

    private IReadOnlyDictionary<int, PlayerID>? _initiative;

    /// <summary>A line to show over the table at the current position: a resume just before it, or the game's end.</summary>
    public string? Notice => NoticeAt(Timeline, Transport.Position);

    /// <summary>
    /// What the viewer says at <paramref name="position"/>: at the end, how the game ended; at a step a resume
    /// precedes (#453), that the state was reloaded there. Null otherwise.
    /// </summary>
    public static string? NoticeAt(ReplayTimeline timeline, int position)
    {
        IReadOnlyList<ReplayStep> steps = timeline.Steps;
        if (position >= steps.Count)
        {
            MarkerEvent? ended = timeline.GameEndedMarker;
            return ended == null
                ? "End of the recording: the game did not finish here."
                : ReplayLabels.Ascii($"Game over: {ended.Outcome ?? "?"}"
                    + (string.IsNullOrEmpty(ended.Message) ? "" : $" - {ended.Message}"));
        }

        return ResumedJustBefore(timeline, position)
            ? "Game was saved and resumed here; the unfinished activation was replayed."
            : null;
    }

    /// <summary>
    /// A <c>Resumed</c> marker lies between step <paramref name="position"/> - 1 and step <paramref name="position"/>
    /// (or the end). Read off the timeline, not a move's span: a step forward across a resume rebuilds from the
    /// snapshot after the marker, so its span never contains it.
    /// </summary>
    public static bool ResumedJustBefore(ReplayTimeline timeline, int position)
    {
        IReadOnlyList<ReplayStep> steps = timeline.Steps;
        long from = position <= 0 ? -1 : steps[Math.Min(position, steps.Count) - 1].RequestSeq;
        long to = position < steps.Count ? steps[position].RequestSeq : long.MaxValue;
        return timeline.Markers.Any(marker => marker.Marker == EReplayMarker.Resumed && marker.Seq > from && marker.Seq <= to);
    }

    // --- Moves ------------------------------------------------------------------------------------

    private void Advance(bool manual)
    {
        PlaybackSpan? span;
        try
        {
            span = Player.StepForward();
        }
        catch (ReplayPlaybackException exception)
        {
            Fail(exception);
            return;
        }

        if (span == null)
        {
            Transport.Arrived(Transport.StepCount, crossedResume: false);
            return;
        }

        if (span.StoreReplaced)
        {
            // Crossing a resume rebuilds the store (#453) - here, on the main thread, as it is a single step.
            _canvas?.ReplaceTableState(Player.TableState);
            Presentation.Reset();
        }
        else if (manual)
        {
            Presentation.Reset(); // a step asked for while the last still animates cuts it short
        }

        if (Transport.PlaysBeats)
        {
            foreach (FDG.Presentation.PresentationBeat beat in span.Beats)
            {
                Presentation.OnBeat(beat);
            }
        }

        foreach (LogEvent line in span.LogLines)
        {
            Log.Add(line.Line, LogColor, isDebug: line.Debug);
        }

        foreach (string problem in span.Problems)
        {
            Log.Add($"[replay] {problem}", ProblemColor, isDebug: true);
        }

        Error = null;
        int arrived = Transport.Position + 1;
        Transport.Arrived(arrived, ResumedJustBefore(Timeline, arrived));
    }

    private void StartSeek(int position)
    {
        _seekTarget = position;
        _pendingSeek = null;
        _seekSeconds = 0f;
        ReplayPlayer player = Player;
        _seek = Task.Run(() => position >= player.Steps.Count ? player.SeekToEnd() : player.SeekToStep(position));
    }

    private void FinishSeek()
    {
        Task<PlaybackSpan> seek = _seek!;
        _seek = null;

        // The worker built a new store; the canvas drew the old one, untouched, until now.
        _canvas?.ReplaceTableState(Player.TableState);
        Presentation.Reset();
        if (seek.IsFaulted)
        {
            Fail(seek.Exception!.GetBaseException());
        }
        else
        {
            Error = null;
            RebuildLog(seek.Result.ToSeq);
            Transport.Arrived(_seekTarget, crossedResume: false);
        }

        if (_pendingSeek is int next)
        {
            _pendingSeek = null;
            if (next != Transport.Position || seek.IsFaulted) StartSeek(next);
        }
    }

    private void Fail(Exception exception)
    {
        Transport.Pause();
        Error = ReplayLabels.Ascii(exception.Message);
        Log.Add($"[replay] {Error}", ProblemColor);
    }

    // The log as it stood at the position just reached: every recorded line up to it.
    private void RebuildLog(long throughSeq)
    {
        Log.Clear();
        foreach (ReplayEvent replayEvent in Timeline.Events)
        {
            if (replayEvent.Seq > throughSeq) break;
            if (replayEvent is LogEvent line) Log.Add(line.Line, LogColor, isDebug: line.Debug);
        }

        _canvas?.ScrollConsoleToEnd();
    }
}
