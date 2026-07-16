using System;
using System.Collections.Generic;
using Raylib_cs;

namespace FdgRaylib.Rendering;

/// <summary>
/// The classic-RTS player colour palette (#221): eight named options a player can pick from the lobby
/// dropdown, plus the deterministic resolution from per-slot picks to effective colours. Pure data + logic
/// (unit-tested); the lobby draws the dropdown and <see cref="GameGuiWiring"/> consumes the resolution at
/// launch.
///
/// Picks sync through the lobby protocol (<c>LobbyPlayerInfoSummary.ColorIndex</c>, engine-side): the host
/// applies and rebroadcasts them, so every machine resolves the same effective colours from the same synced
/// roster. The engine treats the index as an opaque int - this palette is the only decoder.
/// </summary>
public static class PlayerColorOptions
{
    /// <summary>
    /// Order matters twice: it is the dropdown order, and unchosen slots consume these in order - so the
    /// first four preserve the pre-picker defaults (P1 orange, P2 purple, P3 green, P4 yellow).
    /// </summary>
    public static readonly (string Name, Color Color)[] Options =
    {
        ("Orange", Color.Orange),                     // (255, 161, 0)
        ("Purple", new Color(150, 70, 200, 255)),     // the old GameGuiWiring.TeamPurple
        ("Green",  Color.Green),                      // (0, 228, 48)
        ("Yellow", Color.Yellow),                     // (253, 249, 0)
        ("Red",    Color.Red),                        // (230, 41, 55)
        ("Blue",   Color.Blue),                       // (0, 121, 241)
        ("Teal",   new Color(0, 180, 170, 255)),      // no Raylib built-in
        ("Pink",   Color.Pink),                       // (255, 109, 194)
    };

    public static int Count => Options.Length;

    /// <summary>
    /// True when option <paramref name="optionIdx"/> is unavailable to row <paramref name="rowIdx"/>
    /// because it is some OTHER row's effective colour - explicit pick or assigned default alike. The
    /// dropdown disables these, so no pick can ever change another player's colour (defaults are reserved,
    /// not stealable). A row's own current colour is never "taken" from itself.
    /// </summary>
    public static bool IsTakenByAnother(int[] effectiveIndices, int rowIdx, int optionIdx)
    {
        for (int k = 0; k < effectiveIndices.Length; k++)
            if (k != rowIdx && effectiveIndices[k] == optionIdx) return true;
        return false;
    }

    /// <summary>
    /// Resolves each slot's effective option index. Explicit picks (non-null, in range) always win;
    /// unchosen slots take the first option nobody has explicitly claimed or already been defaulted to,
    /// scanning in <see cref="Options"/> order. The dropdown (via <see cref="IsTakenByAnother"/>) never
    /// offers a colour that is currently anyone else's, so in practice picks and defaults stay disjoint;
    /// if a stale pick does collide with another pick (host-side races resolve first-committed-wins),
    /// the bump-to-next-free below is the deterministic fallback. Slots beyond <see cref="Count"/> wrap
    /// by slot index.
    /// </summary>
    public static int[] ResolveIndices(IReadOnlyList<int?> chosenPerSlot)
    {
        int n = chosenPerSlot.Count;
        var result = new int[n];
        var taken = new HashSet<int>();
        for (int i = 0; i < n; i++)
            if (chosenPerSlot[i] is int c && c >= 0 && c < Options.Length)
                taken.Add(c);

        int next = 0;
        for (int i = 0; i < n; i++)
        {
            if (chosenPerSlot[i] is int chosen && chosen >= 0 && chosen < Options.Length)
            {
                result[i] = chosen;
                continue;
            }

            while (next < Options.Length && taken.Contains(next)) next++;
            if (next < Options.Length)
            {
                result[i] = next;
                taken.Add(next);
            }
            else
            {
                result[i] = i % Options.Length; // more slots than colours: wrap, duplicates unavoidable
            }
        }
        return result;
    }
}
