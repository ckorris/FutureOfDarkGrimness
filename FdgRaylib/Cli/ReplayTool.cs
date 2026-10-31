using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using FDG;
using FDG.Data;
using FDG.Data.Containers;
using FDG.Replay;
using FDG.SaveLoad;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace FdgRaylib.Cli;

/// <summary>
/// <c>--replay FILE ...</c> (#455): reads a recorded game headless, for an agent debugging a reported game. One
/// mode per run - <c>--dump</c> (the step timeline with the log, beats, markers and store changes each step
/// produced), <c>--events</c> (every event, one line each, with a field diff for store updates), <c>--stats</c>
/// (sizes by kind / store type / request type, plan section 5), <c>--verify</c> (<see cref="ReplayVerifier"/>),
/// <c>--export OUT.jsonl</c> (the decompressed container) and <c>--at STEP --make-save OUT.fdgsave</c> (a
/// resumable save of the state at a step, for <c>--scenario</c>). Exit code 0 on success, 1 with a one-line
/// reason otherwise; never opens a window. Output is ASCII with width-stable columns and no colour (it is
/// read by agents and diffed).
/// <para>
/// Only <c>.fdgreplay</c> files today: a replay inside a <c>.fdgsave</c>, and <c>--make-save</c> embedding the
/// replay up to the cut, arrive with #453.
/// </para>
/// </summary>
public static class ReplayTool
{
    public const string Usage =
        "Usage: --replay FILE.fdgreplay (--dump [--from N] [--to M] [--folded] [--no-log] [--no-beats] [--debug-log]"
        + " | --events [--from SEQ] [--to SEQ] [--kind K,...] [--verbose] | --stats | --verify | --export OUT.jsonl"
        + " | --at STEP|end --make-save OUT.fdgsave)";

    private static readonly string[] Modes = { "--dump", "--events", "--stats", "--verify", "--export", "--make-save" };

    /// <summary>Runs one <c>--replay</c> command; returns the process exit code.</summary>
    public static int Run(string[] args, TextWriter output)
    {
        try
        {
            string path = ValueAfter(args, "--replay") ?? throw new ToolException("--replay needs a file. " + Usage);
            List<string> modes = Modes.Where(args.Contains).ToList();
            if (modes.Count != 1)
            {
                throw new ToolException((modes.Count == 0 ? "No mode given. " : $"One mode at a time (got {string.Join(", ", modes)}). ") + Usage);
            }

            Loaded replay = Loaded.Open(path);
            return modes[0] switch
            {
                "--dump" => Dump(replay, args, output),
                "--events" => Events(replay, args, output),
                "--stats" => Stats(replay, output),
                "--verify" => Verify(replay, output),
                "--export" => Export(replay, ValueAfter(args, "--export") ?? throw new ToolException("--export needs an output file."), output),
                _ => MakeSave(replay, args, output),
            };
        }
        catch (Exception exception) when (exception is ToolException or ReplayFormatException or ReplayPlaybackException
            or IOException or UnauthorizedAccessException)
        {
            output.WriteLine($"Replay error: {Ascii(exception.Message)}");
            return 1;
        }
    }

    // --- loading ------------------------------------------------------------------------------------

    private sealed class ToolException : Exception
    {
        public ToolException(string message) : base(message) { }
    }

    private sealed class Loaded
    {
        public string Path = "";
        public long FileBytes;
        public byte[] Gzip = Array.Empty<byte>();
        public ReplayDocument Document = null!;
        public ReplayTimeline Timeline = null!;

        private List<string>? _typeNames;

        public static Loaded Open(string path)
        {
            if (path.EndsWith(GameSaveFile.EXTENSION_WITH_PERIOD, StringComparison.OrdinalIgnoreCase))
            {
                throw new ToolException($"{path} is a save. Replays inside saves arrive with #453; pass the .fdgreplay file.");
            }
            if (File.Exists(path) == false)
            {
                throw new ToolException($"File not found: {path}");
            }

            byte[] bytes = File.ReadAllBytes(path);
            using var stream = new MemoryStream(bytes);
            ReplayDocument document = ReplayReader.ReadGzip(stream);
            return new Loaded
            {
                Path = path,
                FileBytes = bytes.Length,
                Gzip = bytes,
                Document = document,
                Timeline = new ReplayTimeline(document),
            };
        }

        /// <summary>The container's lines as written: the header, then one per event.</summary>
        public string Decompressed()
        {
            using var gzip = new GZipStream(new MemoryStream(Gzip), CompressionMode.Decompress);
            using var reader = new StreamReader(gzip, Encoding.UTF8);
            return reader.ReadToEnd();
        }

