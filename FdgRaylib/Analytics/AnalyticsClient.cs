using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using FdgRaylib.Config;

namespace FdgRaylib.Analytics;

/// <summary>
/// Collects anonymous usage events and uploads them in small batches to the list-server Worker
/// (#406). Never blocks a caller and never throws: <see cref="Track"/> only appends to an in-memory
/// queue, and a background timer (or <see cref="Flush"/> at exit) does the network work.
///
/// <para><b>Gates.</b> Nothing is queued unless all hold: the player left analytics on, the player has
/// clicked through the notice, a URL is configured, and this run is not excluded (headless, scenario,
/// <c>-dev</c> build) - unless <c>FDG_ANALYTICS=1</c> forces it - and <c>FDG_ANALYTICS</c> is not 0.
/// When a gate fails, <see cref="Track"/> is a no-op; nothing is held back to send later.</para>
///
/// <para><b>Failure handling.</b> A batch that fails for a transient reason (no network, timeout, 5xx,
/// 429 rate limit) goes back to the front of the queue, and the queue is written to a small spool
/// file capped at <see cref="MaxSpooled"/> events (oldest dropped), which the next launch sends
/// first. A batch the server rejects outright (other 4xx) is dropped: resending it can't succeed.</para>
///
/// <para><b>Opt-out.</b> <see cref="SetEnabled"/>(false) clears the queue, deletes the spool and the
/// install ID, and sends nothing - there is no "opted out" event.</para>
///
/// <para>Thread-safe: the engine thread tracks game events while the main thread tracks UI events,
/// toggles the opt-out, and flushes at exit.</para>
/// </summary>
public sealed class AnalyticsClient : IDisposable
{
    public const int MaxBatch = 20;
    public const int MaxSpooled = 200;
    public const string SpoolFileName = "analytics-spool.json";

    /// <summary>The Worker's per-IP minimum interval is 2s; consecutive batches wait a little longer.</summary>
    internal static readonly TimeSpan BatchSpacing = TimeSpan.FromMilliseconds(2_200);
    internal static readonly TimeSpan FlushInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(5);

    private static readonly JsonSerializerOptions Json = new() { WriteIndented = false };

    private readonly Func<UserConfig> _config;
    private readonly Action _saveConfig;
    private readonly string _spoolPath;
    private readonly string? _baseUrl;
    private readonly string? _excludedReason;
    private readonly bool? _switch;
    private readonly string _version;
    private readonly HttpClient _http;
    private readonly Func<DateTime> _clock;
    private readonly bool _spaceBatches;

    private readonly object _lock = new();
    private readonly List<AnalyticsEvent> _queue = new();
    private readonly SemaphoreSlim _flushGate = new(1, 1);
    private Timer? _timer;
    private Action? _onTick;

    /// <summary>Random per process launch: groups one launch's events without identifying anyone.</summary>
    public string SessionId { get; } = Guid.NewGuid().ToString("D");

    public AnalyticsClient(Func<UserConfig> config, Action saveConfig, string spoolPath, string? baseUrl,
        string? excludedReason, bool? analyticsSwitch, string version,
        HttpMessageHandler? handler = null, Func<DateTime>? clock = null, bool spaceBatches = true)
    {
        _config = config;
        _saveConfig = saveConfig;
        _spoolPath = spoolPath;
        _baseUrl = baseUrl;
        _excludedReason = excludedReason;
        _switch = analyticsSwitch;
        _version = version.Length > AnalyticsEvent.MaxVersionChars ? version[..AnalyticsEvent.MaxVersionChars] : version;
        _http = handler != null ? new HttpClient(handler, disposeHandler: false) : new HttpClient();
        _http.Timeout = RequestTimeout;
        _clock = clock ?? (() => DateTime.UtcNow);
        _spaceBatches = spaceBatches;
    }

    // ── Gates ──────────────────────────────────────────────────────────────────────────────

