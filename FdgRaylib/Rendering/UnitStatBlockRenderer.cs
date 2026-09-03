using System.Linq;
using FDG;
using FDG.Rules.Dispatch;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// Shared read-only unit stat block: name, model count + Quality/Defense, mobility, each distinct weapon on
/// its own datasheet line (rules included, via <c>GetWeaponNameAndStats</c>), then the unit's own special
/// rules. Drawn into whatever ImGui container the caller opens -- the deploy panel (#223's sibling) and the
/// deploy / activate unit-picker hover tooltip (#223) both use it, so the two stay in sync. ASCII only
/// (CLAUDE.md).
/// </summary>
public static class UnitStatBlockRenderer
{
    /// <param name="includeRuleDescriptions">When true, each special rule's description is shown under it
    /// (the fuller hover-tooltip treatment); when false, only the rule names (the compact panel treatment).</param>
    /// <param name="descriptionWrapWidth">How wide a rule description may run before wrapping, in pixels
    /// from the text's own left edge. The default suits an auto-sizing tooltip; a docked panel (#288) passes
    /// its own content width so long descriptions wrap inside the panel instead of running off it.</param>
    public static void Draw(IUnit unit, bool includeRuleDescriptions, float descriptionWrapWidth = 300f)
    {
        ImGui.TextUnformatted(unit.Name);

        int live = unit.Models.Count(m => m.GetIsAlive());
        ImGui.TextDisabled($"{live} model{(live == 1 ? "" : "s")}   Qua {unit.Quality}+  Def {unit.Defense}+");
        // #287/#288: wounds, matching the model hover tooltip - a unit placed after taking damage
        // (disembark, reposition) is the case where this matters, and it costs one line otherwise.
        ImGui.TextDisabled($"Wounds {WoundFormat.Fraction(unit.RemainingWounds, unit.MaxWounds)}");
        if (unit.GetMobility(out float advance, out float charge))
            ImGui.TextDisabled($"Advance {advance}\"   Charge {charge}\"");

        var weapons = unit.AllWeapons();
        if (weapons.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextUnformatted("Weapons:");
            foreach (var grp in weapons.GroupBy(w => w.Name))
                ImGui.TextUnformatted($"- {grp.First().GetWeaponNameAndStats(grp.Count())}");
        }

        // Unit-wide special rules (live #042 ResolvedRules), minus the engine-internal Disembark/Embark
        // abilities attached to every unit -- same filter the hover tooltip uses.
        var rules = unit.RuleDefinitions
            .Where(r => r.Definition.Name != CoreRuleCatalog.DisembarkRuleName
                     && r.Definition.Name != CoreRuleCatalog.EmbarkRuleName)
            .ToList();
        if (rules.Count > 0)
        {
            ImGui.Spacing();
            ImGui.TextUnformatted("Special Rules:");
            foreach (var rule in rules)
            {
                ImGui.TextUnformatted($"- {rule.RequestedName}");
                if (includeRuleDescriptions && !string.IsNullOrEmpty(rule.Definition.Description))
                {
                    ImGui.Indent();
                    ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + descriptionWrapWidth);
                    ImGui.TextDisabled(rule.Definition.Description);
                    ImGui.PopTextWrapPos();
                    ImGui.Unindent();
                }
            }
        }
    }
}
