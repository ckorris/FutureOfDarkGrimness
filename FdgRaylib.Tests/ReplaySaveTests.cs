using FDG;
using FDG.Replay;
using FDG.StageResolution;
using FdgRaylib.SaveLoad;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #452: the replay writer every game exit goes through - file names, the no-decision and failure cases, and one
// file per recording however many exit paths hand it over.
[TestFixture]
public class ReplaySaveTests
{
    private static readonly DateTime Stamp = new(2026, 10, 31, 14, 33, 55, DateTimeKind.Utc);

    private string _root = "";

    [SetUp]
    public void SetUp() => _root = Directory.CreateTempSubdirectory(nameof(ReplaySaveTests)).FullName;

    [TearDown]
    public void TearDown()
    {
        try { Directory.Delete(_root, recursive: true); } catch (IOException) { }
    }

    private sealed class FakeReplay : IReplaySource
    {
        public bool IsRecording => true;

        public ReplayHeader? Header { get; set; } = HeaderWith(("Battle Brothers", 0), ("Orks", 1));

        public ReplaySummary Summary { get; set; } = new(Steps: 1, Rounds: 1, Outcome: null, DurationMs: 10, SizeBytes: 100);

        public byte[] Payload { get; set; } = { 1, 2, 3 };

        public int Exports { get; private set; }

        public byte[] ExportGzip()
        {
            Exports++;
            return Payload;
        }
    }

    private static ReplayHeader HeaderWith(params (string Army, int Team)[] slots) => new()
    {
        GameId = Guid.NewGuid(),
        Slots = slots.Select((slot, index) => new ReplaySlot
        {
            SlotId = index,
            PlayerId = new PlayerID(Guid.NewGuid()),
            Name = $"Player {index + 1}",
            ArmyName = slot.Army,
            Team = slot.Team,
        }).ToList(),
    };

    // --- File names -----------------------------------------------------------------------------------------

    [Test]
    public void FileStem_OneVersusOne_IsTheStampThenBothArmies()
    {
        Assert.That(ReplaySave.FileStem(HeaderWith(("Battle Brothers", 0), ("Orks", 1)), Stamp),
            Is.EqualTo("replay-20261031-143355-Battle-Brothers-vs-Orks"));
    }

    [TestCase("Player 1's Test Army", "Player-1-s-Test-Army")]
    [TestCase("  Orks!! ", "Orks")]
    [TestCase("Dark-Elf Raiders", "Dark-Elf-Raiders")]
    [TestCase("\u00c9lite Garde", "lite-Garde")]
    [TestCase("", "army")]
    [TestCase("!!!", "army")]
    public void FileNamePart_KeepsLettersDigitsAndHyphens_AndCollapsesEverythingElse(string army, string expected)
    {
        Assert.That(ReplaySave.FileNamePart(army), Is.EqualTo(expected));
    }

    [Test]
    public void FileNamePart_TruncatesTo24_WithoutATrailingHyphen()
    {
        Assert.That(ReplaySave.FileNamePart("Target Dummies (signal army)"), Is.EqualTo("Target-Dummies-signal-ar"));
        // Character 24 is the space before "xyz": cut there, the hyphen it became goes too.
        Assert.That(ReplaySave.FileNamePart("abcdefghijklmnopqrstuvw xyz"), Is.EqualTo("abcdefghijklmnopqrstuvw"));
    }

    [Test]
    public void FileStem_MoreThanTwoSlots_NamesTheFirstArmyOnAnotherTeam_AndCountsThePlayers()
    {
        // 2v2: slots 0 and 1 are team-mates, so the opponent named is slot 2.
        Assert.That(ReplaySave.FileStem(HeaderWith(("A", 0), ("B", 0), ("C", 1), ("D", 1)), Stamp),
            Is.EqualTo("replay-20261031-143355-A-vs-C-4p"));
        // Free-for-all: everyone on their own team.
        Assert.That(ReplaySave.FileStem(HeaderWith(("A", 0), ("B", 1), ("C", 2)), Stamp),
            Is.EqualTo("replay-20261031-143355-A-vs-B-3p"));
    }

    // --- Writing --------------------------------------------------------------------------------------------

