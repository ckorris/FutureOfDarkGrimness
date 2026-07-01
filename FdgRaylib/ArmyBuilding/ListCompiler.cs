using System;
using System.Collections.Generic;
using System.Linq;
using FDG.SaveLoad;

namespace FdgRaylib.ArmyBuilding
{
    // #153 (P0) — the heart of the feature: turn a BookFile + a BuilderList of selections into the playable
    // ArmyListFile the engine already consumes, wrapped as a BuiltArmyFile that also embeds the selections +
    // a full book snapshot (so one .fdgarmy both plays and re-opens for editing).
    //
    // Model of the world (audit finding 1): a UnitFileEntry carries unit-level weapons with per-type Quantity;
    // the engine expands + round-robins them across models. So all upgrades are AGGREGATE operations on weapon
    // counts — "replace one Rifle with a Heavy" = Rifle.Quantity--, Heavy.Quantity++. Applying an option N
    // times (see Applications) multiplies its cost and its gains by N.
    //
    // Totality: Compile throws only on a malformed list (unknown roster/section/option id) — a programming
    // error, not a legal user selection. Any legal BuilderList compiles without throwing.
    public static class ListCompiler
    {
        public static BuiltArmyFile Compile(BookFile book, BuilderList list)
        {
            if (book is null) throw new ArgumentNullException(nameof(book));
            if (list is null) throw new ArgumentNullException(nameof(list));

            var army = new BuiltArmyFile
            {
                Name = string.IsNullOrWhiteSpace(list.Name) ? book.Name : list.Name,
                Faction = book.Faction,
                PointsLimit = list.PointsLimit,
                // Carry the book's rule/spell definitions so the engine's RuleValidator gate passes and unit
                // rule references resolve at load (audit finding 3).
                RuleDefinitions = new List<FDG.Rules.Definitions.SpecialRuleDefinition>(book.RuleDefinitions),
                Spells = new List<FDG.Rules.Definitions.SpellDefinition>(book.Spells),
                Selections = list,
                Book = book,
            };

            foreach (BuilderUnit bu in list.Units)
                army.Units.Add(CompileUnit(book, bu));

            return army;
        }

        private static UnitFileEntry CompileUnit(BookFile book, BuilderUnit bu)
        {
            RosterUnit roster = book.Units.FirstOrDefault(u => u.Id == bu.RosterUnitId)
                ?? throw new InvalidOperationException($"Roster unit '{bu.RosterUnitId}' not found in book '{book.Name}'.");

            var unit = new UnitFileEntry
            {
                Name = roster.Name,
                Quality = roster.Quality,
                Defense = roster.Defense,
                ModelCount = roster.BaseModelCount,
                PointCost = roster.BasePointCost,
                Base = CloneBase(roster.Base),
                Id = bu.Id,
                JoinsUnitId = bu.JoinsUnitId,
                SpecialRules = new List<SpecialRuleEntry>(roster.Rules),
                Weapons = roster.Weapons.Select(CloneWeapon).ToList(),
            };

            foreach (UpgradeChoice choice in bu.Choices)
            {
                UpgradeSection section = roster.Sections.FirstOrDefault(s => s.Id == choice.SectionId)
                    ?? throw new InvalidOperationException($"Section '{choice.SectionId}' not found on unit '{roster.Id}'.");
                UpgradeOption option = section.Options.FirstOrDefault(o => o.Id == choice.OptionId)
                    ?? throw new InvalidOperationException($"Option '{choice.OptionId}' not found in section '{section.Id}'.");

                int applications = Applications(section, choice, unit.Weapons);
                if (applications <= 0) continue;

                unit.PointCost += option.Cost * applications;

                switch (section.Variant)
                {
                    case UpgradeVariant.Replace:
                        foreach (string target in section.Targets)
                            RemoveWeapon(unit.Weapons, target, applications);
                        AddGains(unit, option, applications);
                        break;

                    case UpgradeVariant.AddModels:
                        unit.ModelCount += option.ModelsGained * applications;
                        AddGains(unit, option, applications);
                        break;

                    case UpgradeVariant.Upgrade:
                    case UpgradeVariant.PickN:
                        AddGains(unit, option, applications);
                        break;
                }
            }

            return unit;
        }

        /// <summary>How many times an option applies — drives cost scaling (audit finding 5).</summary>
        private static int Applications(UpgradeSection section, UpgradeChoice choice, List<WeaponFileEntry> weapons)
        {
            if (section.Variant == UpgradeVariant.AddModels)
                return Math.Max(1, choice.Count);

            return section.Affects switch
            {
                UpgradeAffects.One => 1,
                UpgradeAffects.Any => Math.Max(1, choice.Count),
                UpgradeAffects.All => Math.Max(1, section.Targets.Sum(t => CountWeapon(weapons, t))),
                _ => 1,
            };
        }

        private static void AddGains(UnitFileEntry unit, UpgradeOption option, int applications)
        {
            foreach (WeaponFileEntry w in option.WeaponsGained)
                AddWeapon(unit.Weapons, w, applications);
            foreach (SpecialRuleEntry r in option.RulesGained)
                if (!unit.SpecialRules.Contains(r))
                    unit.SpecialRules.Add(r);
        }

        private static int CountWeapon(List<WeaponFileEntry> weapons, string name) =>
            weapons.Where(w => w.Name == name).Sum(w => w.Quantity);

        private static void RemoveWeapon(List<WeaponFileEntry> weapons, string name, int count)
        {
            int remaining = count;
            for (int i = 0; i < weapons.Count && remaining > 0; i++)
            {
                if (weapons[i].Name != name) continue;
                int take = Math.Min(weapons[i].Quantity, remaining);
                weapons[i].Quantity -= take;
                remaining -= take;
            }
            weapons.RemoveAll(w => w.Quantity <= 0);
        }

        private static void AddWeapon(List<WeaponFileEntry> weapons, WeaponFileEntry template, int applications)
        {
            int add = Math.Max(1, template.Quantity) * applications;
            WeaponFileEntry? existing = weapons.FirstOrDefault(w => SameProfile(w, template));
            if (existing != null) existing.Quantity += add;
            else { WeaponFileEntry clone = CloneWeapon(template); clone.Quantity = add; weapons.Add(clone); }
        }

        // Two weapons merge only if their whole profile matches — name alone isn't enough (a rule difference
        // would otherwise be silently lost into an existing entry).
        private static bool SameProfile(WeaponFileEntry a, WeaponFileEntry b) =>
            a.Name == b.Name && a.RangeInches == b.RangeInches && a.Attacks == b.Attacks &&
            a.ArmorPenetration == b.ArmorPenetration && a.SpecialRules.SequenceEqual(b.SpecialRules);

        private static WeaponFileEntry CloneWeapon(WeaponFileEntry w) => new()
        {
            Name = w.Name, Quantity = w.Quantity, RangeInches = w.RangeInches,
            Attacks = w.Attacks, ArmorPenetration = w.ArmorPenetration,
            SpecialRules = new List<SpecialRuleEntry>(w.SpecialRules),
        };

        private static BaseFileEntry CloneBase(BaseFileEntry b) => new()
        {
            Shape = b.Shape, DiameterInches = b.DiameterInches,
            WidthInches = b.WidthInches, HeightInches = b.HeightInches,
        };
    }
}
