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

    /// <summary>
    /// #424: the compact "what is this weapon for" label the hover, the wound dialog and the unit picker print
    /// next to a weapon's name - <c>24"</c> for a gun, <paramref name="meleeWord"/> for a close-combat weapon,
    /// and <c>12"/melee</c> for a weapon that is BOTH (Assault), so the player can see the pistol swings too.
    /// Reads the classification seam (<c>IsRanged</c> / <c>IsMelee</c>), never the range directly.
    /// </summary>
    public static string RangeLabel(IWeapon weapon, string meleeWord)
    {
        bool ranged = weapon.IsRanged();
        bool melee = weapon.IsMelee();
        string range = $"{weapon.RangeInches:0.#}\"";
        if (ranged && melee) return $"{range}/{meleeWord}";
        return ranged ? range : meleeWord;
    }
}