        /// <summary>Store type names by TypeID, from the recording's first snapshot (the default store without one).</summary>
        public string TypeName(int typeId)
        {
            _typeNames ??= (Timeline.Snapshots.Count > 0
                    ? GameSaveSerializer.Load(Timeline.Snapshots[0].SaveJson)
                    : GameDataStore.GameDataStoreBuilder.GetDefault())
                .GetTypeMap().Select(type => type.Name).ToList();
            return typeId >= 0 && typeId < _typeNames.Count ? _typeNames[typeId] : $"type{typeId}";
        }

        public string Entry(DataReference reference) => $"{TypeName(reference.TypeID.ID)}[{reference.Index}]";
    }

    // --- header -------------------------------------------------------------------------------------

    private static void PrintHeader(Loaded replay, TextWriter o)
    {
        ReplayHeader header = replay.Document.Header;
        ReplayTimeline timeline = replay.Timeline;
        IReadOnlyList<ReplayEvent> events = timeline.Events;

        Line(o, "Replay", $"{replay.Path}  ({Bytes(replay.FileBytes)} gzip, {events.Count} events)");
        Line(o, "Format", $"{header.FormatVersion}   build {Or(header.BuildVersion)}   engine {Or(header.EngineVersion)}");
        string recordedBy = header.RecordedBy.ToString()
            + (header.RecordedByPlayerId is PlayerID by ? $" ({PlayerLabel(header, by)})" : "");
        Line(o, "Game", $"{header.GameId}   seed {header.Seed?.ToString() ?? "none"}   {header.RandomnessType}   "
            + $"recorded by {recordedBy}   started {header.StartedUtc.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)}Z");
        foreach (ReplaySlot slot in header.Slots.OrderBy(s => s.SlotId))
        {
            Line(o, "Slot", $"P{slot.SlotId + 1}  {Controller(slot.Controller),-6}  team {slot.Team}  {Ascii(Or(slot.Name))}  -  {Ascii(Or(slot.ArmyName))}");
        }

        int rounds = timeline.Markers.Where(m => m.Marker == EReplayMarker.RoundStart).Select(m => m.Round ?? 0).DefaultIfEmpty(0).Max();
        int folded = timeline.Requests.Count - timeline.Steps.Count;
        Line(o, "Rounds", $"{rounds}   steps {timeline.Steps.Count} (+{folded} folded)   snapshots {timeline.Snapshots.Count}");

        MarkerEvent? ended = timeline.Markers.FirstOrDefault(m => m.Marker == EReplayMarker.GameEnded);
        string outcome = ended == null
            ? "(no GameEnded marker - abandoned or cut short)"
            : $"{ended.Outcome ?? "?"}{(string.IsNullOrEmpty(ended.Message) ? "" : " - " + Ascii(ended.Message))}";
        long durationMs = events.Count > 0 ? events[^1].T : 0;
        Line(o, "Outcome", $"{outcome}   duration {Duration(durationMs)}");

        foreach (string warning in replay.Document.Warnings)
        {
            Line(o, "Warning", warning);
        }
    }

    private static void Line(TextWriter o, string label, string text) => o.WriteLine($"{label,-9}{text}");

    // --- --dump -------------------------------------------------------------------------------------

