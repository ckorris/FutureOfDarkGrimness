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
            // #319: firing a once-per-game weapon spends it for good, and the player may decline it
            // (hold fire, below) - so say which state it is in on every line that offers it.
            if (weaponOption.LimitedRule != null)
            {
                weaponStats += weaponOption.LimitedAlreadyFired
                    ? $"  [{weaponOption.LimitedRule}: already fired this game]"
                    : $"  [{weaponOption.LimitedRule}: ONCE PER GAME - firing spends it]";
            }
            foreach (var targetStats in weaponOption.WeaponTargetStats)
            {
                int canShoot = targetStats.modelsThatCanShoot.Count;
                int cannotShoot = targetStats.modelsWithWeaponThatCannotShoot.Count;
                var targetUnit = targetStats.TargetUnit.GetValue();
                // #158: count the target's LIVING models — dead ones aren't shootable.
                int targetModels = targetUnit.ModelBindings.Count(mb => mb.GetValue().GetIsAlive());

                string label = $"{weaponStats}  ->  {targetUnit.Name} ({targetModels} models, {canShoot} shooters in range";
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

        // #319: hold fire - decline one weapon for this shoot action without firing it. Listed per weapon
        // that can still fire, since that is the only case where declining changes anything; a Limited
        // weapon says what holding fire preserves.
        var holdFireWeapons = request.WeaponOptions
            .Where(wo => wo.WeaponTargetStats.Any(ts =>
                ts.UnselectableReason == null && ts.modelsThatCanShoot.Count > 0))
            .ToList();
        for (int i = 0; i < holdFireWeapons.Count; i++)
        {
            var wo = holdFireWeapons[i];
            string keeps = wo.LimitedRule != null
                ? $" (keeps its {wo.LimitedRule} once-per-game shot)"
                : "";
            Console.WriteLine($"  [h{i + 1}] Hold fire: {wo.Weapon.Name} - do not fire it this action{keeps}");
        }

        // #308/#319: exactly one exit, and the engine says which. Back (nothing fired) rewinds to Choose
        // Action; Done (something fired) ends the shoot action with the remaining weapons unfired.
        if (request.AllowCancel)
            Console.WriteLine($"  [0] Back");
        else if (request.AllowStopShooting)
            Console.WriteLine($"  [0] Done shooting - end the action with {holdFireWeapons.Count} weapon" +
                $"{(holdFireWeapons.Count != 1 ? "s" : "")} unfired");

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
            if (input.StartsWith("h", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(input.AsSpan(1), out int holdIndex))
            {
                if (holdIndex >= 1 && holdIndex <= holdFireWeapons.Count)
                {
                    return Task.FromResult<CancellableResult<RangedAttackChoice>>(
                        new Selected<RangedAttackChoice>(
                            RangedAttackChoice.HoldFire(holdFireWeapons[holdIndex - 1].Weapon)));
                }
                Console.WriteLine("  No such weapon to hold fire with.");
                continue;
            }
            if (int.TryParse(input, out int choice))
            {
                if (choice == 0 && (request.AllowCancel || request.AllowStopShooting))
                {
                    // #319 (user sign-off): ending the action with loaded weapons asks first. Backing out
                    // before anything has fired costs nothing, so it does not.
                    if (request.AllowStopShooting && !ConfirmStopShooting(holdFireWeapons))
                        continue;
                    return Task.FromResult<CancellableResult<RangedAttackChoice>>(new Cancelled<RangedAttackChoice>());
                }
                if (choice >= 1 && choice <= options.Count)
                {
                    var picked = options[choice - 1].choice;
                    if (picked != null)
                        return Task.FromResult<CancellableResult<RangedAttackChoice>>(new Selected<RangedAttackChoice>(picked));
                    Console.WriteLine("  That option is unavailable.");
                    continue;
                }
            }
            string exit = request.AllowCancel ? "0 (Back), " : request.AllowStopShooting ? "0 (Done), " : "";
            string hold = holdFireWeapons.Count > 0 ? $"h1-h{holdFireWeapons.Count} (hold fire), " : "";
            Console.WriteLine($"  Enter {exit}{hold}or a number between 1 and {options.Count}.");
        }
    }

    // #319: names what the shoot action is giving up before it ends. EOF answers "yes" - a piped script
    // that asked to stop shooting means it, and re-prompting forever is the one wrong answer here.
    private static bool ConfirmStopShooting(List<WeaponOption> unfired)
    {
        Console.WriteLine();
        if (unfired.Count > 0)
        {
            Console.WriteLine($"  Ending the shoot action leaves {unfired.Count} weapon" +
                $"{(unfired.Count != 1 ? "s" : "")} unfired this turn:");
            foreach (var wo in unfired)
            {
                Console.WriteLine(wo.LimitedRule != null
                    ? $"    - {wo.Weapon.Name} (keeps its {wo.LimitedRule} once-per-game shot)"
                    : $"    - {wo.Weapon.Name}");
            }
        }
        Console.Write("  End the shoot action? [y/N]: ");
        string? answer = Console.ReadLine()?.Trim();
        if (answer == null) return true;
        return answer.Equals("y", StringComparison.OrdinalIgnoreCase)
            || answer.Equals("yes", StringComparison.OrdinalIgnoreCase);
    }
}
