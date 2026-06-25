using FDG;
using FDG.Players;
using FdgRaylib.Rendering;
using NUnit.Framework;
using System.Linq;

namespace FdgRaylib.Tests;

// The in-game chat UI sink: received chat lands in the shared GameLog (side panel), and submitting from
// the bottom input box raises the event the engine relay forwards (#077 in-game chat).
[TestFixture]
public class GuiPlayerMessageUITests
{
    [Test]
    public void DisplayPlayerMessage_AppendsSenderTaggedLineToLog()
    {
        var log = new GameLog();
        var ui = new GuiPlayerMessageUI(log);

        ui.DisplayPlayerMessage("Bob", EChatMessageType.Global, "well played");

        var entries = log.Snapshot();
        Assert.That(entries.Count, Is.EqualTo(1));
        Assert.That(entries.Last().Message, Is.EqualTo("[Bob] well played"));
    }

    [Test]
    public void Submit_RaisesOnMessageSentByPlayer()
    {
        var ui = new GuiPlayerMessageUI(new GameLog());
        (string msg, EChatMessageType type)? captured = null;
        ui.OnMessageSentByPlayer += (m, t) => captured = (m, t);

        ui.Submit("gg", EChatMessageType.Global);

        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.Value.msg, Is.EqualTo("gg"));
        Assert.That(captured.Value.type, Is.EqualTo(EChatMessageType.Global));
    }

    [Test]
    public void Submit_IgnoresWhitespaceOnly()
    {
        var ui = new GuiPlayerMessageUI(new GameLog());
        int fired = 0;
        ui.OnMessageSentByPlayer += (_, _) => fired++;

        ui.Submit("   ");

        Assert.That(fired, Is.EqualTo(0), "an empty/whitespace line is not sent.");
    }
}
