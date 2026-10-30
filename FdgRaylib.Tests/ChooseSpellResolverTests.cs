using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using FDG;
using FDG.Data;
using FDG.Stages;
using FDG.StageResolution.Requests;
using FdgRaylib.Cli.Resolvers;
using FdgRaylib.Rendering.Resolvers;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #429 - Core Rules v3.6.0 moved "target in line of sight" out of Caster into each spell's own text, so both
// spell pickers tag the spells that need it ("(LoS)"), grey a spell whose in-range targets are all out of sight
// with the engine's reason, and state the caster's own casting threshold with its terms (Spell Amplifier: "base
// 4+, rules +1"). The engine computes every fact (ChooseSpellRequest); these pin that the front ends print it.
[TestFixture]
public class ChooseSpellResolverTests
{
    private TextReader _originalIn = null!;
    private TextWriter _originalOut = null!;
    private StringWriter _out = null!;
    private GameDataStore _store = null!;

    [SetUp]
    public void SetUp()
    {
        _originalIn = Console.In;
        _originalOut = Console.Out;
        _out = new StringWriter();
        Console.SetOut(_out);
        _store = GameDataStore.GameDataStoreBuilder.GetDefault();
    }

    [TearDown]
    public void TearDown()
    {
        Console.SetIn(_originalIn);
        Console.SetOut(_originalOut);
    }

    [Test]
    public async Task Cli_TagsLineOfSightSpells_AndPrintsTheGreyedReason()
    {
        Console.SetIn(new StringReader("1\n\n"));

        ChooseSpellReply reply = await new ChooseSpellResolver().Resolve(Request(note: "base 4+, rules +1", threshold: 3));

        string text = _out.ToString();
        Assert.That(text, Does.Contain($"Seen Hex (1) (LoS) (unavailable: {CastSpellStage.NoTargetInSightReason})"));
        Assert.That(text, Does.Contain("[1] Unseen Hex (1)" + Environment.NewLine),
            "a spell without the clause carries no tag");
        Assert.That(text, Does.Contain("[2] Seen Bolt (2) (LoS)"));
        Assert.That(reply.SpellIndex, Is.EqualTo(1), "[1] is the first CASTABLE row, Unseen Hex");
    }

    [Test]
    public async Task Cli_StatesTheCastersOwnThreshold_AndItsTerms()
    {
        Console.SetIn(new StringReader("1\n\n"));

        await new ChooseSpellResolver().Resolve(Request(note: "base 4+, rules +1", threshold: 3));

        Assert.That(_out.ToString(), Does.Contain("Casting roll: 3+ before boosts (base 4+, rules +1)."));
    }

    [Test]
    public async Task Cli_APlainCaster_GetsThePlainLine()
    {
        Console.SetIn(new StringReader("1\n\n"));

        await new ChooseSpellResolver().Resolve(Request(note: "", threshold: 4));

        Assert.That(_out.ToString(), Does.Contain("Casting roll: 4+ before boosts." + Environment.NewLine));
    }

    [Test]
    public void Gui_RowText_TagsLineOfSight_AndAppendsTheReason()
    {
        ChooseSpellRequest request = Request(note: "", threshold: 4);

        Assert.That(GuiChooseSpellResolver.RowText(request.Spells[0]),
            Is.EqualTo($"Seen Hex (1) (LoS) ({CastSpellStage.NoTargetInSightReason})"));
        Assert.That(GuiChooseSpellResolver.RowText(request.Spells[1]), Is.EqualTo("Unseen Hex (1)"));
        Assert.That(GuiChooseSpellResolver.RowText(request.Spells[2]), Is.EqualTo("Seen Bolt (2) (LoS)"));
    }

    [TestCase("", 4, 0, "Roll needed: 4+")]
    [TestCase("", 4, 1, "Roll needed: 3+ (base 4+, self +1)")]
    [TestCase("base 4+, rules +1", 3, 0, "Roll needed: 3+ (base 4+, rules +1)")]
    [TestCase("base 4+, rules +1", 3, 1, "Roll needed: 2+ (base 4+, rules +1, self +1)")]
    [TestCase("base 4+, rules +1", 3, 2, "Roll needed: 2+ (base 4+, rules +1, self +2)")]
    [TestCase("base 4+, rules +1, granted -1", 4, 0, "Roll needed: 4+ (base 4+, rules +1, granted -1)")]
    public void Gui_RollNeeded_ShowsTheSameTermsAsTheCastBanner(string note, int threshold, int boost, string expected)
    {
        Assert.That(GuiChooseSpellResolver.RollNeededLine(Request(note, threshold), boost), Is.EqualTo(expected));
    }

    [Test]
    public void EveryPickerString_IsAscii()
    {
        ChooseSpellRequest request = Request(note: "base 4+, rules +1", threshold: 3);
        IEnumerable<string> strings = request.Spells.Select(GuiChooseSpellResolver.RowText)
            .Concat(request.Spells.Select(ChooseSpellResolver.RowLabel))
            .Append(GuiChooseSpellResolver.RollNeededLine(request, 1))
            .Append(ChooseSpellResolver.CastingRollLine(request));

        Assert.That(strings.SelectMany(s => s).All(c => c < 128), Is.True, "game text is ASCII-only");
    }

    // Three spells: a LoS spell greyed for sight, a no-LoS spell, and a castable LoS spell.
    private ChooseSpellRequest Request(string note, int threshold)
    {
        var playerID = new PlayerID(Guid.NewGuid());
        var model = new ModelData(0.5f, new List<Weapon>(), new Position(), _store);
        var unit = new UnitData(playerID, "Psy-Seer", 4, 4,
            new List<DataBinding<ModelData>> { _store.GetDataBinding<ModelData>(_store.Create(model)) });
        DataBinding<UnitData> caster = _store.GetDataBinding<UnitData>(_store.Create(unit));

        return new ChooseSpellRequest(playerID, caster, availableTokens: 3, baseThreshold: threshold,
            hinderTokensInRange: 0,
            new List<ChooseSpellRequest.SpellOption>
            {
                new("Seen Hex (1)", "", 1, Castable: false, CastSpellStage.NoTargetInSightReason, RequiresLineOfSight: true),
                new("Unseen Hex (1)", "", 1, Castable: true, null),
                new("Seen Bolt (2)", "", 2, Castable: true, null, RequiresLineOfSight: true),
            },
            baseThresholdNote: note);
    }
}
