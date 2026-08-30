using FDG.Players;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #388 - who gets a starter army rolled onto them when a lobby opens. #372 seeded bots only; the same
// pass now covers every slot this machine owns, which is the host's own row and its local humans, and
// a client's own row on the client's machine.
[TestFixture]
public class LobbyStarterArmyTests
{
    // A slot nobody has served yet, that this machine may write.
    private static bool Needs(EPlayerType type, bool armyAssigned = false, bool canModify = true,
        bool alreadyServed = false) =>
        LobbyScreen.NeedsStarterArmy(type, armyAssigned, canModify, alreadyServed);

    [Test]
    public void EmptyHumanSlot_GetsOne()
    {
        Assert.That(Needs(EPlayerType.Local), Is.True,
            "the host's own row and every added local player start with no army at all");
    }

    [Test]
    public void FreshBot_GetsOne_EvenThoughItsStubReadsAsAssigned()
    {
        Assert.That(Needs(EPlayerType.AI, armyAssigned: true), Is.True,
            "AddAiPlayer stamps every bot with the 100-pt Test Army stub, so a bot row is never unassigned");
    }

    [Test]
    public void HumanWhoAlreadyHasAnArmy_IsLeftAlone()
    {
        Assert.That(Needs(EPlayerType.Local, armyAssigned: true), Is.False,
            "an army loaded before the folder scan landed, or one that rode in with the slot, is a choice");
    }

    [Test]
    public void SlotThisMachineDoesNotOwn_IsNeverRolledFor()
    {
        // The host sees a connected client as Network and may not write its army; a client sees every
        // other row (bots included) the same way. Both cases arrive here as canModify: false.
        Assert.That(Needs(EPlayerType.Network, canModify: false), Is.False);
        Assert.That(Needs(EPlayerType.AI, armyAssigned: true, canModify: false), Is.False,
            "a client must not roll for the host's bots");
    }

    [Test]
    public void AlreadyServedSlot_IsNotRolledAgain()
    {
        // Both a hand-picked Load Army and a Random Army press mark the slot, so neither is overwritten
        // on the next frame - and a client's own roll stays put while it is in flight to the host.
        Assert.That(Needs(EPlayerType.Local, alreadyServed: true), Is.False);
        Assert.That(Needs(EPlayerType.AI, armyAssigned: true, alreadyServed: true), Is.False);
    }
}
