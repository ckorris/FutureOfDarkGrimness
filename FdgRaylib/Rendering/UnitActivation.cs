using FDG;

namespace FdgRaylib.Rendering;

/// <summary>
/// #329 — the one definition of "this unit has spent its activation this round", shared by the canvas
/// tooltip/labels and the army list overlay so the two can never disagree about a red Activated marker.
/// </summary>
public static class UnitActivation
{
    /// <summary>
    /// A unit has spent its activation this round once the main phase is under way (RoundCount != null)
    /// and it is no longer in the unactivated pool. The unit currently taking its turn stays in the pool
    /// until the turn ends (SingleTurnStage.MarkUnitAsActivated), and is excluded here too so a unit
    /// mid-activation does not prematurely read as done. Outside the main phase nothing is activated.
    /// </summary>
    public static bool HasActivated(IGameProgress progress, IUnit unit)
    {
        if (progress.RoundCount == null) return false;
        if (progress.ActivatingUnit?.ID.Equals(unit.ID) == true) return false;
        // #404: a unit held off-table has spent nothing - it cannot act until it arrives. It is absent
        // from the pool for the OPPOSITE reason an activated unit is (SingleRoundContext's
        // SetUnactivatedUnits admits only units on the battlefield or embarked), so the pool test alone
        // read every Ambush reserve as Activated in the army list.
        if (IsInReserve(unit)) return false;

        foreach (var u in progress.UnactivatedUnits)
            if (u.ID.Equals(unit.ID)) return false;
        return true;
    }

    /// <summary>
    /// #404: the unit is held off-table in reserve (Ambush) and has not arrived yet. A distinct state
    /// from both "ready" and "activated", and the one the army list labels "In Reserve".
    /// </summary>
    public static bool IsInReserve(IUnit unit) => FDG.Rules.Dispatch.ReserveRules.IsInReserve(unit);
}
