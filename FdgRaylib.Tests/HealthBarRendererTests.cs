using System;
using System.Collections.Generic;
using System.Linq;
using FDG;
using FDG.Data;
using FdgRaylib.Rendering;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #152: unit health bars. Pure float logic — hide-at-full (Civ-style), fraction clamping, color ramp.
[TestFixture]
public class HealthBarRendererTests
{
    [Test]
    public void ShouldShow_IsHiddenAtFullStrength_ShownWhenDamaged()
    {
        Assert.That(HealthBarRenderer.ShouldShow(9f, 9f), Is.False, "exactly full is hidden");
        Assert.That(HealthBarRenderer.ShouldShow(8.9999f, 9f), Is.False, "within epsilon of full is hidden (no float-noise sliver)");
        Assert.That(HealthBarRenderer.ShouldShow(8f, 9f), Is.True, "a Tough model down one wound (but alive) still shows the bar");
        Assert.That(HealthBarRenderer.ShouldShow(2.5f, 3f), Is.True, "fractional (deterministic-path) wounds show");
        Assert.That(HealthBarRenderer.ShouldShow(0f, 5f), Is.True);
    }

    [Test]
    public void ShouldShow_HandlesDegenerateMax()
    {
        Assert.That(HealthBarRenderer.ShouldShow(0f, 0f), Is.False);
        Assert.That(HealthBarRenderer.ShouldShow(5f, 0f), Is.False);
    }

    [Test]
    public void Fraction_IsAFloatRatio_ClampedToUnitRange()
    {
        Assert.That(HealthBarRenderer.Fraction(2.5f, 3f), Is.EqualTo(0.8333f).Within(0.001f));
        Assert.That(HealthBarRenderer.Fraction(8f, 9f), Is.EqualTo(0.8889f).Within(0.001f));
        Assert.That(HealthBarRenderer.Fraction(-2f, 5f), Is.EqualTo(0f), "overkill / negative clamps to 0");
        Assert.That(HealthBarRenderer.Fraction(10f, 5f), Is.EqualTo(1f), "over-full clamps to 1");
        Assert.That(HealthBarRenderer.Fraction(1f, 0f), Is.EqualTo(0f));
    }

    [Test]
    public void FillColor_RampsGreenToRed()
    {
        Assert.That(HealthBarRenderer.FillColor(1f), Is.EqualTo(((byte)70, (byte)200, (byte)90)), "full = green");
        Assert.That(HealthBarRenderer.FillColor(0.5f), Is.EqualTo(((byte)230, (byte)200, (byte)60)), "half = yellow");
        Assert.That(HealthBarRenderer.FillColor(0f), Is.EqualTo(((byte)220, (byte)60, (byte)60)), "empty = red");
    }

    [Test]
    public void FillColor_SnapsToYellowAtHalfStrength()
    {
        // The morale cliff is at <=50%, so the colour changes AT 50% rather than easing through it:
        // flat green above half, yellow the instant it reaches half.
        Assert.That(HealthBarRenderer.FillColor(0.75f), Is.EqualTo(((byte)70, (byte)200, (byte)90)), "above half = flat green");
        Assert.That(HealthBarRenderer.FillColor(0.5001f), Is.EqualTo(((byte)70, (byte)200, (byte)90)), "just above half = still green");
        Assert.That(HealthBarRenderer.FillColor(0.5f), Is.EqualTo(((byte)230, (byte)200, (byte)60)), "at half = yellow");
    }

    // #347 — the bar gains a scale: a rule at every point where the unit loses a MODEL, so "how many more
    // hits before someone dies" reads off the bar instead of being arithmetic.
    [Test]
    public void CasualtyTicks_MarkEveryModelBoundary_OnASquadOfOneWoundModels()
    {
        Assert.That(HealthBarRenderer.CasualtyTicks(Squad(1f, 1f, 1f, 1f, 1f)),
            Is.EqualTo(new[] { 0.2f, 0.4f, 0.6f, 0.8f }).Within(0.0001f),
            "5 troopers: a tick at every fifth, and none at the far end (that boundary IS the bar's end)");
    }

    [Test]
    public void CasualtyTicks_AreToughAware()
    {
        // Tough(3) x 4: 12 wounds, a body lost every three. Spacing is what carries the meaning - three
        // wounds' worth of bar per model, not one.
        Assert.That(HealthBarRenderer.CasualtyTicks(Squad(3f, 3f, 3f, 3f)),
            Is.EqualTo(new[] { 0.25f, 0.5f, 0.75f }).Within(0.0001f));
    }

    [Test]
    public void CasualtyTicks_AreEmptyForASingleModelUnit()
    {
        Assert.That(HealthBarRenderer.CasualtyTicks(Squad(1f)), Is.Empty, "nothing to lose but itself");
        Assert.That(HealthBarRenderer.CasualtyTicks(Squad(6f)), Is.Empty,
            "a lone Tough(6) monster likewise - the ticks would only re-draw the border");
    }

    [Test]
    public void CasualtyTicks_HandleAMixedUnit()
    {
        // A joined Tough(3) hero on 4 one-wound troopers: 7 wounds, boundaries at 3, 4, 5, 6. The set is
        // right whatever order the roster is in; only the arrangement varies, and the bar's fill is an
        // aggregate anyway, so no tick could name a particular model.
        Assert.That(HealthBarRenderer.CasualtyTicks(Squad(3f, 1f, 1f, 1f, 1f)).Select(t => t * 7f),
            Is.EqualTo(new[] { 3f, 4f, 5f, 6f }).Within(0.0001f));
    }

    [Test]
    public void CasualtyTicks_AreEmptyForAUnitWithNoWounds()
    {
        Assert.That(HealthBarRenderer.CasualtyTicks(Squad()), Is.Empty, "no models, no division by zero");
    }

    // A unit of models with the given max wounds each. Real UnitData/ModelData - the tick maths reads
    // TotalWounds off IModel, and a hand-rolled fake would not prove it reads the right field.
    private static IUnit Squad(params float[] woundsPerModel)
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        var bindings = new List<DataBinding<ModelData>>();
        foreach (float wounds in woundsPerModel)
        {
            var model = new ModelData(baseRadiusInches: 0.5f, weapons: new List<Weapon>(),
                initialPosition: new Position(), gameDataStore: store);
            model.SetMaxWounds((int)wounds);
            bindings.Add(store.GetDataBinding<ModelData>(store.Create(model)));
        }
        return new UnitData(new PlayerID(Guid.NewGuid()), "Squad", quality: 4, defense: 4,
            modelBindings: bindings);
    }
}
