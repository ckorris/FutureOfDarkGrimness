using FDG.StageResolution.Requests;

namespace FdgRaylib.Placement;

/// <summary>
/// #426 — why a placement spot inside one of the request's enemy keep-out discs is refused, worded once for
/// both front ends (the CLI and GUI placement resolvers). The stage words its own clause when it has one -
/// Scout's "over 9in away from non-Scout enemy units", Vanguard's and Fanatic's "non-Vanguard" - built from
/// the same number as the discs, so no resolver restates a distance; an Ambush arrival's discs are Repel
/// Ambushers keep-aways, which keep their established wording.
/// </summary>
public static class PlacementKeepOutText
{
    public static string Reason<T>(PlaceObjectsRequest<T> request) =>
        request.EnemyKeepOutNote.Length > 0
            ? request.EnemyKeepOutNote
            : "Inside an enemy's Repel Ambushers keep-away - set up further from that unit " +
              "(or within a friendly Ambush Beacon's range).";
}
