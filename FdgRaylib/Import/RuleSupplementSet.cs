using System;
using System.Collections.Generic;
using System.IO;
using FDG.ArmyBuilding;
using FDG.Rules.Definitions;

namespace FdgRaylib.Import;

// #375 — the bundled rule-supplement files as one merged authoring universe. GDF and AoF definitions
// live in separate files; an AoF rule whose text diverges from a GDF definition of the same name simply
// redefines that name in the AoF file. A bake or validation that spans systems merges the files
// later-wins by name: pass the shared/base supplement first and the system's own file last, so the
// system-specific definition of a shared name is the one that embeds. GDF books keep baking against the
// GDF file alone.
public static class RuleSupplementSet
{
    /// <summary>The shipped supplement files under Assets/Books, in merge order (later wins on a
    /// name collision).</summary>
    public static readonly IReadOnlyList<string> BundledFileNames = new[]
    {
        "GdfRuleSupplement.json",
        "AofRuleSupplement.json",
    };

    /// <summary>Load and merge supplement files in order. A later file's definition of a name replaces
    /// the earlier one in place (case-insensitive, matching the resolver's lookup); new names append in
    /// file order.</summary>
    public static List<SpecialRuleDefinition> LoadMerged(IEnumerable<string> paths)
    {
        List<SpecialRuleDefinition> merged = new();
        Dictionary<string, int> indexByName = new(StringComparer.OrdinalIgnoreCase);
        foreach (string path in paths)
        {
            foreach (SpecialRuleDefinition definition in BookRuleSupplement.LoadDefinitions(File.ReadAllText(path)))
            {
                if (indexByName.TryGetValue(definition.Name, out int existing))
                {
                    merged[existing] = definition;
                }
                else
                {
                    indexByName[definition.Name] = merged.Count;
                    merged.Add(definition);
                }
            }
        }
        return merged;
    }
}
