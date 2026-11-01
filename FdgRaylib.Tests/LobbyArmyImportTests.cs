using System;
using System.Collections.Generic;
using System.IO;
using FDG.ArmyBuilding;
using FDG.SaveLoad;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #460: the lobby Import popup's ImGui-free half - the saved copy's name (the shipped armies' "2k - Faction
// - Name" pattern), which folder it lands in, that it never overwrites, and that its wording is born ASCII.
[TestFixture]
public class LobbyArmyImportTests
{
    private static ArmyListFile Army(string name, string faction, int limit, int points = 1985) => new()
    {
        Name = name,
        Faction = faction,
        PointsLimit = limit,
        Units = { new UnitFileEntry { Name = "U", PointCost = points } },
    };

    [Test]
    public void ARoundThousandTargetReadsLikeTheShippedArmies()
    {
        Assert.That(LobbyArmyImport.SaveFileName(Army("Horde Mixed", "Orks", 2000)),
            Is.EqualTo("2k - Orks - Horde Mixed.fdgarmy"));
    }

    [Test]
    public void AnOddTargetIsWrittenOut_AndNoTargetFallsBackToTheActualTotal()
    {
        Assert.That(LobbyArmyImport.SaveFileName(Army("Raid", "Orks", 1500)), Is.EqualTo("1500 - Orks - Raid.fdgarmy"));
        Assert.That(LobbyArmyImport.SaveFileName(Army("Raid", "Orks", 0)), Is.EqualTo("1985 - Orks - Raid.fdgarmy"));
    }

    // "1k - Machine Cults" rather than "1k - Machine Cults - Machine Cults".
    [Test]
    public void TheFactionIsNotRepeatedWhenTheNameAlreadySaysIt()
    {
        Assert.That(LobbyArmyImport.SaveFileName(Army("Machine Cults", "Machine Cults", 1000)),
            Is.EqualTo("1k - Machine Cults.fdgarmy"));
        Assert.That(LobbyArmyImport.SaveFileName(Army("My machine cults list", "Machine Cults", 1000)),
            Is.EqualTo("1k - My machine cults list.fdgarmy"));
    }

    // The importer names an unnamed Army Forge list "Imported Army"; the faction says more.
    [Test]
    public void AnUnnamedListIsNamedForItsFaction()
    {
        Assert.That(LobbyArmyImport.SaveFileName(Army("Imported Army", "Orks", 2000)), Is.EqualTo("2k - Orks.fdgarmy"));
        Assert.That(LobbyArmyImport.SaveFileName(Army("Imported Army", "", 2000)), Is.EqualTo("2k - Army.fdgarmy"));
    }

    // Saved on Linux, opened on Windows: every character Windows refuses goes, and so do trailing dots.
    [Test]
    public void CharactersNoPlatformAcceptsAreScrubbed()
    {
        string name = LobbyArmyImport.SaveFileName(Army("Kill/Team: \"Alpha\"?*  ...", "Orks", 1000));

        Assert.That(name, Is.EqualTo("1k - Orks - Kill Team Alpha.fdgarmy"));
        Assert.That(name.IndexOfAny("<>:\"/\\|?*".ToCharArray()), Is.EqualTo(-1));
    }

    [Test]
    public void TheCopyGoesInTheSystemsSubfolderWhenTheArmiesFolderHasOne()
    {
        string root = Path.Combine("x", "armies");
        var existing = new HashSet<string> { Path.Combine(root, "GDF"), Path.Combine(root, "AoF") };

        Assert.That(LobbyArmyImport.SaveFolder(root, GameSystems.AgeOfFantasy, existing.Contains),
            Is.EqualTo(Path.Combine(root, "AoF")));
        Assert.That(LobbyArmyImport.SaveFolder(root, null, existing.Contains), Is.EqualTo(Path.Combine(root, "GDF")),
            "absent system means Grimdark Future (#378)");
        Assert.That(LobbyArmyImport.SaveFolder(root, GameSystems.AgeOfFantasy, _ => false), Is.EqualTo(root),
            "no subfolder: the armies folder itself");
        Assert.That(LobbyArmyImport.SaveFolder(root, "homebrew", existing.Contains), Is.EqualTo(root));
        Assert.That(LobbyArmyImport.SaveFolder(null, null, existing.Contains), Is.Null,
            "no armies folder at all: no copy, rather than an invented location");
    }

    [Test]
    public void ACopyNeverOverwritesAnExistingArmy()
    {
        var taken = new HashSet<string> { Path.Combine("d", "2k - Orks.fdgarmy"), Path.Combine("d", "2k - Orks (2).fdgarmy") };

        Assert.That(LobbyArmyImport.UniquePath("d", "2k - Orks.fdgarmy", taken.Contains),
            Is.EqualTo(Path.Combine("d", "2k - Orks (3).fdgarmy")));
        Assert.That(LobbyArmyImport.UniquePath("d", "1k - Orks.fdgarmy", taken.Contains),
            Is.EqualTo(Path.Combine("d", "1k - Orks.fdgarmy")));
    }

    // The gate's lines follow a player name ("Alice: army is ..."); standing alone they get a capital.
    [Test]
    public void SettingsProblemsReadAsSentences()
    {
        IReadOnlyList<string> problems = LaunchGate.SettingsProblems(Army("A", "Orks", 0, points: 2500), 2000,
            EAllowedGameSystems.All);

        Assert.That(LobbyArmyImport.ProblemLine(problems[0]), Does.StartWith("Army is 2500 pts"));
    }

    [Test]
    public void ThePopupsWordingIsAscii()
    {
        foreach (string text in new[]
                 {
                     LobbyArmyImport.Instructions,
                     LobbyArmyImport.NotALinkHint,
                     LobbyArmyImport.SettingsConfirmLead,
                     LobbyArmyImport.SettingsConfirmBody,
                     LobbyScreen.ImportTooltip,
                 })
        {
            foreach (char c in text)
                Assert.That(c, Is.LessThanOrEqualTo((char)0x7F), $"non-ASCII in \"{text}\"");
        }
    }
}
