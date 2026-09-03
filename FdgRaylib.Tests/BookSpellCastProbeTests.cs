using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using FDG;
using FDG.ArmyBuilding;
using FDG.Data;
using FDG.Players;
using FDG.Rules.Definitions;
using FDG.Rules.Dispatch;
using FDG.Rules.Foundation;
using FDG.Rules.Serialization;
using FDG.Rules.Tokens;
using FDG.SaveLoad;
using FDG.Stages;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Tests;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// The app-side twin of the engine's SpellCorpusProbeTests (#377), always-on: that fixture only casts
// spells when FDG_SPELL_PROBE_BOOKS points at the off-repo copyrighted OPR corpus, so it never actually
// exercises the bundled, already-shipped books this app plays with. Every spell of every bundled book is
// CAST through the real CastSpellStage in a minimal probe world (a Caster funded with exactly the
// threshold, friendly and enemy targets in range), asserting the cast completed, spent its tokens, and
// raised ZERO rule diagnostics (an unresolvable granted name or dropped WithRules ref fails the book).
// RuleFireLint (BookRuleFireLintTests) proves each rule/effect CAN fire in a synthesized context; this
// fixture proves the actual shipped spell data drives the real pipeline end to end.
[TestFixture]
[NonParallelizable] // subscribes the global RuleDiagnostics channels
public class BookSpellCastProbeTests
{
    private static string BooksDirectory => Path.Combine(AppContext.BaseDirectory, "Assets", "Books");

    [Test]
    public async Task EverySpellInEveryBundledBook_CastsCleanly()
    {
        string[] bookPaths = Directory.EnumerateFiles(BooksDirectory, "*" + BookFile.EXTENSION_WITH_PERIOD)
            .OrderBy(p => p, StringComparer.Ordinal).ToArray();
        Assert.That(bookPaths, Is.Not.Empty, $"no books in '{BooksDirectory}'.");

        var problems = new List<string>();
        var diagnostics = new List<string>();
        void CaptureWarning(string message) => diagnostics.Add(message);
        void CaptureDrop(RuleDrop drop) => diagnostics.Add(drop.Message);

        RuleDiagnostics.OnWarning += CaptureWarning;
        RuleDiagnostics.OnRuleDropped += CaptureDrop;
        try
        {
            int casts = 0;
            foreach (string path in bookPaths)
            {
                string bookName = Path.GetFileNameWithoutExtension(path);
                BookFile book = JsonSerializer.Deserialize<BookFile>(File.ReadAllText(path), RuleJson.Options)!;
                RuleResolver resolver = CoreRuleCatalog.CreateResolver();
                foreach (SpecialRuleDefinition definition in book.RuleDefinitions)
                {
                    resolver.RegisterOrReplace(definition);
                }

                diagnostics.Clear();
                IReadOnlyList<RuntimeSpell> spells = ArmyListSpellResolution.ResolveSpells(book.Spells, resolver);
                foreach (string diagnostic in diagnostics)
                {
                    problems.Add($"{bookName} (load): {diagnostic}");
                }

                foreach (RuntimeSpell spell in spells)
                {
                    diagnostics.Clear();
                    string? failure = await CastOnce(spell, resolver);
                    if (failure != null)
                    {
                        problems.Add($"{bookName} | {spell.Definition.Name}: {failure}");
                    }

                    foreach (string diagnostic in diagnostics)
                    {
                        problems.Add($"{bookName} | {spell.Definition.Name}: {diagnostic}");
                    }

                    casts++;
                }
            }

            TestContext.Out.WriteLine($"cast {casts} spells from {bookPaths.Length} bundled books.");
        }
        finally
        {
            RuleDiagnostics.OnWarning -= CaptureWarning;
            RuleDiagnostics.OnRuleDropped -= CaptureDrop;
        }

        Assert.That(problems, Is.Empty,
            "spells that failed the cast sweep:" + Environment.NewLine + "  " +
            string.Join(Environment.NewLine + "  ", problems));
    }

    /// <summary>One full cast of <paramref name="spell"/> in a fresh probe world. Returns null on a
    /// clean cast, or a description of what went wrong.</summary>
    private static async Task<string?> CastOnce(RuntimeSpell spell, IRuleResolver resolver)
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var casterPlayer = new PlayerID(Guid.NewGuid());
        var enemyPlayer = new PlayerID(Guid.NewGuid());
        store.Create(new TeamData(0, new List<PlayerID> { casterPlayer }));
        store.Create(new TeamData(1, new List<PlayerID> { enemyPlayer }));

        // The caster at the origin; friendlies beside it, enemies 6" out - inside every corpus
        // range (12/18/24) whatever the affinity and count (up to 3).
        DataBinding<UnitData> caster = MakeUnit(store, casterPlayer, "Caster", new Position(0f, 0f));
        var friendlyUnits = new List<DataBinding<UnitData>> { caster };
        for (int i = 0; i < 2; i++)
        {
            friendlyUnits.Add(MakeUnit(store, casterPlayer, $"Friends{i}", new Position(2f + i * 2f, 0f)));
        }

