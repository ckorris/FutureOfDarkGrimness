using FDG;

namespace FdgRaylib.Rendering.Previews;

/// <summary>One slot of a preview: which cache slot it lands in and the payload object for it.</summary>
public sealed record PreviewSlotPayload(string Slot, object Payload);

/// <summary>
/// A resolver's shareable preview for the current frame (#280): who is deciding, and the payload
/// per slot. Slot order matters - the publisher sends them in order, so put slow-changing state
/// (rosters, committed paths) before the stream that references it.
/// </summary>
public sealed record PreviewState(PlayerID SourcePlayerID, IReadOnlyList<PreviewSlotPayload> Slots);

/// <summary>
/// Optional interface on GUI resolvers (#280): a resolver that wants other players to see its
/// in-progress decision (ghost models, planned paths) returns its current preview here. Called on
/// the main thread by <see cref="PreviewPublisher"/> at ~10 Hz while the resolver has a pending
/// request; the publisher handles throttling, dedup, and the end-of-request clear, so this just
/// reports state. Return null to share nothing (e.g. before any meaningful state exists).
/// Resolvers that never opt in simply don't implement this - the whole feature is per-resolver
/// opt-in, which is also the escape hatch for intent-sensitive future request types.
/// </summary>
public interface IPreviewSource
{
    PreviewState? BuildPreviewState();
}
