using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG;
using FDG.ArmyBuilding;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Dispatch.Contexts;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using FDG.Utilities;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #197 Hazardous - "Attacks with this weapon get AP(4), but this weapon's unit takes one wound on
// unmodified rolls of 1 to hit." Both halves now ship: #196 authored the AP as a weapon-scoped Actor-seat
// save modifier (the Thrust-AP pattern), and the 2026-07-29 slice added the self-wound as a second entry at
// the same hook. Until that landed the rule was upside-only, which is why the balance flag came off the
// ledger with it. The mechanism is pinned engine-side by HazardousRuleIntegrationTests; these pin the
// authored JSON, the embedded book copies, and the corpus shape.
[TestFixture]
public class HazardousShippedDataTests
{
    private const string RuleName = "Hazardous";

    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    private static SpecialRuleDefinition Hazardous() =>
        BookRuleSupplement.LoadDefinitions(File.ReadAllText(
                Path.Combine(BooksDirectory, "GdfRuleSupplement.json")))
            .Single(r => r.Name == RuleName);

    private static HookEntry ApEntry(SpecialRuleDefinition rule) =>
        rule.Passive.Single(e => e.Effect is Effect.RollModifier);

    private static HookEntry SelfWoundEntry(SpecialRuleDefinition rule) =>
        rule.Passive.Single(e => e.Effect is Effect.SelfWoundOnUnmodifiedRoll);

    // ---- The authored definition ----------------------------------------------------------------------

    [Test]
    public void Hazardous_IsWeaponScoped_GrantingAPFourAsAnActorSaveModifier()
    {
        SpecialRuleDefinition rule = Hazardous();
        Assert.That(rule.Scope, Is.EqualTo(ERuleScope.Weapon));

        HookEntry entry = ApEntry(rule);
        Assert.That(entry.HookID, Is.EqualTo(EHookID.Shooting_OnHitRollComplete),
            "AP folds at the save-complete hook, like Thrust's AP.");
        Assert.That(entry.Seat, Is.EqualTo(ERuleSeat.Actor), "the ATTACKER worsens the defender's save.");
        var mod = (Effect.RollModifier)entry.Effect;
        Assert.That(mod.RollKind, Is.EqualTo(ERollKind.Save));
        Assert.That(mod.Delta, Is.EqualTo(-4), "AP(4) = -4 to the enemy save.");
    }

    [Test]
    public void Hazardous_SelfWoundsOnUnmodifiedOnes_AtTheSameHookAndSeat()
    {
        SpecialRuleDefinition rule = Hazardous();
        Assert.That(rule.Passive, Has.Exactly(2).Items, "AP plus the overheat - both halves, one rule.");

        HookEntry entry = SelfWoundEntry(rule);
        Assert.That(entry.HookID, Is.EqualTo(EHookID.Shooting_OnHitRollComplete),
            "the unmodified hit dice are only readable at the hook that just rolled them.");
        Assert.That(entry.Seat, Is.EqualTo(ERuleSeat.Actor),
            "the SHOOTER is the one that overheats, so the rule sits in the Actor seat.");
        Assert.That(entry.Effect, Is.EqualTo(new Effect.SelfWoundOnUnmodifiedRoll(OnRollValue: 1, Count: 1)),
            "'takes one wound on unmodified rolls of 1 to hit' - one wound, on 1s.");
    }

    // ---- What it produces in play ---------------------------------------------------------------------

    [Test]
    public void Hazardous_NetsMinusFourToTheDefendersSave()
    {
        var sink = new RollModifierSink();
        sink.ApplyFrom(Evaluate(Faces(4)));

        Assert.That(sink.Net(ERollKind.Save), Is.EqualTo(-4));
    }

    [Test]
    public void ThreeUnmodifiedOnes_OweThreeSelfWounds()
    {
        float wounds = Evaluate(Faces(1, 1, 1, 4)).OfType<RuleOperation.InflictSelfWounds>()
            .Sum(op => op.Wounds);

        Assert.That(wounds, Is.EqualTo(3f), "one wound per 1, and the 4 costs nothing.");
    }

    [Test]
    public void NoOnes_OweNothing()
    {
        Assert.That(Evaluate(Faces(4, 5, 6)).OfType<RuleOperation.InflictSelfWounds>(), Is.Empty,
            "the condition gates the entry, so a clean volley produces no operation at all.");
    }

