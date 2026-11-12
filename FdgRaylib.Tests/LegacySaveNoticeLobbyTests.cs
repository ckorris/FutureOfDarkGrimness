using System.Text.Json.Nodes;
using FDG;
using FDG.ArmyBuilding;
using FDG.Data;
using FDG.Network.Connection;
using FDG.Network.Connection.Lobby;
using FDG.Network.Messages;
using FDG.Players;
using FDG.SaveLoad;
using FdgRaylib.Cli;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #444 D2 (option B): a saved game written under an older OPR ruleset - or before saves carried a ruleset stamp at
// all - loads and plays with 3.6 rules, and the resume lobby says so in one amber line beside the #440 army lines.
// The headless --scenario path prints the same line, the way ArmyLoader prints the army notice. Never a blocker.
[TestFixture]
public class LegacySaveNoticeLobbyTests
{
    private const string PredatesLine = "This save predates the OPR 3.6 upgrade; it plays with 3.6 rules";

    private static LobbyPlayerInfoSummary Slot(ArmyListSummary summary) =>
        new("player", summary, ETeamOption.Team1, EPlayerType.Local, new ConnectionID(Guid.NewGuid()),
            new PlayerID(Guid.NewGuid()));

    private static readonly LobbyPlayerInfoSummary[] OneLegacyArmyOneFresh =
    {
        Slot(new ArmyListSummary(true, "Old Guard", "Demo", 1000, GameSystems.GrimdarkFuture, "OPR v3.5.3")),
        Slot(new ArmyListSummary(true, "Fresh", "Demo", 1000, GameSystems.GrimdarkFuture, "OPR v3.6.0")),
    };

    private const string OldGuardLine = "Army 'Old Guard' was built for OPR v3.5.3; it plays with 3.6 rules";

    [Test]
    public void AResumeOfAnUnstampedSave_ShowsTheSaveLineFirst_ThenTheArmyLines()
    {
        Assert.That(LobbyScreen.LegacyNotices(OneLegacyArmyOneFresh, isResume: true, saveRulesVersion: null),
            Is.EqualTo(new[] { PredatesLine, OldGuardLine }));
    }

    [Test]
    public void AResumeOfA35Save_NamesTheRuleset()
    {
        Assert.That(LobbyScreen.LegacySaveNoticeLine(isResume: true, "OPR v3.5.3"),
            Is.EqualTo("This save was written for OPR v3.5.3; it plays with 3.6 rules"));
    }

    [Test]
    public void AResumeOfACurrentSave_ShowsOnlyTheArmyLines()
    {
        Assert.That(LobbyScreen.LegacyNotices(OneLegacyArmyOneFresh, isResume: true, OprRulesVersion.CurrentStamp),
            Is.EqualTo(new[] { OldGuardLine }));
    }

    [Test]
    public void AFreshLobby_NeverShowsASaveLine_WhateverTheStampSays()
    {
        Assert.That(LobbyScreen.LegacySaveNoticeLine(isResume: false, null), Is.Null);
        Assert.That(LobbyScreen.LegacyNotices(OneLegacyArmyOneFresh, isResume: false, "OPR v3.5.3"),
            Is.EqualTo(new[] { OldGuardLine }));
    }

    [Test]
    public void TheHostLobby_CarriesTheStampItWasLoadedWith_AndAClientReportsNone()
    {
        GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
        string json = GameSaveSerializer.Save(store);
        GameDataStore loaded = GameSaveSerializer.Load(json, out string? stamp);

        using var resumed = new LobbyViewModel_Host("Host", "Loaded Game", "", new NullNetworkHost(), loaded, stamp);
        Assert.That(resumed.IsResumeMode, Is.True);
        Assert.That(resumed.SaveRulesVersion, Is.EqualTo(OprRulesVersion.CurrentStamp));
        Assert.That(LobbyScreen.LegacySaveNoticeLine(resumed.IsResumeMode, resumed.SaveRulesVersion), Is.Null);

        using var fresh = new LobbyViewModel_Host("Host", "The Table", "", new NullNetworkHost());
        Assert.That(((ILobbyViewModel)fresh).SaveRulesVersion, Is.Null);
    }

    // -- The headless --scenario path prints the line, like ArmyLoader prints the army one. --------------------

    [Test]
    public void ScenarioLoadStore_PrintsTheLineForAnUnstampedSave_AndNothingForACurrentOne()
    {
        string dir = Path.Combine(Path.GetTempPath(), "fdg-444-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        TextWriter outBefore = Console.Out;
        try
        {
            string json = GameSaveSerializer.Save(GameDataStore.GameDataStoreBuilder.GetDefault());
            string current = Path.Combine(dir, "current.fdgsave");
            string unstamped = Path.Combine(dir, "unstamped.fdgsave");
            File.WriteAllText(current, json);
            JsonNode file = JsonNode.Parse(json)!;
            Assert.That(file.AsObject().Remove("RulesVersion"), Is.True, "a fresh save carries the stamp");
            File.WriteAllText(unstamped, file.ToJsonString());

            var captured = new StringWriter();
            Console.SetOut(captured);

            ScenarioLauncher.LoadStore(current, out string? noticeForCurrent);
            Assert.That(noticeForCurrent, Is.Null);
            Assert.That(captured.ToString(), Is.Empty);

            ScenarioLauncher.LoadStore(unstamped, out string? noticeForUnstamped);
            Assert.That(noticeForUnstamped, Is.EqualTo(PredatesLine));
            Assert.That(captured.ToString().Trim(), Is.EqualTo(PredatesLine));
        }
        finally
        {
            Console.SetOut(outBefore);
            Directory.Delete(dir, recursive: true);
        }
    }

    private sealed class NullNetworkHost : INetworkHost
    {
        public event Action<ConnectionID>? OnNewClientConnected { add { } remove { } }
        public event Action<ConnectionID>? OnClientDisconnected { add { } remove { } }
        public event Action<ArraySegment<byte>, ConnectionID>? OnMessageReceived { add { } remove { } }

        public Task StartAsync() => Task.CompletedTask;
        public Task SendCommandToAllAsync(ArraySegment<byte> data, bool isPooled) => Task.CompletedTask;
        public Task SendCommandToSingleClientAsync(ConnectionID clientId, ArraySegment<byte> data, bool isPooled)
            => Task.CompletedTask;
        public void DisconnectClient(ConnectionID clientId) { }
        public void MarkClientAuthenticated(ConnectionID clientId) { }
        public void Stop() { }
    }
}
