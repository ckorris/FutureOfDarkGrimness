using FDG;
using FDG.Data;
using FDG.GameModel;
using FDG.Replay;
using FDG.SaveLoad;
using FdgRaylib.Cli;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #455: the --replay CLI, run in-process on a replay recorded by a real game: the example shootout scenario with
// AI on both slots, through the same launch path as `--scenario --all-ai`. Pins structure, not content (plan rule
// 8: no golden files), so rule changes can't turn these red.
[TestFixture]
public class ReplayToolTests
{
    private string _root = "";
    private string _replayPath = "";
    private ReplayDocument _document = null!;

    [OneTimeSetUp]
    public async Task RecordAGame()
    {
        _root = Directory.CreateTempSubdirectory(nameof(ReplayToolTests)).FullName;
        GameDataStore store = ScenarioLauncher.LoadStore(FindRepoFile("Scenarios/example-shootout.json"));
        ScenarioLauncher.ResumeParts parts = ScenarioLauncher.BuildResume(store, seedOverride: 455, allAi: true);

        var recorder = new ReplayRecorder();
        parts.HumanGame.AttachReplay(recorder);
        var ended = new TaskCompletionSource<GameResult>();
        var server = new FDGServer(parts.Store, parts.Bus, parts.Slots, replay: recorder);
        server.OnGameCompleted += result => ended.TrySetResult(result);

        Task finished = await Task.WhenAny(ended.Task, Task.Delay(TimeSpan.FromSeconds(120)));
        Assert.That(finished, Is.SameAs(ended.Task), "the recorded scenario must play to completion");

        byte[] gzip = recorder.ExportGzip();
        _replayPath = Path.Combine(_root, "shootout.fdgreplay");
        File.WriteAllBytes(_replayPath, gzip);
        using var stream = new MemoryStream(gzip);
        _document = ReplayReader.ReadGzip(stream);
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static (int Code, string Output) Run(params string[] args)
    {
        using var output = new StringWriter();
        int code = ReplayTool.Run(args, output);
        return (code, output.ToString());
    }

    private static string[] Lines(string output) => output.Split('\n').Select(line => line.TrimEnd('\r')).ToArray();

    [Test]
    public void Dump_PrintsTheHeader_AndEveryStepExactlyOnce()
    {
        var timeline = new ReplayTimeline(_document);
        Assert.That(timeline.Steps, Has.Count.GreaterThan(3), "precondition: a game with decisions");

        (int code, string output) = Run("--replay", _replayPath, "--dump");

        Assert.That(code, Is.EqualTo(0), output);
        string[] lines = Lines(output);
        Assert.That(lines[0], Does.StartWith($"Replay   {_replayPath}"));
        Assert.That(output, Does.Contain($"{_document.Header.GameId}   seed 455   "));
        Assert.That(lines.Count(l => l.StartsWith("Slot     P", StringComparison.Ordinal)), Is.EqualTo(_document.Header.Slots.Count));
        Assert.That(output, Does.Contain($"steps {timeline.Steps.Count} (+"));
        Assert.That(lines.Single(l => l.StartsWith("Outcome  ", StringComparison.Ordinal)), Does.Match("^Outcome  (Win|Tie) "));
        for (int i = 0; i < timeline.Steps.Count; i++)
        {
            Assert.That(lines.Count(l => l.StartsWith($"#{i:000}  ", StringComparison.Ordinal)), Is.EqualTo(1), $"step {i}");
        }
        Assert.That(lines.Count(l => l.StartsWith("-- RoundStart R", StringComparison.Ordinal)), Is.GreaterThan(0), "markers print as their own lines");
        Assert.That(output.All(c => c == '\n' || c == '\r' || (c >= ' ' && c <= '~')), Is.True, "ASCII only");

        (code, output) = Run("--replay", _replayPath, "--dump", "--from", "1", "--to", "2", "--no-log", "--no-beats");
        Assert.That(code, Is.EqualTo(0), output);
        Assert.That(Lines(output).Where(l => l.StartsWith("#", StringComparison.Ordinal)).Select(l => l.Substring(0, 4)), Is.EqualTo(new[] { "#001", "#002" }));
        Assert.That(output, Does.Not.Contain("    log: ").And.Not.Contain("    beat: "));
    }

    [Test]
    public void Verify_Passes_AndReportsTheSeqOfARemovedUpdate()
    {
        (int code, string output) = Run("--replay", _replayPath, "--verify");
        Assert.That(code, Is.EqualTo(0), output);
        Assert.That(Lines(output).Last(l => l.Length > 0), Does.StartWith("Result: PASS - "));

        StoreUpdateEvent removed = _document.Events.OfType<StoreUpdateEvent>().First();
        string tampered = Path.Combine(_root, "tampered.fdgreplay");
        using (FileStream file = File.Create(tampered))
        {
            ReplayWriter.WriteGzip(_document.Header, _document.Events.Where(e => e.Seq != removed.Seq), file);
        }

        (code, output) = Run("--replay", tampered, "--verify");
        Assert.That(code, Is.EqualTo(1));
        Assert.That(Lines(output).Where(l => l.StartsWith("FAIL  seq", StringComparison.Ordinal)).Single(),
            Does.Match($@"Seq +{removed.Seq}  Event {removed.Seq} is missing"));
        Assert.That(Lines(output).Last(l => l.Length > 0), Does.StartWith("Result: FAIL - ").And.Contain($"first: Seq {removed.Seq} (seq)"));
    }

    [Test]
    public void Export_IsTheDecompressedContainer()
    {
        string outPath = Path.Combine(_root, "export", "shootout.jsonl");
        (int code, string output) = Run("--replay", _replayPath, "--export", outPath);

        Assert.That(code, Is.EqualTo(0), output);
        string[] lines = File.ReadAllText(outPath).Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.That(lines, Has.Length.EqualTo(_document.Events.Count + 1));
        Assert.That(lines[0], Does.StartWith("{\"FormatVersion\":"));
    }

    [Test]
    public void Events_DiffStoreUpdates_AndFilterByKind()
    {
        (int code, string output) = Run("--replay", _replayPath, "--events", "--kind", "storeupdate");

        Assert.That(code, Is.EqualTo(0), output);
        string[] lines = Lines(output).Where(l => l.Length > 0).ToArray();
        Assert.That(lines, Has.Length.EqualTo(_document.Events.OfType<StoreUpdateEvent>().Count()));
        Assert.That(lines, Has.All.Contain("  StoreUpdate   "));
        Assert.That(lines, Has.Some.Match(@"UnitData\[\d+\] \(.+\)  changed: \w+"), "a unit update names the unit and its changed fields");

        (code, output) = Run("--replay", _replayPath, "--events", "--kind", "Nonsense");
        Assert.That(code, Is.EqualTo(1));
        Assert.That(output, Does.StartWith("Replay error: Unknown kind 'Nonsense'"));
    }

    [Test]
    public void Stats_BreaksTheFileDown()
    {
        (int code, string output) = Run("--replay", _replayPath, "--stats");

        Assert.That(code, Is.EqualTo(0), output);
        foreach (string kind in _document.Events.Select(e => e.Kind).Distinct())
        {
            Assert.That(output, Does.Match($@"\n  {kind} +{_document.Events.Count(e => e.Kind == kind)} "), kind);
        }
        Assert.That(output, Does.Contain("By store type").And.Contain("By request type").And.Contain("Snapshots"));
    }

    [Test]
    [CancelAfter(180_000)]
    public async Task MakeSave_AtStep3_WritesTheStateThere_AndItResumes()
    {
        string outPath = Path.Combine(_root, "cut", "at3.fdgsave");
        (int code, string output) = Run("--replay", _replayPath, "--at", "3", "--make-save", outPath);
        Assert.That(code, Is.EqualTo(0), output);
        Assert.That(output, Does.StartWith("Step 3 of ").And.Contain("--scenario " + outPath));

        var player = new ReplayPlayer(new ReplayTimeline(_document));
        player.SeekToStep(3);
        Assert.That(File.ReadAllText(outPath), Is.EqualTo(GameSaveSerializer.Save(player.Store)), "the state step 3 was asked in");

        // And --scenario can take it from there to a result.
        ScenarioLauncher.ResumeParts parts = ScenarioLauncher.BuildResume(ScenarioLauncher.LoadStore(outPath), allAi: true);
        var ended = new TaskCompletionSource<GameResult>();
        var server = new FDGServer(parts.Store, parts.Bus, parts.Slots);
        server.OnGameCompleted += result => ended.TrySetResult(result);
        Task finished = await Task.WhenAny(ended.Task, Task.Delay(TimeSpan.FromSeconds(120)));
        Assert.That(finished, Is.SameAs(ended.Task), "the cut save resumes and plays to completion");
    }

    [Test]
    public void BadInvocations_ExitOne_WithAOneLineReason()
    {
        AssertError(Run("--replay", Path.Combine(_root, "missing.fdgreplay"), "--dump"), "File not found");
        AssertError(Run("--replay", Path.Combine(_root, "game.fdgsave"), "--dump"), "#453");
        AssertError(Run("--replay", _replayPath, "--dump", "--stats"), "One mode at a time");
        AssertError(Run("--replay", _replayPath), "No mode given");
        AssertError(Run("--replay", _replayPath, "--at", "99999", "--make-save", Path.Combine(_root, "x.fdgsave")), "--at takes a step");

        string notGzip = Path.Combine(_root, "plain.fdgreplay");
        File.WriteAllText(notGzip, "{}");
        AssertError(Run("--replay", notGzip, "--verify"), "not gzip");

        static void AssertError((int Code, string Output) result, string reason)
        {
            Assert.That(result.Code, Is.EqualTo(1), result.Output);
            Assert.That(result.Output.TrimEnd('\n', '\r'), Does.Not.Contain("\n"), "one line");
            Assert.That(result.Output, Does.StartWith("Replay error: ").And.Contain(reason));
        }
    }

    private static string FindRepoFile(string relative)
    {
        for (var dir = new DirectoryInfo(TestContext.CurrentContext.TestDirectory); dir != null; dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }
        throw new FileNotFoundException($"Could not find {relative} above the test directory.");
    }
}
