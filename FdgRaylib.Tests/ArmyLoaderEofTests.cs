using FdgRaylib.Cli;
using FDG.SaveLoad;
using NUnit.Framework;
using System;
using System.IO;
using System.Text;

namespace FdgRaylib.Tests;

// #305: ArmyLoader's path prompt used to fold EOF into "empty line" (IsNullOrEmpty + continue), so a
// piped headless run whose army failed to load spun on a ReadLine() that could never block again —
// printing "No path entered." as fast as the process could write (5.8 GB of log in one #197 probe).
// EOF is now terminal and PromptForArmy supplies the built-in test army, matching every CLI resolver.
//
// The prompt loop is bounded by a writer that throws once the output is clearly runaway, so a
// regression fails the test in milliseconds instead of hanging (or OOM-ing) the suite.
[TestFixture]
public class ArmyLoaderEofTests
{
    private const int MaxWrites = 500;

    private TextReader _originalIn = null!;
    private TextWriter _originalOut = null!;

    [SetUp]
    public void SetUp()
    {
        _originalIn = Console.In;
        _originalOut = Console.Out;
        Console.SetOut(new BoundedWriter(MaxWrites));
    }

    [TearDown]
    public void TearDown()
    {
        Console.SetIn(_originalIn);
        Console.SetOut(_originalOut);
    }

    // "1" (load from file), then the path prompt hits EOF straight away.
    [Test]
    public void PromptForArmy_EofAtPathPrompt_FallsBackToTestArmy()
    {
        Console.SetIn(new StringReader("1\n"));

        ArmyListFile army = ArmyLoader.PromptForArmy("Player 1");

        Assert.That(army.Name, Is.EqualTo("Player 1's Test Army"),
            "EOF at the path prompt must fall back to the built-in army, not retry forever.");
    }

    // The bug's actual trigger: a path that fails to load, THEN EOF. The failure itself is fine — it is
    // the retry into a ReadLine() that can no longer block that used to become an unbounded loop.
    [Test]
    public void PromptForArmy_FailedLoadThenEof_FallsBackToTestArmy()
    {
        string missing = Path.Combine(Path.GetTempPath(), "fdg-305-does-not-exist.fdgarmy");
        Console.SetIn(new StringReader($"1\n{missing}\n"));

        ArmyListFile army = ArmyLoader.PromptForArmy("Player 2");

        Assert.That(army.Name, Is.EqualTo("Player 2's Test Army"));
    }

    // An empty line is still a real retry — the fix splits EOF out of IsNullOrEmpty, it doesn't make
    // pressing Enter terminal.
    [Test]
    public void PromptForArmy_EmptyLineThenPath_RetriesAndLoads()
    {
        string path = Path.Combine(Path.GetTempPath(), $"fdg-305-{Guid.NewGuid():N}.fdgarmy");
        // camelCase keys - RuleJson.Options sets JsonNamingPolicy.CamelCase for reads as well as writes.
        File.WriteAllText(path, "{\"name\":\"Piped Army\",\"faction\":\"Test\",\"pointsLimit\":500,\"units\":[]}");

        try
        {
            Console.SetIn(new StringReader($"1\n\n{path}\n"));

            ArmyListFile army = ArmyLoader.PromptForArmy("Player 3");

            Assert.That(army.Name, Is.EqualTo("Piped Army"),
                "a blank line must re-prompt, then the real path must load.");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Swallows the prompt text like the other CLI resolver tests, but throws once the writes pass a
    /// bound no bounded run reaches — turning "loops forever" into a fast, legible failure.
    /// </summary>
    private sealed class BoundedWriter : TextWriter
    {
        private readonly int _maxWrites;
        private int _writes;

        public BoundedWriter(int maxWrites) => _maxWrites = maxWrites;

        public override Encoding Encoding => Encoding.UTF8;

        public override void Write(char value) => Count();

        public override void Write(string? value) => Count();

        public override void WriteLine(string? value) => Count();

        private void Count()
        {
            if (++_writes > _maxWrites)
            {
                throw new InvalidOperationException(
                    $"ArmyLoader wrote more than {_maxWrites} console lines - the prompt loop is not terminating (#305).");
            }
        }
    }
}
