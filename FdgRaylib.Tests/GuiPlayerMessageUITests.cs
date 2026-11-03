using FDG;
using FDG.Players;
using FdgRaylib.Rendering;
using NUnit.Framework;
using System.Linq;

namespace FdgRaylib.Tests;

// The in-game chat UI sink (#077, reworked in #105): received chat lands in the UI's own ChatLog (shown
// in the console's Chat tab, separate from the engine log), tagged with the sender's colour; submitting
// from the input raises the event the engine relay forwards.
[TestFixture]
public class GuiPlayerMessageUITests
{
    [Test]
    public void DisplayPlayerMessage_AppendsSenderTaggedLineToChatLog()
    {
        var ui = new GuiPlayerMessageUI();

        ui.DisplayPlayerMessage("Bob", EChatMessageType.Global, "well played");

        var entries = ui.ChatLog.Snapshot();
        Assert.That(entries.Count, Is.EqualTo(1));
        Assert.That(entries.Last().Message, Is.EqualTo("[Bob] well played"));
    }

    [Test]
    public void DisplayPlayerMessage_ColoursBySenderAndTagsTeamChannel()
    {
        var bob = new TextColor(10, 20, 30, 255);
        var ui = new GuiPlayerMessageUI(name => name == "Bob" ? bob : new TextColor(0, 0, 0, 255));

        ui.DisplayPlayerMessage("Bob", EChatMessageType.Team, "fall back");

        var entry = ui.ChatLog.Snapshot().Last();
        Assert.That(entry.Message, Is.EqualTo("[Team] [Bob] fall back"), "team chat is tagged [Team].");
        Assert.That((entry.Color.R, entry.Color.G, entry.Color.B), Is.EqualTo((bob.R, bob.G, bob.B)),
            "the line takes the sender's colour.");
    }

    [Test]
    public void Submit_RaisesOnMessageSentByPlayer()
    {
        var ui = new GuiPlayerMessageUI();
        (string msg, EChatMessageType type)? captured = null;
        ui.OnMessageSentByPlayer += (m, t) => captured = (m, t);

        ui.Submit("gg", EChatMessageType.Team);

        Assert.That(captured, Is.Not.Null);
        Assert.That(captured!.Value.msg, Is.EqualTo("gg"));
        Assert.That(captured.Value.type, Is.EqualTo(EChatMessageType.Team));
    }

    [Test]
    public void Submit_IgnoresWhitespaceOnly()
    {
        var ui = new GuiPlayerMessageUI();
        int fired = 0;
        ui.OnMessageSentByPlayer += (_, _) => fired++;

        ui.Submit("   ");

        Assert.That(fired, Is.EqualTo(0), "an empty/whitespace line is not sent.");
    }
}
