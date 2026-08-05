using FDG;
using FDG.Ai.Tactician;
using FDG.Data;
using FDG.StageResolution;
using FDG.StageResolution.Requests;
using FDG.Stages;
using FDG.Utilities;
using FdgRaylib.Placement;

namespace FdgRaylib.Cli.Resolvers;

public class DefineMovementPathResolver : IStageResolver<DefineMovementPathRequest, CancellableResult<List<ModelMoveEntry>>>
{
    private readonly ITableState? _tableState;

    public DefineMovementPathResolver(ITableState? tableState = null)
    {
        _tableState = tableState;
    }

    public Task<CancellableResult<List<ModelMoveEntry>>> Resolve(DefineMovementPathRequest request)
    {
        var unit = request.UnitDataBinding.GetValue();
        var models = unit.ModelBindings;

        while (true)
        {
            Console.WriteLine();
            Console.WriteLine($"--- Move: {unit.Name} ({models.Count} model{(models.Count != 1 ? "s" : "")}) ---");
            Console.WriteLine($"  Advance (<= {request.MaxAdvanceDistance:F1}\"): move freely, can still shoot afterward");
            Console.WriteLine($"  Rush    (<= {request.MaxDistanceInches:F1}\"): move farther, but cannot shoot this turn");
            if (models.Count > 1)
            {
                Console.WriteLine($"  Cohesion: each model must end within {GameWideConstants.MAX_MODEL_DISTANCE_FROM_ANY_OTHER_MODEL_INCHES:F0}\" (base-to-base) of at least one teammate");
                Console.WriteLine($"            and within {GameWideConstants.MAX_MODEL_DISTANCE_FROM_ALL_OTHER_MODELS_INCHES:F0}\" of every other model");
            }
            // #334: state the standoff band up front, the way the cohesion limits are stated. Ending inside it
            // is legal (#206) but costs the unit its Pass, and a CLI player has no ghost to warn them.
            if (EnemyPoses(request).Count > 0)
            {
                Console.WriteLine($"  Standoff: ending within {GameWideConstants.ENEMY_STANDOFF_DISTANCE_INCHES:F0}\" (base-to-base) of an enemy is allowed,");
                Console.WriteLine("            but the unit then cannot Pass - it must Charge.");
            }
            Console.WriteLine("  Enter destination as 'x z' (inches, e.g. '24 18'), or press Enter to leave in place.");
            if (request.AllowCancel)
                Console.WriteLine("  Type 'back' to cancel the move and choose a different action.");
            Console.WriteLine();

            var entries = new List<ModelMoveEntry>();
            bool eof = false;
            bool cancelled = false;

            for (int i = 0; i < models.Count; i++)
            {
                var modelBinding = models[i];
                var model = modelBinding.GetValue();
                Console.Write($"  Model {i + 1} at ({model.Position.x:F1}\", {model.Position.z:F1}\"): ");
                string? input = Console.ReadLine()?.Trim();

                if (input == null)
                {
                    eof = true;
                    break;
                }

                if (request.AllowCancel && input.Equals("back", StringComparison.OrdinalIgnoreCase))
                {
                    cancelled = true;
                    break;
                }

                if (string.IsNullOrEmpty(input))
                {
                    entries.Add(new ModelMoveEntry(modelBinding, new List<Position> { model.Position }));
                    continue;
                }

                string[] parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length >= 2 && float.TryParse(parts[0], out float x) && float.TryParse(parts[1], out float z))
                    entries.Add(new ModelMoveEntry(modelBinding, new List<Position> { new Position(x, z) }));
                else
                {
                    Console.WriteLine("    Could not parse - leaving in place.");
                    entries.Add(new ModelMoveEntry(modelBinding, new List<Position> { model.Position }));
                }
            }

            if (cancelled)
                return Task.FromResult<CancellableResult<List<ModelMoveEntry>>>(new Cancelled<List<ModelMoveEntry>>());

            // EOF keeps the piped-stdin default: advance rather than cancel, so an automated run still plays.
            if (eof)
                return Selected(WarnIfForcedCharge(request, AutoAdvance(request)));

            // #159: lenientCoherency mirrors DefinePathStage - a unit already broken by a casualty may hold
            // (or pull together) rather than being rejected for a coherency it can't restore while boxed in.
            if (MovementUtilities.ValidatePaths(entries, BudgetFor(request),
                    GetEnemyFootprints(request), request.CanMoveThroughEnemies, request.IgnoresDifficultTerrain,
                    request.IgnoresImpassibleTerrain, _tableState?.Terrain.Objects, out var errors,
                    GetFriendlyFootprints(request), lenientCoherency: true))
                return Selected(WarnIfForcedCharge(request, entries));

            Console.WriteLine();
            Console.WriteLine("  Movement is invalid - please re-enter all models:");
            foreach (var err in errors)
                Console.WriteLine($"    ! {MovementUtilities.ErrorReasonToString(err.ErrorReasonType)}");
        }
    }

    private static Task<CancellableResult<List<ModelMoveEntry>>> Selected(List<ModelMoveEntry> entries) =>
        Task.FromResult<CancellableResult<List<ModelMoveEntry>>>(new Selected<List<ModelMoveEntry>>(entries));

    // Automatically advance the unit toward the nearest enemy, re-forming the living models into a
    // cohesive grid at the destination. A rigid translate would preserve any hole a casualty left in the
    // formation and be rejected for breaking cohesion (which would crash DefinePathStage), so we re-pack.
    private List<ModelMoveEntry> AutoAdvance(DefineMovementPathRequest request)
    {
        var unit = request.UnitDataBinding.GetValue();
        var living = unit.ModelBindings.Where(mb => mb.GetValue().GetIsAlive()).ToList();
        if (living.Count == 0)
            return StayInPlace(request);

        // Live enemy footprints (centre + radius) via ITableState.
        var footprints = GetEnemyFootprints(request);
        // #205: friendly footprints the auto-advance must not end stacked on (else DefinePathStage throws).
        var friendlies = GetFriendlyFootprints(request);

        // Compute the living models' centre.
        float cx = living.Average(mb => mb.GetValue().Position.x);
        float cz = living.Average(mb => mb.GetValue().Position.z);

        // No enemy to advance on — re-form in place (closes any casualty hole so the move is legal).
        if (footprints.Count == 0)
            return CohesiveFormation.PackGrid(living, cx, cz);

        EnemyModelFootprint nearest = footprints
            .OrderBy(f => (f.Center.x - cx) * (f.Center.x - cx) + (f.Center.z - cz) * (f.Center.z - cz))
            .First();

        float dx = nearest.Center.x - cx;
        float dz = nearest.Center.z - cz;
        float dist = MathF.Sqrt(dx * dx + dz * dz);

        if (dist < 0.01f)
            return CohesiveFormation.PackGrid(living, cx, cz);

        // Auto-advance is a non-charge move: close toward the enemy but hold at the 1" standoff (base-to-base),
        // not on top of it — "1" short of centre" is actually base overlap. Tiny advance margin so float
        // rounding can't disqualify a follow-up shot; clamped so the re-pack keeps every model in budget.
        float leadRadius = living.Max(mb => mb.GetValue().BaseRadiusInches);
        float targetGap = GameWideConstants.ENEMY_STANDOFF_DISTANCE_INCHES + 0.05f;
        float step = Math.Min(request.MaxAdvanceDistance - 0.001f,
            Math.Max(0f, dist - (leadRadius + nearest.BaseRadiusInches) - targetGap));
        step = CohesiveFormation.ClampRepackStep(living, cx, cz, step, request.MaxDistanceInches);
        float ndx = dx / dist;
        float ndz = dz / dist;

        // The move-through / standoff validator (#011) can still reject an advance that ends too near an
        // enemy (e.g. packing offset), and DefinePathStage throws (no retry). Back the step off until the
        // engine accepts the candidate — reforming in place as a last resort — so EOF auto-play never crashes.
        var terrain = _tableState?.Terrain.Objects;

        var candidate = CohesiveFormation.PackGrid(living, cx + ndx * step, cz + ndz * step);
        bool valid = MovementUtilities.ValidatePaths(candidate, BudgetFor(request),
            footprints, request.CanMoveThroughEnemies, request.IgnoresDifficultTerrain, request.IgnoresImpassibleTerrain, terrain, out _, friendlies, lenientCoherency: true);
        int attempts = 0;
        while (!valid && attempts < 6)
        {
            step *= 0.5f;
            candidate = step < 0.05f
                ? CohesiveFormation.PackGrid(living, cx, cz)
                : CohesiveFormation.PackGrid(living, cx + ndx * step, cz + ndz * step);
            valid = MovementUtilities.ValidatePaths(candidate, BudgetFor(request),
            footprints, request.CanMoveThroughEnemies, request.IgnoresDifficultTerrain, request.IgnoresImpassibleTerrain, terrain, out _, friendlies, lenientCoherency: true);
            attempts++;
        }
        if (!valid)
        {
            // Reform in place to close casualty gaps...
            candidate = CohesiveFormation.PackGrid(living, cx, cz);
            valid = MovementUtilities.ValidatePaths(candidate, BudgetFor(request),
            footprints, request.CanMoveThroughEnemies, request.IgnoresDifficultTerrain, request.IgnoresImpassibleTerrain, terrain, out _, friendlies, lenientCoherency: true);

            // ...but a unit intermingled with enemies can't re-pack without a model crossing an enemy base;
            // hold exact positions then (zero-length paths can't move through anything).
            if (!valid)
                candidate = living
                    .Select(mb => new ModelMoveEntry(mb, new List<Position> { mb.GetValue().Position }))
                    .ToList();
        }

        Console.WriteLine("  [auto] advancing toward nearest enemy");
        return candidate;
    }

    // #093: cap each model against its OWN budget (a joined hero's Fast/Slow) so the CLI's validation agrees
    // with the authoritative per-model stage check. A unit with no per-model rules gets the unit scalars.
    private static Func<ModelMoveEntry, ModelMoveBudget> BudgetFor(DefineMovementPathRequest request)
        => entry =>
        {
            var (_, rush, maxDist) = request.BudgetFor(entry.Model.GetValue().ID);
            return new ModelMoveBudget(rush, maxDist);
        };

    // Living enemy model footprints (centre + radius), tagged with a per-unit key, for the move-through /
    // standoff validator. Empty when there's no table state (the resolver can run detached in tests).
    private List<EnemyModelFootprint> GetEnemyFootprints(DefineMovementPathRequest request)
    {
        var footprints = new List<EnemyModelFootprint>();
        if (_tableState == null) return footprints;

        int unitKey = 0;
        foreach (var u in _tableState.Units.Objects)
        {
            if (!TeamAwareness.IsEnemyUnit(_tableState, request.TargetPlayerID, u)) continue;
            bool uncontactable = FDG.Rules.Dispatch.AircraftRules.IsAircraft(u); // #029
            bool anyLiving = false;
            foreach (var m in u.Models)
                if (m.GetIsAlive())
                {
                    footprints.Add(new EnemyModelFootprint(m.Position, m.BaseRadiusInches, unitKey, uncontactable, m.BaseShape, m.Facing));
                    anyLiving = true;
                }
            if (anyLiving) unitKey++;
        }
        return footprints;
    }

    // #334: living enemy model poses, tagged with their unit, for the forced-charge standoff band. Separate
    // from GetEnemyFootprints because that one exists for the move-through validator and carries #029's
    // uncontactable flag; this one mirrors the ChooseActionStage gate, which screens on GetIsOnBattlefield
    // instead (#263 - a reserve unit sits at the origin and must not force anything).
    private List<(IUnit unit, ForcedChargeUtilities.StandoffPose pose)> EnemyPoses(DefineMovementPathRequest request)
    {
        var poses = new List<(IUnit, ForcedChargeUtilities.StandoffPose)>();
        if (_tableState == null) return poses;

        foreach (var u in _tableState.Units.Objects)
        {
            if (!TeamAwareness.IsEnemyUnit(_tableState, request.TargetPlayerID, u) || !u.GetIsOnBattlefield())
                continue;
            foreach (var m in u.Models)
                if (m.GetIsAlive())
                    poses.Add((u, new ForcedChargeUtilities.StandoffPose(m.Position, m.BaseShape, m.Facing)));
        }
        return poses;
    }

    /// <summary>
    /// Reports, on the accepted move, that it ends inside the standoff band - the CLI's half of #334. Returns
    /// the entries unchanged: the move is legal and is NOT re-prompted, exactly as the GUI leaves Done live.
    ///
    /// A CLI entry carries no facings, so the executor leaves each model's facing alone - which is why the
    /// measurement uses the model's CURRENT facing rather than a direction of travel.
    /// </summary>
    private List<ModelMoveEntry> WarnIfForcedCharge(DefineMovementPathRequest request, List<ModelMoveEntry> entries)
    {
        var enemies = EnemyPoses(request);
        if (enemies.Count == 0) return entries;

        var movers = new List<ForcedChargeUtilities.StandoffPose>();
        foreach (var entry in entries)
        {
            var model = entry.Model.GetValue();
            if (!model.GetIsAlive() || entry.Positions.Count == 0) continue;
            movers.Add(new ForcedChargeUtilities.StandoffPose(
                entry.Positions[^1], model.BaseShape, model.Facing));
        }

        var contacts = ForcedChargeUtilities.FindContacts(movers,
            enemies.Select(e => e.pose).ToList());
        if (contacts.Count == 0) return entries;

        var movingModels = new HashSet<int>(contacts.Select(c => c.MoverIndex));
        var names = new List<string>();
        foreach (var c in contacts)
        {
            string name = enemies[c.EnemyIndex].unit.Name;
            if (!names.Contains(name)) names.Add(name);
        }

        Console.WriteLine();
        Console.WriteLine($"  ! FORCED CHARGE: {movingModels.Count} model{(movingModels.Count != 1 ? "s" : "")} "
            + $"end{(movingModels.Count == 1 ? "s" : "")} within {GameWideConstants.ENEMY_STANDOFF_DISTANCE_INCHES:F0}\" "
            + $"of {string.Join(", ", names)}.");
        // Charge needs a melee weapon; Pass is gated by proximity alone. A rifle-only unit inside the band can
        // do neither and lands on the engine's zero-options fallback, so promising a Charge would be a lie.
        Console.WriteLine(request.UnitDataBinding.GetValue().GetMeleeWeapons().Count > 0
            ? "    This unit cannot Pass - it must Charge."
            : "    This unit cannot Pass, and has no melee weapon to Charge with.");
        return entries;
    }

    // #205: friendly model footprints (same team, excluding the moving unit) the move may not end stacked on.
    // Empty when detached (no table state). Reuses the engine's team-based gatherer so it matches the
    // authoritative DefinePathStage check exactly.
    private List<EnemyModelFootprint> GetFriendlyFootprints(DefineMovementPathRequest request)
        => _tableState == null
            ? new List<EnemyModelFootprint>()
            : MovementPlanner.LiveFriendlyFootprints(_tableState, request.TargetPlayerID,
                request.UnitDataBinding.GetValue().ID);

    private static List<ModelMoveEntry> StayInPlace(DefineMovementPathRequest request)
    {
        return request.UnitDataBinding.GetValue().ModelBindings
            .Select(mb => new ModelMoveEntry(mb, new List<Position> { mb.GetValue().Position }))
            .ToList();
    }
}
