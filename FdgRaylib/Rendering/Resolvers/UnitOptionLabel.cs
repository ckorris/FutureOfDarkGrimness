using System.Collections.Generic;
using System.Linq;

namespace FdgRaylib.Rendering.Resolvers;

/// <summary>
/// Pure formatter for a unit's dialog content in the deploy / activate unit picker
/// (<see cref="GuiUnitSelectionResolver"/>): a bright heading (the unit name) plus smaller, dimmer detail
/// lines (a stat line, then weapons) — mirroring the model picker and the wound-assignment dialog so every
/// selector reads the same. Unit-wide special rules are deliberately omitted (they belong in the hover
/// tooltip, not the pick button). Extracted so the formatting is unit-testable without a live unit.
/// Game-facing text: ASCII only (CLAUDE.md).
/// </summary>
public static class UnitOptionLabel
{
    /// <param name="displayName">The option's display name (may already carry a suffix like "(Ambush)").</param>
    /// <param name="distinctWeapons">One entry per distinct weapon (name + range in inches; 0 = melee).</param>
    public static (string Heading, IReadOnlyList<string> Details) Build(
        string displayName, int liveModelCount, int quality, int defense,
        IReadOnlyList<(string Name, float RangeInches)> distinctWeapons)
    {
        string plural = liveModelCount == 1 ? "model" : "models";
        var details = new List<string>
        {
            $"{liveModelCount} {plural}, Q{quality}+ D{defense}+",
            distinctWeapons.Count > 0
                ? string.Join(", ", distinctWeapons.Select(w =>
                    $"{w.Name} ({(w.RangeInches > 0f ? $"{w.RangeInches:0.#}\"" : "melee")})"))
                : "(no weapons)",
        };
        return (displayName, details);
    }
}
