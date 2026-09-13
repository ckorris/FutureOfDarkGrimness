using System.Collections.Generic;
using System.Linq;
using FDG;

namespace FdgRaylib.Rendering;

/// <summary>
/// Shared, ASCII-only formatting of a weapon's special-rule names (#027) so every surface that shows weapon
/// stats also shows the weapon's rules (Rending, Blast(3), Deadly(3)...): the unit hover tooltip, the shoot
/// weapon list, the deploy panel, and the unit picker. The engine's
/// <c>IWeaponExtensions.GetWeaponNameAndStats</c> already appends these on a full datasheet line; this is the
/// same list, exposed for the compact call sites that assemble their own stat string.
/// </summary>
public static class WeaponStatFormatter
{
    /// <summary>The rule display names joined "Rending, Blast(3)", or "" when there are none.</summary>
    public static string RuleList(IEnumerable<string> ruleDisplayNames) =>
        string.Join(", ", ruleDisplayNames);

    /// <summary>A weapon's special-rule display names joined, or "" when the weapon carries none.</summary>
    public static string RuleList(IWeapon weapon) =>
        RuleList(weapon.RuleDefinitions.Select(r => r.RequestedName));
}
