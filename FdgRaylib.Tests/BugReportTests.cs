using System.IO.Compression;
using System.Text;
using System.Text.Json;
using FdgRaylib.BugReport;
using FdgRaylib.Rendering;
using FDG;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// #226: the in-app bug reporter - bundle assembly (merged log order, save capture, crash-log
/// tail), the local BugReports/ writer, and the gzip the uploader sends.
/// </summary>
[TestFixture]
public class BugReportTests
{
    private string _dir = "";
    private string? _previousConfigDir;

    [SetUp]
    public void SetUp()
    {
        _dir = Path.Combine(Path.GetTempPath(), "fdg-bugreport-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        // Build() reads UserConfig.Current for the player name; keep that off the real per-user
        // config file (same isolation UserConfigTests uses).
        _previousConfigDir = Environment.GetEnvironmentVariable("FDG_CONFIG_DIR");
        Environment.SetEnvironmentVariable("FDG_CONFIG_DIR", _dir);
    }

    [TearDown]
    public void TearDown()
    {
        Environment.SetEnvironmentVariable("FDG_CONFIG_DIR", _previousConfigDir);
        try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ }
    }

    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement;

    // ---- Bundle assembly ---------------------------------------------------------------

    [Test]
    public void Build_MergesLogAndChatInArrivalOrder_WithTags()
    {
        var log = new GameLog();
        var chat = new GameLog();
        var white = new TextColor(255, 255, 255, 255);

        log.Add("first engine line", white);
        chat.Add("Alice: hello", white);
        log.Add("debug detail", white, isDebug: true);

        string json = BugReportBundleBuilder.Build("desc", log, chat, saveGameToJson: null,
            crashLogPath: Path.Combine(_dir, "no-such-crash.log"));
        JsonElement root = Parse(json);

        string[] lines = root.GetProperty("log").EnumerateArray().Select(e => e.GetString()!).ToArray();
        Assert.That(lines, Is.EqualTo(new[]
        {
            "first engine line",
            "[chat] Alice: hello",
            "[dbg] debug detail",
        }), "log and chat must interleave in global-sequence order, chat and debug lines tagged");
    }

    [Test]
    public void Build_HostCarriesSave_ClientIsLogOnly()
    {
        string hostJson = BugReportBundleBuilder.Build("desc", null, null, () => "{\"fake\":\"save\"}",
            crashLogPath: Path.Combine(_dir, "none"));
        JsonElement host = Parse(hostJson);
        Assert.That(host.GetProperty("isHost").GetBoolean(), Is.True);
        Assert.That(host.GetProperty("save").GetString(), Is.EqualTo("{\"fake\":\"save\"}"),
            "the save must ride as an opaque string, not be parsed into the bundle");

        string clientJson = BugReportBundleBuilder.Build("desc", null, null, saveGameToJson: null,
            crashLogPath: Path.Combine(_dir, "none"));
        JsonElement client = Parse(clientJson);
        Assert.That(client.GetProperty("isHost").GetBoolean(), Is.False);
        Assert.That(client.TryGetProperty("save", out _), Is.False,
            "clients can't save (#054) - null save is omitted, not sent as JSON null");
    }

    [Test]
    public void Build_CapsTheLog_KeepingTheNewestLines()
    {
        var log = new GameLog();
        var white = new TextColor(255, 255, 255, 255);
        for (int i = 0; i < BugReportBundleBuilder.MaxLogLines + 10; i++)
            log.Add($"line {i}", white);

        JsonElement root = Parse(BugReportBundleBuilder.Build("desc", log, null, null,
            crashLogPath: Path.Combine(_dir, "none")));
        var lines = root.GetProperty("log").EnumerateArray().Select(e => e.GetString()!).ToList();

        Assert.That(lines.Count, Is.EqualTo(BugReportBundleBuilder.MaxLogLines + 1),
            "capped lines plus the one dropped-lines marker");
        Assert.That(lines[0], Does.Contain("10 earlier lines dropped"));
        Assert.That(lines[^1], Is.EqualTo($"line {BugReportBundleBuilder.MaxLogLines + 9}"),
            "the NEWEST lines are the ones that must survive the cap");
    }

