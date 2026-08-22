namespace FdgRaylib.Rendering;

/// <summary>How a lobby roster row's army points read against the lobby's points limit.</summary>
public enum ELobbyPointsStatus
{
    /// <summary>Within the budget (or no army to judge). Drawn in the ordinary text colour.</summary>
    Ok,

    /// <summary>At least <see cref="LobbyPointsStatus.UnderWarningThreshold"/> points UNDER the limit.
    /// Legal, but the player is giving away a chunk of their budget. Drawn in yellow.</summary>
    Under,

    /// <summary>Over the limit. The #153 launch gate flags this. Drawn in red.</summary>
    Over,
}

/// <summary>
/// The Pts column's colour rule, as arithmetic rather than ImGui calls so it can be unit-tested — the
/// same split as <see cref="Resolvers.PlacementPanelLayout"/> and <see cref="Resolvers.ModelRoster"/>.
/// </summary>
public static class LobbyPointsStatus
{
    /// <summary>An army this far (or further) under the limit reads as UNDERBUILT. Wide enough that a
    /// list a model or two short of the budget isn't nagged about, and it matches the granularity a
    /// player actually builds at.</summary>
    public const int UnderWarningThreshold = 50;

    /// <param name="isAssigned">False for a slot with no army loaded. Such a row is never
    /// <see cref="ELobbyPointsStatus.Under"/> — it has no army to be under WITH, and painting every
    /// empty row yellow the moment a lobby opens is noise, not a warning. It can still read
    /// <see cref="ELobbyPointsStatus.Over"/>, which would mean a 0-point army against a negative
    /// limit; kept rather than special-cased so the over-check has exactly one form.</param>
    public static ELobbyPointsStatus Classify(bool isAssigned, int pointCost, int pointsLimit)
    {
        if (pointCost > pointsLimit) return ELobbyPointsStatus.Over;
        if (isAssigned && pointCost <= pointsLimit - UnderWarningThreshold) return ELobbyPointsStatus.Under;
        return ELobbyPointsStatus.Ok;
    }
}
