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
    public static void Draw(IUnit unit, bool includeRuleDescriptions)
    {
        ImGui.TextUnformatted(unit.Name);

        int live = unit.Models.Count(m => m.GetIsAlive());
        ImGui.TextDisabled($"{live} model{(live == 1 ? "" : "s")}   Qua {unit.Quality}+  Def {unit.Defense}+");
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
                    ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + 300f);
                    ImGui.TextDisabled(rule.Definition.Description);
                    ImGui.PopTextWrapPos();
                    ImGui.Unindent();
                }
            }
        }
    }
}
