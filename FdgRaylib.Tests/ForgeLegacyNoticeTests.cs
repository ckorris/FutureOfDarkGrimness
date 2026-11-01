using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Rules.Serialization;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #441 F2 (signed off B, 2026-10-31): the Army Forge reopens a 3.5 list against its own embedded 3.5 book; the status
// line now carries the #440 legacy line (the lobby's and the CLI's) in amber on open. A 3.6 list says only that it
// loaded. Only on open - the "Rebuild against the 3.6 book" button (fork option C) waits on #440 D1-D4.
[TestFixture]
public class ForgeLegacyNoticeTests
{
    private static string FixturePath(string file) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "legacy-3.5", file);

    private static BuiltArmyFile LoadFixture(string file) =>
        JsonSerializer.Deserialize<BuiltArmyFile>(File.ReadAllText(FixturePath(file)), RuleJson.Options)!;

    [Test]
    public void OpeningA35List_SaysItLoaded_AndThatItPlaysWith36Rules_InAmber()
    {
        BuiltArmyFile legacy = LoadFixture("1k - Ogres.fdgarmy");
        Assume.That(LegacyArmyNotice.IsLegacy(LegacyArmyNotice.RulesVersionOf(legacy)), Is.True, "the fixture is a 3.5 list");

        (EForgeStatusKind kind, string text) = ArmyForgeScreen.LoadedStatus(legacy, "1k - Ogres.fdgarmy");

        Assert.That(kind, Is.EqualTo(EForgeStatusKind.Notice));
        Assert.That(text, Is.EqualTo($"Loaded 1k - Ogres.fdgarmy - Army '{legacy.Name}' was built for " +
                                     $"{legacy.Book!.Version}; it plays with 3.6 rules"));
        Assert.That(text.All(c => c < 128), Is.True, "game text is ASCII-only");
    }

    [Test]
    public void OpeningA36List_SaysOnlyThatItLoaded()
    {
        var screen = new ArmyForgeScreen(DemoBook.Build());
        screen.AddToList("warriors");
        BuiltArmyFile current = screen.Compile();
        Assume.That(LegacyArmyNotice.Line(current), Is.Null, "a list built on a 3.6 book carries no legacy line");

        Assert.That(ArmyForgeScreen.LoadedStatus(current, "Warband.fdgarmy"),
            Is.EqualTo((EForgeStatusKind.Success, "Loaded Warband.fdgarmy")));
    }

    [Test]
    public void TheScreenShowsTheNoticeAfterAdoptingA35List()
    {
        var screen = new ArmyForgeScreen(DemoBook.Build());
        BuiltArmyFile legacy = LoadFixture("1k - Ogres.fdgarmy");

        ELoadOutcome outcome = screen.TryAdopt(legacy, "1k - Ogres.fdgarmy", null);

        Assert.That(outcome, Is.EqualTo(ELoadOutcome.Adopted), "a Forge-authored 3.5 file measures no drift");
        Assert.That(screen.Status.Kind, Is.EqualTo(EForgeStatusKind.Notice));
        Assert.That(screen.Status.Text, Does.StartWith("Loaded 1k - Ogres.fdgarmy - Army '").And.EndWith("it plays with 3.6 rules"));
    }
}