    private static int Dump(Loaded replay, string[] args, TextWriter o)
    {
        bool folded = args.Contains("--folded");
        bool noLog = args.Contains("--no-log");
        bool noBeats = args.Contains("--no-beats");
        bool debugLog = args.Contains("--debug-log");

        ReplayTimeline timeline = replay.Timeline;
        IReadOnlyList<ReplayStep> steps = timeline.Steps;
        int from = IntAfter(args, "--from") ?? 0;
        int to = IntAfter(args, "--to") ?? steps.Count - 1;
        if (steps.Count > 0 && (from < 0 || to >= steps.Count || from > to))
        {
            throw new ToolException($"--from/--to must name steps 0-{steps.Count - 1} with from <= to (got {from}-{to}).");
        }

        PrintHeader(replay, o);
        if (debugLog == false && noLog == false)
        {
            Line(o, "Note", "Debug log lines are hidden (--debug-log shows them).");
        }

        // The player's constructor fills every step's Summary; its store at step 0 names the units.
        var player = new ReplayPlayer(timeline);
        Dictionary<(int, int), string> unitNames = player.Store.GetAllDataReferences<UnitData>()
            .ToDictionary(r => (r.TypeID.ID, r.Index), r => player.Store.GetValue<UnitData>(r).Name);

        var stepAt = steps.ToDictionary(step => step.RequestSeq);
        long startSeq = steps.Count == 0 || from == 0 ? long.MinValue : steps[from].RequestSeq;
        long endSeq = steps.Count == 0 || to + 1 >= steps.Count ? long.MaxValue : steps[to + 1].RequestSeq - 1;

        int indexWidth = Math.Max(3, (steps.Count - 1).ToString(CultureInfo.InvariantCulture).Length);
        int roundWidth = 1 + Math.Max(1, steps.Select(s => s.Round?.ToString(CultureInfo.InvariantCulture).Length ?? 1).DefaultIfEmpty(1).Max());
        int whoWidth = steps.Select(s => Who(timeline.Header, s).Length).DefaultIfEmpty(0).Max();
        int typeWidth = Math.Max(28, steps.Select(s => ShortType(s.RequestType).Length).DefaultIfEmpty(0).Max());

        var store = new StoreTally(replay);
        o.WriteLine();
        if (startSeq == long.MinValue)
        {
            o.WriteLine("Before the first step:");
        }

        foreach (ReplayEvent replayEvent in timeline.Events)
        {
            if (replayEvent.Seq < startSeq) continue;
            if (replayEvent.Seq > endSeq) break;

            switch (replayEvent)
            {
                case RequestEvent request when stepAt.TryGetValue(request.Seq, out ReplayStep? step):
                    store.Flush(o);
                    string round = (step.Round is int r ? "R" + r : "R-").PadRight(roundWidth);
                    string activation = step.Activation is int a ? "A" + a.ToString("00", CultureInfo.InvariantCulture) : "A--";
                    o.WriteLine($"#{step.Index.ToString(CultureInfo.InvariantCulture).PadLeft(indexWidth, '0')}  {round} {activation}  "
                        + $"{Who(timeline.Header, step).PadRight(whoWidth)}  {ShortType(step.RequestType).PadRight(typeWidth)}  {Ascii(step.Summary)}");
                    break;

                case RequestEvent request:
                    if (folded) o.WriteLine($"    folded: {FoldedText(timeline, request)}");
                    break;

                case StoreAddEvent or StoreUpdateEvent or StoreRemoveEvent:
                    store.Add(replayEvent);
                    break;

                case SnapshotEvent snapshot:
                    store.Flush(o);
                    o.WriteLine($"-- Snapshot {snapshot.Index}");
                    break;

                case MarkerEvent marker:
                    store.Flush(o);
                    o.WriteLine($"-- {MarkerText(timeline.Header, marker, reference => NameOf(unitNames, reference))}");
                    break;

                case LogEvent log:
                    if (noLog == false && (log.Debug == false || debugLog))
                        o.WriteLine($"    log: {(log.Debug ? "(debug) " : "")}{Ascii(log.Line)}");
                    break;

                case BeatEvent beat:
                    if (noBeats == false) o.WriteLine($"    beat: {BeatText(beat.BeatJson)}");
                    break;
            }
        }
        store.Flush(o);
        return 0;
    }

    private static string Who(ReplayHeader header, ReplayStep step) =>
        $"{PlayerLabel(header, step.PlayerId)} ({(step.Controller is EReplayController c ? Controller(c) : "?")})";

    private static string FoldedText(ReplayTimeline timeline, RequestEvent request)
    {
        ReplayRequest? joined = timeline.FindRequest(request.TaskId);
        int? options = DefaultStepFilter.CountOptions(request);
        string why = joined?.Error != null ? $"error: {joined.Error.Message}"
            : joined?.IsAnswered != true ? "unanswered"
            : options is int n ? $"{n} option{(n == 1 ? "" : "s")}"
            : "folded";
        return Ascii($"{PlayerLabel(timeline.Header, request.PlayerId)}  {ShortType(request.RequestType)}  {request.DisplayName}  ({why})");
    }

    /// <summary>"store: 2 adds, 14 updates (UnitData x3, Position x10, ...)" for the store events since the last line.</summary>
    private sealed class StoreTally
    {
        private readonly Loaded _replay;
        private readonly Dictionary<string, int> _byType = new Dictionary<string, int>();
        private int _adds, _updates, _removes;

        public StoreTally(Loaded replay) => _replay = replay;

        public void Add(ReplayEvent replayEvent)
        {
            DataReference reference;
            switch (replayEvent)
            {
                case StoreAddEvent add: _adds++; reference = add.Ref; break;
                case StoreUpdateEvent update: _updates++; reference = update.Ref; break;
                case StoreRemoveEvent remove: _removes++; reference = remove.Ref; break;
                default: return;
            }

            string type = _replay.TypeName(reference.TypeID.ID);
            _byType[type] = _byType.TryGetValue(type, out int count) ? count + 1 : 1;
        }

        public void Flush(TextWriter o)
        {
            if (_byType.Count == 0)
            {
                return;
            }

            var parts = new List<string>();
            if (_adds > 0) parts.Add(Plural(_adds, "add"));
            if (_updates > 0) parts.Add(Plural(_updates, "update"));
            if (_removes > 0) parts.Add(Plural(_removes, "remove"));
            string types = string.Join(", ", _byType.OrderByDescending(p => p.Value).ThenBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => $"{p.Key} x{p.Value}"));
            o.WriteLine($"    store: {string.Join(", ", parts)} ({types})");

