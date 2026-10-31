using System;
using System.Collections.Generic;
using FDG.ArmyBuilding;
using FDG.Players;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #402: the lobby roster's Faction-cell rule (which army is from a system this lobby won't take) and the
// wording of the three blocked-launch explanations.
[TestFixture]
public class LobbyArmySourceTests
{
    private static ArmyListSummary Army(string? system) =>
        new(IsAssigned: true, ArmyName: "A", FactionName: "F", PointCost: 1000, GameSystem: system);

    private static readonly ArmyListSummary Unassigned = new(false, "N/A", "N/A", 0);

    [Test]
    public void AllTakesEverySystem()
    {
        foreach (string? system in new[] { null, GameSystems.GrimdarkFuture, GameSystems.AgeOfFantasy, "homebrew" })
            Assert.That(LobbyArmySource.IsWrongSystem(Army(system), EAllowedGameSystems.All), Is.False,
                $"All should accept '{system ?? "(absent)"}'");
    }

    [Test]
    public void AofArmyIsWrongInAGdfLobby_AndViceVersa()
    {
        Assert.That(LobbyArmySource.IsWrongSystem(Army(GameSystems.AgeOfFantasy), EAllowedGameSystems.GrimdarkFuture), Is.True);
        Assert.That(LobbyArmySource.IsWrongSystem(Army(GameSystems.GrimdarkFuture), EAllowedGameSystems.AgeOfFantasy), Is.True);
    }

    // Absent-means-GDF (#378), so every pre-#378 army list passes a GDF-only lobby untouched.
    [Test]
    public void ArmyWithNoRecordedSystemPassesAGdfLobby()
    {
        Assert.That(LobbyArmySource.IsWrongSystem(Army(null), EAllowedGameSystems.GrimdarkFuture), Is.False);
        Assert.That(LobbyArmySource.IsWrongSystem(Army(null), EAllowedGameSystems.AgeOfFantasy), Is.True);
    }

    // An empty slot has no system to be wrong about; its own blocker is called out on the Army cell.
    [Test]
    public void UnassignedSlotIsNeverWrongSystem()
    {
        Assert.That(LobbyArmySource.IsWrongSystem(Unassigned, EAllowedGameSystems.AgeOfFantasy), Is.False);
        Assert.That(LobbyArmySource.IsWrongSystem(Unassigned, EAllowedGameSystems.GrimdarkFuture), Is.False);
    }

    // ── An empty Army cell ──────────────────────────────────────────────────────────────────

    [Test]
    public void AnEmptySlotIsAProblemInAFreshLobby()
    {
        Assert.That(LobbyArmySource.IsMissingArmy(Unassigned, isResumeLobby: false), Is.True);
        Assert.That(LobbyArmySource.IsMissingArmy(Army(null), isResumeLobby: false), Is.False);
    }

    // A resume lobby's slots ALL report no army - the real ones are in the save, and the roster's copy
    // is vestigial - so flagging them would invent a fault and tell the player to load what they have.
    [Test]
    public void AResumeLobbyNeverFlagsAnEmptySlot()
    {
        Assert.That(LobbyArmySource.IsMissingArmy(Unassigned, isResumeLobby: true), Is.False);
    }

    [Test]
    public void WrongSystemTooltipNamesBothSides()
    {
        string tip = LobbyArmySource.WrongSystemTooltip(Army(GameSystems.AgeOfFantasy),
            EAllowedGameSystems.GrimdarkFuture);

        Assert.That(tip, Does.Contain("Age of Fantasy").And.Contain("Grimdark Future"));
    }

    // Game text is ASCII-only: the ImGui atlas bakes Basic Latin + Latin-1 and renders anything else as '?'.
    [Test]
    public void EveryUserFacingStringIsAscii()
    {
        var strings = new List<string> { LobbyArmySource.NoArmyTooltip };
        foreach (EAllowedGameSystems allowed in Enum.GetValues<EAllowedGameSystems>())
        {
            strings.Add(GameSystems.DisplayName(allowed));
            strings.Add(LobbyArmySource.WrongSystemTooltip(Army(GameSystems.AgeOfFantasy), allowed));
        }
        strings.Add(LobbyPointsStatus.Tooltip(ELobbyPointsStatus.Over, 2400, 2000)!);
        strings.Add(LobbyPointsStatus.Tooltip(ELobbyPointsStatus.Under, 1000, 2000)!);
        strings.Add(LobbyScreen.LaunchTooltip(isHost: false, Array.Empty<string>())!);
        strings.Add(LobbyScreen.LaunchTooltip(isHost: true, new[] { "Bob: no army assigned." })!);
        // #405: the add-player / add-bot button tooltips.
        strings.AddRange(LobbyScreen.AddPlayerTooltips);

        foreach (string text in strings)
            foreach (char c in text)
                Assert.That(c, Is.LessThanOrEqualTo((char)0xFF), $"non-ASCII in \"{text}\"");
    }

    // ── The Pts cell's tooltips (#402 gave the red state one too) ───────────────────────────

    [Test]
    public void OverPointsTooltipSaysItBlocks()
    {
        string? tip = LobbyPointsStatus.Tooltip(ELobbyPointsStatus.Over, pointCost: 2400, pointsLimit: 2000);

        Assert.That(tip, Does.Contain("400 points OVER").And.Contain("blocked"));
    }