    /// <summary>Why this run sends nothing regardless of the player's choice, or null when it may send.
    /// Shown in the Debug log; the Options checkbox uses <see cref="Enabled"/>.</summary>
    public string? DisabledReason
    {
        get
        {
            if (_switch == false) return $"{AnalyticsConfig.SwitchEnvVar}=0";
            if (_baseUrl == null) return "no analytics URL configured";
            if (_excludedReason != null && _switch != true) return _excludedReason;
            return null;
        }
    }

    /// <summary>All gates pass: events are being collected and sent.</summary>
    public bool Enabled
    {
        get
        {
            UserConfig cfg = _config();
            return DisabledReason == null && cfg.AnalyticsEnabled && cfg.AnalyticsNoticeAcknowledged;
        }
    }

    // ── Tracking ───────────────────────────────────────────────────────────────────────────

    /// <summary>Queues one event. A no-op unless <see cref="Enabled"/>. Never throws.</summary>
    public void Track(string name, IReadOnlyDictionary<string, object?>? props = null, string? gameId = null)
    {
        try
        {
            lock (_lock)
            {
                if (!Enabled) return;
                string install = EnsureInstallIdLocked();
                _queue.Add(new AnalyticsEvent(name, new DateTimeOffset(_clock()).ToUnixTimeSeconds(), install,
                    SessionId, gameId, _version, AnalyticsEvent.CurrentOs(), AnalyticsEvent.CurrentArch(),
                    props ?? new Dictionary<string, object?>()));
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"analytics: could not queue '{name}' ({ex.Message})");
        }
    }

    /// <summary>Events waiting to be sent (tests and the Debug log).</summary>
    public IReadOnlyList<AnalyticsEvent> PendingForTests()
    {
        lock (_lock) return _queue.ToList();
    }

    // ── Opt-in / opt-out ───────────────────────────────────────────────────────────────────

    /// <summary>The player's choice from the notice or the settings toggle. Off clears the queue,
    /// the spool and the install ID; on generates a fresh ID next time an event is tracked.</summary>
    public void SetEnabled(bool enabled)
    {
        lock (_lock)
        {
            UserConfig cfg = _config();
            cfg.AnalyticsEnabled = enabled;
            if (!enabled)
            {
                cfg.AnalyticsInstallId = null;
                _queue.Clear();
                DeleteSpool();
            }
            _saveConfig();
        }
    }

    /// <summary>Records that the notice was clicked through, with the player's choice from it.</summary>
    public void AcknowledgeNotice(bool enabled)
    {
        lock (_lock)
        {
            _config().AnalyticsNoticeAcknowledged = true;
        }
        SetEnabled(enabled);
    }

    private string EnsureInstallIdLocked()
    {
        UserConfig cfg = _config();
        if (cfg.AnalyticsInstallId is { } id && Guid.TryParse(id, out _))
            return id.ToLowerInvariant();
        cfg.AnalyticsInstallId = Guid.NewGuid().ToString("D");
        _saveConfig();
        return cfg.AnalyticsInstallId;
    }

    // ── Background sending ─────────────────────────────────────────────────────────────────

    /// <summary>Loads any spooled events from a previous run and starts the 30s flush timer.
    /// <paramref name="onTick"/> runs on every tick (the session heartbeat rides on it).</summary>
    public void Start(Action? onTick = null)
    {
        LoadSpool();
        _onTick = onTick;
        _timer = new Timer(_ => Tick(), null, TimeSpan.FromSeconds(5), FlushInterval);
    }

    private void Tick()
    {
        try { _onTick?.Invoke(); } catch { /* the heartbeat is best-effort */ }
        _ = FlushAsync();
    }

    /// <summary>Exit path: one bounded attempt to send what is queued, then spool the rest.</summary>
    public void Flush(TimeSpan timeout)
    {
        _timer?.Dispose();
        _timer = null;
        try
        {
            using var cts = new CancellationTokenSource(timeout);
            // waitForInFlight: if the 30s timer is mid-send right now, wait for it and then send the
            // rest, rather than leaving the last events (a game_abandon, say) for the next launch.
            FlushAsync(cts.Token, waitForInFlight: true).Wait(timeout);
        }
        catch { /* timed out or cancelled: whatever is left is spooled below */ }
        lock (_lock)
        {
            if (Enabled) SaveSpoolLocked();
        }
    }

