using System;
using FDG;
using FDG.Presentation.Beats;
using FdgRaylib.Rendering.Presentation;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #244 — the dice caption strip fades in and out instead of popping. PresentationPlayer owns the
// alpha: eased in over the start of a beat, eased out over the end of a non-held beat's duration or
// the tail of a held beat's linger, and skipped entirely when a new roll replaces a still-visible
// panel (no blink to zero between back-to-back rolls).
[TestFixture]
public class DicePanelAlphaTests
{
    private static DiceRolledBeat Dice(bool held) =>
        new(new[] { 1f, 1f, 1f, 1f, 1f, 1f }, 1, 4, ERandomnessType.Realistic, "Roll to Hit", held: held);

    [Test]
    public void FreshRoll_FadesIn()
    {
        var player = new PresentationPlayer();
        player.OnBeat(Dice(held: false));

        player.Update(0.05f);
        Assert.That(player.TryGetActiveDice(out _, out _, out float alpha), Is.True);
        Assert.That(alpha, Is.GreaterThan(0f).And.LessThan(1f), "mid fade-in");

        player.Update(0.15f);
        player.TryGetActiveDice(out _, out _, out alpha);
        Assert.That(alpha, Is.EqualTo(1f), "fully faded in after the ease window");
    }

    [Test]
    public void NonHeldRoll_FadesOutOverItsTail()
    {
        var player = new PresentationPlayer();
        DiceRolledBeat beat = Dice(held: false);
        player.OnBeat(beat);

        float dur = (float)beat.NominalDuration.TotalSeconds;
        player.Update(dur - 0.1f);
        Assert.That(player.TryGetActiveDice(out _, out _, out float alpha), Is.True);
        Assert.That(alpha, Is.GreaterThan(0f).And.LessThan(1f), "mid fade-out near the beat's end");

        player.Update(0.2f);
        Assert.That(player.TryGetActiveDice(out _, out _, out _), Is.False, "cleared after its duration");
    }

    [Test]
    public void HeldRoll_ParksAtFullAlpha_ThenFadesOutOverTheLingerTail()
    {
        var player = new PresentationPlayer();
        player.OnBeat(Dice(held: true));

        player.Update(0.7f); // past the 600ms HoldLeadIn — parks
        Assert.That(player.TryGetActiveDice(out _, out float progress, out float alpha), Is.True);
        Assert.That(progress, Is.EqualTo(1f), "parked dice display settled");
        Assert.That(alpha, Is.EqualTo(1f), "parked at full alpha");

        player.Update(2.0f); // linger 2.0s of 2.5s — before the fade tail
        player.TryGetActiveDice(out _, out _, out alpha);
        Assert.That(alpha, Is.EqualTo(1f), "still solid before the linger's fade tail");

        player.Update(0.3f); // linger 2.3s — inside the fade tail
        Assert.That(player.TryGetActiveDice(out _, out _, out alpha), Is.True);
        Assert.That(alpha, Is.GreaterThan(0f).And.LessThan(1f), "fading over the linger tail");

        player.Update(0.3f); // linger 2.6s — expired
        Assert.That(player.TryGetActiveDice(out _, out _, out _), Is.False, "cleared after the linger");
    }

    [Test]
    public void ReplacingAVisiblePanel_SkipsTheFadeIn()
    {
        var player = new PresentationPlayer();
        player.OnBeat(Dice(held: true));
        player.Update(0.7f); // first roll parked and visible

        DiceRolledBeat second = Dice(held: false);
        player.OnBeat(second);
        player.Update(0.01f);

        Assert.That(player.TryGetActiveDice(out DiceRolledBeat shown, out _, out float alpha), Is.True);
        Assert.That(shown, Is.SameAs(second), "the new roll replaced the parked one");
        Assert.That(alpha, Is.EqualTo(1f), "no blink to zero between back-to-back rolls");
    }
}
