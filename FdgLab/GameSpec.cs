using FDG;
using FDG.SaveLoad;

namespace FdgLab;

/// <summary>
/// Which AI drives a slot. Only the solo-rules bot exists today; the Tactician (#191) joins here,
/// which is the point of the enum — every rung of the ladder stays benchmarkable against every other.
/// </summary>
public enum EAiProfile
{
    SoloRules,
}

/// <summary>
/// Everything needed to run one fully in-process AI-vs-AI game reproducibly (#194).
/// <para>
/// The seed is the GAME seed (<see cref="GameSettings.DiceSeed"/>): the engine derives each AI
/// player's own stream from it by SLOT ID (#193). Slot identity here is positional — slot i plays
/// <see cref="Slots"/>[i] — which is what keeps seeded games reproducible; never key anything on the
/// PlayerID GUIDs, which are minted fresh per run.
/// </para>
/// </summary>
public sealed record GameSpec(
    IReadOnlyList<SlotSpec> Slots,
    int Seed,
    ERandomnessType Randomness = ERandomnessType.Realistic,
    int WatchdogSeconds = 120,
    bool CaptureLog = false)
{
    public static GameSpec TwoPlayer(SlotSpec a, SlotSpec b, int seed,
        ERandomnessType randomness = ERandomnessType.Realistic) =>
        new(new[] { a, b }, seed, randomness);
}

/// <summary>One player slot: an army (already loaded) and the AI profile that plays it.</summary>
public sealed record SlotSpec(string ArmyLabel, ArmyListFile Army, EAiProfile Profile = EAiProfile.SoloRules);

/// <summary>What one game produced. <see cref="Result"/> is the engine's structured record (#192).</summary>
public sealed record GameRecord(
    GameSpec Spec,
    GameResult Result,
    TimeSpan WallClock,
    DecisionStats Decisions,
    int? WinnerSlot,
    IReadOnlyList<string>? Log = null)
{
    /// <summary>True when the watchdog killed the game rather than the engine finishing it.</summary>
    public bool TimedOut => Result.Outcome == EGameOutcome.Fault && Result.Message.StartsWith("watchdog:");

    /// <summary>The winning slot's army label, or null on tie/fault.</summary>
    public string? WinnerArmy => WinnerSlot.HasValue ? Spec.Slots[WinnerSlot.Value].ArmyLabel : null;
}

/// <summary>
/// Resolver-call timing across a game (all slots pooled): every AI decision passes through
/// <see cref="TimingRegistry"/>, so Count is also "how many decisions the game asked for".
/// </summary>
public sealed record DecisionStats(int Count, double TotalMs, double MeanMs, double P95Ms, double MaxMs)
{
    public static DecisionStats From(IReadOnlyList<double> samplesMs)
    {
        if (samplesMs.Count == 0) return new DecisionStats(0, 0, 0, 0, 0);
        var sorted = samplesMs.OrderBy(x => x).ToArray();
        double total = sorted.Sum();
        return new DecisionStats(
            Count: sorted.Length,
            TotalMs: total,
            MeanMs: total / sorted.Length,
            P95Ms: sorted[(int)Math.Min(sorted.Length - 1, Math.Ceiling(sorted.Length * 0.95) - 1)],
            MaxMs: sorted[^1]);
    }
}
