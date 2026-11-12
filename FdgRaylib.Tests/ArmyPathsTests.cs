using System.IO;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// Default folder for the .fdgarmy Save/Load dialogs: the "armies" folder beside the executable (shipped
// layout) or under the working directory (dotnet run from the repo root), whichever exists first.
[TestFixture]
public class ArmyPathsTests
{
    private string _root = null!;

    [SetUp]
    public void SetUp() =>
        _root = Directory.CreateTempSubdirectory(nameof(ArmyPathsTests)).FullName;

    [TearDown]
    public void TearDown() => Directory.Delete(_root, recursive: true);

    [Test]
    public void FindIn_NullWhenNoCandidateHasAnArmiesFolder()
    {
        string a = Directory.CreateDirectory(Path.Combine(_root, "a")).FullName;
        string b = Directory.CreateDirectory(Path.Combine(_root, "b")).FullName;
        Assert.That(ArmyPaths.FindIn(a, b), Is.Null);
    }

    [Test]
    public void FindIn_TakesTheFirstRootThatHasOne()
    {
        string a = Directory.CreateDirectory(Path.Combine(_root, "a")).FullName;
        string b = Directory.CreateDirectory(Path.Combine(_root, "b")).FullName;
        Directory.CreateDirectory(Path.Combine(a, ArmyPaths.DirectoryName));
        Directory.CreateDirectory(Path.Combine(b, ArmyPaths.DirectoryName));

        Assert.That(ArmyPaths.FindIn(a, b), Is.EqualTo(Path.Combine(a, ArmyPaths.DirectoryName)));
    }

    [Test]
    public void FindIn_FallsThroughToALaterRoot()
    {
        string a = Directory.CreateDirectory(Path.Combine(_root, "a")).FullName;
        string b = Directory.CreateDirectory(Path.Combine(_root, "b")).FullName;
        Directory.CreateDirectory(Path.Combine(b, ArmyPaths.DirectoryName));

        Assert.That(ArmyPaths.FindIn(a, b), Is.EqualTo(Path.Combine(b, ArmyPaths.DirectoryName)));
    }

    // tinyfiledialogs reads a trailing separator as "open this directory, no preset file name" - without it
    // the Save dialog would pre-fill the file name box with "armies".
    [Test]
    public void DefaultDialogPath_EndsWithASeparatorOrIsEmpty()
    {
        string path = ArmyPaths.DefaultDialogPath;
        if (path.Length == 0) Assert.Pass("No armies folder in this test environment.");
        Assert.That(path, Does.EndWith(Path.DirectorySeparatorChar.ToString()));
        Assert.That(Directory.Exists(path), Is.True);
    }
}
