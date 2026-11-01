using FDG;
using FDG.Data;
using FDG.GameModel;
using FDG.Replay;
using FDG.SaveLoad;
using FdgRaylib.Cli;
using FdgRaylib.Rendering.Replays;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #456: the replay viewer's logic without a window - the browser's rows, the transport, the decision list, and the
// session driven against a real recorded game (the example shootout, AI on both slots) through a stand-in canvas.
// Pins structure, not content (plan rule 8).
[TestFixture]
public class ReplayViewerTests
{
    private string _root = "";
    private string _replayPath = "";
    private string _resumedPath = "";

    [OneTimeSetUp]
    public async Task RecordGames()
    {
        _root = Directory.CreateTempSubdirectory(nameof(ReplayViewerTests)).FullName;

        ReplayRecorder played = await Play(ScenarioLauncher.LoadStore(ReplayToolTests.FindRepoFile("Scenarios/example-shootout.json")), null);
        _replayPath = Path.Combine(_root, "replay-20261031-100000-shootout.fdgreplay");
        File.WriteAllBytes(_replayPath, played.ExportGzip());

        // The same game cut at step 3 and resumed (#453): one replay with a Saved / Resumed boundary in it.
        ReplayDocument document = Read(played.ExportGzip());
        var player = new ReplayPlayer(new ReplayTimeline(document));
        player.SeekToStep(3);
        string save = GameSaveSerializer.Save(player.Store, ReplayRecorder.CutForSave(document, player.Steps[3].RequestSeq));
        var (store, prior) = GameSaveSerializer.LoadWithReplay(save);
        ReplayRecorder resumed = await Play(store, prior);
        _resumedPath = Path.Combine(_root, "replay-20261031-110000-resumed.fdgreplay");
        File.WriteAllBytes(_resumedPath, resumed.ExportGzip());
    }

    [OneTimeTearDown]
    public void TearDown()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private static async Task<ReplayRecorder> Play(GameDataStore store, byte[]? prior)
    {
        ScenarioLauncher.ResumeParts parts = ScenarioLauncher.BuildResume(store, seedOverride: 456, allAi: true);
        ReplayRecorder recorder = ReplayRecorder.Continue(prior);
        var ended = new TaskCompletionSource<GameResult>();
        var server = new FDGServer(parts.Store, parts.Bus, parts.Slots, replay: recorder);
        server.OnGameCompleted += result => ended.TrySetResult(result);
        Task finished = await Task.WhenAny(ended.Task, Task.Delay(TimeSpan.FromSeconds(120)));
        Assert.That(finished, Is.SameAs(ended.Task), "the recorded scenario must play to completion");
        return recorder;
    }

    private static ReplayDocument Read(byte[] gzip)
    {
        using var stream = new MemoryStream(gzip);
        return ReplayReader.ReadGzip(stream);
    }

    private sealed class Canvas : IReplayCanvas
    {
        public ITableState? TableState;
        public int Replacements;
        public int Scrolls;

        public void ReplaceTableState(ITableState tableState)
        {
            TableState = tableState;
            Replacements++;
        }

        public void ScrollConsoleToEnd() => Scrolls++;
    }

    private static (ReplaySession Session, Canvas Canvas) Open(string path)
    {
        ReplaySession session = ReplaySession.Load(path);
        var canvas = new Canvas { TableState = session.Player.TableState };
        session.Attach(canvas);
        return (session, canvas);
    }

    /// <summary>Frames until a seek lands (bounded).</summary>
    private static void Settle(ReplaySession session)
    {
        for (int frame = 0; frame < 2000 && session.IsSeeking; frame++)
        {
            Thread.Sleep(5);
            session.Update(1f / 60f);
        }
        Assert.That(session.IsSeeking, Is.False, "the seek must land");
    }

    private static int LogLinesThrough(ReplayTimeline timeline, long seq) =>
        timeline.Events.Count(e => e is LogEvent && e.Seq <= seq);

    // --- Browser --------------------------------------------------------------------------------------------

