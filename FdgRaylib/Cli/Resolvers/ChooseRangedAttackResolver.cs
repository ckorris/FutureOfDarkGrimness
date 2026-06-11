using FDG;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using static FDG.StageResolution.Requests.ChooseRangedAttackRequest;

namespace FdgRaylib.Cli.Resolvers;

public class ChooseRangedAttackResolver : IStageResolver<ChooseRangedAttackRequest, CancellableResult<RangedAttackChoice>>
{
    public Task<CancellableResult<RangedAttackChoice>> Resolve(ChooseRangedAttackRequest request)
    {
        var attackerUnit = request.AttackingUnit.GetValue();
        Console.WriteLine();
        Console.WriteLine($"--- Shoot: {attackerUnit.Name} ---");
        Console.WriteLine("  Choose a weapon and target. Models out of range/LOS cannot contribute.");
        Console.WriteLine();

        var options = new List<(string label, RangedAttackChoice? choice)>();

        foreach (var weaponOption in request.WeaponOptions)
        {
            string weaponStats = weaponOption.Weapon.GetWeaponNameAndStats();
            // #042/#052: attribute any cover-/LoS-ignore to the responsible rule, so the player sees why a
            // blocked or in-cover unit is still a normal target (e.g. "(Indirect ignores line of sight)").
            weaponStats += SightRuleLabel.Parenthetical(
                weaponOption.CoverIgnoreRule, weaponOption.LineOfSightIgnoreRule);
            foreach (var targetStats in weaponOption.WeaponTargetStats)
            {
                int canShoot = targetStats.modelsThatCanShoot.Count;
                int cannotShoot = targetStats.modelsWithWeaponThatCannotShoot.Count;
                var targetUnit = targetStats.TargetUnit.GetValue();
                int targetModels = targetUnit.ModelBindings.Count;

                string label = $"{weaponStats}  →  {targetUnit.Name} ({targetModels} models, {canShoot} shooters in range";
                if (cannotShoot > 0)
                    label += $", {cannotShoot} out of range";
                // #042 Blast/Indirect/Takedown: when the weapon ignores cover the +1 doesn't apply, so show
                // it as ignored rather than a penalty.
                if (targetStats.HasCover)
                    label += weaponOption.IgnoresCover ? ", Cover (ignored)" : ", Cover";
                label += ")";

                bool selectable = targetStats.UnselectableReason == null && canShoot > 0;
                if (targetStats.UnselectableReason != null)
                    label += $"  [unavailable: {targetStats.UnselectableReason}]";

                options.Add((label,
                    selectable ? new RangedAttackChoice(weaponOption.Weapon, targetStats.TargetUnit) : null));
            }
        }

        if (options.Count == 0)
            throw new InvalidOperationException("ChooseRangedAttackRequest had no valid options.");

        for (int i = 0; i < options.Count; i++)
        {
            string prefix = options[i].choice != null ? $"  [{i + 1}]" : "  [-]";
            Console.WriteLine($"{prefix} {options[i].label}");
        }

        Console.WriteLine($"  [0] Back");

        // First selectable option, for EOF default.
        int firstSelectable = options.FindIndex(o => o.choice != null);

        while (true)
        {
            Console.Write("Choice: ");
            string? input = Console.ReadLine()?.Trim();
            // EOF default: first selectable option (keeps piped-input scripts working).
            if (input == null)
            {
                if (firstSelectable < 0) return Task.FromResult<CancellableResult<RangedAttackChoice>>(new Cancelled<RangedAttackChoice>());
                return Task.FromResult<CancellableResult<RangedAttackChoice>>(new Selected<RangedAttackChoice>(options[firstSelectable].choice!));
            }
            if (int.TryParse(input, out int choice))
            {
                if (choice == 0) return Task.FromResult<CancellableResult<RangedAttackChoice>>(new Cancelled<RangedAttackChoice>());
                if (choice >= 1 && choice <= options.Count)
                {
                    var picked = options[choice - 1].choice;
                    if (picked != null)
                        return Task.FromResult<CancellableResult<RangedAttackChoice>>(new Selected<RangedAttackChoice>(picked));
                    Console.WriteLine("  That option is unavailable.");
                    continue;
                }
            }
            Console.WriteLine($"  Enter 0 (Back) or a number between 1 and {options.Count}.");
        }
    }
}
