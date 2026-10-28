using FdgRaylib.Config;

namespace FdgRaylib.Analytics;

/// <summary>
/// The app-wide entry point for anonymous usage analytics (#406). <see cref="Initialize"/> once at
/// startup; until then, and in any run that never calls it, every member is a harmless no-op.
/// See <see cref="AnalyticsClient"/> for the gates, batching and opt-out behavior, and
/// <c>WorkItems/406-usage-analytics.md</c> for what is collected and why.
/// </summary>
public static class UsageAnalytics
{
    private static AnalyticsClient? _client;
    private static SessionTracker? _session;
    private static SessionTracker.PreviousSession? _previousSession;
    private static int _launchSent;

    /// <summary>The live client, or null before <see cref="Initialize"/>.</summary>
    public static AnalyticsClient? Client => _client;

    /// <summary>True when events are being collected and sent right now.</summary>
    public static bool Enabled => _client?.Enabled ?? false;

    /// <summary>
    /// Builds the client and starts the session marker and flush timer. <paramref name="excludedRun"/>
    /// names why this kind of run stays out of the numbers ("headless", "scenario"), or null for a
    /// normal GUI launch. Sends <c>app_launch</c> straight away when the notice has already been
    /// acknowledged; otherwise the notice sends it when the player clicks OK.
    /// </summary>
    public static void Initialize(string? excludedRun)
    {
        if (_client != null) return;

        string version = AppVersion.Value;
        string? excluded = excludedRun ?? AnalyticsConfig.BuildExclusion(version);
        _client = new AnalyticsClient(() => UserConfig.Current, UserConfig.Save,
            Path.Combine(UserConfig.Directory, AnalyticsClient.SpoolFileName),
            AnalyticsConfig.ResolveBaseUrl(), excluded, AnalyticsConfig.ResolveSwitch(), version);

        if (excludedRun == null)
        {
            _session = new SessionTracker(Path.Combine(UserConfig.Directory, SessionTracker.FileName));
            _previousSession = _session.Begin(DateTime.UtcNow, version);
        }

        string? reason = _client.DisabledReason;
        Console.WriteLine(reason != null
            ? $"analytics: disabled ({reason})"
            : _client.Enabled ? "analytics: enabled"
            : UserConfig.Current.AnalyticsNoticeAcknowledged ? "analytics: disabled (player opted out)"
            : "analytics: waiting for the notice");

        _client.Start(onTick: () => _session?.Heartbeat(DateTime.UtcNow));
        if (UserConfig.Current.AnalyticsNoticeAcknowledged) SendLaunch();
    }

    /// <summary>Sends this launch's <c>app_launch</c>, at most once per process. Called at startup
    /// when the notice was acknowledged on an earlier run, or by the notice's OK on the first.</summary>
    public static void SendLaunch()
    {
        if (_client == null || !_client.Enabled) return;
        if (Interlocked.Exchange(ref _launchSent, 1) == 1) return;

        // No ID yet means this launch is about to create one: the first launch with analytics on
        // (a fresh install, an upgrade past the notice, or the first launch after re-enabling).
        bool firstLaunch = string.IsNullOrEmpty(UserConfig.Current.AnalyticsInstallId);
        _client.Track(AnalyticsEvents.AppLaunch, new Dictionary<string, object?>
        {
            ["first_launch"] = firstLaunch,
            ["prev_session_seconds"] = _previousSession?.Seconds,
            ["prev_session_clean"] = _previousSession?.Clean,
        });
    }

    /// <summary>Queues one event (a no-op when disabled or uninitialized).</summary>
    public static void Track(string name, IReadOnlyDictionary<string, object?>? props = null, string? gameId = null) =>
        _client?.Track(name, props, gameId);

    /// <summary>Shorthand for <c>feature_open</c>.</summary>
    public static void FeatureOpened(string feature) =>
        Track(AnalyticsEvents.FeatureOpen, new Dictionary<string, object?> { ["feature"] = feature });

    /// <summary>Normal exit: marks the session clean, then one bounded attempt to send what's queued.</summary>
    public static void Shutdown()
    {
        _session?.End(DateTime.UtcNow);
        _client?.Flush(TimeSpan.FromSeconds(2));
    }
}
