using System;
using System.Collections.Generic;
using System.Linq;
using FDG;
using FDG.Data;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #411 - the renderer's overlays read rules through the core catalog PLUS each army's embedded definitions.
// Found while hand-verifying #408: picking Versatile Attack's AP(+1) granted "Versatile Attack (Piercing)", a
// Prime Brothers BOOK rule, and the table tooltip's core-only capability evaluator logged "has no definition
// in the registry - the grant does nothing" the moment it walked the unit's grants. The game's own evaluator
// had the rule all along (the effect applied); the false warning is warn-once, so it would also have
// swallowed a genuine one later, and token chips showed such grants with no description.
[TestFixture]
public class DisplayRuleSetTests
{
    // Unique per test run: RuleDiagnostics.WarnOnce dedupes by key process-wide.
    private readonly string _bookRule = $"Book Hold {Guid.NewGuid():N}";

    [Test]
    public void ABookRuleGrantedMidGame_ResolvesAndAnswersCapabilities_WithoutWarning()
    {
        GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
        PlayerID player = new PlayerID(Guid.NewGuid());
        var model = new ModelData(0.5f, new List<Weapon>(), new Position(10f, 10f), store);
        var unit = new UnitData(player, "Prime Master", quality: 3, defense: 3,
            modelBindings: new List<DataBinding<ModelData>> { store.GetDataBinding<ModelData>(store.Create(model)) });
        unit.Tokens.AddToken(new Token(TokenType.RuleGrant, 1, new TokenClearTrigger.ActivationEnd(),
            Payload: new TokenPayload.RuleGrant(_bookRule, ELifetime.ThisActivation)));
        var army = new ArmyData(player, new List<DataBinding<UnitData>> { store.GetDataBinding<UnitData>(store.Create(unit)) });
        army.PersistRuleData(new[] { BookRule() }, Array.Empty<SpellDefinition>());
        store.Create(army);

        var warnings = new List<string>();
        void Capture(string message) => warnings.Add(message);
        RuleDiagnostics.OnWarning += Capture;
        try
        {
            var rules = new DisplayRuleSet();
            rules.Refresh(new TableState(store));

            Assert.That(rules.Resolver.TryResolve(_bookRule, out _), Is.True,
                "the army's embedded rule is known to the overlays");
            Assert.That(CapabilityRuleQueries.TransportCapacity(unit, rules.Evaluator), Is.EqualTo(4),
                "and the capability evaluator actually reads the grant");
        }
        finally
        {
            RuleDiagnostics.OnWarning -= Capture;
        }

        Assert.That(warnings.Where(w => w.Contains(_bookRule)), Is.Empty, "no false 'no definition' alarm");
    }

    [Test]
    public void WithoutArmyData_TheCoreCatalogStillAnswers()
    {
        GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var rules = new DisplayRuleSet();
        rules.Refresh(new TableState(store));

        Assert.That(rules.Resolver.TryResolve("Transport", out _), Is.True);
        Assert.That(rules.Resolver.TryResolve(_bookRule, out _), Is.False);
    }

    // A capability rule with a fixed hold, so a successful read is visible as a number.
    private SpecialRuleDefinition BookRule() => new(_bookRule,
        new[]
        {
            new HookEntry(EHookID.Lifecycle_OnCapabilityQuery, new Condition.Always(),
                new Effect.EnableTransport(new ValueSource.Literal(4)), ELifetime.UntilEndOfGame),
        },
        Array.Empty<ActivatedAbility>());
}
