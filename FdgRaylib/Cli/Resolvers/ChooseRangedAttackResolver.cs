using FDG;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using static FDG.StageResolution.Requests.ChooseRangedAttackRequest;

namespace FdgRaylib.Cli.Resolvers;

public class ChooseRangedAttackResolver : IStageResolver<ChooseRangedAttackRequest, RangedAttackChoice>
{
    public Task<RangedAttackChoice> Resolve(ChooseRangedAttackRequest request)
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

        while (true)
        {
            Console.Write("Choice: ");
            string? input = Console.ReadLine()?.Trim();
            if (input == null) return Task.FromResult(options[0].choice); // EOF default: first option
            if (int.TryParse(input, out int choice) && choice >= 1 && choice <= options.Count)
                return Task.FromResult(options[choice - 1].choice);
            Console.WriteLine($"  Enter a number between 1 and {options.Count}.");
        }
    }
}
