using FDG;

namespace FdgRaylib.Rendering;

/// <summary>
/// #401 - the wording both wound-assignment fronts (the ImGui panel and the CLI prompt) use for a
/// queue that carries Deadly clumps. A plain volley keeps the old "x / y wounds assigned" line; once
/// a clump is in the queue the count alone misleads, because a clump's excess is lost at the model it
/// lands on and the headline can never be reached. So the two fronts say what the next click does
/// instead, from the same strings, and cannot drift apart. ASCII only (CLAUDE.md).
/// </summary>
public static class WoundAssignmentText
{
    /// <summary>
    /// Whether the clump presentation (explanation, chip strip, per-model preview) says anything the
    /// plain dialog would not. A clump matters only where a model could take more than one wound: against
    /// a unit of 1-wound models every clump kills exactly one model, so the defender's choice is the
    /// same as for a plain volley and the dialog should look the same (Chris, after the first GUI pass).
    /// A joined Tough hero among 1-wound grunts keeps it on - the clumps matter for the hero.
    /// </summary>
    public static bool ClumpModeMatters(AssignWoundsResults results) =>
        results.HasConfinedPackets
        && results.PendingWounds.Any(entry => entry.Model.GetValue().TotalWounds > 1f);

    /// <summary>The header's progress line(s). One line for a plain pool; two for a clump queue.</summary>
    public static string Progress(AssignWoundsResults results)
    {
        if (!results.HasConfinedPackets)
        {
            return $"{WoundFormat.Format(results.TotalAssignedWounds)} / " +
                   $"{WoundFormat.Format(results.TotalWoundsToAssign)} wounds assigned";
        }
        if (!ClumpModeMatters(results))
        {
            // Clumps into 1-wound models: each one is one kill, so say so plainly - and the total is what
            // will land (4 clumps of 3 land 4), not the queue's carry (12).
            return $"{WoundFormat.Format(results.TotalAssignedWounds)} / " +
                   $"{WoundFormat.Format(results.AutoFillTotal())} wounds assigned";
        }

        string tally = $"{WoundFormat.Format(results.TotalAssignedWounds)} landed, " +
                       $"{WoundFormat.Format(results.ClumpExcessLost)} lost (Deadly: no carry-over)";
        return $"{tally}\n{Next(results)}";
    }

    /// <summary>What the next click places: "Clump 2 of 4 - 3 wounds to ONE model", or the plain
    /// tail of a mixed queue.</summary>
    public static string Next(AssignWoundsResults results)
    {
        WoundPacket? next = results.NextPacket;
        if (next == null) return "All wounds placed.";
        int number = results.PacketsCommitted + 1;
        int count = results.Packets.Count;
        return next.Confined
            ? $"Next: clump {number} of {count} - {Wounds(next.WeightedWounds)} to ONE model"
            : $"Next: {Wounds(next.Wounds)}, spread as needed ({number} of {count})";
    }

    /// <summary>"1 wound", "3 wounds", "0.67 wounds" - the count with its noun agreed.</summary>
    public static string Wounds(float count) =>
        $"{WoundFormat.Format(count)} wound{(count == 1f ? "" : "s")}";

    /// <summary>The click affordance for a legal target - what landing the next packet here means.</summary>
    public static string ClickHint(AssignWoundsResults results)
    {
        WoundPacket? next = results.NextPacket;
        return next != null && next.Confined && ClumpModeMatters(results)
            ? $"Click to land this clump here ({Wounds(next.WeightedWounds)} - what does not fit is lost)"
            : "Click to assign wounds (fills this model)";
    }

    /// <summary>Why the panel is in clump mode, for a queue that has clumps. Names Deadly's X, and
    /// mentions Regeneration only when some clump actually shrank. Empty for a plain volley or when
    /// <see cref="ClumpModeMatters"/> is false.</summary>
    public static string Explanation(AssignWoundsResults results)
    {
        if (!ClumpModeMatters(results)) return "";
        float x = results.Packets.Where(packet => packet.Confined).Max(packet => packet.OriginalWounds);
        string text = $"Deadly({WoundFormat.Format(x)}): each failed save is a clump of {Wounds(x)} that must all " +
                      "land on ONE model. What that model cannot absorb is lost - it never carries to the next.";
        if (results.Packets.Any(packet => packet.Ignored > 0f))
            text += " Regeneration was rolled per clump: a smaller clump is one that ignored some.";
        return text;
    }

