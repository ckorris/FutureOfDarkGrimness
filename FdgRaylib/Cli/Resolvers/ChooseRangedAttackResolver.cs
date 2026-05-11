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

        var options = new List<(string label, RangedAttackChoice choice)>();

        foreach (var weaponOption in request.WeaponOptions)
        {
            string weaponStats = weaponOption.Weapon.GetWeaponNameAndStats();
            foreach (var targetStats in weaponOption.WeaponTargetStats)
            {
                int canShoot = targetStats.modelsThatCanShoot.Count;
                int cannotShoot = targetStats.modelsWithWeaponThatCannotShoot.Count;
                var targetUnit = targetStats.TargetUnit.GetValue();
                int targetModels = targetUnit.ModelBindings.Count;

                string label = $"{weaponStats}  →  {targetUnit.Name} ({targetModels} models, {canShoot} shooters in range";
                if (cannotShoot > 0)
                    label += $", {cannotShoot} out of range";
                if (targetStats.HasCover)
                    label += ", Cover";
                label += ")";

                options.Add((label, new RangedAttackChoice(weaponOption.Weapon, targetStats.TargetUnit)));
            }
        }

        if (options.Count == 0)
            throw new InvalidOperationException("ChooseRangedAttackRequest had no valid options.");

        for (int i = 0; i < options.Count; i++)
            Console.WriteLine($"  [{i + 1}] {options[i].label}");

        Console.WriteLine($"  [0] Back");

        while (true)
        {
            Console.Write("Choice: ");
            string? input = Console.ReadLine()?.Trim();
            // EOF default: first option (keeps piped-input scripts working).
            if (input == null) return Task.FromResult<CancellableResult<RangedAttackChoice>>(new Selected<RangedAttackChoice>(options[0].choice));
            if (int.TryParse(input, out int choice))
            {
                if (choice == 0) return Task.FromResult<CancellableResult<RangedAttackChoice>>(new Cancelled<RangedAttackChoice>());
                if (choice >= 1 && choice <= options.Count)
                    return Task.FromResult<CancellableResult<RangedAttackChoice>>(new Selected<RangedAttackChoice>(options[choice - 1].choice));
            }
            Console.WriteLine($"  Enter 0 (Back) or a number between 1 and {options.Count}.");
        }
    }
}
