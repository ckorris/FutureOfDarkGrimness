using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using FDG;
using FDG.Data;
using FDG.GameModel;
using FDG.Players;
using FDG.Rules.Dispatch;
using FDG.Rules.Serialization;
using FDG.SaveLoad;
using NUnit.Framework;

namespace FdgRaylib.Tests;

// #424: the legacy half of Assault. A 3.5 .fdgarmy (embedded definitions, no Assault anywhere) must load and classify
// exactly as it did before the seam widened: every weapon in exactly one pool, chosen by its printed range. #440: the
// shipped armies/ lists are 3.6 rebuilds now, so the 3.5 list is the legacy fixture kept from that set.
[TestFixture]
public class AssaultLegacyArmyTests
{
    private const string LegacyArmy = "2k - Robot Legions - Mixed.fdgarmy";

    [Test]
    public void AShipped35Army_LoadsAndClassifiesByRangeAlone()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "legacy-3.5", LegacyArmy);
        Assert.That(File.Exists(path), Is.True, $"{LegacyArmy} has moved - update this pin");

        ArmyListFile army = JsonSerializer.Deserialize<ArmyListFile>(File.ReadAllText(path), RuleJson.Options)!;
        Assume.That(army.RuleDefinitions.Any(d => d.Name == "Assault"), Is.False, "a 3.5 file embeds no Assault");
        Assume.That(army.Units.SelectMany(u => u.Weapons).SelectMany(w => w.SpecialRules).Any(r => r.PrintableName == "Assault"),
            Is.False, "and names it on no weapon");

        GameDataStore store = GameDataStore.GameDataStoreBuilder.GetDefault();
        RuleResolver resolver = CoreRuleCatalog.CreateResolver();
        ArmyListRuleResolution.RegisterEmbeddedDefinitions(resolver, army);
        GameBootstrap.CreateArmy(new PlayerID(Guid.NewGuid()), army, store, resolver);
        List<IUnit> units = store.GetAllValues<UnitData>().Cast<IUnit>().ToList();

        Assert.That(units, Is.Not.Empty);
        int weapons = 0;
        foreach (IUnit unit in units)
        {
            List<Weapon> all = unit.AllWeapons(_ => true);
            weapons += all.Count;
            Assert.That(unit.GetRangedWeapons().Select(w => w.Name), Is.EquivalentTo(
                all.Where(w => w.RangeInches > 0f && !StrafingRules.IsStrafeOnly(w)).Select(w => w.Name)), unit.Name);
            Assert.That(unit.GetMeleeWeapons().Select(w => w.Name), Is.EquivalentTo(
                all.Where(w => w.RangeInches <= 0f && !StrafingRules.IsStrafeOnly(w)).Select(w => w.Name)), unit.Name);
            Assert.That(unit.GetRangedWeapons().Intersect(unit.GetMeleeWeapons()), Is.Empty, unit.Name);
            Assert.That(all.All(w => w.GrantedRoles == EWeaponRoles.None), Is.True, unit.Name);
        }
        Assert.That(weapons, Is.GreaterThan(0), "the army actually carries weapons to classify");
    }
}
