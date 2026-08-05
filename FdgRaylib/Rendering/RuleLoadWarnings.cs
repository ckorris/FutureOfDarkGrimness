using System.Text;
using FDG;
using FDG.Rules.Dispatch;

namespace FdgRaylib.Rendering;

/// <summary>
/// App-side subscriber for the engine's <see cref="RuleDiagnostics"/> channel (#168). In GUI modes the
/// channel's stdout fallback is invisible, so a player fielding an army whose rules silently do nothing
/// got no signal. This collector buffers everything emitted before a game log exists (army loads happen
/// deep in the host's launch path, before <see cref="GameGuiWiring.Launch"/> creates the
/// <see cref="GameLog"/>), then on <see cref="AttachLog"/> posts one visible aggregated summary
/// ("N special rules ... are not implemented: ...") plus every raw warning as a Debug-channel line.
/// Warnings arriving after attach (dispatch-time diagnostics) stream straight into the log as Debug lines.
///
/// Headless mode never installs this, keeping the channel's plain-stdout fallback that automated runs
/// grep. When installed, every warning is still echoed to stdout in the same "[rules]" format.
/// </summary>
public static class RuleLoadWarnings
{
    private static readonly object _lock = new();
    private static readonly List<string> _pendingMessages = new();
    private static readonly List<RuleDrop> _pendingDrops = new();
    private static GameLog? _log;
    private static bool _installed;

    private static readonly TextColor WarnColor = TextColor.FromRgb(255, 170, 60);
    private static readonly TextColor DetailColor = TextColor.FromRgb(200, 170, 120);

    /// <summary>Subscribes the channel. Call once at startup in GUI modes; idempotent.</summary>
    public static void Install()
    {
        lock (_lock)
        {
            if (_installed) return;
            _installed = true;
        }

        RuleDiagnostics.OnRuleDropped += HandleDrop;
        RuleDiagnostics.OnWarning += HandleWarning;
    }

    /// <summary>
    /// Points the collector at the just-launched game's log: flushes the buffered load-time warnings
    /// (aggregated summary visible, raw lines on the Debug channel) and streams future ones there.
    /// </summary>
    public static void AttachLog(GameLog log)
    {
        List<string> messages;
        lock (_lock)
        {
            _log = log;
            messages = new List<string>(_pendingMessages);
            _pendingMessages.Clear();
        }

        FlushPending();

        foreach (string message in messages)
            log.Add(message, DetailColor, isDebug: true);
    }

    /// <summary>
    /// Summarizes any drops still buffered into the attached log. <see cref="AttachLog"/> calls this,
    /// covering paths where armies load before the log exists (lobby launch); the GUI scenario path
    /// builds its FDGServer AFTER <see cref="GameGuiWiring.Launch"/>, so it calls this again once the
    /// server (and its army loads) is up. No-op with nothing buffered or no log attached.
    /// </summary>
    public static void FlushPending()
    {
        GameLog? log;
        List<RuleDrop> drops;
        lock (_lock)
        {
            log = _log;
            if (log == null || _pendingDrops.Count == 0) return;
            drops = new List<RuleDrop>(_pendingDrops);
            _pendingDrops.Clear();
        }

        foreach (string summaryLine in Summarize(drops))
            log.Add(summaryLine, WarnColor);
    }

    /// <summary>
    /// The one-line "not implemented" aggregate for a set of drops, or null when none are
    /// <see cref="ERuleDropReason.Unimplemented"/>. Shared by the launch summary here and the army
    /// builder's validation pane (#168), which differ only in <paramref name="subject"/>
    /// ("the loaded armies" / "this list"). ASCII only.
    /// </summary>
    internal static string? SummarizeUnimplemented(IReadOnlyList<RuleDrop> drops, string subject)
    {
        List<RuleDrop> unimplemented = drops.Where(d => d.Reason == ERuleDropReason.Unimplemented).ToList();
        if (unimplemented.Count == 0) return null;

        List<string> names = unimplemented.Select(d => d.RuleName)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        StringBuilder line = new StringBuilder();
        line.Append(names.Count == 1
            ? $"1 special rule on {subject} is not implemented and will do nothing: "
            : $"{names.Count} special rules on {subject} are not implemented and will do nothing: ");
        line.Append(string.Join(", ", names));
        if (unimplemented.Count > names.Count)
            line.Append($" ({unimplemented.Count} references)");
        line.Append('.');
        return line.ToString();
    }

