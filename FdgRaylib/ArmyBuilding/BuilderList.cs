using System.Collections.Generic;

namespace FdgRaylib.ArmyBuilding
{
    // #153 (P0) — the editable "work in progress": which roster units are in the list and what was chosen
    // for each. This is the source of truth the builder edits; the compiler turns it (against a BookFile)
    // into the playable ArmyListFile. Not saved as its own file — it is embedded inside the compiled
    // .fdgarmy (see BuiltArmyFile) so one self-contained file both plays and re-opens for editing.

    /// <summary>A list under construction: a sequence of unit instances and their upgrade choices.</summary>
    public class BuilderList
    {
        /// <summary>The army's display name (user-chosen). Compiled into <c>ArmyListFile.Name</c>.</summary>
        public string Name { get; set; } = string.Empty;

        /// <summary>Provenance — the name of the book this list was built from (the full book is embedded
        /// alongside in <c>BuiltArmyFile</c>; this is just a human-facing label / sanity check).</summary>
        public string BookName { get; set; } = string.Empty;

        public int PointsLimit { get; set; }

        public List<BuilderUnit> Units { get; set; } = new();
    }

    /// <summary>One unit instance in the list: a roster entry plus its size and selected options.</summary>
    public class BuilderUnit
    {
        /// <summary>References <see cref="RosterUnit.Id"/> in the book.</summary>
        public string RosterUnitId { get; set; } = string.Empty;

        public int ModelCount { get; set; } = 1;

        public List<UpgradeChoice> Choices { get; set; } = new();

        /// <summary>Author-stable handle compiled into <c>UnitFileEntry.Id</c> (#006), so a Hero can target it.</summary>
        public string? Id { get; set; }

        /// <summary>If this instance is a Hero, the <see cref="Id"/> of the unit it joins at setup (#006).</summary>
        public string? JoinsUnitId { get; set; }
    }

    /// <summary>A selected option within a section. <see cref="Count"/> is the number of applications for
    /// <see cref="UpgradeAffects.Any"/> sections (cost = count × option cost); 1 otherwise.</summary>
    public class UpgradeChoice
    {
        public string SectionId { get; set; } = string.Empty;
        public string OptionId { get; set; } = string.Empty;
        public int Count { get; set; } = 1;
    }
}
