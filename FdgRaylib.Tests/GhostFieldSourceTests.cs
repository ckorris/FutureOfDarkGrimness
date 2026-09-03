using System.Collections.Generic;
using FDG;
using FDG.Data;
using FdgRaylib.Rendering.Resolvers;
using NUnit.Framework;

namespace FdgRaylib.Tests;

/// <summary>
/// #230 — the tactical overlay's opportunity field ("what can I hit from here") was reachable only from
/// the movement resolver; placement now offers its ghosts through the same seam, so a deployment / ambush
/// / teleport spot can be judged by what it would reach, LoS and cover included.
///
/// <para>The field's own geometry is #162's and already covered; what is new and worth pinning is the
/// ROUTING — that the overlay hands the controller a source only while that resolver is actually pending,
/// and that a placement with nothing on screen reports nothing rather than leaving stale reach painted
/// over the table. Draw-side ghost capture is ImGui, so it stays hand-verified (same split as #280's
/// PreviewSourceTests).</para>
/// </summary>
[TestFixture]
public class GhostFieldSourceTests
{
    private sealed class FakeResolver : IGuiResolver
    {
        public bool HasPendingRequest { get; set; }
        public void Draw(int screenW, int screenH) { }
    }

    private sealed class FakeGhostSource : IGuiResolver, IGhostFieldSource
    {
        public bool HasPendingRequest { get; set; }
        public void Draw(int screenW, int screenH) { }

        public bool TryGetGhostField(out IUnit unit, out IReadOnlyDictionary<IModel, Position> ghosts)
        {
            unit = null!;
            ghosts = new Dictionary<IModel, Position>();
            return false;
        }
    }

    private static GuiPlaceObjectsResolver<ModelData> MakePlacementResolver()
    {
        var store = GameDataStore.GameDataStoreBuilder.GetDefault();
        return new GuiPlaceObjectsResolver<ModelData>(new TableState(store), new FormationModeState());
    }

    [Test]
    public void PlacementResolver_OffersItselfAsAGhostFieldSource()
    {
        // The interface is what routes the field to placement at all; dropping it would silently take the
        // feature away with nothing failing to compile.
        Assert.That(MakePlacementResolver(), Is.InstanceOf<IGhostFieldSource>());
    }

    [Test]
    public void PlacementResolver_WithNoPendingRequest_OffersNoGhostField()
    {
        var resolver = MakePlacementResolver();

        Assert.That(resolver.TryGetGhostField(out _, out var ghosts), Is.False,
            "a resolver that isn't placing anything must not leave reach painted on the table");
        Assert.That(ghosts, Is.Empty);
    }

    [Test]
    public void ActiveGhostField_IsThePendingResolversSource()
    {
        var overlay = new GuiResolverOverlay();
        var idle = new FakeGhostSource { HasPendingRequest = false };
        var pending = new FakeGhostSource { HasPendingRequest = true };
        overlay.Register(idle);
        overlay.Register(pending);

        Assert.That(overlay.ActiveGhostField, Is.SameAs(pending));
    }

    [Test]
    public void ActiveGhostField_IsNullWhenNothingIsPending()
    {
        var overlay = new GuiResolverOverlay();
        overlay.Register(new FakeGhostSource { HasPendingRequest = false });

        Assert.That(overlay.ActiveGhostField, Is.Null,
            "between decisions there is no placement to anchor a field on");
    }

    [Test]
    public void ActiveGhostField_IsNullWhenThePendingResolverDoesNotOptIn()
    {
        // A pending resolver that isn't a placement (a dice prompt, a target pick) must not inherit the
        // previous placement's field just by being the active one.
        var overlay = new GuiResolverOverlay();
        overlay.Register(new FakeGhostSource { HasPendingRequest = false });
        overlay.Register(new FakeResolver { HasPendingRequest = true });

        Assert.That(overlay.ActiveGhostField, Is.Null);
    }
}
