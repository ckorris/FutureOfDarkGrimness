using FDG.Replay;

namespace FdgRaylib.Rendering.Replays;

public enum EReplayRowKind
{
    /// <summary>A decision (#454's step).</summary>
    Step,

    /// <summary>A request folded into the step before it (one option, or failed) - listed only on request.</summary>
    Folded,

    /// <summary>A separator: a round starting, the game saved, resumed, or over.</summary>
    Marker,
}

/// <summary>One line of the viewer's decision list. Clicking it seeks to <see cref="StepIndex"/>: the step itself, the
/// step a folded request belongs to, or the first step after a marker (the step count, the end, after the last).</summary>
public sealed record ReplayListRow(EReplayRowKind Kind, string Text, int StepIndex, long Seq);

/// <summary>The viewer's decision list (#456): steps in order, separated by the markers a reader steers by.</summary>
public static class ReplayListRows
{
    public static IReadOnlyList<ReplayListRow> Build(ReplayTimeline timeline, bool includeFolded)
    {
        IReadOnlyList<ReplayStep> steps = timeline.Steps;
        var rows = new List<ReplayListRow>();

        foreach (ReplayStep step in steps)
        {
            rows.Add(new ReplayListRow(EReplayRowKind.Step, StepText(timeline.Header, step), step.Index, step.RequestSeq));
            if (includeFolded)
            {
                rows.AddRange(step.Folded.Select(folded => FoldedRow(timeline, folded, step.Index)));
            }
        }

        if (includeFolded)
        {
            rows.AddRange(timeline.FoldedBeforeFirstStep.Select(folded => FoldedRow(timeline, folded, 0)));
        }

        foreach (MarkerEvent marker in timeline.Markers)
        {
            string? text = MarkerText(marker);
            if (text == null)
            {
                continue;
            }

            int next = steps.FirstOrDefault(step => step.RequestSeq > marker.Seq)?.Index ?? steps.Count;
            rows.Add(new ReplayListRow(EReplayRowKind.Marker, text, next, marker.Seq));
        }

        return rows.OrderBy(row => row.Seq).ToList();
    }

    /// <summary>"#012  R2 A05  P1 (AI)  Warriors: Shoot" - the describer's summary (#454) of what was decided.</summary>
    public static string StepText(ReplayHeader header, ReplayStep step) =>
        ReplayLabels.Ascii($"#{step.Index:000}  {RoundText(step.Round, step.Activation)}  {ReplayLabels.Who(header, step)}  "
            + (string.IsNullOrEmpty(step.Summary) ? step.DisplayName : step.Summary));

    /// <summary>"P1  Choosing a Unit to Activate  (1 option)".</summary>
    public static string FoldedText(ReplayTimeline timeline, ReplayRequest request)
    {
        RequestEvent asked = request.Request;
        int? options = DefaultStepFilter.CountOptions(asked);
        string why = request.Error != null ? $"error: {request.Error.Message}"
            : request.IsAnswered == false ? "unanswered"
            : options is int n ? ReplayLabels.Plural(n, "option")
            : "folded";
        return ReplayLabels.Ascii($"      {ReplayLabels.PlayerLabel(timeline.Header, asked.PlayerId)}  {asked.DisplayName}  ({why})");
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

    /// <summary>"R2 A05", "R2 A--" before the unit is known, "setup" before the main phase.</summary>
    public static string RoundText(int? round, int? activation) =>
        round is int r ? $"R{r} A{(activation is int a ? a.ToString("00") : "--")}" : "setup ";

    private static string InRound(MarkerEvent marker) => marker.Round is int round ? $" in round {round}" : "";

    private static ReplayListRow FoldedRow(ReplayTimeline timeline, ReplayRequest folded, int stepIndex) =>
        new ReplayListRow(EReplayRowKind.Folded, FoldedText(timeline, folded), stepIndex, folded.Request.Seq);
}