    [Test]
    public void TheApHalfStillFires_OnAVolleyThatAlsoOverheats()
    {
        // The two entries share a hook. A condition mistake on either could silently shadow the other,
        // and a volley with both a 1 and a hit is the only case that would show it.
        IReadOnlyList<RuleOperation> operations = Evaluate(Faces(1, 6));

        var sink = new RollModifierSink();
        sink.ApplyFrom(operations);
        Assert.That(sink.Net(ERollKind.Save), Is.EqualTo(-4), "AP(4) applies to the hit that landed...");
        Assert.That(operations.OfType<RuleOperation.InflictSelfWounds>().Sum(op => op.Wounds),
            Is.EqualTo(1f), "...and the 1 still costs a wound.");
    }

    // ---- The corpus -----------------------------------------------------------------------------------

    private record Site(string Book, string Unit, string Weapon);

    private static IEnumerable<Site> Sites()
    {
        foreach (string path in ShippedBooks.GdfPaths()
                     .OrderBy(p => p))
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
            string bookName = Path.GetFileNameWithoutExtension(path);

            foreach (RosterUnit unit in book.Units)
            {
                foreach (WeaponFileEntry weapon in unit.Weapons.Concat(
                             unit.Sections.SelectMany(s => s.Options).SelectMany(o => o.WeaponsGained)))
                {
                    foreach (SpecialRuleEntry rule in weapon.SpecialRules)
                    {
                        if (string.Equals(ArmyListRuleResolution.DescribeRuleEntry(rule).lookupName, RuleName,
                                StringComparison.OrdinalIgnoreCase))
                        {
                            yield return new Site(bookName, unit.Name, weapon.Name);
                        }
                    }
                }
            }
        }
    }

    [Test]
    public void EveryReference_IsAWeaponInRatmenClans()
    {
        List<Site> sites = Sites().ToList();

        Assert.That(sites.Count, Is.EqualTo(15),
            "the audit's 15 references - a change here means the corpus moved, not the engine");
        Assert.That(sites.Select(s => s.Book).Distinct(), Is.EquivalentTo(new[] { "RatmenClans" }));
    }

    [Test]
    public void TheEmbeddedBookCopy_CarriesBothHalves()
    {
        // --apply-rules embeds the supplement's definition into each referencing book. A slice that edits
        // the supplement and forgets to re-embed ships a book whose Hazardous is still AP-only, and nothing
        // else would notice.
        foreach (string bookName in Sites().Select(s => s.Book).Distinct())
        {
            BookFile book = JsonSerializer.Deserialize<BookFile>(
                File.ReadAllText(Path.Combine(BooksDirectory, bookName + BookFile.EXTENSION_WITH_PERIOD)),
                RuleJson.Options)!;

            RuleResolver resolver = CoreRuleCatalog.CreateResolver();
            foreach (SpecialRuleDefinition definition in book.RuleDefinitions)
            {
                resolver.RegisterOrReplace(definition);
            }

            Assert.That(resolver.TryResolve(RuleName, out ResolvedRule resolved), Is.True, bookName);
            Assert.That(resolved.Definition.Passive.Any(e => e.Effect is Effect.RollModifier), Is.True,
                $"{bookName}: the AP half");
            Assert.That(resolved.Definition.Passive.Any(e => e.Effect is Effect.SelfWoundOnUnmodifiedRoll),
                Is.True, $"{bookName}: the self-wound half - re-run --apply-rules");
        }
    }

    // ---- Harness --------------------------------------------------------------------------------------

    private static IReadOnlyList<RuleOperation> Evaluate(DiceResults hitRolls)
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var resolver = new RuleResolver();
        resolver.Register(Hazardous());
        var evaluator = new RuleEvaluator(new ProbabilisticDiceRoller(), ruleResolver: resolver);

        IUnit attacker = BuildUnit(store, "P1");
        IUnit defender = BuildUnit(store, "P2");
        var weapon = new Weapon("Plas-Burst", rangeInches: 24f, attacks: 1, armorPenetration: 0);
        weapon.AttachRuleDefinition(resolver.Resolve(RuleName));

        return evaluator.EvaluateAll(
            new HitRollCompleteContext(attacker, defender, hitRolls, DistanceInches: 6f),
            RuleParticipant.Actor(attacker, weapon));
    }

    private static IUnit BuildUnit(GameDataStore store, string name)
    {
        var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
            initialPosition: new Position(), gameDataStore: store);
        DataBinding<ModelData> binding = store.GetDataBinding<ModelData>(store.Create(model));
        return new UnitData(new PlayerID(Guid.NewGuid()), name, quality: 4, defense: 4,
            modelBindings: new List<DataBinding<ModelData>> { binding });
    }

    private static DiceResults Faces(params int[] faces)
    {
        var perSide = new float[6];
        foreach (int face in faces) perSide[face - 1] += 1f;
        return new DiceResults(perSide);
    }
}
