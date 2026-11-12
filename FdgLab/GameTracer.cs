using System.Text;
using FDG;

namespace FdgLab;

/// <summary>
/// Records every position-bearing state change of a game — model position writes, terrain and
/// objective creations — interleaved with the game log, in one ordered trace (#198). Two runs of the
/// same seed must produce identical traces; when they don't, the first divergent line says exactly
/// which write diverged and the surrounding log lines say which stage was executing. This is the
/// instrument that turns "outcomes differ" into "THIS write differs".
/// </summary>
public sealed class GameTracer
{
    private readonly List<string> _entries = new();
    private readonly object _lock = new();
    private readonly Dictionary<IModel, string> _modelNames = new(ReferenceEqualityComparer.Instance);
    private int _modelCounter;
    private int _terrainCounter;
    private int _objectiveCounter;

    public IReadOnlyList<string> Entries { get { lock (_lock) return _entries.ToArray(); } }

    public void Attach(ITableState tableState)
    {
        // Creation order is store order, which is deterministic, so the per-run counters give models
        // stable identities across runs (PlayerID/instance identities do not survive a rerun).
        tableState.Models.OnObjectCreated += model =>
        {
            string name;
            lock (_lock)
            {
                name = $"M{_modelCounter++:D3}";
                _modelNames[model] = name;
                _entries.Add($"NEW {name} @ {Fmt(model.Position)}");
            }
            model.OnPositionChanged += (oldPos, newPos) =>
            {
                lock (_lock)
                    _entries.Add($"POS {name} {Fmt(oldPos)} -> {Fmt(newPos)}");
            };
        };

        tableState.Terrain.OnObjectCreated += _ =>
        {
            lock (_lock) _entries.Add($"TER T{_terrainCounter++:D2}");
        };

        tableState.Objectives.OnObjectCreated += objective =>
        {
            lock (_lock) _entries.Add($"OBJ O{_objectiveCounter++} @ {Fmt(objective.Position)}");
        };
    }

    public void AddLog(string line)
    {
        lock (_lock) _entries.Add($"LOG {line}");
    }

    private static string Fmt(Position p) => $"({p.x:F3},{p.z:F3})";
}
