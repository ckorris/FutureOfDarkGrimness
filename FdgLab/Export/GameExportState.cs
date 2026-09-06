using FDG;
using FDG.Ai.Tactician;

namespace FdgLab.Export;

/// <summary>
/// Per-game C1 export bookkeeping (#191 step 4), shared by every player's <see cref="ExportingRegistry"/>
/// in that game: the global boundary counter (schema's "boundary" field, 0-based within the
/// GAME), per-round bookkeeping for activation_frac/acting_side_is_first (round-scoped facts
/// PositionEncoder cannot derive from ITableState alone, see its type doc), the row buffer, and
/// (for the 5% sampled games) the entity table buffer.
/// </summary>
public sealed class GameExportState
{
    public readonly string GameId;
    public readonly bool EntitySampled;
    private readonly List<ExportRow> _rows = new();
    private readonly List<(int Boundary, List<float[]> Entities)> _entityRows = new();
    private readonly object _lock = new();

    private int _globalBoundary = -1;
    private int _lastRound = -1;
    private int _boundaryInRound = -1;
    private int _expectedBoundariesThisRound = 1;
    private PlayerID? _firstActorThisRound;

    private double _encoderMsTotal;
    private int _encoderCalls;

    /// <summary>
    /// <paramref name="seed"/> is the game's own seed, and the GameId is derived from it rather
    /// than random (#191 step 12a): schema sec 7 check 5 asks for byte-identical output on a fixed
    /// seed, which a fresh <c>Guid.NewGuid()</c> per game made impossible to check literally - the
    /// content matched but every id differed. Seeds never repeat inside an output directory (a
    /// resumed run starts past the last batch, a new run gets a new seed base), so this is unique
    /// where it needs to be and reproducible where that is worth more.
    /// </summary>
    public GameExportState(bool entitySampled, int seed)
    {
        EntitySampled = entitySampled;
        byte[] hash = System.Security.Cryptography.SHA256.HashData(BitConverter.GetBytes(seed));
        GameId = new Guid(hash.AsSpan(0, 16)).ToString("N");
    }

    public int NextGlobalBoundary() { lock (_lock) return ++_globalBoundary; }

    /// <summary>
    /// Call once per activation, BEFORE encoding, with the acting player. Returns the within-round
    /// boundary index, this round's expected-boundary estimate (living-unit count observed at the
    /// round's first activation - a cheap proxy, not tracked exactly), and whether this player's
    /// side took the round's first activation (observed order, per Encode's own doc).
    /// </summary>
    public (int BoundaryInRound, int Expected, bool ActingSideIsFirst) OpenBoundary(
        ITableState tableState, PlayerID actingPlayer)
    {
        lock (_lock)
        {
            int round = tableState.Progress.RoundCount ?? 1;
            if (round != _lastRound)
            {
                _lastRound = round;
                _boundaryInRound = -1;
                _firstActorThisRound = actingPlayer;
                _expectedBoundariesThisRound = Math.Max(1,
                    tableState.Armies.Objects.Sum(a => a is ArmyData d
                        ? d.UnitBindings.Count(u => u.GetValue().GetIsAlive() && u.GetValue().GetIsOnBattlefield())
                        : 0));
            }
            _boundaryInRound++;
            bool actingSideIsFirst = _firstActorThisRound.HasValue
                && (actingPlayer.Equals(_firstActorThisRound.Value)
                    || TacticalAnalysis.AreAllied(tableState, actingPlayer, _firstActorThisRound.Value));
            return (_boundaryInRound, _expectedBoundariesThisRound, actingSideIsFirst);
        }
    }

    public void RecordEncoderMs(double ms)
    {
        lock (_lock) { _encoderMsTotal += ms; _encoderCalls++; }
    }

    public double EncoderMsMean { get { lock (_lock) return _encoderCalls == 0 ? 0 : _encoderMsTotal / _encoderCalls; } }

    public void FlushRow(ExportRow row) { lock (_lock) _rows.Add(row); }

    public void AddEntityRows(int boundary, List<float[]> entities)
    {
        lock (_lock) _entityRows.Add((boundary, entities));
    }

    /// <summary>Rows captured so far - labels (Result/ObjDiffNorm/RoundsPlayed) are unset until the
    /// caller joins them at game end (schema sec 1).</summary>
    public IReadOnlyList<ExportRow> Rows { get { lock (_lock) return _rows.ToArray(); } }

    public IReadOnlyList<(int Boundary, List<float[]> Entities)> EntityRows { get { lock (_lock) return _entityRows.ToArray(); } }
}
