using FDG;
using FDG.Replay;

namespace FdgRaylib.Rendering.Replays;

public enum EReplayRowKind
{
    /// <summary>A decision (#454's step).</summary>
    Step,

    /// <summary>A request folded into the step before it (one option, or failed): it happened, nobody chose.</summary>
    Folded,

    /// <summary>A separator: a round starting, the game saved, resumed, or over.</summary>
    Marker,
}

/// <summary>One line of the viewer's decision list. Clicking it seeks to <see cref="StepIndex"/>: the step itself, the
/// step a folded request belongs to, or the first step after a marker (the step count, the end, after the last).
/// <see cref="Player"/> is whose line it is, for its colour (#457): the deciding player; for a marker, the one it
/// concerns (the sole winner at the game's end), else the player with the initiative in its round, else none.</summary>
public sealed record ReplayListRow(EReplayRowKind Kind, string Text, int StepIndex, long Seq, PlayerID? Player);

/// <summary>The viewer's decision list (#456): every request in order, separated by the markers a reader steers by.</summary>
public static class ReplayListRows
{
    public static IReadOnlyList<ReplayListRow> Build(ReplayTimeline timeline)
    {
        IReadOnlyList<ReplayStep> steps = timeline.Steps;
        IReadOnlyDictionary<int, PlayerID> initiative = InitiativeByRound(timeline);
        var rows = new List<ReplayListRow>();

        foreach (ReplayStep step in steps)
        {
            rows.Add(new ReplayListRow(EReplayRowKind.Step, StepText(timeline.Header, step), step.Index, step.RequestSeq, step.PlayerId));
            rows.AddRange(step.Folded.Select(folded => FoldedRow(timeline, folded, step.Index)));
        }

        rows.AddRange(timeline.FoldedBeforeFirstStep.Select(folded => FoldedRow(timeline, folded, 0)));

        int? round = null; // the round in progress at each marker - GameEnded carries none of its own
        foreach (MarkerEvent marker in timeline.Markers)
        {
            round = marker.Round ?? round;
            string? text = MarkerText(marker);
            if (text == null)
            {
                continue;
            }

            int next = steps.FirstOrDefault(step => step.RequestSeq > marker.Seq)?.Index ?? steps.Count;
            rows.Add(new ReplayListRow(EReplayRowKind.Marker, text, next, marker.Seq, MarkerPlayer(marker, round, initiative)));
        }

        return rows.OrderBy(row => row.Seq).ToList();
    }

    /// <summary>The player who activated first in each round: the initiative (#457 colours fall back to it). From the
    /// ActivationStart markers; a round whose first activation was not recorded (a resume mid-round) takes its first
    /// recorded one.</summary>
    public static IReadOnlyDictionary<int, PlayerID> InitiativeByRound(ReplayTimeline timeline)
    {
        var initiative = new Dictionary<int, PlayerID>();
        foreach (MarkerEvent marker in timeline.Markers)
        {
            if (marker.Marker == EReplayMarker.ActivationStart && marker.Round is int round && marker.PlayerId is PlayerID player
                && (marker.Activation == 1 || initiative.ContainsKey(round) == false))
            {
                initiative[round] = player;
            }
        }

        return initiative;
    }

    /// <summary>"P1 (AI)  Warriors: Shoot" - who, then the describer's summary (#454) of what was decided.</summary>
    public static string StepText(ReplayHeader header, ReplayStep step) =>
        ReplayLabels.Ascii($"{ReplayLabels.Who(header, step)}  " + (string.IsNullOrEmpty(step.Summary) ? step.DisplayName : step.Summary));

    /// <summary>"    P1  Choosing a Unit to Activate  (1 option)".</summary>
    public static string FoldedText(ReplayTimeline timeline, ReplayRequest request)
    {
        RequestEvent asked = request.Request;
        int? options = DefaultStepFilter.CountOptions(asked);
        string why = request.Error != null ? $"error: {request.Error.Message}"
            : request.IsAnswered == false ? "unanswered"
            : options is int n ? ReplayLabels.Plural(n, "option")
            : "folded";
        return ReplayLabels.Ascii($"    {ReplayLabels.PlayerLabel(timeline.Header, asked.PlayerId)}  {asked.DisplayName}  ({why})");
    }

    /// <summary>A separator's text, or null for the markers the list leaves out (the game's start, each activation).</summary>
    public static string? MarkerText(MarkerEvent marker) => marker.Marker switch
    {
        EReplayMarker.RoundStart => $"-- Round {marker.Round} --",
        EReplayMarker.Saved => $"-- Saved{InRound(marker)} --",
        EReplayMarker.Resumed => $"-- Resumed{InRound(marker)}: the game was loaded from that save --",
        EReplayMarker.GameEnded => ReplayLabels.Ascii($"-- Game over: {marker.Outcome ?? "?"}"
            + (string.IsNullOrEmpty(marker.Message) ? "" : $" - {marker.Message}") + " --"),
        _ => null,
    };

    /// <summary>Whose line a marker is: the sole winner at the game's end, else the initiative of
    /// <paramref name="round"/> (the marker's own, or the one in progress when it fell), else nobody's.</summary>
    public static PlayerID? MarkerPlayer(MarkerEvent marker, int? round, IReadOnlyDictionary<int, PlayerID> initiative)
    {
        if (marker.Winner is PlayerID winner) return winner;
        return round is int r && initiative.TryGetValue(r, out PlayerID player) ? player : null;
    }

    private static string InRound(MarkerEvent marker) => marker.Round is int round ? $" in round {round}" : "";

    private static ReplayListRow FoldedRow(ReplayTimeline timeline, ReplayRequest folded, int stepIndex) =>
        new ReplayListRow(EReplayRowKind.Folded, FoldedText(timeline, folded), stepIndex, folded.Request.Seq, folded.Request.PlayerId);
}