    private static ReplayHeader Header(params (string Name, string Army, int Team, EReplayController Controller)[] slots) => new()
    {
        GameId = Guid.NewGuid(),
        StartedUtc = new DateTime(2026, 10, 31, 14, 33, 0, DateTimeKind.Utc),
        Slots = slots.Select((s, i) => new ReplaySlot
        {
            SlotId = i, PlayerId = new PlayerID(Guid.NewGuid()), Name = s.Name, ArmyName = s.Army, Team = s.Team, Controller = s.Controller,
        }).ToList(),
        Summary = new ReplaySummary(Steps: 41, Rounds: 4, Outcome: "Win", DurationMs: 1000, SizeBytes: 5000),
    };

    [Test]
    public void BrowserRow_ReadsTheHeaderAndItsSummary()
    {
        ReplayHeader header = Header(("Chris", "Battle Brothers", 0, EReplayController.Human), ("Bot", "Orks", 1, EReplayController.Ai));
        header.ResumedUtc.Add(header.StartedUtc.AddHours(1));

        ReplayBrowserRow row = ReplayCatalog.From("/x/replay.fdgreplay", header, 2048);

        Assert.That(row.Armies, Is.EqualTo("Battle Brothers vs Orks"));
        Assert.That(row.Players, Is.EqualTo("Chris (Human), Bot (AI)"));
        Assert.That((row.Rounds, row.Steps, row.Outcome), Is.EqualTo((4, 41, "Win")), "a Win recorded before #457 named the winner");
        Assert.That(row.Size, Is.EqualTo("2.0 KB"));
        Assert.That(row.Started, Is.EqualTo(header.StartedUtc.ToLocalTime().ToString("yyyy-MM-dd HH:mm",
            System.Globalization.CultureInfo.InvariantCulture) + " (1 resume)"));
        Assert.That(row.Error, Is.Null);
    }

    [Test]
    public void BrowserRow_TeamsAndAnUnfinishedGame()
    {
        ReplayHeader header = Header(("A", "Elves", 0, EReplayController.Human), ("B", "Dwarves", 0, EReplayController.Remote),
            ("C", "Orks", 1, EReplayController.Ai), ("D", "Goblins", 1, EReplayController.Ai));
        header.Summary = header.Summary! with { Outcome = null };

        ReplayBrowserRow row = ReplayCatalog.From("/x/r.fdgreplay", header, 10);

        Assert.That(row.Armies, Is.EqualTo("Elves & Dwarves vs Orks & Goblins"));
        Assert.That(row.Outcome, Is.EqualTo("Unfinished"));
        Assert.That(ReplayCatalog.Outcome(null), Is.EqualTo("?"), "a file with no summary");

        // #457: the Outcome column names the winners.
        Assert.That(ReplayCatalog.Outcome(header.Summary with { Outcome = "Win", Winner = "A" }), Is.EqualTo("A wins"));
        Assert.That(ReplayCatalog.Outcome(header.Summary with { Outcome = "Win", Winner = "A and B" }), Is.EqualTo("A and B win"));
        Assert.That(ReplayCatalog.Outcome(header.Summary with { Outcome = "Tie", Winner = null }), Is.EqualTo("Tie"));
    }

    [Test]
    public void Browser_ListsNewestFirst_AndAnUnreadableFileIsARowSayingWhy()
    {
        string folder = Directory.CreateDirectory(Path.Combine(_root, "browser")).FullName;
        File.Copy(_replayPath, Path.Combine(folder, "replay-20261031-100000-a.fdgreplay"));
        File.Copy(_resumedPath, Path.Combine(folder, "replay-20261031-120000-b.fdgreplay"));
        File.WriteAllText(Path.Combine(folder, "replay-20261031-110000-broken.fdgreplay"), "not gzip");

        IReadOnlyList<ReplayBrowserRow> rows = ReplayCatalog.List(folder);

        Assert.That(rows.Select(r => r.FileName), Is.EqualTo(new[]
        {
            "replay-20261031-120000-b.fdgreplay", "replay-20261031-110000-broken.fdgreplay", "replay-20261031-100000-a.fdgreplay",
        }));
        Assert.That(rows[1].Error, Does.Contain("not gzip"));
        Assert.That(rows[0].Error, Is.Null);
        Assert.That(rows[0].Started, Does.EndWith("(1 resume)"));
        ReplayHeader full = Read(File.ReadAllBytes(_replayPath)).Header;
        Assert.That(rows[2].Steps, Is.EqualTo(full.Summary!.Steps).And.GreaterThan(0));
        Assert.That(rows[2].Outcome, Is.EqualTo(ReplayCatalog.Outcome(full.Summary)).And.Not.EqualTo("Unfinished"));
    }

