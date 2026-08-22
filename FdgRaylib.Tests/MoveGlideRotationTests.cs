using System;
using System.Collections.Generic;
using FDG;
using FDG.Data;
using FDG.Presentation.Beats;
using FdgRaylib.Rendering.Presentation;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #341: a move beat carries the attitude at each waypoint, and the glide TURNS the model between them
// instead of snapping it. That is the visible half of "a rotation belongs to the node it was placed at":
// the engine no longer applies a turn to the ground before its node, so the turn has to happen across the
// leg into it. Before this the authoritative facing was already final when the animation started, so every
// rotation popped on frame one and multi-node paths never showed the model turning at all.
[TestFixture]
public class MoveGlideRotationTests
{
    private const float DurationSeconds = 1.0f;

    private GameDataStore _store = null!;

    [SetUp]
    public void SetUp() => _store = GameDataStore.GameDataStoreBuilder.GetDefault();

    [Test]
    public void Facing_TurnsAcrossTheLeg_ReachingTheNodesAttitudeOnArrival()
    {
        // Straight 10" run, turning 90deg from +Z to +X over the leg.
        ModelData model = MakeModel(new Position(0f, 0f), new Float2(0f, 1f));
        var player = Play(model, new List<Position> { new Position(0f, 0f), new Position(0f, 10f) },
            new List<Float2> { new Float2(0f, 1f), new Float2(1f, 0f) });

        AssertHeadingDegrees(player, model, 90f, "the pose the model set off in, before any progress");

        player.Update(DurationSeconds * 0.5f);
        AssertHeadingDegrees(player, model, 45f, "halfway along the leg it is halfway through the turn");

        player.Update(DurationSeconds * 0.499f);
        AssertHeadingDegrees(player, model, 0f, "it arrives at the attitude the node was placed with");
    }

    [Test]
    public void Facing_TurnsPerLeg_NotOnceAcrossTheWholePath()
    {
        // Two legs: run +Z facing +Z, then run +X facing +X. The corner is where the turn happens - at the
        // midpoint of the whole beat the model is AT the corner and has just finished turning, not halfway
        // through a single sweep from start attitude to end attitude.
        ModelData model = MakeModel(new Position(0f, 0f), new Float2(0f, 1f));
        var player = Play(model,
            new List<Position> { new Position(0f, 0f), new Position(0f, 10f), new Position(10f, 10f) },
            new List<Float2> { new Float2(0f, 1f), new Float2(0f, 1f), new Float2(1f, 0f) });

        player.Update(DurationSeconds * 0.25f);
        AssertHeadingDegrees(player, model, 90f, "the first leg carries no turn at all");

        player.Update(DurationSeconds * 0.5f);
        AssertHeadingDegrees(player, model, 45f, "halfway along the SECOND leg, halfway through its turn");
    }

    [Test]
    public void Facing_PivotInPlace_StillAnimates()
    {
        // A move that does not travel: the polyline has no length, so a length-weighted glide has nothing to
        // distribute - but the turn still has to play, or a rotate-on-the-spot would look like a dropped frame.
        ModelData model = MakeModel(new Position(3f, 3f), new Float2(0f, 1f));
        var player = Play(model, new List<Position> { new Position(3f, 3f), new Position(3f, 3f) },
            new List<Float2> { new Float2(0f, 1f), new Float2(1f, 0f) });

        player.Update(DurationSeconds * 0.5f);
        AssertHeadingDegrees(player, model, 45f, "the pivot animates over the beat's own duration");
    }

    [Test]
    public void Facing_TakesTheShortWayRound()
    {
        // -90deg, not +270deg. Turning the long way is the tell that the angles were interpolated raw.
        ModelData model = MakeModel(new Position(0f, 0f), new Float2(1f, 0f));
        var player = Play(model, new List<Position> { new Position(0f, 0f), new Position(0f, 10f) },
            new List<Float2> { new Float2(1f, 0f), new Float2(0f, -1f) });

        player.Update(DurationSeconds * 0.5f);
        AssertHeadingDegrees(player, model, -45f, "halfway through a 90deg turn clockwise, not 135deg round");
    }

    [Test]
    public void Facing_BeatWithoutFacings_LeavesTheModelsOwn()
    {
        // AI moves, aircraft and holds carry none: the renderer must fall through to the authoritative
        // facing rather than be handed a default that would point every gliding model at +X.
        ModelData model = MakeModel(new Position(0f, 0f), new Float2(0f, 1f));
        var player = Play(model, new List<Position> { new Position(0f, 0f), new Position(0f, 10f) }, facings: null);

        player.Update(DurationSeconds * 0.5f);
        Assert.That(player.GetModelDrawState(model).Facing, Is.Null);
    }

    [Test]
    public void Facing_MismatchedFacingCount_IsIgnoredRatherThanMispaired()
    {
        // A list that does not pair 1:1 with the polyline would turn the model toward the wrong node, which
        // reads far worse on screen than not turning it. The engine pads before sending; this is the guard
        // for anything that does not (an older peer, a hand-built beat).
        ModelData model = MakeModel(new Position(0f, 0f), new Float2(0f, 1f));
        var player = Play(model,
            new List<Position> { new Position(0f, 0f), new Position(0f, 5f), new Position(0f, 10f) },
            new List<Float2> { new Float2(0f, 1f), new Float2(1f, 0f) });

        player.Update(DurationSeconds * 0.5f);
        Assert.That(player.GetModelDrawState(model).Facing, Is.Null);
    }

    private static PresentationPlayer Play(ModelData model, List<Position> waypoints, List<Float2>? facings)
    {
        var player = new PresentationPlayer();
        player.OnBeat(new UnitMovedBeat(new UnitID(Guid.NewGuid()), "Bikers",
            new List<ModelMove> { new ModelMove(model.ID, waypoints, facings) },
            TimeSpan.FromSeconds(DurationSeconds)));
        return player;
    }

    private static void AssertHeadingDegrees(PresentationPlayer player, ModelData model, float expected,
        string because)
    {
        Float2? facing = player.GetModelDrawState(model).Facing;
        Assert.That(facing, Is.Not.Null, because);
        float degrees = MathF.Atan2(facing!.Value.Y, facing.Value.X) * 180f / MathF.PI;
        Assert.That(degrees, Is.EqualTo(expected).Within(1f), because);
    }

    private ModelData MakeModel(Position at, Float2 facing)
    {
        var model = new ModelData(0.5f, new List<Weapon>(), at, _store);
        model.SetFacing(facing);
        _store.Create(model);
        return model;
    }
}
