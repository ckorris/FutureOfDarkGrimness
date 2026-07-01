using System.Collections.Generic;
using FDG.Rules.Definitions;
using FDG.SaveLoad;

namespace FdgRaylib.ArmyBuilding
{
    // #153 (P0) — the catalog ("book") an Army-Forge-style builder picks from. App-side only; the
    // compiler (ListCompiler) turns a BookFile + a BuilderList of selections into the engine's existing
    // ArmyListFile / .fdgarmy, so nothing here is known to the engine. Reuses the engine's authored-army
    // vocabulary (WeaponFileEntry / SpecialRuleEntry / BaseFileEntry / SpecialRuleDefinition / SpellDefinition)
    // so a compiled unit is byte-for-byte the same shape as a hand-authored one, and everything rides the
    // same System.Text.Json kind-schema (RuleJson.Options).

    /// <summary>
    /// A faction "book": a fixed roster of pre-statted units plus the rule/spell definitions they reference.
    /// A full snapshot of this is embedded inside every army the builder saves (see <c>BuiltArmyFile</c>), so
    /// an old army stays reproducible and re-editable even after the catalog book changes in a later version.
    /// </summary>
    public class BookFile
    {
        public const string EXTENSION_NO_PERIOD = "fdgbook";
        public const string EXTENSION_WITH_PERIOD = "." + EXTENSION_NO_PERIOD;

        public string Name { get; set; } = string.Empty;
        public string Faction { get; set; } = string.Empty;

        /// <summary>Author-set version string (e.g. "1.0.0"). Frozen into a saved army for provenance.</summary>
        public string Version { get; set; } = string.Empty;

        public List<RosterUnit> Units { get; set; } = new();

        /// <summary>Rule definitions the roster's units/upgrades reference. Copied onto a compiled army so
        /// the engine's <c>RuleValidator</c> gate passes and rule references resolve (audit finding 3).</summary>
        public List<SpecialRuleDefinition> RuleDefinitions { get; set; } = new();

        /// <summary>The army-wide spell list (#033): available to any unit carrying Caster(X). Copied onto a
        /// compiled army as-is.</summary>
        public List<SpellDefinition> Spells { get; set; } = new();
    }

    /// <summary>
    /// A pre-statted roster entry. Base stats are fixed; customisation happens through <see cref="Sections"/>.
    /// Weapons are unit-level with per-type <c>Quantity</c> exactly as <c>UnitFileEntry</c> expects — the engine
    /// expands + round-robins them across models at runtime, so "one model's weapon" is expressed as an
    /// aggregate count, not an explicit per-model slot (audit finding 1).
    /// </summary>
    public class RosterUnit
    {
        /// <summary>Stable within-book id, referenced by a <c>BuilderUnit</c> and by upgrade targeting.</summary>
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;

        public int Quality { get; set; }
        public int Defense { get; set; }

        /// <summary>Model count / cost at the unit's default size; <see cref="MinModels"/>..<see cref="MaxModels"/>
        /// bound the allowed range (equal = fixed-size). Size changes are otherwise modelled as AddModels sections.</summary>
        public int BaseModelCount { get; set; } = 1;
        public int MinModels { get; set; } = 1;
        public int MaxModels { get; set; } = 1;

        /// <summary>Cost of the unit at <see cref="BaseModelCount"/> with no upgrades selected.</summary>
        public int BasePointCost { get; set; }

        public BaseFileEntry Base { get; set; } = new();

        public List<WeaponFileEntry> Weapons { get; set; } = new();
        public List<SpecialRuleEntry> Rules { get; set; } = new();

        public List<UpgradeSection> Sections { get; set; } = new();
    }

    /// <summary>What an upgrade section does to the unit when an option is chosen.</summary>
    public enum UpgradeVariant
    {
        /// <summary>Swap the weapon/rule named in <see cref="UpgradeSection.Targets"/> for the option's gains.</summary>
        Replace,
        /// <summary>Add the option's gains (wargear / rules) on top of the current loadout.</summary>
        Upgrade,
        /// <summary>Pick between <see cref="UpgradeSection.MinPicks"/> and <see cref="UpgradeSection.MaxPicks"/> options.</summary>
        PickN,
        /// <summary>Add models to the unit (each option's <see cref="UpgradeOption.ModelsGained"/> &gt; 0).</summary>
        AddModels,
    }

    /// <summary>How many of the unit's models an option touches — drives cost scaling (audit finding 5).</summary>
    public enum UpgradeAffects
    {
        /// <summary>Exactly one model. Cost = option cost.</summary>
        One,
        /// <summary>0..N models, chosen by a count stepper. Cost = count × option cost.</summary>
        Any,
        /// <summary>Every eligible model. Cost = eligible-model-count × option cost.</summary>
        All,
    }

    /// <summary>A group of mutually-related options presented together (e.g. "Replace any Heavy Razor Claw").</summary>
    public class UpgradeSection
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;

        public UpgradeVariant Variant { get; set; } = UpgradeVariant.Upgrade;
        public UpgradeAffects Affects { get; set; } = UpgradeAffects.One;

        /// <summary>Weapon/rule names removed when an option is applied (Replace only).</summary>
        public List<string> Targets { get; set; } = new();

        /// <summary>Pick bounds for <see cref="UpgradeVariant.PickN"/> (ignored otherwise).</summary>
        public int MinPicks { get; set; } = 0;
        public int MaxPicks { get; set; } = 1;

        public List<UpgradeOption> Options { get; set; } = new();
    }

    /// <summary>One selectable choice within a section. <see cref="Cost"/> is per application (see <see cref="UpgradeAffects"/>).</summary>
    public class UpgradeOption
    {
        public string Id { get; set; } = string.Empty;
        public string Label { get; set; } = string.Empty;

        /// <summary>Points added per application.</summary>
        public int Cost { get; set; }

        public List<WeaponFileEntry> WeaponsGained { get; set; } = new();
        public List<SpecialRuleEntry> RulesGained { get; set; } = new();

        /// <summary>Models added to the unit (AddModels sections). 0 for weapon/rule upgrades.</summary>
        public int ModelsGained { get; set; }
    }
}