    [Test]
    public void UnderPointsTooltipSaysItIsLegal()
    {
        string? tip = LobbyPointsStatus.Tooltip(ELobbyPointsStatus.Under, pointCost: 1000, pointsLimit: 2000);

        Assert.That(tip, Does.Contain("1000 points under").And.Contain("Legal"));
        Assert.That(tip, Does.Not.Contain("blocked"), "being underbuilt never blocks a launch");
    }

    [Test]
    public void OkPointsHaveNothingToExplain() =>
        Assert.That(LobbyPointsStatus.Tooltip(ELobbyPointsStatus.Ok, 2000, 2000), Is.Null);

    // ── The LAUNCH button's tooltip ─────────────────────────────────────────────────────────

    [Test]
    public void LaunchTooltipIsNullWhenNothingIsWrong() =>
        Assert.That(LobbyScreen.LaunchTooltip(isHost: true, Array.Empty<string>()), Is.Null);

    [Test]
    public void ClientSeesWhoMayLaunch() =>
        Assert.That(LobbyScreen.LaunchTooltip(isHost: false, Array.Empty<string>()),
            Is.EqualTo("Only the host can launch."));

    [Test]
    public void LaunchTooltipListsEveryBlocker()
    {
        string? tip = LobbyScreen.LaunchTooltip(isHost: true,
            new[] { "Bob: no army assigned.", "Cid: army is 2400 pts, over the 2000 pt lobby limit." });

        Assert.That(tip, Does.StartWith("Cannot launch:"));
        Assert.That(tip, Does.Contain("Bob").And.Contain("Cid"));
    }

    // ── #405: squishing roster text to fit its column ───────────────────────────────────────
    //
    // Both widths are REAL SCREEN PIXELS: CalcTextSize honours SetWindowFontScale (measured 2026-09-12 -
    // doubling the window scale doubles the reported width), so what it returns is what the text
    // actually renders at, and the quotient is a factor applied TO the current scale.

    [Test]
    public void TextThatAlreadyFitsIsNeverResized()
    {
        Assert.That(LobbyScreen.FitFontScale(90f, 200f, 1.5f), Is.EqualTo(1.5f));
    }

    [Test]
    public void TextThatFitsExactlyIsNotShrunk()
    {
        // Exactly filling the cell is a fit - the boundary must not shrink.
        Assert.That(LobbyScreen.FitFontScale(150f, 150f, 1.5f), Is.EqualTo(1.5f));
    }

    [Test]
    public void OverlongTextShrinksJustEnoughToFit()
    {
        // 180px of rendered text into 150px of cell needs 150/180 of the current size: 1.5 -> 1.25,
        // comfortably above the 0.975 floor (1.5 * 0.65), so the shrink is exact rather than clamped.
        float scale = LobbyScreen.FitFontScale(180f, 150f, 1.5f);

        Assert.That(scale, Is.EqualTo(1.25f).Within(0.0001f));
        Assert.That(180f * (scale / 1.5f), Is.EqualTo(150f).Within(0.01f),
            "the whole point is that it now fits");
        Assert.That(scale, Is.LessThan(1.5f), "and it is genuinely smaller than the row's normal size");
    }

    /// <summary>Guards the bug this formula was rewritten to fix: treating the measured width as if it
    /// were unscaled shrank text to the floor almost every time it did not fit.</summary>
    [Test]
    public void ASlightOverflowShrinksOnlySlightly()
    {
        float scale = LobbyScreen.FitFontScale(155f, 150f, 1.5f);

        Assert.That(scale, Is.EqualTo(1.5f * (150f / 155f)).Within(0.0001f));
        Assert.That(scale, Is.GreaterThan(1.4f),
            "5px of overflow must cost a hair of size, not a drop to the floor");
    }

    [Test]
    public void VeryLongTextStopsAtTheFloorRatherThanBecomingUnreadable()
    {
        // 2000px into 150px wants 0.075 of the current size - far past the 0.65 floor.
        float scale = LobbyScreen.FitFontScale(2000f, 150f, 1.5f);

        Assert.That(scale, Is.EqualTo(1.5f * 0.65f).Within(0.0001f));
        Assert.That(2000f * (scale / 1.5f), Is.GreaterThan(150f),
            "at the floor the text genuinely still clips - that is the accepted trade, not a fit");
    }

    [TestCase(0f, 150f)]
    [TestCase(-5f, 150f)]
    [TestCase(200f, 0f)]
    [TestCase(200f, -5f)]
    public void DegenerateMeasurementsLeaveTheRowAlone(float textWidth, float available)
    {
        // A cell can measure zero on the frame a table is first laid out; never divide by it.
        Assert.That(LobbyScreen.FitFontScale(textWidth, available, 1.5f), Is.EqualTo(1.5f));
    }

    // ── #405: the Type cell's label ─────────────────────────────────────────────────────────

    [Test]
    public void BotsReadAsBotNotAI() =>
        Assert.That(LobbyScreen.PlayerTypeLabel(EPlayerType.AI), Is.EqualTo("Bot"));

    [TestCase(EPlayerType.Local, "Local")]
    [TestCase(EPlayerType.Network, "Network")]
    public void OtherPlayerTypesKeepTheirName(EPlayerType type, string expected) =>
        Assert.That(LobbyScreen.PlayerTypeLabel(type), Is.EqualTo(expected));

    [Test]
    public void EveryPlayerTypeLabelIsAscii()
    {
        foreach (EPlayerType type in Enum.GetValues<EPlayerType>())
            foreach (char c in LobbyScreen.PlayerTypeLabel(type))
                Assert.That(c, Is.LessThanOrEqualTo((char)0xFF));
    }
}
