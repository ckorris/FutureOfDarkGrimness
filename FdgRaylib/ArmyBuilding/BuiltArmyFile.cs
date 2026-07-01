using FDG.SaveLoad;

namespace FdgRaylib.ArmyBuilding
{
    // #153 (P0) — the single-file persistence format for a catalog-built army.
    //
    // A BuiltArmyFile IS an ArmyListFile (it inherits Name/Faction/PointsLimit/Units/RuleDefinitions/Spells),
    // so to the engine it is an ordinary, fully-playable .fdgarmy: FDGServer.CreateArmyDataFromArmyFile reads
    // the base Units[] exactly as today, and Deserialize<ArmyListFile> on the file silently skips the two
    // extra members below (System.Text.Json's default UnmappedMemberHandling is Skip, and RuleJson.Options
    // does not change it — verified 2026-07-01). That is why embedding the editable state needs NO engine
    // change.
    //
    // SERIALIZATION CONTRACT (audit finding 4): the extra members only survive if the file is serialized with
    // this *derived* type — JsonSerializer.Serialize serializes by the declared type. The builder therefore
    // Serialize<BuiltArmyFile>(...) to save and Deserialize<BuiltArmyFile>(...) to reopen; everything else in
    // the app (lobby "Load Army", headless ArmyLoader, the network ArmyListUpdateMessage) reads it as the base
    // ArmyListFile and never sees — nor needs — the embedded book (only Units[] matter for play).
    public class BuiltArmyFile : ArmyListFile
    {
        /// <summary>The editable selections that produced <see cref="ArmyListFile.Units"/>. Null on a file that
        /// was not authored by the forge (e.g. a hand-authored .fdgarmy) — such a file still plays, it just
        /// can't be reopened for catalog editing.</summary>
        public BuilderList? Selections { get; set; }

        /// <summary>A full snapshot of the source book as it existed when this army was authored (decision 6),
        /// so the army re-opens and re-edits against the book it was built with — never silently re-pointed at
        /// a newer catalog. Null when <see cref="Selections"/> is null.</summary>
        public BookFile? Book { get; set; }
    }
}
