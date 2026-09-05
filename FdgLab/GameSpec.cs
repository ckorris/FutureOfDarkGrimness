using FDG;
using FDG.Ai;
using FDG.SaveLoad;

namespace FdgLab;
// EAiProfile moved into the engine with the Tactician scaffold (#191 A0): the profile is selected
// at every launch path (CLI, scenario, lab), so the engine owns the enum and the dispatch
// (FDG.Ai.AiProfileFactory).

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
    bool CaptureLog = false,
    bool Trace = false,
    // #191 tooling: interleave each planning AI's Choose Action narration (winner + full scored
    // candidate table, prefixed "[ai N]") into the captured log - a replay of decisions, not
    // just outcomes. Requires CaptureLog.
    bool LogDecisions = false,
    // #191 step 10: what a Strategist in this game thinks under. Null = GameRunner.LabSearchBudget
    // (the 1-2s benchmark budget every bench before 2026-09-05 used). Set it to measure a DIFFERENT
    // bot than the lab default - notably UctOptions.Interactive, the 5-10s budget that actually
    // ships to players, which is ~4.3x the thinking time per activation at 2k and had never been
    // benchmarked when the B-gate's main matrix came in at 56.6%.
    FDG.Ai.Tactician.Search.UctOptions? SearchBudget = null)
{
    public static GameSpec TwoPlayer(SlotSpec a, SlotSpec b, int seed,
        ERandomnessType randomness = ERandomnessType.Realistic) =>
        new(new[] { a, b }, seed, randomness);

    /// <summary>
    /// A team game (B+C campaign generalization axis: 2v2 panels, section 5 of
    /// docs/tactician-bc-campaign.md): every slot in <paramref name="teamA"/> is stamped Team=0,
    /// every slot in <paramref name="teamB"/> Team=1, concatenated teamA-then-teamB - the same
    /// grouped seating convention as Scenarios/crowded-2v2-3k.json and ScenarioCompiler (teams
    /// occupy consecutive slots, not interleaved). FDGServer wires TeamData from these Team
    /// numbers automatically (GameBootstrap.AddTeams) - no other plumbing needed.
    /// </summary>
    public static GameSpec TeamGame(IReadOnlyList<SlotSpec> teamA, IReadOnlyList<SlotSpec> teamB, int seed,
        ERandomnessType randomness = ERandomnessType.Realistic)
    {
        var slots = new List<SlotSpec>(teamA.Count + teamB.Count);
        slots.AddRange(teamA.Select(s => s with { Team = 0 }));
        slots.AddRange(teamB.Select(s => s with { Team = 1 }));
        return new GameSpec(slots, seed, randomness);
    }
}

/// <summary>
/// One player slot: an army (already loaded) and the AI profile that plays it.
/// <see cref="Team"/> is null by default (every slot its own team - free-for-all, matching every
/// existing 1v1/FFA caller unchanged); set it to group slots into shared teams (2v2 etc).
/// </summary>
public sealed record SlotSpec(string ArmyLabel, ArmyListFile Army, EAiProfile Profile = EAiProfile.SoloRules,
    int? Team = null);

/// <summary>What one game produced. <see cref="Result"/> is the engine's structured record (#192).</summary>
public sealed record GameRecord(
    GameSpec Spec,
    GameResult Result,
    TimeSpan WallClock,
    DecisionStats Decisions,
    int? WinnerSlot,
    IReadOnlyList<string>? Log = null,
    IReadOnlyList<string>? Trace = null,
    /// <summary>Per-request-type decision cost: type name -> (calls, total ms). #191 step 3/5.</summary>
    IReadOnlyDictionary<string, (long Count, double TotalMs)>? DecisionsByType = null)
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
