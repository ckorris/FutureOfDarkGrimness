using FDG;
using FDG.Data;
using FDG.Rules.Dispatch;
using FDG.StageResolution.Requests;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// #315 pure lookups linking embarked units to their transports within a unit-selection request. An
/// embarked unit's transport is always one of the acting player's own units, so it already sits in the
/// request's valid or invalid option lists — these search there instead of needing table state. Used by
/// <see cref="GuiUnitSelectionResolver"/> for the two-way hover binding: list row -> ring the transport
/// on the canvas, canvas transport -> emphasise its occupants' rows.
/// </summary>
public static class TransportOptionLookup
{
    /// <summary>The transport <paramref name="unit"/> is embarked in, found among the request's own
    /// options; null when the unit is not embarked (or the transport is somehow not listed).</summary>
    public static UnitData? FindTransportOf(SelectionRequest<UnitData> request, UnitData unit)
    {
        UnitID? transportId = TransportUtilities.GetTransportId(unit);
        if (transportId == null) return null;

        foreach (UnitData candidate in AllOptionUnits(request))
        {
            if (candidate.ID == transportId.Value) return candidate;
        }
        return null;
    }

    /// <summary>References of every option (valid and invalid — an already-activated occupant still
    /// needs the link) currently embarked in <paramref name="transport"/>. Empty for non-transports.</summary>
    public static IReadOnlyList<DataReference> CargoOptionRefs(
        SelectionRequest<UnitData> request, IUnit transport)
    {
        List<DataReference> refs = new();
        foreach (SelectionRequest<UnitData>.ValidOption opt in request.ValidOptions)
        {
            if (IsEmbarkedIn(opt.Option.GetValue(), transport)) refs.Add(opt.Option.Reference);
        }
        foreach (SelectionRequest<UnitData>.InvalidOption opt in request.InvalidOptions)
        {
            if (IsEmbarkedIn(opt.Option.GetValue(), transport)) refs.Add(opt.Option.Reference);
        }
        return refs;
    }

    private static bool IsEmbarkedIn(UnitData unit, IUnit transport) =>
        TransportUtilities.GetTransportId(unit) is UnitID id && id == transport.ID;

    private static IEnumerable<UnitData> AllOptionUnits(SelectionRequest<UnitData> request)
    {
        foreach (SelectionRequest<UnitData>.ValidOption opt in request.ValidOptions)
            yield return opt.Option.GetValue();
        foreach (SelectionRequest<UnitData>.InvalidOption opt in request.InvalidOptions)
            yield return opt.Option.GetValue();
    }
}
