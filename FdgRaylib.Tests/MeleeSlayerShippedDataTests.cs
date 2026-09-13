using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #380 - GDF Melee Slayer, book text: "When this model charges, its weapons get AP(+2) if most models
// in the target have Tough(3) or higher." The def shipped by #196 F1 gated on isMelee instead, so it
// over-granted on strike-back and non-charge melee; 16 of 17 AoF books read the same charge gate and
// the AoF supplement (#375 C9) always shipped it. These pin the corrected shape in BOTH supplements -
// they must never drift apart again - and in an embedded GDF book copy, so a supplement edit that
// forgets the --apply-rules rebake fails here.
[TestFixture]
public class MeleeSlayerShippedDataTests
{
    private const string RuleName = "Melee Slayer";

    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static SpecialRuleDefinition Supplement(string file) =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(Path.Combine(BooksDirectory, file)))
            .Single(r => r.Name == RuleName);

    [TestCase("GdfRuleSupplement.json")]
    [TestCase("AofRuleSupplement.json")]
    public void MeleeSlayer_GatesOnChargingAndToughMajority(string supplementFile)
    {
        SpecialRuleDefinition rule = Supplement(supplementFile);

        HookEntry entry = rule.Passive.Single();
        Assert.That(entry.HookID, Is.EqualTo(EHookID.Shooting_OnHitRollComplete),
            "the AP fold happens where hit results turn into save deltas");

        Condition.And gate = (Condition.And)entry.Condition;
        Assert.That(gate.Left, Is.InstanceOf<Condition.IsCharging>(),
            "#380: 'when this model charges' - NOT isMelee, which over-granted on strike-back");
        Assert.That(((Condition.TargetMajorityHasTough)gate.Right).MinToughValue, Is.EqualTo(3));

        Effect.RollModifier effect = (Effect.RollModifier)entry.Effect;
        Assert.That(effect.RollKind, Is.EqualTo(ERollKind.Save));
        Assert.That(effect.Delta, Is.EqualTo(-2), "AP(+2) = save delta -2");
    }

    // The def the game actually loads is the BOOK's embedded copy - pin one GDF book so an edited
    // supplement without the --apply-rules rebake fails loudly.
    [Test]
    public void EmbeddedGdfBookCopy_CarriesTheChargeGate()
    {
        BookFile book = JsonSerializer.Deserialize<BookFile>(
            File.ReadAllText(Path.Combine(BooksDirectory, "MachineCults" + BookFile.EXTENSION_WITH_PERIOD)),
            RuleJson.Options)!;

        SpecialRuleDefinition rule = book.RuleDefinitions.Single(r => r.Name == RuleName);
        Condition.And gate = (Condition.And)rule.Passive.Single().Condition;
        Assert.That(gate.Left, Is.InstanceOf<Condition.IsCharging>(),
            "the rebaked book carries the #380 charge gate");
    }
}