    [Test]
    public void Build_ReadsOnlyTheCrashLogTail()
    {
        string crashPath = Path.Combine(_dir, "crash.log");
        // Well past the tail cap, with a known marker at the very end.
        var content = new StringBuilder();
        for (int i = 0; i < 10_000; i++) content.AppendLine($"old crash line {i}");
        content.Append("FINAL MARKER");
        File.WriteAllText(crashPath, content.ToString());

        JsonElement root = Parse(BugReportBundleBuilder.Build("desc", null, null, null, crashPath));
        string crashLog = root.GetProperty("crashLog").GetString()!;

        Assert.That(crashLog, Does.EndWith("FINAL MARKER"));
        Assert.That(crashLog, Does.StartWith("[... truncated ...]"));
        Assert.That(crashLog.Length, Is.LessThanOrEqualTo(BugReportBundleBuilder.CrashLogTailBytes + 64));
        Assert.That(crashLog, Does.Not.Contain("old crash line 0\n"), "the head must be dropped");
    }

    [Test]
    public void Build_StampsVersionAndDescription()
    {
        JsonElement root = Parse(BugReportBundleBuilder.Build("it broke", null, null, null,
            crashLogPath: Path.Combine(_dir, "none")));

        Assert.That(root.GetProperty("description").GetString(), Is.EqualTo("it broke"));
        Assert.That(root.GetProperty("appVersion").GetString(), Is.Not.Empty,
            "every report must say which binary produced it (dev builds report 'dev')");
        Assert.That(root.GetProperty("protocolVersion").GetInt32(),
            Is.EqualTo(FDG.Network.NetworkProtocol.Version));
        Assert.That(root.TryGetProperty("crashLog", out _), Is.False,
            "no crash.log on disk means the member is omitted, not sent as JSON null");
    }

    // ---- Local store -------------------------------------------------------------------

    [Test]
    public void Store_WritesTimestampedFile_AndNeverOverwritesASameSecondReport()
    {
        var stamp = new DateTime(2026, 8, 5, 14, 33, 55, DateTimeKind.Utc);

        string? first = BugReportStore.TryWrite("{\"a\":1}", stamp, _dir);
        string? second = BugReportStore.TryWrite("{\"a\":2}", stamp, _dir);

        Assert.That(first, Is.Not.Null);
        Assert.That(Path.GetFileName(first), Is.EqualTo("report-20260805-143355.json"));
        Assert.That(second, Is.Not.Null.And.Not.EqualTo(first),
            "a same-second report must get a suffixed name, not overwrite");
        Assert.That(File.ReadAllText(first!), Is.EqualTo("{\"a\":1}"));
        Assert.That(File.ReadAllText(second!), Is.EqualTo("{\"a\":2}"));
    }

    [Test]
    public void Store_ReturnsNullOnEmptyBundle()
    {
        Assert.That(BugReportStore.TryWrite("", DateTime.UtcNow, _dir), Is.Null);
    }

    // ---- Uploader gzip -----------------------------------------------------------------

    [Test]
    public void Gzip_RoundTrips_AndActuallyCompresses()
    {
        // Repetitive like real JSON bundles; must round-trip through the same gzip the Worker gunzips.
        string text = string.Concat(Enumerable.Repeat("{\"unit\":\"Warrior\",\"wounds\":1}", 2000));
        byte[] gzipped = BugReportUploader.Gzip(text);

        using var input = new MemoryStream(gzipped);
        using var gunzip = new GZipStream(input, CompressionMode.Decompress);
        using var reader = new StreamReader(gunzip, Encoding.UTF8);
        Assert.That(reader.ReadToEnd(), Is.EqualTo(text));
        Assert.That(gzipped.Length, Is.LessThan(text.Length / 4),
            "the whole design leans on JSON compressing well");
    }
}
