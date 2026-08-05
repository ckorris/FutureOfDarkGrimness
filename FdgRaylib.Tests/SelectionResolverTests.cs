using FDG;
using FDG.Data;
using FDG.StageResolution.Requests;
using FdgRaylib.Cli.Resolvers;
using NUnit.Framework;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace FdgRaylib.Tests;

// #333 — a playtester reported having to press Back to deploy a unit next to a transport. The GUI at least
// HAD a Back button to stumble onto; the CLI resolver had no cancel path whatsoever, so with a transport
// eligible "deploy normally" could not be typed at all. Both front ends now take the wording from the
// request (SelectionRequest.CancelLabel), which is what these pin: the option is listed, it is named, and
// it replies null the way the GUI button does.
[TestFixture]
public class SelectionResolverTests
{
    private TextReader _originalIn = null!;
    private TextWriter _originalOut = null!;
    private StringWriter _out = null!;

    private GameDataStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _originalIn = Console.In;
        _originalOut = Console.Out;
        _out = new StringWriter();
        Console.SetOut(_out);
        _store = GameDataStore.GameDataStoreBuilder.GetDefault();
    }

    [TearDown]
    public void TearDown()
    {
        Console.SetIn(_originalIn);
        Console.SetOut(_originalOut);
    }

    [Test]
    public async Task Resolve_CancellableSelection_ListsTheNamedExitAndRepliesNullForIt()
    {
        Console.SetIn(new StringReader("0\n"));
        SelectionRequest<RectangularZone> request = Request(allowCancel: true, cancelLabel: "Deploy Normally");

        DataBinding<RectangularZone> answer = await new SelectionResolver<RectangularZone>().Resolve(request);

        Assert.That(answer, Is.Null, "the exit replies null, exactly like the GUI's button.");
        Assert.That(_out.ToString(), Does.Contain("[0] Deploy Normally"),
            "the exit is a listed, named option - not an undocumented keyword.");
    }

    // The default wording is unchanged for every stage that doesn't name its exit.
    [Test]
    public async Task Resolve_CancellableSelectionWithoutALabel_OffersBack()
    {
        Console.SetIn(new StringReader("0\n"));
        SelectionRequest<RectangularZone> request = Request(allowCancel: true, cancelLabel: null);

        DataBinding<RectangularZone> answer = await new SelectionResolver<RectangularZone>().Resolve(request);

        Assert.That(answer, Is.Null);
        Assert.That(_out.ToString(), Does.Contain("[0] Back"));
    }

    // A mandatory selection must not gain an exit: replying null to one of those is what crashes the
    // networked reply path (the reason AllowCancel exists at all).
    [Test]
    public async Task Resolve_MandatorySelection_HasNoZeroOptionAndRejectsIt()
    {
        Console.SetIn(new StringReader("0\n2\n"));
        SelectionRequest<RectangularZone> request = Request(allowCancel: false, cancelLabel: null);

        DataBinding<RectangularZone> answer = await new SelectionResolver<RectangularZone>().Resolve(request);

        Assert.That(answer, Is.EqualTo(request.ValidOptions[1].Option), "0 is rejected; the retyped 2 wins.");
        Assert.That(_out.ToString(), Does.Not.Contain("[0]"));
        Assert.That(_out.ToString(), Does.Contain("between 1 and 2"));
    }

    // EOF (piped play) still auto-picks the first option rather than cancelling: automated runs have to make
    // forward progress, and a stage that re-prompts after a cancel would spin forever.
    [Test]
    public async Task Resolve_AtEof_StillTakesTheFirstOptionEvenWhenCancellable()
    {
        Console.SetIn(new StringReader(""));
        SelectionRequest<RectangularZone> request = Request(allowCancel: true, cancelLabel: "Deploy Normally");

        DataBinding<RectangularZone> answer = await new SelectionResolver<RectangularZone>().Resolve(request);

        Assert.That(answer, Is.EqualTo(request.ValidOptions[0].Option));
    }

    private SelectionRequest<RectangularZone> Request(bool allowCancel, string? cancelLabel)
    {
        var options = new List<SelectionRequest<RectangularZone>.ValidOption>
        {
            new(Zone(), "First"),
            new(Zone(), "Second"),
        };

        return new SelectionRequest<RectangularZone>(new PlayerID(Guid.NewGuid()), "Pick one.",
            options, new List<SelectionRequest<RectangularZone>.InvalidOption>(),
            allowCancel: allowCancel, displayName: null, cancelLabel: cancelLabel);
    }

    private DataBinding<RectangularZone> Zone() =>
        _store.GetDataBinding<RectangularZone>(_store.Create(new RectangularZone(0f, 10f, 0f, 10f)));
}
