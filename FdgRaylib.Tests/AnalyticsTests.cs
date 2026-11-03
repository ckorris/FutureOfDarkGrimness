using System.Net;
using System.Net.Http;
using System.Text.Json;
using FdgRaylib.Analytics;
using FdgRaylib.Config;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// #412 anonymous usage analytics, client side: the gates that keep a run silent, the opt-out
/// cleanup, batching, the retry spool, the session marker, and the name contract with the Worker's
/// allowlist. No real network: a fake handler stands in for the Worker.
/// </summary>
[TestFixture]
public class AnalyticsTests
{
    private string _dir = "";
    private string SpoolPath => Path.Combine(_dir, AnalyticsClient.SpoolFileName);

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "fdg-analytics-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
    }

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { }
    }

    /// <summary>Records every POST body; answers with the queued status codes (then 202).</summary>
    private sealed class FakeWorker : HttpMessageHandler
    {
        public readonly List<string> Bodies = new();
        public readonly Queue<HttpStatusCode> Answers = new();
        public bool ThrowNetworkError;
        public TimeSpan Delay = TimeSpan.Zero;
        private int _inFlight;
        public int MaxInFlight;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            int now = Interlocked.Increment(ref _inFlight);
            lock (Bodies) MaxInFlight = Math.Max(MaxInFlight, now);
            try
            {
                if (Delay > TimeSpan.Zero) await Task.Delay(Delay, ct);
                if (ThrowNetworkError) throw new HttpRequestException("no network");
                string body = await request.Content!.ReadAsStringAsync(ct);
                lock (Bodies) Bodies.Add(body);
                HttpStatusCode code = Answers.Count > 0 ? Answers.Dequeue() : HttpStatusCode.Accepted;
                return new HttpResponseMessage(code) { Content = new StringContent("{}") };
            }
            finally
            {
                Interlocked.Decrement(ref _inFlight);
            }
        }

        public int EventsSent => Bodies.Sum(b => JsonDocument.Parse(b).RootElement.GetArrayLength());
    }

    private static UserConfig Acknowledged(bool enabled = true) =>
        new() { AnalyticsEnabled = enabled, AnalyticsNoticeAcknowledged = true };

    private AnalyticsClient Client(UserConfig config, FakeWorker worker, string? baseUrl = "https://worker.test",
        string? excluded = null, bool? analyticsSwitch = null) =>
        new(() => config, () => { }, SpoolPath, baseUrl, excluded, analyticsSwitch, "0.3.0+git-abc",
            worker, spaceBatches: false);

    private static void TrackLaunch(AnalyticsClient client) =>
        client.Track(AnalyticsEvents.AppLaunch, new Dictionary<string, object?> { ["first_launch"] = true });

    // ── Gates ──────────────────────────────────────────────────────────────────────────────

    [Test]
    public void NothingIsQueuedBeforeTheNoticeIsAcknowledged()
    {
        var config = new UserConfig(); // fresh install: enabled by default, notice not yet seen
        using var client = Client(config, new FakeWorker());

        TrackLaunch(client);

        Assert.That(client.PendingForTests(), Is.Empty);
        Assert.That(config.AnalyticsInstallId, Is.Null, "no ID may exist before the player has seen the notice.");
    }

    [Test]
    public void NothingIsQueuedWhenThePlayerOptedOut()
    {
        var config = Acknowledged(enabled: false);
        using var client = Client(config, new FakeWorker());

        TrackLaunch(client);

        Assert.That(client.PendingForTests(), Is.Empty);
        Assert.That(config.AnalyticsInstallId, Is.Null);
    }

    [Test]
    public void ExcludedRunsAreSilentUnlessForced()
    {
        using (var excluded = Client(Acknowledged(), new FakeWorker(), excluded: "headless"))
        {
            TrackLaunch(excluded);
            Assert.That(excluded.PendingForTests(), Is.Empty);
            Assert.That(excluded.DisabledReason, Is.EqualTo("headless"));
        }

        using var forced = Client(Acknowledged(), new FakeWorker(), excluded: "headless", analyticsSwitch: true);
        TrackLaunch(forced);
        Assert.That(forced.PendingForTests(), Has.Count.EqualTo(1), "FDG_ANALYTICS=1 overrides a run exclusion.");
    }

    [Test]
    public void TheSwitchOffAndAMissingUrlBothDisable()
    {
        using var off = Client(Acknowledged(), new FakeWorker(), analyticsSwitch: false);
        using var noUrl = Client(Acknowledged(), new FakeWorker(), baseUrl: null);

        TrackLaunch(off);
        TrackLaunch(noUrl);

        Assert.That(off.PendingForTests(), Is.Empty);
        Assert.That(noUrl.PendingForTests(), Is.Empty);
    }

    [Test]
    public void ForcingOnNeverOverridesThePlayersOptOut()
    {
        using var client = Client(Acknowledged(enabled: false), new FakeWorker(), analyticsSwitch: true);
        TrackLaunch(client);
        Assert.That(client.PendingForTests(), Is.Empty);
    }

    [Test]
    public void DevBuildsAreExcludedByDefault()
    {
        Assert.That(AnalyticsConfig.BuildExclusion("0.3.0-dev"), Is.Not.Null);
        Assert.That(AnalyticsConfig.BuildExclusion("0.3.0-dev+abc123"), Is.Not.Null);
        Assert.That(AnalyticsConfig.BuildExclusion("0.3.0+opr.3.5.1.git-abc-20261028"), Is.Null);
    }

    [Test]
    public void UrlOverridesNormalizeOrDisable()
    {
        Assert.That(AnalyticsConfig.Normalize("https://x.workers.dev/ "), Is.EqualTo("https://x.workers.dev"));
        Assert.That(AnalyticsConfig.Normalize(""), Is.Null, "a blank override disables analytics.");
        Assert.That(AnalyticsConfig.Normalize("ftp://x"), Is.Null);
    }

    // ── Event shape ────────────────────────────────────────────────────────────────────────

    [Test]
    public async Task EventsSerializeToTheWorkersSchema()
    {
        var config = Acknowledged();
        var worker = new FakeWorker();
        using var client = Client(config, worker);

        client.Track(AnalyticsEvents.GameEnd, new Dictionary<string, object?> { ["rounds"] = 4, ["winner"] = "ai" },
            gameId: "2b3c4d5e-6f70-4b1c-8d2e-4f5a6b7c8d9e");
        await client.FlushAsync();

        Assert.That(worker.Bodies, Has.Count.EqualTo(1));
        JsonElement ev = JsonDocument.Parse(worker.Bodies[0]).RootElement[0];
        Assert.That(ev.EnumerateObject().Select(p => p.Name),
            Is.EquivalentTo(new[] { "name", "t", "install", "session", "game", "ver", "os", "arch", "props" }));
        Assert.That(ev.GetProperty("name").GetString(), Is.EqualTo("game_end"));
        Assert.That(ev.GetProperty("install").GetString(), Is.EqualTo(config.AnalyticsInstallId));
        Assert.That(ev.GetProperty("install").GetString(), Does.Match("^[0-9a-f-]{36}$"), "the Worker wants a lowercase UUID.");
        Assert.That(ev.GetProperty("session").GetString(), Is.EqualTo(client.SessionId));
        Assert.That(ev.GetProperty("ver").GetString(), Is.EqualTo("0.3.0+git-abc"));
        Assert.That(ev.GetProperty("props").GetProperty("rounds").GetInt32(), Is.EqualTo(4));
        Assert.That(new[] { "windows", "linux", "macos", "other" }, Does.Contain(ev.GetProperty("os").GetString()));
    }

    [Test]
    public void TheInstallIdPersistsInTheConfigAndIsReused()
    {
        var config = Acknowledged();
        using var client = Client(config, new FakeWorker());

        TrackLaunch(client);
        string? first = config.AnalyticsInstallId;
        TrackLaunch(client);

        Assert.That(first, Is.Not.Null);
        Assert.That(client.PendingForTests().Select(e => e.Install).Distinct(), Is.EqualTo(new[] { first }));

        // A later launch (a new client, maybe a new build in a new folder) reads the same config.
        using var nextLaunch = Client(config, new FakeWorker());
        TrackLaunch(nextLaunch);
        Assert.That(nextLaunch.PendingForTests()[0].Install, Is.EqualTo(first));
        Assert.That(nextLaunch.SessionId, Is.Not.EqualTo(client.SessionId), "sessions are per launch.");
    }

    // ── Opt-out, notice, reset ─────────────────────────────────────────────────────────────

    [Test]
    public async Task OptingOutClearsTheQueueTheSpoolAndTheId()
    {
        var config = Acknowledged();
        var worker = new FakeWorker { ThrowNetworkError = true };
        using var client = Client(config, worker);
        TrackLaunch(client);
        await client.FlushAsync(); // fails, so the event is spooled
        Assert.That(File.Exists(SpoolPath), Is.True, "precondition: a spool exists.");

        client.SetEnabled(false);

        Assert.That(client.PendingForTests(), Is.Empty);
        Assert.That(File.Exists(SpoolPath), Is.False);
        Assert.That(config.AnalyticsInstallId, Is.Null);
        Assert.That(config.AnalyticsEnabled, Is.False);

        worker.ThrowNetworkError = false;
        await client.FlushAsync();
        Assert.That(worker.Bodies, Is.Empty, "no 'opted out' event, nothing at all.");
    }

    [Test]
    public void ReEnablingStartsAFreshId()
    {
        var config = Acknowledged();
        using var client = Client(config, new FakeWorker());
        TrackLaunch(client);
        string? before = config.AnalyticsInstallId;

        client.SetEnabled(false);
        client.SetEnabled(true);
        TrackLaunch(client);

        Assert.That(config.AnalyticsInstallId, Is.Not.Null.And.Not.EqualTo(before));
    }

    [Test]
    public void AcknowledgingTheNoticeRecordsTheChoice()
    {
        var config = new UserConfig();
        using var client = Client(config, new FakeWorker());

        client.AcknowledgeNotice(enabled: false);

        Assert.That(config.AnalyticsNoticeAcknowledged, Is.True);
        Assert.That(config.AnalyticsEnabled, Is.False);
        TrackLaunch(client);
        Assert.That(client.PendingForTests(), Is.Empty);
    }

    // ── Batching and the spool ─────────────────────────────────────────────────────────────

    [Test]
    public async Task FlushSendsBatchesOfAtMostTwenty()
    {
        var worker = new FakeWorker();
        using var client = Client(Acknowledged(), worker);
        for (int i = 0; i < 45; i++) TrackLaunch(client);

        await client.FlushAsync();

        Assert.That(worker.Bodies.Select(b => JsonDocument.Parse(b).RootElement.GetArrayLength()),
            Is.EqualTo(new[] { 20, 20, 5 }));
        Assert.That(client.PendingForTests(), Is.Empty);
        Assert.That(File.Exists(SpoolPath), Is.False);
    }

    [TestCase(HttpStatusCode.TooManyRequests)]
    [TestCase(HttpStatusCode.InternalServerError)]
    [TestCase(HttpStatusCode.ServiceUnavailable)]
    public async Task TransientFailuresAreSpooledAndSentFirstNextLaunch(HttpStatusCode failure)
    {
        var config = Acknowledged();
        var worker = new FakeWorker();
        worker.Answers.Enqueue(failure);
        using (var client = Client(config, worker))
        {
            TrackLaunch(client);
            await client.FlushAsync();
            Assert.That(File.Exists(SpoolPath), Is.True, $"a {(int)failure} must keep the event for later.");
        }

        var nextWorker = new FakeWorker();
        using var next = Client(config, nextWorker);
        next.Start();
        next.Track(AnalyticsEvents.FeatureOpen, new Dictionary<string, object?> { ["feature"] = AnalyticsFeatures.CombatCalculator });
        await next.FlushAsync();

        Assert.That(nextWorker.Bodies, Has.Count.EqualTo(1));
        JsonElement batch = JsonDocument.Parse(nextWorker.Bodies[0]).RootElement;
        Assert.That(batch[0].GetProperty("name").GetString(), Is.EqualTo("app_launch"), "spooled events go first.");
        Assert.That(batch[1].GetProperty("name").GetString(), Is.EqualTo("feature_open"));
        Assert.That(File.Exists(SpoolPath), Is.False, "a fully sent queue leaves no spool.");
    }

    [Test]
    public async Task ARejectedBatchIsDroppedNotRetried()
    {
        var worker = new FakeWorker();
        worker.Answers.Enqueue(HttpStatusCode.BadRequest);
        using var client = Client(Acknowledged(), worker);
        TrackLaunch(client);

        await client.FlushAsync();
        await client.FlushAsync();

        Assert.That(worker.Bodies, Has.Count.EqualTo(1));
        Assert.That(client.PendingForTests(), Is.Empty);
        Assert.That(File.Exists(SpoolPath), Is.False);
    }

    [Test]
    public async Task TheSpoolIsCappedDroppingTheOldest()
    {
        var worker = new FakeWorker { ThrowNetworkError = true };
        using var client = Client(Acknowledged(), worker);
        for (int i = 0; i < AnalyticsClient.MaxSpooled + 50; i++)
            client.Track(AnalyticsEvents.GameEnd, new Dictionary<string, object?> { ["rounds"] = i });

        await client.FlushAsync();

        var spooled = JsonSerializer.Deserialize<List<JsonElement>>(File.ReadAllText(SpoolPath))!;
        Assert.That(spooled, Has.Count.EqualTo(AnalyticsClient.MaxSpooled));
        Assert.That(spooled[0].GetProperty("props").GetProperty("rounds").GetInt32(), Is.EqualTo(50),
            "the oldest events are the ones dropped.");
    }

    [Test]
    public async Task OnlyOneUploadIsEverInFlight()
    {
        var worker = new FakeWorker { Delay = TimeSpan.FromMilliseconds(50) };
        using var client = Client(Acknowledged(), worker);
        for (int i = 0; i < 60; i++) TrackLaunch(client);

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => Task.Run(() => client.FlushAsync())));
        await client.FlushAsync();

        Assert.That(worker.MaxInFlight, Is.EqualTo(1));
        Assert.That(worker.EventsSent, Is.EqualTo(60), "every event sent exactly once.");
    }

    [Test]
    public void ExitFlushSpoolsWhatItCouldNotSend()
    {
        var worker = new FakeWorker { ThrowNetworkError = true };
        using var client = Client(Acknowledged(), worker);
        TrackLaunch(client);

        client.Flush(TimeSpan.FromSeconds(2));

        Assert.That(File.Exists(SpoolPath), Is.True);
    }

    [Test]
    public async Task ExitFlushWaitsForAnInFlightSendThenSendsTheRest()
    {
        // The 30s timer is mid-send when the player quits; the quit's events must still go out now.
        var worker = new FakeWorker { Delay = TimeSpan.FromMilliseconds(300) };
        using var client = Client(Acknowledged(), worker);
        TrackLaunch(client);
        Task timerSend = Task.Run(() => client.FlushAsync());
        await Task.Delay(50); // let the timer's send take the gate
        client.Track(AnalyticsEvents.GameAbandon, new Dictionary<string, object?> { ["reason"] = "quit" });

        client.Flush(TimeSpan.FromSeconds(2));

        // Checked the instant Flush returns: at real exit the process ends here, taking any background
        // send with it, so whatever hasn't gone out by now doesn't go out this run.
        int sentAtExit;
        lock (worker.Bodies) sentAtExit = worker.Bodies.Count;
        bool abandonSent;
        lock (worker.Bodies) abandonSent = worker.Bodies.Any(b => b.Contains("game_abandon"));
        await timerSend;

        Assert.That(sentAtExit, Is.EqualTo(2), "the timer's batch and the quit's batch both went out before exit");
        Assert.That(abandonSent, Is.True);
    }

    // ── Dwell timer (Combat Calculator "used" after a minute) ──────────────────────────────

    [Test]
    public async Task DwellTimerFiresOnlyForAVisitThatLastsTheThreshold()
    {
        int fired = 0;
        var timer = new DwellTimer(TimeSpan.FromMilliseconds(150), () => Interlocked.Increment(ref fired));

        timer.Entered();
        await Task.Delay(50);
        timer.Left();               // a peek: never counts
        await Task.Delay(250);
        Assert.That(fired, Is.EqualTo(0));

        timer.Entered();            // a real visit, still open past the threshold
        await Task.Delay(300);
        Assert.That(fired, Is.EqualTo(1));
        timer.Left();
        await Task.Delay(200);
        Assert.That(fired, Is.EqualTo(1), "once per visit");
    }

    [Test]
    public async Task DwellTimerReEntryRestartsTheClock()
    {
        int fired = 0;
        var timer = new DwellTimer(TimeSpan.FromMilliseconds(200), () => Interlocked.Increment(ref fired));

        timer.Entered();
        await Task.Delay(120);
        timer.Left();
        timer.Entered();            // the first visit's pending timer must not count this one
        await Task.Delay(120);
        Assert.That(fired, Is.EqualTo(0));
        await Task.Delay(200);
        Assert.That(fired, Is.EqualTo(1));
    }

    // ── Session marker ─────────────────────────────────────────────────────────────────────

    [Test]
    public void SessionTrackerReportsCleanAndUncleanPreviousSessions()
    {
        string path = Path.Combine(_dir, SessionTracker.FileName);
        var t0 = new DateTime(2026, 10, 28, 12, 0, 0, DateTimeKind.Utc);

        var first = new SessionTracker(path);
        Assert.That(first.Begin(t0, "v"), Is.Null, "no previous session on a fresh install.");
        first.Heartbeat(t0.AddMinutes(10));
        first.End(t0.AddMinutes(25));

        var second = new SessionTracker(path);
        Assert.That(second.Begin(t0.AddHours(1), "v"), Is.EqualTo(new SessionTracker.PreviousSession(25 * 60, true)));
        second.Heartbeat(t0.AddHours(1).AddMinutes(7));
        // ...and then the process dies: no End().

        var third = new SessionTracker(path);
        Assert.That(third.Begin(t0.AddHours(2), "v"), Is.EqualTo(new SessionTracker.PreviousSession(7 * 60, false)),
            "an unclean end is measured to the last heartbeat.");
    }

    // ── Privacy dialog text ────────────────────────────────────────────────────────────────

    [Test]
    public void PrivacyTextIsAsciiOnly()
    {
        // CLAUDE.md: the ImGui font atlas has no glyphs past U+00FF, so a pasted em dash or curly quote
        // in the hand-written notice would render as '?'. Checked to U+007E to keep it plain ASCII.
        var all = new[] { FdgRaylib.Rendering.PrivacyDialog.IntroText, FdgRaylib.Rendering.PrivacyDialog.ChangeLaterText };
        foreach (string text in all)
        {
            char bad = text.FirstOrDefault(c => c > '~' || (c < ' ' && c != '\n'));
            Assert.That(bad, Is.EqualTo('\0'), $"non-ASCII character U+{(int)bad:X4} in: {text}");
        }
    }

    // ── Contract with the Worker ───────────────────────────────────────────────────────────

    [Test]
    public void EveryClientEventAndFeatureNameIsInTheWorkersAllowlist()
    {
        string schema = File.ReadAllText(FindRepoFile("tools/list-server/src/analytics.ts"));

        foreach (string name in ConstantsOf(typeof(AnalyticsEvents)))
            Assert.That(schema, Does.Contain($"  {name}: {{"), $"event '{name}' is missing from the Worker SCHEMA.");
        foreach (string feature in ConstantsOf(typeof(AnalyticsFeatures)))
            Assert.That(schema, Does.Contain($"\"{feature}\""), $"feature '{feature}' is missing from the Worker SCHEMA.");
    }

    private static IEnumerable<string> ConstantsOf(Type type) =>
        type.GetFields().Where(f => f.IsLiteral).Select(f => (string)f.GetRawConstantValue()!);

    private static string FindRepoFile(string relative)
    {
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }
        Assert.Fail($"could not find {relative} above the test directory");
        return "";
    }
}
