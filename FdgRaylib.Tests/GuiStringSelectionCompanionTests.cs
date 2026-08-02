using System.Collections.Generic;
using System.Linq;
using FDG;
using FDG.StageResolution.Requests;
using FdgRaylib.Rendering.Resolvers;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #317: a companion action ("Hold back: <weapon>") belongs to the option above it and is drawn as a
// second button ON that row, sharing its letter under Shift. Two peer list entries for one weapon read
// as two unrelated choices, which is what this replaced. The drawing is ImGui (hand-verified); these pin
// the row-building rules that decide what gets a row, what gets a letter, and what a greyed companion says.
[TestFixture]
public class GuiStringSelectionCompanionTests
{
    private const string Bomb = "1x One-Shot Bomb - A3, AP2, Limited";
    private const string Blade = "3x Blade - A2, AP0";
    private const string HoldBomb = "Hold back: " + Bomb;
    private const string HoldBlade = "Hold back: " + Blade;

    [Test]
    public void CompanionOptions_AreTheValuesOfTheMap_EmptyWhenThereAreNone()
    {
        StringSelectionRequest paired = Request(
            valid: new[] { Bomb, Blade, HoldBomb, HoldBlade },
            secondaries: new() { [Bomb] = HoldBomb, [Blade] = HoldBlade });

        Assert.That(GuiStringSelectionResolver.CompanionOptions(paired),
            Is.EquivalentTo(new[] { HoldBomb, HoldBlade }));

        StringSelectionRequest plain = Request(valid: new[] { "Move", "Charge" });
        Assert.That(GuiStringSelectionResolver.CompanionOptions(plain), Is.Empty,
            "an ordinary menu has none, and must be unaffected by any of this.");
    }

    // The letters are indexed by VALID-OPTION index, so a companion sitting in the middle of the list must
    // leave a null hole rather than consuming a letter and pushing everyone else's along.
    [Test]
    public void AssignRowLetters_SkipsCompanions_WithoutShiftingTheOthers()
    {
        StringSelectionRequest paired = Request(
            valid: new[] { Bomb, HoldBomb, Blade, HoldBlade },
            secondaries: new() { [Bomb] = HoldBomb, [Blade] = HoldBlade });

        char?[] letters = GuiStringSelectionResolver.AssignRowLetters(
            paired, GuiStringSelectionResolver.CompanionOptions(paired));

        Assert.That(letters[1], Is.Null, "a companion never takes a letter of its own...");
        Assert.That(letters[3], Is.Null);
        Assert.That(letters[0], Is.Not.Null, "...and its owner keeps one.");
        Assert.That(letters[2], Is.Not.Null);
        Assert.That(letters[0], Is.Not.EqualTo(letters[2]), "two weapons never share a letter.");

        char?[] unpaired = GuiStringSelectionResolver.AssignRowLetters(
            Request(valid: new[] { Bomb, Blade }), new HashSet<string>());
        Assert.That(new[] { letters[0], letters[2] }, Is.EqualTo(new[] { unpaired[0], unpaired[1] }),
            "the weapons get exactly the letters they would have had with no companions at all.");
    }

    [Test]
    public void AttachSecondary_AvailableCompanion_LabelsItWithTheShiftShortcut()
    {
        StringSelectionRequest request = Request(
            valid: new[] { Bomb, HoldBomb },
            secondaries: new() { [Bomb] = HoldBomb });
        var row = new GuiStringSelectionResolver.MenuRow(Bomb, Bomb, null, 0);

        GuiStringSelectionResolver.AttachSecondary(row, request, 'E');

        Assert.That(row.Secondary!.Option, Is.EqualTo(HoldBomb));
        Assert.That(row.SecondaryLabel, Is.EqualTo("[^E] Hold back"),
            "the button advertises the row's own letter under Shift.");
        Assert.That(row.SecondaryDisabledReason, Is.Null, "it is available, so it is live.");
    }

    // A refused companion (melee: "at least one weapon must attack after charging") stays ON its row as a
    // greyed button carrying the reason - dropping it would leave the weapon looking like it never had one.
    [Test]
    public void AttachSecondary_RefusedCompanion_CarriesItsReason()
    {
        StringSelectionRequest request = Request(
            valid: new[] { Bomb },
            invalid: new[] { (HoldBomb, "At least one weapon must attack after charging.") },
            secondaries: new() { [Bomb] = HoldBomb });
        var row = new GuiStringSelectionResolver.MenuRow(Bomb, Bomb, null, 0);

        GuiStringSelectionResolver.AttachSecondary(row, request, 'E');

        Assert.That(row.Secondary, Is.Not.Null, "still shown, still attached to its weapon.");
        Assert.That(row.SecondaryDisabledReason,
            Is.EqualTo("At least one weapon must attack after charging."));
    }

    [Test]
    public void AttachSecondary_OptionWithoutOne_LeavesTheRowAlone()
    {
        StringSelectionRequest request = Request(
            valid: new[] { Bomb, Blade, HoldBomb },
            secondaries: new() { [Bomb] = HoldBomb });
        var row = new GuiStringSelectionResolver.MenuRow(Blade, Blade, null, 1);

        GuiStringSelectionResolver.AttachSecondary(row, request, 'R');

        Assert.That(row.Secondary, Is.Null);
        Assert.That(row.SecondaryLabel, Is.Empty);
    }

    private static StringSelectionRequest Request(
        string[] valid,
        (string Option, string Reason)[]? invalid = null,
        Dictionary<string, string>? secondaries = null)
    {
        Dictionary<string, StringSelectionRequest.SecondaryAction>? map = secondaries?.ToDictionary(
            pair => pair.Key,
            pair => new StringSelectionRequest.SecondaryAction(pair.Value, "Hold back"));

        return new StringSelectionRequest(new PlayerID(System.Guid.NewGuid()), "Choose weapon:",
            valid.ToList(),
            (invalid ?? System.Array.Empty<(string, string)>())
                .Select(i => new StringSelectionRequest.InvalidOption(i.Option, i.Reason)).ToList(),
            optionDescriptions: null, allowCancel: false, secondaryActions: map);
    }
}