    // --- Transport --------------------------------------------------------------------------------------------

    [Test]
    public void Transport_AutoplayWaitsForTheAnimations_ThenTheDwell()
    {
        var transport = new ReplayTransport(stepCount: 3);
        Assert.That(transport.Tick(10f, animationsIdle: true), Is.False, "paused: never advances");

        transport.Play();
        Assert.That(transport.Tick(1f, animationsIdle: false), Is.False, "animations still running");
        Assert.That(transport.Tick(0.5f, animationsIdle: true), Is.False, "the dwell has not run out");
        Assert.That(transport.Tick(0.3f, animationsIdle: true), Is.True, "0.8 s idle > the 0.75 s dwell");

        transport.Speed = EReplaySpeed.Double;
        Assert.That(transport.Tick(0.4f, animationsIdle: true), Is.True, "the dwell scales with the speed");
        transport.Speed = EReplaySpeed.Instant;
        Assert.That(transport.PlaysBeats, Is.False);
        Assert.That(transport.Tick(ReplayTransport.InstantDwellSeconds, animationsIdle: true), Is.True);
    }

    [Test]
    public void Transport_StopsAtTheEnd_AndAtAResume()
    {
        var transport = new ReplayTransport(stepCount: 3);
        transport.Play();
        transport.Arrived(1, crossedResume: true);
        Assert.That(transport.IsPlaying, Is.False, "a resume stops autoplay");

        transport.Play();
        transport.Arrived(3, crossedResume: false);
        Assert.That((transport.AtEnd, transport.IsPlaying), Is.EqualTo((true, false)));
        transport.Play();
        Assert.That(transport.IsPlaying, Is.False, "nothing to play at the end");
        Assert.That(transport.Tick(10f, animationsIdle: true), Is.False);
    }

    // --- Decision list ------------------------------------------------------------------------------------------

    [Test]
    public void DecisionList_StepsInOrder_MarkersSeparate_FoldedAlwaysListed_EachLineItsPlayers()
    {
        var timeline = new ReplayTimeline(Read(File.ReadAllBytes(_resumedPath)));
        new ReplayPlayer(timeline); // describes the steps

        IReadOnlyList<ReplayListRow> rows = ReplayListRows.Build(timeline);

        Assert.That(rows.Select(r => r.Seq), Is.Ordered);
        Assert.That(rows.Where(r => r.Kind == EReplayRowKind.Step).Select(r => r.StepIndex), Is.EqualTo(Enumerable.Range(0, timeline.Steps.Count)));
        Assert.That(rows.Count(r => r.Kind == EReplayRowKind.Folded),
            Is.EqualTo(timeline.Steps.Sum(s => s.Folded.Count) + timeline.FoldedBeforeFirstStep.Count).And.GreaterThan(0));

        // #457: a decision is its player's line, a folded request its asker's; a round's start is the initiative's.
        Assert.That(rows.Where(r => r.Kind == EReplayRowKind.Step).Select(r => r.Player),
            Is.EqualTo(timeline.Steps.Select(s => (PlayerID?)s.PlayerId)));
        Assert.That(rows.Where(r => r.Kind == EReplayRowKind.Folded).All(r => r.Player != null), Is.True);
        IReadOnlyDictionary<int, PlayerID> initiative = ReplayListRows.InitiativeByRound(timeline);
        Assert.That(initiative, Is.Not.Empty);
        foreach (ReplayListRow roundRow in rows.Where(r => r.Text.StartsWith("-- Round ")))
        {
            int round = int.Parse(roundRow.Text.Split(' ')[2]);
            Assert.That(roundRow.Player, Is.EqualTo(initiative[round]), roundRow.Text);
        }

        List<string> markers = rows.Where(r => r.Kind == EReplayRowKind.Marker).Select(r => r.Text).ToList();
        Assert.That(markers, Has.Some.StartsWith("-- Round "));
        Assert.That(markers, Has.Some.StartsWith("-- Saved"));
        Assert.That(markers, Has.Some.StartsWith("-- Resumed"));
        Assert.That(markers[^1], Does.StartWith("-- Game over: "));
        Assert.That(rows[^1].StepIndex, Is.EqualTo(timeline.Steps.Count), "the game's end seeks to the end");

        string first = rows.First(r => r.Kind == EReplayRowKind.Step).Text;
        Assert.That(first, Does.StartWith("P").And.Contain(" (AI)  "), "who, then what - no step or round numbers (#457)");
        Assert.That(rows.All(r => r.Text.All(c => c >= ' ' && c <= '~')), Is.True, "ASCII only");
    }

