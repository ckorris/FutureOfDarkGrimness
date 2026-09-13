using System.Linq;
using FDG.Rules.Foundation;
using FDG.Rules.Tokens;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #151 Step 2: the app-side token chip resolution. Pure logic only (no Raylib/ImGui drawing) — the
// deterministic color/shape derivation, override honoring, invisible filtering, and on-canvas sort order.
[TestFixture]
public class TokenChipRendererTests
{
    [Test]
    public void ColorAndShape_AreDeterministicForTheSameDisplayId()
    {
        // "Shielded is a blue square one game, a blue square the next" — stable across calls/runs.
        Assert.That(TokenChipRenderer.ColorFor(Info("Shielded")),
            Is.EqualTo(TokenChipRenderer.ColorFor(Info("Shielded"))));
        Assert.That(TokenChipRenderer.ShapeFor(Info("Shielded")),
            Is.EqualTo(TokenChipRenderer.ShapeFor(Info("Shielded"))));
    }

    [Test]
    public void Overrides_AreHonored()
    {
        Assert.That(TokenChipRenderer.ShapeFor(Info("X", shape: ETokenShape.Diamond)),
            Is.EqualTo(ETokenShape.Diamond));

        uint blue = TokenChipRenderer.ColorFor(Info("X", color: ETokenColor.Blue));
        uint red  = TokenChipRenderer.ColorFor(Info("X", color: ETokenColor.Red));
        Assert.That(blue, Is.Not.EqualTo(red));
    }

    [Test]
    public void Valence_SelectsDifferentColorBands()
    {
        // Same display id, different valence → different palette band (cool vs warm vs muted).
        uint pos = TokenChipRenderer.ColorFor(Info("Same", EValence.Positive));
        uint neg = TokenChipRenderer.ColorFor(Info("Same", EValence.Negative));
        Assert.That(pos, Is.Not.EqualTo(neg));
    }

    [Test]
    public void ResolveVisible_DropsInvisibleTokens_UnlessShowAll()
    {
        var c = new TokenContainer();
        c.AddToken(new Token(TokenType.Shaken, 1, new TokenClearTrigger.ManualOnly()));
        c.AddToken(new Token(new TokenType("AbilityUsed:Furious"), 1, new TokenClearTrigger.ActivationEnd()));

        var visible = TokenChipRenderer.ResolveVisible(c, null, isModelScoped: false, showInvisible: false);
        Assert.That(visible.Count, Is.EqualTo(1));
        Assert.That(visible[0].DisplayId, Is.EqualTo(TokenType.SHAKEN_ID));
        Assert.That(visible.Any(t => t.DisplayId.StartsWith("AbilityUsed")), Is.False);

        var all = TokenChipRenderer.ResolveVisible(c, null, isModelScoped: false, showInvisible: true);
        Assert.That(all.Count, Is.EqualTo(2));
    }

    [Test]
    public void ResolveVisible_SortsFirstClassBeforeNormal()
    {
        var c = new TokenContainer();
        c.AddToken(new Token(TokenType.RuleGrant, 1, new TokenClearTrigger.ManualOnly(),
            Payload: new TokenPayload.RuleGrant("Regeneration", ELifetime.Aura)));
        c.AddToken(new Token(TokenType.Shaken, 1, new TokenClearTrigger.ManualOnly()));

        var visible = TokenChipRenderer.ResolveVisible(c, null, isModelScoped: false, showInvisible: false);
        Assert.That(visible[0].Prominence, Is.EqualTo(ETokenProminence.FirstClass));
    }

    private static TokenDisplayInfo Info(string id, EValence valence = EValence.Neutral,
        ETokenProminence prominence = ETokenProminence.Normal,
        ETokenColor? color = null, ETokenShape? shape = null) =>
        new(id, id, "", valence, prominence, 1, false, "", color, shape, null);
}
