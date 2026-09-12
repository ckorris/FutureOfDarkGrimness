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
    /// <summary>The header's progress line(s). One line for a plain pool; two for a clump queue.</summary>
    public static string Progress(AssignWoundsResults results)
    {
        if (!results.HasConfinedPackets)
        {
            return $"{WoundFormat.Format(results.TotalAssignedWounds)} / " +
                   $"{WoundFormat.Format(results.TotalWoundsToAssign)} wounds assigned";
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
            ? $"Next: clump {number} of {count} - {WoundFormat.Format(next.WeightedWounds)} wounds to ONE model"
            : $"Next: {WoundFormat.Format(next.Wounds)} plain wound(s) ({number} of {count})";
    }

    /// <summary>The click affordance for a legal target - what landing the next packet here means.</summary>
    public static string ClickHint(AssignWoundsResults results)
    {
        WoundPacket? next = results.NextPacket;
        return next != null && next.Confined
            ? $"Click to land this clump here ({WoundFormat.Format(next.WeightedWounds)} wounds - what does not fit is lost)"
            : "Click to assign wounds (fills this model)";
    }
}