    [Test]
    public void DecisionList_MarkerTexts()
    {
        Assert.That(ReplayListRows.MarkerText(new MarkerEvent(EReplayMarker.RoundStart) { Round = 3 }), Is.EqualTo("-- Round 3 --"));
        Assert.That(ReplayListRows.MarkerText(new MarkerEvent(EReplayMarker.Saved) { Round = 2 }), Is.EqualTo("-- Saved in round 2 --"));
        Assert.That(ReplayListRows.MarkerText(new MarkerEvent(EReplayMarker.Resumed) { Round = 2 }),
            Is.EqualTo("-- Resumed in round 2: the game was loaded from that save --"));
        Assert.That(ReplayListRows.MarkerText(new MarkerEvent(EReplayMarker.GameEnded) { Outcome = "Win", Message = "Reds win." }),
            Is.EqualTo("-- Game over: Win - Reds win. --"));
        Assert.That(ReplayListRows.MarkerText(new MarkerEvent(EReplayMarker.GameStart)), Is.Null);
        Assert.That(ReplayListRows.MarkerText(new MarkerEvent(EReplayMarker.ActivationStart) { Round = 1, Activation = 2 }), Is.Null);
    }

    // --- Session --------------------------------------------------------------------------------------------------

    [Test]
    public void Session_OpensAtTheFirstStep_WithTheLogUpToIt()
    {
        var (session, _) = Open(_replayPath);

        Assert.That(session.Transport.Position, Is.Zero);
        Assert.That(session.Transport.StepCount, Is.EqualTo(session.Player.Steps.Count).And.GreaterThan(5));
        Assert.That(session.Log.Count, Is.EqualTo(LogLinesThrough(session.Timeline, session.Player.Steps[0].RequestSeq)));
        Assert.That(session.PositionLabel, Does.EndWith($"step #000 of {session.Transport.StepCount}"));
    }

    [Test]
    public void Session_StepForwardPlaysTheStepsAnimations_SeekAndStepBackRebuildWithout()
    {
        var (session, canvas) = Open(_replayPath);
        ReplayTimeline timeline = session.Timeline;

        // Forward until a step with animations: they are queued at once, in place (no new table state).
        int replacementsBefore = canvas.Replacements;
        while (session.Presentation.IsAnimating == false && session.Transport.AtEnd == false) session.StepForward();
        Assert.That(session.Presentation.IsAnimating, Is.True, "some step of a shooting game animates");
        Assert.That(canvas.Replacements, Is.EqualTo(replacementsBefore), "a step forward applies in place");
        int at = session.Transport.Position;
        Assert.That(session.Log.Count, Is.EqualTo(LogLinesThrough(timeline, session.Player.Steps[at].RequestSeq)));

        // A seek runs on a worker and hands the new table state over when it lands, animations dropped.
        session.Seek(2);
        Assert.That(session.IsSeeking, Is.True);
        Settle(session);
        Assert.That(session.Transport.Position, Is.EqualTo(2));
        Assert.That(canvas.TableState, Is.SameAs(session.Player.TableState));
        Assert.That(canvas.Replacements, Is.EqualTo(replacementsBefore + 1));
        Assert.That(session.Presentation.IsAnimating, Is.False);
        Assert.That(session.Log.Count, Is.EqualTo(LogLinesThrough(timeline, session.Player.Steps[2].RequestSeq)));

        session.StepBack();
        Settle(session);
        Assert.That(session.Transport.Position, Is.EqualTo(1));
    }

