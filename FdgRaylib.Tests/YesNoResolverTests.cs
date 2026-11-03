using FDG;
using FDG.StageResolution.Requests;
using FdgRaylib.Cli.Resolvers;
using NUnit.Framework;
using System;
using System.IO;
using System.Threading.Tasks;

namespace FdgRaylib.Tests;

// The CLI YesNo resolver falls back to the request's declared DefaultAnswer on EOF (piped/automated input)
// instead of a hardcoded "yes", so a question that should decline by default does (#082 follow-up — the
// DefaultAnswer rename made the per-question default reusable by this no-live-player path).
[TestFixture]
public class YesNoResolverTests
{
    private TextReader _originalIn = null!;
    private TextWriter _originalOut = null!;

    [SetUp]
    public void SetUp()
    {
        _originalIn = Console.In;
        _originalOut = Console.Out;
        Console.SetOut(new StringWriter()); // swallow the resolver's prompt text
    }

    [TearDown]
    public void TearDown()
    {
        Console.SetIn(_originalIn);
        Console.SetOut(_originalOut);
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task Resolve_AtEof_ReturnsDeclaredDefault(bool defaultAnswer)
    {
        Console.SetIn(new StringReader("")); // immediate EOF — Console.ReadLine() returns null
        var request = new YesNoRequest(new PlayerID(Guid.NewGuid()), "Proceed?", defaultAnswer: defaultAnswer);

        bool answer = await new YesNoResolver().Resolve(request);

        Assert.That(answer, Is.EqualTo(defaultAnswer),
            "with no input, the resolver uses the request's per-question default rather than a blanket yes.");
    }

    // Explicit input still wins over the default (default is set opposite to prove the input is what's used).
    [TestCase("y\n", true)]
    [TestCase("n\n", false)]
    public async Task Resolve_ExplicitInput_OverridesDefault(string input, bool expected)
    {
        Console.SetIn(new StringReader(input));
        var request = new YesNoRequest(new PlayerID(Guid.NewGuid()), "Proceed?", defaultAnswer: !expected);

        bool answer = await new YesNoResolver().Resolve(request);

        Assert.That(answer, Is.EqualTo(expected));
    }
}
