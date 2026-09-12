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
    // The widths below are RAW CalcTextSize results (CalcTextSize ignores SetWindowFontScale), measured
    // against real pixels of cell - so the quotient is a font scale, not a ratio of like quantities.

    [Test]
    public void TextThatAlreadyFitsIsNeverResized()
    {
        // 60px of text at scale 1.5 renders 90px wide, and the cell has 200px.
        Assert.That(LobbyScreen.FitFontScale(60f, 200f, 1.5f), Is.EqualTo(1.5f));
    }

    [Test]
    public void TextThatFitsExactlyIsNotShrunk()
    {
        // 100px of text at scale 1.5 is exactly the 150px available - the boundary must not shrink.
        Assert.That(LobbyScreen.FitFontScale(100f, 150f, 1.5f), Is.EqualTo(1.5f));
    }

    [Test]
    public void OverlongTextShrinksJustEnoughToFit()
    {
        // 120px of text at scale 1.5 wants 180px but has 150px: 1.25 makes it exactly fit, and that is
        // comfortably above the 0.975 floor (1.5 * 0.65), so the shrink is exact rather than clamped.
        float scale = LobbyScreen.FitFontScale(120f, 150f, 1.5f);

        Assert.That(scale, Is.EqualTo(1.25f).Within(0.0001f));
        Assert.That(120f * scale, Is.EqualTo(150f).Within(0.01f), "the whole point is that it now fits");
        Assert.That(scale, Is.LessThan(1.5f), "and it is genuinely smaller than the row's normal size");
    }

    [Test]
    public void VeryLongTextStopsAtTheFloorRatherThanBecomingUnreadable()
    {
        // 2000px into 150px would need 0.075 - far past the 0.65-of-base floor (1.5 * 0.65 = 0.975).
        float scale = LobbyScreen.FitFontScale(2000f, 150f, 1.5f);

        Assert.That(scale, Is.EqualTo(1.5f * 0.65f).Within(0.0001f));
        Assert.That(scale * 2000f, Is.GreaterThan(150f),
            "at the floor the text genuinely still clips - that is the accepted trade, not a fit");
    }

    [TestCase(0f, 150f)]
    [TestCase(-5f, 150f)]
    [TestCase(100f, 0f)]
    [TestCase(100f, -5f)]
    public void DegenerateMeasurementsLeaveTheRowAlone(float textWidth, float available)
    {
        // A cell can measure zero on the frame a table is first laid out; never divide by it.
        Assert.That(LobbyScreen.FitFontScale(textWidth, available, 1.5f), Is.EqualTo(1.5f));
    }
}