    /// <summary>A clump's state in the strip: "1: 1 -> M3, 2 lost" (placed), "2: 1 wound" (next),
    /// "3: 3" (pending). <paramref name="rowOf"/> maps a committed model to its row number.</summary>
    public static string ChipLabel(AssignWoundsResults results, int index, Func<PacketCommit, int> rowOf)
    {
        WoundPacket packet = results.Packets[index];
        string number = $"{index + 1}:";
        if (index < results.PacketsCommitted)
        {
            var commits = results.Commits.Where(commit => commit.PacketIndex == index).ToList();
            float landed = commits.Sum(commit => commit.Landed);
            float lost = commits.Sum(commit => commit.Lost);
            string rows = string.Join("+", commits.Select(commit => $"M{rowOf(commit)}").Distinct());
            string lostNote = lost > AssignWoundsResults.WoundEpsilon ? $", {WoundFormat.Format(lost)} lost" : "";
            return $"{number} {WoundFormat.Format(landed)} -> {rows}{lostNote}";
        }
        if (index == results.PacketsCommitted)
            return $"{number} {Wounds(packet.Confined ? packet.WeightedWounds : packet.Wounds)}";
        return packet.Confined
            ? $"{number} {WoundFormat.Format(packet.WeightedWounds)}"
            : $"{number} {WoundFormat.Format(packet.Wounds)} plain";
    }

    /// <summary>The chip's hover text: what was rolled, what Regeneration ignored, and - once placed -
    /// where it went.</summary>
    public static string ChipTooltip(AssignWoundsResults results, int index, Func<PacketCommit, int> rowOf)
    {
        WoundPacket packet = results.Packets[index];
        string kind = packet.Confined ? $"Clump {index + 1}" : $"Plain wounds ({index + 1})";
        string rolled = packet.Ignored > AssignWoundsResults.WoundEpsilon
            ? $"{WoundFormat.Format(packet.OriginalWounds)} rolled, {WoundFormat.Format(packet.Ignored)} ignored, " +
              $"{Wounds(packet.Wounds)} to land"
            : $"{Wounds(packet.Wounds)} to land";
        if (packet.Weight < 1f) rolled += $" (a {WoundFormat.Format(packet.Weight * 100f)}% chance of a clump)";
        string text = $"{kind}: {rolled}";
        if (index < results.PacketsCommitted)
        {
            foreach (PacketCommit commit in results.Commits.Where(commit => commit.PacketIndex == index))
            {
                text += $"\n-> Model {rowOf(commit)}: {Wounds(commit.Landed)} landed";
                if (commit.Lost > AssignWoundsResults.WoundEpsilon) text += $", {WoundFormat.Format(commit.Lost)} lost";
            }
        }
        else if (index == results.PacketsCommitted)
        {
            text += "\nNext to place - click a model.";
        }
        return text;
    }

    /// <summary>What the next packet would do to this model if clicked: "takes 1 -> 2/3 left", or
    /// "takes 1, 2 lost - dies". Empty when the model is not a legal target right now.</summary>
    public static string ModelEffect(AssignWoundsResults results, PendingWounds entry)
    {
        WoundPacket? next = results.NextPacket;
        if (next == null || !ClumpModeMatters(results) || !results.CanAssignWoundTo(entry)) return "";
        float landed = results.WoundsNextCommitWouldLand(entry);
        if (landed <= AssignWoundsResults.WoundEpsilon) return "";
        float total = entry.Model.GetValue().TotalWounds;
        float capacity = total - entry.Model.GetValue().WoundsDealt - entry.Wounds;
        float lost = next.Confined ? next.Weight * MathF.Max(0f, next.Wounds - capacity) : 0f;
        float left = capacity - landed;
        string lostNote = lost > AssignWoundsResults.WoundEpsilon ? $", {WoundFormat.Format(lost)} lost" : "";
        return left <= AssignWoundsResults.WoundEpsilon
            ? $"takes {WoundFormat.Format(landed)}{lostNote} - dies"
            : $"takes {WoundFormat.Format(landed)} -> {WoundFormat.Fraction(left, total)} left";
    }
}