        caster.GetValue().AttachRuleDefinition(new ResolvedRule("Caster", CoreRuleCatalog.Caster,
            new RuleArgument[] { new RuleArgument.Int(3) }));
        caster.GetValue().Tokens.AddToken(new Token(TokenType.SpellTokens,
            spell.Definition.Threshold, new TokenClearTrigger.ManualOnly()));
        var casterArmy = new ArmyData(casterPlayer, friendlyUnits);
        casterArmy.SetSpells(new[] { spell });
        store.Create(casterArmy);

        var enemyUnits = new List<DataBinding<UnitData>>();
        for (int i = 0; i < 3; i++)
        {
            enemyUnits.Add(MakeUnit(store, enemyPlayer, $"Enemies{i}", new Position(6f, i * 3f)));
        }

        store.Create(new ArmyData(enemyPlayer, enemyUnits));

        var requester = new ProbeRequester();
        // Face 4: the cast roll (4+) succeeds, so the effect path actually runs; a Quality-4 morale
        // test passes, so a moraleTestThen spell exercises the test without its on-fail arm.
        var ctx = new TriggeredMoveTestContext(store, requester, new FixedDiceRoller(4), ruleResolver: resolver);

        var unitCtx = new UnitActionContext(ctx, caster);
        unitCtx.Reset(caster);
        var stage = new CastSpellStage(ctx, new NoOpLayer<IUnitActionContext>());
        stage.OnFinished.Bind("done");
        try
        {
            await stage.Enter(unitCtx);
        }
        catch (Exception ex)
        {
            return $"cast threw {ex.GetType().Name}: {ex.Message}";
        }

        if (!requester.PickedASpell)
        {
            return "not castable in the probe world (no valid target, or unaffordable).";
        }

        int tokensLeft = caster.GetValue().Tokens.GetTokenCount(TokenType.SpellTokens);
        if (tokensLeft != 0)
        {
            return $"expected the threshold to be fully spent; {tokensLeft} token(s) remain.";
        }

        return null;
    }

    private static DataBinding<UnitData> MakeUnit(GameDataStore store, PlayerID player, string name, Position pos)
    {
        var modelBindings = new List<DataBinding<ModelData>>();
        for (int i = 0; i < 2; i++)
        {
            var model = new ModelData(0.5f, new List<Weapon>(), new Position(pos.x + i * 1.2f, pos.z), store);
            modelBindings.Add(store.GetDataBinding<ModelData>(store.Create(model)));
        }

        var unit = new UnitData(player, name, quality: 4, defense: 4, modelBindings: modelBindings);
        return store.GetDataBinding<UnitData>(store.Create(unit));
    }

    /// <summary>First-castable spell pick, first-option selections, auto-filled wounds, and a fixed 1"
    /// translation for any caster-directed forced move. Any other request type fails the probe loudly
    /// rather than faking an answer.</summary>
    private sealed class ProbeRequester : IPlayerRequestByID
    {
        public bool PickedASpell { get; private set; }

        public Task<TReply> RequestDecision<TRequest, TReply>(TRequest request)
            where TRequest : IStageTaskRequest<TReply>
        {
            switch (request)
            {
                case ChooseSpellRequest spellPick:
                    int index = -1;
                    for (int i = 0; i < spellPick.Spells.Count; i++)
                    {
                        if (spellPick.Spells[i].Castable)
                        {
                            index = i;
                            break;
                        }
                    }
                    ChooseSpellReply reply = index >= 0 ? new ChooseSpellReply(index, 0) : ChooseSpellReply.Cancel;
                    PickedASpell |= reply.SpellIndex >= 0;
                    return Task.FromResult((TReply)(object)reply);
                case SelectionRequest<UnitData> targetPick:
                    return Task.FromResult((TReply)(object)targetPick.ValidOptions[0].Option);
                case SelectionRequest<ModelData> modelPick:
                    return Task.FromResult((TReply)(object)modelPick.ValidOptions[0].Option);
                case AssignWoundsRequest woundPick:
                    var wounds = new AssignWoundsResults(woundPick.UnitReceivingWounds,
                        woundPick.TotalWoundsToAssign);
                    wounds.AutoFill();
                    return Task.FromResult((TReply)(object)wounds);
                case DefineMovementPathRequest moveRequest:
                {
                    var entries = new List<ModelMoveEntry>();
                    foreach (DataBinding<ModelData> model in moveRequest.UnitDataBinding.GetValue().ModelBindings)
                    {
                        Position start = model.GetValue().PositionBinding.GetValue();
                        entries.Add(new ModelMoveEntry(model,
                            new List<Position> { new Position(start.x + 1f, start.z) }));
                    }

                    return Task.FromResult((TReply)(object)new Selected<List<ModelMoveEntry>>(entries));
                }
                default:
                    throw new InvalidOperationException("probe has no answer for " + request.GetType().Name);
            }
        }
    }
}