    [Test]
    public void Session_SeeksAskedForWhileOneRuns_TheLatestWins()
    {
        var (session, _) = Open(_replayPath);

        session.Seek(5);
        session.Seek(8);
        session.Seek(7);
        for (int frame = 0; frame < 4000 && (session.IsSeeking || session.Transport.Position != 7); frame++)
        {
            Thread.Sleep(2);
            session.Update(1f / 60f);
        }

        Assert.That(session.Transport.Position, Is.EqualTo(7));
        Assert.That(session.IsSeeking, Is.False);
    }

    [Test]
    public void Session_InstantAutoplay_RunsToTheEnd_WhereTheStateIsTheGamesEnd()
    {
        var (session, _) = Open(_replayPath);
        session.Transport.Speed = EReplaySpeed.Instant;
        session.TogglePlay();

        int frames = 0;
        for (; frames < 100_000 && session.Transport.AtEnd == false; frames++) session.Update(1f / 60f);

        Assert.That(session.Transport.AtEnd, Is.True);
        Assert.That(frames / 60f, Is.LessThan(session.Transport.StepCount * 0.1f), "Instant outpaces 0.1 s a step");
        Assert.That(session.Presentation.IsAnimating, Is.False, "Instant plays no animations");
        Assert.That(session.Notice, Does.StartWith("Game over: "));

        var rebuilt = new ReplayPlayer(session.Timeline);
        rebuilt.SeekToEnd();
        Assert.That(GameSaveSerializer.Save(session.Player.Store), Is.EqualTo(GameSaveSerializer.Save(rebuilt.Store)),
            "stepping all the way in place reaches the state a rebuild of the end does");
    }

    [Test]
    public void Session_AutoplayStopsAtAResume_AndSaysSo()
    {
        var (session, canvas) = Open(_resumedPath);
        session.Transport.Speed = EReplaySpeed.Instant;
        session.TogglePlay();

        for (int frame = 0; frame < 100_000 && session.Transport.IsPlaying; frame++) session.Update(1f / 60f);

        Assert.That(session.Transport.AtEnd, Is.False, "stopped before the end");
        Assert.That(session.Notice, Does.Contain("saved and resumed here"));
        Assert.That(canvas.TableState, Is.SameAs(session.Player.TableState), "crossing the resume rebuilt the state, handed over");

        session.TogglePlay();
        for (int frame = 0; frame < 100_000 && session.Transport.IsPlaying; frame++) session.Update(1f / 60f);
        Assert.That(session.Transport.AtEnd, Is.True, "play on from there to the end");
    }

    [Test]
    public void Session_OpensTheReplayASaveCarries()
    {
        string savePath = Path.Combine(_root, "carrying.fdgsave");
        ReplayDocument document = Read(File.ReadAllBytes(_replayPath));
        File.WriteAllText(savePath, GameSaveSerializer.Save(GameDataStore.GameDataStoreBuilder.GetDefault(),
            ReplayRecorder.CutForSave(document, document.Events[^1].Seq)));

        var (session, _) = Open(savePath);

        Assert.That(session.Header.GameId, Is.EqualTo(document.Header.GameId));
        Assert.That(session.Transport.StepCount, Is.EqualTo(new ReplayTimeline(document).Steps.Count));
        Assert.Throws<ReplayFileException>(() => ReplaySession.Load(Path.Combine(_root, "missing.fdgreplay")));
    }

    [Test]
    public void RawReply_IsIndentedAndCut()
    {
        var (session, _) = Open(_replayPath);
        ReplayStep step = session.Player.Steps.First(s => s.Decision.Reply != null);

        string raw = ReplayScreen.RawReply(step);

        Assert.That(raw.Split('\n').Length, Is.LessThanOrEqualTo(21));
        Assert.That(raw.All(c => c == '\n' || (c >= ' ' && c <= '~')), Is.True, "ASCII only");
    }
}
