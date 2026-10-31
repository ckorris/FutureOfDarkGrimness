using FDG;

namespace FdgRaylib.Placement;

/// <summary>
/// Who counts as an ENEMY, app-side. The engine's authority
/// (<c>MovementUtilities.GetEnemyModelFootprints</c>) says it plainly: "enemies are everyone not on the
/// moving unit's team". The resolvers had each rolled their own test as <c>unit.PlayerID == me</c>,
/// which only excludes YOUR OWN units - so in a team game a teammate controlled by another player was
/// treated as hostile.
///
/// <para>Visible as: fire lines, charge arrows and threat rings drawn onto allied units during a move
/// (the preview then disagreeing with what the engine would actually allow), and enemy-spacing rules
/// being enforced against allies during deployment and consolidation.</para>
///
/// <para>Falls back to a plain player comparison when the player is on no team at all, which is what
/// every solo/1v1 path relies on and keeps those cases byte-identical.</para>
/// </summary>
internal static class TeamAwareness
{
    /// <summary>True when <paramref name="other"/> is hostile to <paramref name="me"/>.</summary>
    public static bool IsEnemy(ITableState tableState, PlayerID me, PlayerID other)
    {
        ITeam? myTeam = tableState.Teams.Objects.FirstOrDefault(t => t.IsPlayerOnTeam(me));
        return myTeam != null ? !myTeam.IsPlayerOnTeam(other) : !other.Equals(me);
    }

    /// <summary>True when <paramref name="unit"/> is hostile to <paramref name="me"/>.</summary>
    public static bool IsEnemyUnit(ITableState tableState, PlayerID me, IUnit unit)
        => IsEnemy(tableState, me, unit.PlayerID);
}
