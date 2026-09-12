using FDG.ArmyBuilding;
using FDG.Players;

namespace FdgRaylib.Rendering;

/// <summary>
/// How a lobby roster row's army reads against the lobby's Army Source setting (#400) - the game-system
/// sibling of <see cref="LobbyPointsStatus"/>, and split out of the drawing code for the same reason:
/// the rule and its wording are arithmetic over a summary, so they can be unit-tested without ImGui.
/// </summary>
public static class LobbyArmySource
{
    /// <summary>
    /// True when this row's army comes from a game system the lobby doesn't accept, i.e. when the
    /// Faction cell should read red. An UNASSIGNED row is never wrong-system - it has no system to be
    /// wrong about, and its own problem (no army at all) is called out on the Army cell instead.
    /// </summary>
    public static bool IsWrongSystem(ArmyListSummary summary, EAllowedGameSystems allowed) =>
        summary.IsAssigned && !GameSystems.IsAllowed(allowed, summary.GameSystem);

    /// <summary>Why the Faction cell is red. Names both sides - what the army is and what the lobby
    /// takes - because the fix could be either one (load a different army, or change the setting).</summary>
    public static string WrongSystemTooltip(ArmyListSummary summary, EAllowedGameSystems allowed) =>
        $"{GameSystems.DisplayName(summary.GameSystem)} army, but this lobby takes "
        + $"{GameSystems.DisplayName(allowed)} armies only.\n"
        + "Launch is blocked until this row is legal.";

    /// <summary>Why the Army cell reads "N/A" in red: no army on this slot at all. Blocking since #400 -
    /// the host used to substitute a 100-pt stub, silently putting a player in a game with an army they
    /// never picked.</summary>
    public const string NoArmyTooltip =
        "No army assigned.\nLoad one (or roll Random Army). Launch is blocked until every slot has one.";
}
