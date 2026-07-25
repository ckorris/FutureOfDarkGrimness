namespace FdgRaylib.Rendering.Previews;

// #277: the "ghosts + paths" preview vocabulary shared by the movement family (movement now;
// consolidation / aircraft advance / placements are expected to reuse it as they opt in).
// Two slots split cache-friendly from stream:
//  - "base"  (GhostPathBase): model roster + committed waypoints. Changes at click cadence;
//    receivers cache it, and ghost entries reference models by roster INDEX instead of repeating
//    38-char GUIDs at 10 Hz.
//  - "ghost" (GhostPathGhosts): the live pending positions (mouse ghost / group phantoms).
//    Entries whose ghost sits on the committed endpoint are omitted - they carry no information
//    (single mode then streams ~1 entry, not the whole unit).
// BaseVersion is a content hash stamped on both payloads by the same build pass; a ghost payload
// whose version doesn't match the cached base is dropped rather than composed against a stale
// roster. All floats are table inches quantized to 0.01" (sub-pixel at any realistic zoom), which
// also makes the publisher's serialize-and-compare dedup absorb sub-0.01" mouse jitter.
// Everything else (base shapes, colors, names) resolves from the receiver's synced table state.

public static class GhostPathSlots
{
    public const string Base = "base";
    public const string Ghost = "ghost";
}

/// <summary>Move band of a path/ghost, mirroring the local overlay's color language.
/// Int-backed on the wire for version-skew tolerance.</summary>
public static class GhostPathBands
{
    public const int Advance = 0;
    public const int Rush = 1;
    public const int Charge = 2;
    /// <summary>No band semantics - consolidation slides and placements. Cyan, the same "committed
    /// move" color language those resolvers use locally.</summary>
    public const int Neutral = 3;
}

public sealed record GhostPathPoint(float X, float Z);

/// <summary>One model's committed plan: waypoints walked so far, and the facing + band at its
/// current path endpoint. <paramref name="ModelId"/> is the engine's stable per-model Guid.</summary>
public sealed record GhostPathBaseModel(Guid ModelId, IReadOnlyList<GhostPathPoint> Waypoints,
    float FacingX, float FacingZ, int Band);

public sealed record GhostPathBase(int BaseVersion, IReadOnlyList<GhostPathBaseModel> Models);

/// <summary>One live pending position; <paramref name="ModelIndex"/> indexes the base roster.</summary>
public sealed record GhostPathGhost(int ModelIndex, float X, float Z,
    float FacingX, float FacingZ, int Band);

public sealed record GhostPathGhosts(int BaseVersion, IReadOnlyList<GhostPathGhost> Ghosts);