    /// <summary>
    /// The one-line "this list predates the rulebook" aggregate (#342), or null when no drop is
    /// <see cref="ERuleDropReason.OutdatedList"/>. Separate copy from
    /// <see cref="SummarizeUnimplemented"/> because the fix is different: these rules exist and work,
    /// the saved list is just too old to carry their definitions, so rebuilding it is what helps.
    /// ASCII only.
    /// </summary>
    internal static string? SummarizeOutdated(IReadOnlyList<RuleDrop> drops, string subject)
    {
        List<RuleDrop> outdated = drops.Where(d => d.Reason == ERuleDropReason.OutdatedList).ToList();
        if (outdated.Count == 0) return null;

        List<string> names = outdated.Select(d => d.RuleName)
            .Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .ToList();

        StringBuilder line = new StringBuilder();
        line.Append(names.Count == 1
            ? $"1 special rule on {subject} predates the current rulebook and will do nothing until the list is rebuilt: "
            : $"{names.Count} special rules on {subject} predate the current rulebook and will do nothing until the list is rebuilt: ");
        line.Append(string.Join(", ", names));
        if (outdated.Count > names.Count)
            line.Append($" ({outdated.Count} references)");
        line.Append('.');
        return line.ToString();
    }

    private static void HandleDrop(RuleDrop drop)
    {
        lock (_lock)
        {
            // Always buffered (not gated on _log): drops belong to the NEXT launch's summary. In every
            // launch path the army loads strictly before AttachLog runs, so in practice this holds
            // exactly the loading game's drops.
            _pendingDrops.Add(drop);
        }
    }

    private static void HandleWarning(string message)
    {
        // Subscribing suppresses the channel's stdout fallback — re-echo in its exact format so
        // console output is unchanged by the GUI collector existing.
        Console.WriteLine($"[rules] {message}");

        lock (_lock)
        {
            if (_log == null)
            {
                _pendingMessages.Add(message);
                return;
            }
        }

        _log?.Add(message, DetailColor, isDebug: true);
    }

    /// <summary>
    /// The visible log lines for a load's drops: unimplemented rules aggregated by name, then rules an
    /// outdated list can't see (#342), then a count of misauthored references (wrong scope / missing
    /// value / weaponless wargear) if any. Empty when nothing was dropped. Internal for tests. ASCII only.
    /// </summary>
    internal static IReadOnlyList<string> Summarize(IReadOnlyList<RuleDrop> drops)
    {
        List<string> lines = new();
        if (drops.Count == 0) return lines;

        string? unimplementedLine = SummarizeUnimplemented(drops, "the loaded armies");
        if (unimplementedLine != null)
        {
            lines.Add($"{unimplementedLine} Details in the Debug log.");
        }

        string? outdatedLine = SummarizeOutdated(drops, "the loaded armies");
        if (outdatedLine != null)
        {
            lines.Add($"{outdatedLine} Details in the Debug log.");
        }

        int misauthored = drops.Count(d =>
            d.Reason != ERuleDropReason.Unimplemented && d.Reason != ERuleDropReason.OutdatedList);
        if (misauthored > 0)
        {
            lines.Add($"{misauthored} rule reference(s) were dropped as misauthored " +
                      "(wrong scope, missing value, or no weapon to carry them). Details in the Debug log.");
        }

        return lines;
    }
}
