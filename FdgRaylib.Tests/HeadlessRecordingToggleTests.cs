using FDG.Data;
using FdgRaylib.Cli;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #457: --no-replay runs a headless game unrecorded. The same AI-vs-AI shootout through CliApp's scenario path, once
// recorded (the default) and once not: the game plays to its end either way, and only the default leaves a recording
// for Program.cs to write.
[TestFixture]
public class HeadlessRecordingToggleTests
{
    private static GameDataStore Shootout() =>
        ScenarioLauncher.LoadStore(ReplayToolTests.FindRepoFile("Scenarios/example-shootout.json"));

    [Test]
    public async Task ByDefault_AHeadlessScenarioIsRecorded()
    {
        var app = new CliApp(headless: true, diceSeed: 457);

        await app.RunScenarioAsync(Shootout(), allAi: true);

        Assert.That(app.Replay, Is.Not.Null);
        Assert.That(app.Replay!.Summary.Steps, Is.GreaterThan(0));
        Assert.That(app.Replay.Summary.Outcome, Is.Not.Null, "the recording ran to the game's end");
    }

    [Test]
    public async Task NoReplay_PlaysTheGame_WithoutARecorder()
    {
        var app = new CliApp(headless: true, diceSeed: 457, record: false);

        await app.RunScenarioAsync(Shootout(), allAi: true);

        Assert.That(app.Replay, Is.Null, "nothing for Program.cs to write");
    }
}