    /// <summary>Sends everything queued, one batch at a time. Single-flight: a call while another is
    /// running returns at once (the running one will pick up new events next tick) - unless
    /// <paramref name="waitForInFlight"/>, the exit path, which queues up behind it instead.</summary>
    public async Task FlushAsync(CancellationToken cancellationToken = default, bool waitForInFlight = false)
    {
        if (waitForInFlight)
        {
            try { await _flushGate.WaitAsync(cancellationToken).ConfigureAwait(false); }
            catch (OperationCanceledException) { return; } // out of time: the caller spools
        }
        else if (!await _flushGate.WaitAsync(0, CancellationToken.None).ConfigureAwait(false))
        {
            return;
        }
        try
        {
            bool first = true;
            while (true)
            {
                List<AnalyticsEvent> batch;
                lock (_lock)
                {
                    if (!Enabled)
                    {
                        _queue.Clear();
                        return;
                    }
                    if (_queue.Count == 0)
                    {
                        DeleteSpool();
                        return;
                    }
                    batch = _queue.Take(MaxBatch).ToList();
                    _queue.RemoveRange(0, batch.Count);
                }

                if (!first && _spaceBatches)
                    await Task.Delay(BatchSpacing, cancellationToken).ConfigureAwait(false);
                first = false;

                SendResult result = await SendAsync(batch, cancellationToken).ConfigureAwait(false);
                if (result == SendResult.Retry)
                {
                    lock (_lock)
                    {
                        if (Enabled)
                        {
                            _queue.InsertRange(0, batch);
                            SaveSpoolLocked();
                        }
                    }
                    return;
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"analytics: flush failed ({ex.Message})");
        }
        finally
        {
            _flushGate.Release();
        }
    }

    internal enum SendResult { Sent, Dropped, Retry }

    private async Task<SendResult> SendAsync(List<AnalyticsEvent> batch, CancellationToken cancellationToken)
    {
        try
        {
            string body = JsonSerializer.Serialize(batch, Json);
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using HttpResponseMessage response = await _http.PostAsync($"{_baseUrl}/events", content, cancellationToken)
                .ConfigureAwait(false);
            int code = (int)response.StatusCode;
            if (response.IsSuccessStatusCode) return SendResult.Sent;
            if (response.StatusCode == HttpStatusCode.TooManyRequests || code >= 500) return SendResult.Retry;
            // A 4xx other than 429 means the server will never accept this batch (schema drift
            // between an old client and a newer Worker, say). Say so once and move on.
            string reason = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            Console.WriteLine($"analytics: server rejected a batch ({code}): {reason.Trim()}");
            return SendResult.Dropped;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return SendResult.Retry;
        }
        catch (Exception)
        {
            return SendResult.Retry; // offline, DNS, timeout: try again later
        }
    }

    // ── Spool ──────────────────────────────────────────────────────────────────────────────

    private void LoadSpool()
    {
        lock (_lock)
        {
            if (!Enabled)
            {
                // Opted out (or excluded) since the spool was written: it must not linger.
                if (_config().AnalyticsEnabled == false) DeleteSpool();
                return;
            }
            try
            {
                if (!File.Exists(_spoolPath)) return;
                var spooled = JsonSerializer.Deserialize<List<AnalyticsEvent>>(File.ReadAllText(_spoolPath), Json);
                if (spooled != null) _queue.InsertRange(0, spooled.TakeLast(MaxSpooled));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
            {
                DeleteSpool(); // unreadable: start over rather than fail every launch
            }
        }
    }

    private void SaveSpoolLocked()
    {
        try
        {
            if (_queue.Count == 0)
            {
                DeleteSpool();
                return;
            }
            if (_queue.Count > MaxSpooled) _queue.RemoveRange(0, _queue.Count - MaxSpooled);
            string? dir = Path.GetDirectoryName(_spoolPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_spoolPath, JsonSerializer.Serialize(_queue, Json));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private void DeleteSpool()
    {
        try { if (File.Exists(_spoolPath)) File.Delete(_spoolPath); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _http.Dispose();
    }
}