    [Test]
    public void TryWrite_NoRecording_ReturnsNull()
    {
        Assert.That(ReplaySave.TryWrite(null, Stamp, _root), Is.Null);
    }

    [Test]
    public void TryWrite_BeforeTheFirstDecision_WritesNothing()
    {
        var replay = new FakeReplay { Summary = new ReplaySummary(0, 0, null, 0, 0) };

        Assert.That(ReplaySave.TryWrite(replay, Stamp, _root), Is.Null);
        Assert.That(Directory.GetFiles(_root), Is.Empty);
        Assert.That(replay.Exports, Is.Zero);
    }

    [Test]
    public void TryWrite_ADirectoryThatCannotBeCreated_ReturnsNull_WithoutThrowing()
    {
        string notADirectory = Path.Combine(_root, "a-file");
        File.WriteAllText(notADirectory, "");

        string? path = null;
        Assert.DoesNotThrow(() => path = ReplaySave.TryWrite(new FakeReplay(), Stamp, notADirectory));
        Assert.That(path, Is.Null);
    }

    [Test]
    public void TryWrite_TheSameRecordingAgain_RewritesItsOneFile_WithTheLaterExport()
    {
        var replay = new FakeReplay();
        string? first = ReplaySave.TryWrite(replay, Stamp, _root);

        // The game went on (the GameEnded marker, say) and a later exit path hands it over again.
        replay.Payload = new byte[] { 9, 8, 7, 6 };
        replay.Summary = replay.Summary with { Outcome = "Win", SizeBytes = 200 };
        string? second = ReplaySave.TryWrite(replay, Stamp.AddMinutes(5), _root);

        Assert.That(first, Is.Not.Null);
        Assert.That(second, Is.EqualTo(first), "a rewrite keeps the first write's name");
        Assert.That(Directory.GetFiles(_root), Is.EqualTo(new[] { first }), "one file, no leftover temporary");
        Assert.That(File.ReadAllBytes(first!), Is.EqualTo(replay.Payload));
    }

    [Test]
    public void TryWrite_NothingRecordedSince_DoesNotExportAgain()
    {
        var replay = new FakeReplay();
        string? first = ReplaySave.TryWrite(replay, Stamp, _root);
        string? second = ReplaySave.TryWrite(replay, Stamp.AddMinutes(5), _root);

        Assert.That(second, Is.EqualTo(first));
        Assert.That(replay.Exports, Is.EqualTo(1));
    }

    [Test]
    public void TryWrite_TwoGamesInOneSecond_GetTwoFiles_NewestListedFirst()
    {
        string? older = ReplaySave.TryWrite(new FakeReplay(), Stamp, _root);
        string? newer = ReplaySave.TryWrite(new FakeReplay(), Stamp, _root);

        Assert.That(Path.GetFileName(older), Is.EqualTo("replay-20261031-143355-Battle-Brothers-vs-Orks.fdgreplay"));
        Assert.That(Path.GetFileName(newer), Is.EqualTo("replay-20261031-143355-Battle-Brothers-vs-Orks_2.fdgreplay"));
        Assert.That(ReplaySave.List(_root), Is.EqualTo(new[] { newer, older }));
    }

    [Test]
    public void TryWrite_ARealRecording_ReadsBackAsTheSameGame()
    {
        ReplayHeader header = HeaderWith(("Battle Brothers", 0), ("Orks", 1));
        using var recorder = new ReplayRecorder();
        recorder.Start(header);
        var task = new TaskID(Guid.NewGuid());
        recorder.OnRequested(task, header.Slots[0].PlayerId, "Request", "Reply", "Pick a unit", "{}");
        recorder.OnReplied(task, "{}");

        string? path = ReplaySave.TryWrite(recorder, Stamp, _root);

        Assert.That(path, Is.Not.Null);
        using FileStream file = File.OpenRead(path!);
        ReplayDocument document = ReplayReader.ReadGzip(file);
        Assert.That(document.Header.GameId, Is.EqualTo(header.GameId));
        Assert.That(document.Events.OfType<RequestEvent>().Single().TaskId, Is.EqualTo(task));
        Assert.That(document.Events.OfType<ReplyEvent>().Single().TaskId, Is.EqualTo(task));
    }
}