            _byType.Clear();
            _adds = _updates = _removes = 0;
        }
    }

    // --- --events -----------------------------------------------------------------------------------

    private static readonly string[] Kinds =
        { "StoreAdd", "StoreUpdate", "StoreRemove", "Snapshot", "Request", "Reply", "RequestError", "Beat", "Log", "Marker" };

    private static int Events(Loaded replay, string[] args, TextWriter o)
    {
        long from = LongAfter(args, "--from") ?? long.MinValue;
        long to = LongAfter(args, "--to") ?? long.MaxValue;
        bool verbose = args.Contains("--verbose");
        HashSet<string>? kinds = null;
        if (ValueAfter(args, "--kind") is string kindList)
        {
            kinds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string kind in kindList.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                string? known = Kinds.FirstOrDefault(k => string.Equals(k, kind, StringComparison.OrdinalIgnoreCase));
                kinds.Add(known ?? throw new ToolException($"Unknown kind '{kind}'. Kinds: {string.Join(", ", Kinds)}."));
            }
        }

        ReplayTimeline timeline = replay.Timeline;
        ReplayHeader header = timeline.Header;
        int seqWidth = Math.Max(4, (timeline.Events.Count > 0 ? timeline.Events[^1].Seq : 0).ToString(CultureInfo.InvariantCulture).Length);

        // A request names its step (or says it folded); a step's reply says what was decided. The player's
        // constructor fills the summaries.
        _ = new ReplayPlayer(timeline);
        var stepByTask = timeline.Steps.ToDictionary(step => step.Decision.TaskId);

        // The last recorded value of every entry - from the latest snapshot, then each add/update - for the field
        // diff and for naming units. Tracked over the whole recording so a --from window still diffs correctly.
        var values = new Dictionary<(int, int), string>();
        string Name(DataReference reference) =>
            values.TryGetValue((reference.TypeID.ID, reference.Index), out string? json) && TopLevelString(json, "Name") is string name
                ? $"{replay.Entry(reference)} ({Ascii(name)})"
                : replay.Entry(reference);

        foreach (ReplayEvent replayEvent in timeline.Events)
        {
            bool show = replayEvent.Seq >= from && replayEvent.Seq <= to && (kinds == null || kinds.Contains(replayEvent.Kind));
            if (replayEvent.Seq > to) break;

            string detail = "";
            string? json = null;
            switch (replayEvent)
            {
                case StoreAddEvent add:
                    values[(add.Ref.TypeID.ID, add.Ref.Index)] = add.Json;
                    detail = $"{Name(add.Ref)}  gen {add.Ref.Generation}";
                    json = add.Json;
                    break;

                case StoreUpdateEvent update:
                    (int, int) key = (update.Ref.TypeID.ID, update.Ref.Index);
                    string? before = values.TryGetValue(key, out string? recorded) ? recorded : null;
                    values[key] = update.Json;
                    if (show)
                    {
                        string changed = before == null ? "changed: (no earlier value recorded)"
                            : ReplayJsonDiff.ChangedProperties(before, update.Json) is { Count: > 0 } fields ? "changed: " + string.Join(", ", fields)
                            : "unchanged";
                        detail = $"{Name(update.Ref)}  {changed}";
                    }
                    json = update.Json;
                    break;

                case StoreRemoveEvent remove:
                    detail = Name(remove.Ref);
                    values.Remove((remove.Ref.TypeID.ID, remove.Ref.Index));
                    break;

                case SnapshotEvent snapshot:
                    values.Clear();
                    foreach (ReferenceJsonValuePair entry in JsonConvert.DeserializeObject<GameSaveFile>(snapshot.SaveJson)?.Entries
                                 ?? new List<ReferenceJsonValuePair>())
                    {
                        values[(entry.DataReference.TypeID.ID, entry.DataReference.Index)] = entry.JsonValue;
                    }
                    detail = $"#{snapshot.Index}  {Bytes(Encoding.UTF8.GetByteCount(snapshot.SaveJson))} save JSON";
                    break;

                case RequestEvent request:
                    string role = stepByTask.TryGetValue(request.TaskId, out ReplayStep? asked) ? $"[step {asked.Index}]" : "[folded]";
                    detail = Ascii($"task {Short(request.TaskId)}  {PlayerLabel(header, request.PlayerId)}  {ShortType(request.RequestType)}  {request.DisplayName}  {role}");
                    json = request.RequestJson;
                    break;

                case ReplyEvent reply:
                    detail = stepByTask.TryGetValue(reply.TaskId, out ReplayStep? answered) && answered.ReplySeq == reply.Seq
                        ? Ascii($"task {Short(reply.TaskId)}  step {answered.Index}: {answered.Summary}")
                        : $"task {Short(reply.TaskId)}  {Cut(Ascii(Compact(reply.ReplyJson)), 100)}";
                    json = reply.ReplyJson;
                    break;

                case RequestErrorEvent error:
                    detail = Ascii($"task {Short(error.TaskId)}  {error.Message}");
                    break;

                case BeatEvent beat:
                    detail = BeatText(beat.BeatJson);
                    json = beat.BeatJson;
                    break;

                case LogEvent log:
                    detail = Ascii((log.Debug ? "(debug) " : "") + log.Line);
                    break;

                case MarkerEvent marker:
                    detail = MarkerText(header, marker, Name);
                    break;
            }

            if (show)
            {
                string time = (replayEvent.T / 1000.0).ToString("0.000", CultureInfo.InvariantCulture);
                o.WriteLine($"{replayEvent.Seq.ToString(CultureInfo.InvariantCulture).PadLeft(seqWidth)}  {time,9}s  {replayEvent.Kind,-12}  {detail}");
                if (verbose && json != null)
                {
                    o.WriteLine($"{new string(' ', seqWidth + 2)}{Ascii(json)}");
                }
            }
        }
        return 0;
    }

    // --- --stats ------------------------------------------------------------------------------------

    private sealed class Group
    {
        public int Count;
        public long Raw;
        public readonly StringBuilder Text = new StringBuilder();

        public void Add(string line)
        {
            Count++;
            Raw += Encoding.UTF8.GetByteCount(line) + 1;
            Text.Append(line).Append('\n');
        }

        public long Gzip()
        {
            using var buffer = new MemoryStream();
            using (var gzip = new GZipStream(buffer, CompressionLevel.Optimal, leaveOpen: true))
            {
                byte[] bytes = Encoding.UTF8.GetBytes(Text.ToString());
                gzip.Write(bytes, 0, bytes.Length);
            }
            return buffer.Length;
        }
    }

    private static int Stats(Loaded replay, TextWriter o)
    {
        // The lines as written pair one-to-one with the document's events (the writer never writes a blank line).
        List<string> lines = replay.Decompressed().Split('\n').Where(line => line.Length > 0).ToList();
        IReadOnlyList<ReplayEvent> events = replay.Timeline.Events;
        if (lines.Count != events.Count + 1)
        {
            throw new ToolException($"The file has {lines.Count} lines for {events.Count} events plus a header; cannot attribute sizes.");
        }

        var byKind = new SortedDictionary<string, Group>(StringComparer.Ordinal);
        var byStoreType = new SortedDictionary<string, Group>(StringComparer.Ordinal);
        var byRequest = new SortedDictionary<string, Group>(StringComparer.Ordinal);
        var byBeat = new SortedDictionary<string, Group>(StringComparer.Ordinal);
        var all = new Group();
        Group For(SortedDictionary<string, Group> groups, string key) =>
            groups.TryGetValue(key, out Group? group) ? group : groups[key] = new Group();

        var snapshots = new List<(SnapshotEvent Snapshot, Group Line)>();
        for (int i = 0; i < events.Count; i++)
        {
            string line = lines[i + 1];
            ReplayEvent replayEvent = events[i];
            all.Add(line);
            For(byKind, replayEvent.Kind).Add(line);
            switch (replayEvent)
            {
                case StoreAddEvent add: For(byStoreType, $"{add.Kind}:{replay.TypeName(add.Ref.TypeID.ID)}").Add(line); break;
                case StoreUpdateEvent update: For(byStoreType, $"{update.Kind}:{replay.TypeName(update.Ref.TypeID.ID)}").Add(line); break;
                case StoreRemoveEvent remove: For(byStoreType, $"{remove.Kind}:{replay.TypeName(remove.Ref.TypeID.ID)}").Add(line); break;
                case RequestEvent request: For(byRequest, ShortType(request.RequestType)).Add(line); break;
                case BeatEvent beat: For(byBeat, BeatType(beat.BeatJson)).Add(line); break;
                case SnapshotEvent snapshot:
                    var group = new Group();
                    group.Add(line);
                    snapshots.Add((snapshot, group));
                    break;
            }
        }

        Line(o, "Replay", $"{replay.Path}");
        Line(o, "Size", $"{Bytes(replay.FileBytes)} gzip, {Bytes(all.Raw + Encoding.UTF8.GetByteCount(lines[0]) + 1)} raw, "
            + $"{events.Count} events (header {Bytes(Encoding.UTF8.GetByteCount(lines[0]) + 1)})");
        Line(o, "Note", "Each group is gzipped on its own; the file compresses them together, so groups do not sum to it.");

        Table(o, "By kind", byKind, all);
        Table(o, "By store type", byStoreType, null);
        Table(o, "By request type", byRequest, null);
        Table(o, "By beat type", byBeat, null);

        o.WriteLine();
        o.WriteLine($"{"Snapshots",-34}{"seq",7}  {"raw",10}  {"gzip",10}");
        foreach ((SnapshotEvent snapshot, Group line) in snapshots)
        {
            o.WriteLine($"  {("#" + snapshot.Index),-32}{snapshot.Seq,7}  {Bytes(line.Raw),10}  {Bytes(line.Gzip()),10}");
        }
        return 0;
    }

    private static void Table(TextWriter o, string title, SortedDictionary<string, Group> groups, Group? total)
    {
        if (groups.Count == 0)
        {
            return;
        }

        int labelWidth = Math.Max(32, groups.Keys.Max(k => k.Length) + 2);
        o.WriteLine();
        o.WriteLine($"{title.PadRight(labelWidth + 2)}{"count",7}  {"raw",10}  {"gzip",10}");
        foreach (KeyValuePair<string, Group> group in groups.OrderByDescending(g => g.Value.Raw).ThenBy(g => g.Key, StringComparer.Ordinal))
        {
            o.WriteLine($"  {group.Key.PadRight(labelWidth)}{group.Value.Count,7}  {Bytes(group.Value.Raw),10}  {Bytes(group.Value.Gzip()),10}");
        }
        if (total != null)
        {
            o.WriteLine($"  {"(all events)".PadRight(labelWidth)}{total.Count,7}  {Bytes(total.Raw),10}  {Bytes(total.Gzip()),10}");
        }
    }

    // --- --verify -----------------------------------------------------------------------------------

    private static int Verify(Loaded replay, TextWriter o)
    {
        Line(o, "Verify", replay.Path);
        foreach (string warning in replay.Document.Warnings)
        {
            Line(o, "Warning", warning);
        }

        ReplayVerification result = ReplayVerifier.Verify(replay.Timeline);
        int seqWidth = Math.Max(4, result.Failures.Concat(result.Notes).Select(i => i.Seq.ToString(CultureInfo.InvariantCulture).Length).DefaultIfEmpty(0).Max());
        void Print(string label, ReplayVerifyIssue issue)
        {
            string lead = $"{label,-5} {issue.Check,-9} Seq {issue.Seq.ToString(CultureInfo.InvariantCulture).PadLeft(seqWidth)}  ";
            o.WriteLine(lead + issue.Message);
            foreach (string detail in issue.Details)
            {
                o.WriteLine(new string(' ', lead.Length + 2) + detail);
            }
        }

        foreach (ReplayVerifyIssue failure in result.Failures) Print("FAIL", failure);
        foreach (ReplayVerifyIssue note in result.Notes) Print("note", note);

        string counts = $"{Plural(result.SnapshotsCompared, "snapshot")} rebuilt and compared, {Plural(result.DeltasApplied, "delta")} applied, "
            + $"{Plural(result.BeatsRead, "beat")} read, {Plural(result.Failures.Count, "failure")}, {Plural(result.Notes.Count, "note")}";
        if (result.Passed)
        {
            o.WriteLine($"Result: PASS - {counts}.");
            return 0;
        }

        ReplayVerifyIssue first = result.Failures[0];
        o.WriteLine($"Result: FAIL - {counts}; first: Seq {first.Seq} ({first.Check}) {first.Message}");
        return 1;
    }

    // --- --export -----------------------------------------------------------------------------------

    private static int Export(Loaded replay, string outPath, TextWriter o)
    {
        string text = replay.Decompressed();
        CreateDirectoryFor(outPath);
        File.WriteAllText(outPath, text, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        int lines = text.Count(c => c == '\n');
        o.WriteLine($"Exported {lines} lines ({Bytes(Encoding.UTF8.GetByteCount(text))}) -> {outPath}");
        return 0;
    }

    // --- --at --make-save ---------------------------------------------------------------------------

    private static int MakeSave(Loaded replay, string[] args, TextWriter o)
    {
        string outPath = ValueAfter(args, "--make-save") ?? throw new ToolException("--make-save needs an output file.");
        string at = ValueAfter(args, "--at") ?? throw new ToolException("--make-save needs --at STEP (a step index, or 'end').");
        ReplayTimeline timeline = replay.Timeline;
        IReadOnlyList<ReplayStep> steps = timeline.Steps;

        bool atEnd = string.Equals(at, "end", StringComparison.OrdinalIgnoreCase);
        int index = 0;
        if (atEnd == false && (int.TryParse(at, NumberStyles.None, CultureInfo.InvariantCulture, out index) == false || index >= steps.Count))
        {
            throw new ToolException($"--at takes a step 0-{steps.Count - 1} or 'end' (got '{at}').");
        }

        var player = new ReplayPlayer(timeline);
        if (atEnd) player.SeekToEnd();
        else player.SeekToStep(index);

        // The engine resumes only into the main phase, from the #052 progress record the first activation
        // boundary writes (FDGServer's resume constructor refuses a store without one), so a cut before round 1
        // would write a save nothing can load.
        GameProgressData? progress = GameProgressUtilities.TryGetProgress(player.Store);
        if (progress == null)
        {
            int? firstResumable = FirstResumableStep(player);
            throw new ToolException($"Step {index} is before the main phase (no game-progress record yet): "
                + "a save from there cannot be resumed. "
                + (firstResumable == null ? "No step of this replay can be." : $"The first resumable step is {firstResumable}."));
        }

        string saveJson = GameSaveSerializer.Save(player.Store);
        CreateDirectoryFor(outPath);
        File.WriteAllText(outPath, saveJson);

        ReplayHeader header = timeline.Header;
        if (atEnd)
        {
            o.WriteLine($"The end of the recording (after step {steps.Count - 1}): the state the game finished in.");
        }
        else
        {
            ReplayStep step = steps[index];
            o.WriteLine(Ascii($"Step {index} of {steps.Count} (R{step.Round} A{step.Activation?.ToString("00", CultureInfo.InvariantCulture) ?? "--"}, "
                + $"{Who(header, step)} {ShortType(step.RequestType)}: {step.Summary}) - the state that decision was asked in."));
        }
        o.WriteLine($"Wrote {outPath} ({Bytes(Encoding.UTF8.GetByteCount(saveJson))}).");

        o.WriteLine($"Resume: the game resumes at round {progress.RoundCount}'s activation boundary saved in its progress record "
            + "(#052): an activation already under way when the save was cut re-plays from its start, on the board as it stands here.");
        if (atEnd == false && steps[index].Activation is int activation
            && steps.FirstOrDefault(s => s.Round == steps[index].Round && s.Activation == activation) is ReplayStep first
            && first.Index != index)
        {
            o.WriteLine($"Resume: step {index} is inside activation R{first.Round} A{activation:00}; for a clean cut use --at {first.Index} (its first decision).");
        }
        o.WriteLine("Note: the save carries no replay; embedding the replay up to the cut arrives with #453.");
        o.WriteLine($"Play it: --headless --scenario {outPath}   (add --all-ai for AI on every slot)");
        return 0;
    }

    // The first step whose state carries the progress record (the main phase's first activation boundary writes it).
    private static int? FirstResumableStep(ReplayPlayer player)
    {
        foreach (ReplayStep step in player.Steps.Where(s => s.Round != null))
        {
            player.SeekToStep(step.Index);
            if (GameProgressUtilities.TryGetProgress(player.Store) != null)
            {
                return step.Index;
            }
        }
        return null;
    }

    // --- shared formatting --------------------------------------------------------------------------

    private static string MarkerText(ReplayHeader header, MarkerEvent marker, Func<DataReference, string> nameOf)
    {
        var text = new StringBuilder(marker.Marker.ToString());
        if (marker.Round is int round) text.Append($" R{round}");
        if (marker.Activation is int activation) text.Append($" A{activation:00}");
        if (marker.PlayerId is PlayerID player) text.Append($" {PlayerLabel(header, player)}");
        if (marker.UnitRef is DataReference unit) text.Append($" {nameOf(unit)}");
        if (marker.Outcome != null) text.Append($" {marker.Outcome}");
        if (string.IsNullOrEmpty(marker.Message) == false) text.Append($" - {marker.Message}");
        return Ascii(text.ToString());
    }

    private static string NameOf(Dictionary<(int, int), string> unitNames, DataReference reference) =>
        unitNames.TryGetValue((reference.TypeID.ID, reference.Index), out string? name) ? name : $"unit[{reference.Index}]";

    private static readonly Regex TimeSpanText = new Regex(@"^-?(\d+\.)?\d{2}:\d{2}:\d{2}(\.\d+)?$", RegexOptions.CultureInvariant);
    private static readonly Regex EnumName = new Regex(@"^[A-Z][A-Za-z0-9]*$", RegexOptions.CultureInvariant);

    /// <summary>
    /// "DiceRolledBeat - Roll to Hit (3+): 4 / 6; ResultSummary: 4 hits": the beat's type, its <c>Text</c>, then any
    /// other prose-like top-level string not already in the text (durations and enum names are skipped).
    /// </summary>
    private static string BeatText(string beatJson)
    {
        if (RecordedJsonObject(beatJson) is not JObject beat)
        {
            return "(unreadable beat)";
        }

        string? text = beat.Value<string>("Text");
        var parts = new List<string>();
        foreach (JProperty property in beat.Properties())
        {
            if (property.Name == "$type" || property.Name == "Text" || property.Value.Type != JTokenType.String)
            {
                continue;
            }

            string value = property.Value.Value<string>() ?? "";
            if (value.Length == 0 || TimeSpanText.IsMatch(value) || EnumName.IsMatch(value) || (text != null && text.Contains(value, StringComparison.Ordinal)))
            {
                continue;
            }
            parts.Add($"{property.Name}: {value}");
        }

        string type = TypeNameOf(beat.Value<string>("$type"));
        string body = string.IsNullOrEmpty(text) ? "" : " - " + text;
        return Ascii(type + body + (parts.Count > 0 ? "; " + string.Join("; ", parts) : ""));
    }

    private static string BeatType(string beatJson) =>
        RecordedJsonObject(beatJson) is JObject beat ? TypeNameOf(beat.Value<string>("$type")) : "(unreadable)";

    // "FDG.Presentation.Beats.BannerBeat, FutureOfDarkGrimness" -> "BannerBeat".
    private static string TypeNameOf(string? typeName) => typeName == null ? "?" : ShortType(typeName.Split(',')[0]);

    private static JObject? RecordedJsonObject(string json)
    {
        try
        {
            using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None };
            return JToken.ReadFrom(reader) as JObject;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    // Recorded JSON without its $type tags and $values wrappers, on one line: what a reader needs from a reply.
    private static string Compact(string json)
    {
        JToken? token;
        try
        {
            using var reader = new JsonTextReader(new StringReader(json)) { DateParseHandling = DateParseHandling.None };
            token = JToken.ReadFrom(reader);
        }
        catch (JsonException)
        {
            return json;
        }

        static JToken Strip(JToken node) => node switch
        {
            JObject wrapper when wrapper["$values"] is JArray values => Strip(values),
            JObject obj => new JObject(obj.Properties().Where(p => p.Name != "$type").Select(p => new JProperty(p.Name, Strip(p.Value)))),
            JArray array => new JArray(array.Select(Strip)),
            _ => node,
        };
        return Strip(token).ToString(Formatting.None);
    }

    private static string? TopLevelString(string json, string property) =>
        RecordedJsonObject(json)?[property] is JValue { Type: JTokenType.String } value ? value.Value<string>() : null;

    /// <summary>The recorded CLR type name without namespace or generic arguments.</summary>
    private static string ShortType(string recordedType)
    {
        string name = recordedType;
        int generic = name.IndexOf('`');
        if (generic >= 0) name = name.Substring(0, generic);
        int dot = name.LastIndexOf('.');
        return dot >= 0 ? name.Substring(dot + 1) : name;
    }

    private static string PlayerLabel(ReplayHeader header, PlayerID player)
    {
        ReplaySlot? slot = header.Slots.FirstOrDefault(s => s.PlayerId == player);
        return slot == null ? "P?" : $"P{slot.SlotId + 1}";
    }

    private static string Controller(EReplayController controller) => controller switch
    {
        EReplayController.Ai => "AI",
        _ => controller.ToString(),
    };

    private static string Short(FDG.StageResolution.TaskID task) => task.ID.ToString().Substring(0, 8);

    private static string Or(string? text) => string.IsNullOrEmpty(text) ? "(unknown)" : text;

    private static string Cut(string text, int length) => text.Length <= length ? text : text.Substring(0, length - 3) + "...";

    private static string Plural(long count, string noun) => $"{count} {noun}{(count == 1 ? "" : "s")}";

    private static string Bytes(long bytes) => bytes switch
    {
        < 1024 => $"{bytes} B",
        < 1024 * 1024 => (bytes / 1024.0).ToString("0.0", CultureInfo.InvariantCulture) + " KB",
        _ => (bytes / (1024.0 * 1024.0)).ToString("0.00", CultureInfo.InvariantCulture) + " MB",
    };

    private static string Duration(long milliseconds)
    {
        TimeSpan span = TimeSpan.FromMilliseconds(milliseconds);
        return span.TotalHours >= 1
            ? $"{(int)span.TotalHours}h {span.Minutes:00}m {span.Seconds:00}s"
            : $"{span.Minutes}m {span.Seconds:00}s";
    }

    private static string Ascii(string text) => ReplayText.Ascii(text).Replace('\n', ' ');

    private static void CreateDirectoryFor(string path)
    {
        string? directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path));
        if (string.IsNullOrEmpty(directory) == false) Directory.CreateDirectory(directory);
    }

    // --- argument parsing ---------------------------------------------------------------------------

    private static string? ValueAfter(string[] args, string flag)
    {
        int index = Array.IndexOf(args, flag);
        return index >= 0 && index + 1 < args.Length && args[index + 1].StartsWith("--", StringComparison.Ordinal) == false
            ? args[index + 1]
            : null;
    }

    private static int? IntAfter(string[] args, string flag) => LongAfter(args, flag) switch
    {
        null => null,
        long value when value is >= int.MinValue and <= int.MaxValue => (int)value,
        long value => throw new ToolException($"{flag} is out of range (got {value})."),
    };

    private static long? LongAfter(string[] args, string flag)
    {
        if (Array.IndexOf(args, flag) < 0)
        {
            return null;
        }

        string? value = ValueAfter(args, flag);
        return long.TryParse(value, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out long parsed)
            ? parsed
            : throw new ToolException($"{flag} needs a number (got '{value ?? ""}').");
    }
}
