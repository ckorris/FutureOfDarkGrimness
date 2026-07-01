using FDG.SaveLoad;

namespace FdgRaylib.ArmyBuilding
{
    // #153 (P0) — a small hand-authored FDG book used to drive the compiler tests and the first UI. This is
    // our own IP (original faction/unit/weapon names), NOT imported OPR content. It exercises the compiler's
    // variants: Replace-one, Replace-all (cost scales with target count), AddModels (count scales cost + models
    // + the added models' default weapon), and a plain Upgrade that grants a rule. It references only *core*
    // rules by name (Fear, AP, Blast), so it needs no embedded RuleDefinitions and a compiled army passes the
    // engine's RuleValidator gate trivially.
    public static class DemoBook
    {
        public static BookFile Build() => new()
        {
            Name = "Dark Vanguard", Faction = "Dark Vanguard", Version = "demo-1",
            Units =
            {
                new RosterUnit
                {
                    Id = "warriors", Name = "Vanguard Warriors",
                    Quality = 4, Defense = 4, BaseModelCount = 5, MinModels = 5, MaxModels = 10, BasePointCost = 65,
                    Weapons = { Weapon("Rifle", qty: 5, range: 24, attacks: 1) },
                    Sections =
                    {
                        new UpgradeSection
                        {
                            Id = "warriors-special", Label = "Upgrade one Rifle to a Plasma Rifle",
                            Variant = UpgradeVariant.Replace, Affects = UpgradeAffects.One, Targets = { "Rifle" },
                            Options =
                            {
                                new UpgradeOption
                                {
                                    Id = "plasma", Label = "Plasma Rifle (24\", A1, AP(2))", Cost = 5,
                                    WeaponsGained = { Weapon("Plasma Rifle", 1, 24, 1, ap: 2) },
                                },
                            },
                        },
                        new UpgradeSection
                        {
                            Id = "warriors-reinforce", Label = "Add Warriors (with Rifle)",
                            Variant = UpgradeVariant.AddModels,
                            Options =
                            {
                                new UpgradeOption
                                {
                                    Id = "add-warrior", Label = "+1 Warrior", Cost = 13,
                                    ModelsGained = 1, WeaponsGained = { Weapon("Rifle", 1, 24, 1) },
                                },
                            },
                        },
                        new UpgradeSection
                        {
                            Id = "warriors-banner", Label = "Upgrade with a War Banner",
                            Variant = UpgradeVariant.Upgrade, Affects = UpgradeAffects.One,
                            Options =
                            {
                                new UpgradeOption
                                {
                                    Id = "war-banner", Label = "War Banner (Fear)", Cost = 10,
                                    RulesGained = { new SpecialRuleEntry_Core("Fear") },
                                },
                            },
                        },
                    },
                },
                new RosterUnit
                {
                    Id = "gunners", Name = "Heavy Gunners",
                    Quality = 4, Defense = 4, BaseModelCount = 3, MinModels = 3, MaxModels = 3, BasePointCost = 120,
                    Weapons = { Weapon("Heavy Rifle", qty: 3, range: 36, attacks: 1, ap: 1) },
                    Sections =
                    {
                        new UpgradeSection
                        {
                            Id = "gunners-missiles", Label = "Replace all Heavy Rifles with Missile Launchers",
                            Variant = UpgradeVariant.Replace, Affects = UpgradeAffects.All, Targets = { "Heavy Rifle" },
                            Options =
                            {
                                new UpgradeOption
                                {
                                    Id = "missile", Label = "Missile Launcher (48\", A1, AP(2), Blast(3))", Cost = 15,
                                    WeaponsGained = { Weapon("Missile Launcher", 1, 48, 1, ap: 2, rules: new[] { "Blast:3" }) },
                                },
                            },
                        },
                    },
                },
            },
        };

        private static WeaponFileEntry Weapon(string name, int qty, int range, int attacks, int ap = 0, string[]? rules = null)
        {
            var w = new WeaponFileEntry { Name = name, Quantity = qty, RangeInches = range, Attacks = attacks, ArmorPenetration = ap };
            if (ap > 0) w.SpecialRules.Add(new SpecialRuleEntry_CoreNumeric("AP", ap));
            if (rules != null)
                foreach (string r in rules)
                {
                    int c = r.IndexOf(':');
                    if (c > 0) w.SpecialRules.Add(new SpecialRuleEntry_CoreNumeric(r[..c], int.Parse(r[(c + 1)..])));
                    else w.SpecialRules.Add(new SpecialRuleEntry_Core(r));
                }
            return w;
        }
    }
}
