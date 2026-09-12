namespace FdgRaylib.Rendering.CombatCalc;

/// <summary>
/// #398: one column's stack of candidate units, shown as tabs across the top of it. Only the selected
/// tab fights - the rest are kept so "what if I gave it the plasma instead" is a click away rather than
/// a rebuild, which is the comparison the calculator exists for.
///
/// <para>Each slot carries a <see cref="Slot.Key"/> because ImGui identifies a tab by its id, not by its
/// position: keyed by index, inserting a copy in the middle would renumber every tab after it and ImGui
/// would read that as "the third tab's contents changed" rather than "a tab was inserted", losing the
/// selection and the scroll. Keys come from a per-side counter and are never reused.</para>
///
/// <para>ImGui-free on purpose, so the tab arithmetic - which slot survives a close, where a copy lands,
/// what a swap does - is testable without a window.</para>
/// </summary>
internal sealed class SideTabs
{
    /// <summary>One tab: the unit slot, and the id its column's tab bar knows it by.</summary>
    internal sealed record Slot(int Key, CalculatorSide Side);

    /// <summary>What a tab with no unit yet is called.</summary>
    internal const string EmptyLabel = "(empty)";

    /// <summary>Longest tab label before it is shortened. Tabs share a 30%-wide column, so a long name
    /// would push its neighbours out of reach - but the cut has to clear ordinary unit names ("Vanguard
    /// Warriors" is 17 characters), or every tab reads as an abbreviation.</summary>
    internal const int MaxLabelChars = 20;

    private readonly List<Slot> _slots = new();
    private int _nextKey;
    private int _active;

    internal SideTabs() => _slots.Add(new Slot(_nextKey++, new CalculatorSide()));

    internal IReadOnlyList<Slot> Slots => _slots;

    internal int Active => _active;

    /// <summary>The unit this column is actually fighting with.</summary>
    internal CalculatorSide Current => _slots[_active].Side;

    internal void Select(int index)
    {
        if (index >= 0 && index < _slots.Count) _active = index;
    }

    /// <summary>
    /// The "+" tab: a copy of the unit in hand, landing immediately after it and selected. A copy rather
    /// than a blank slot because the question being asked is almost always a variation on the unit
    /// already configured - and a blank tab is one click away anyway, via Choose unit.
    /// </summary>
    internal int Duplicate()
    {
        _slots.Insert(_active + 1, new Slot(_nextKey++, Current.Clone()));
        return ++_active;
    }

    /// <summary>
    /// Closes a tab. The last one never closes: a column with no tab has nowhere to put a unit, so the
    /// close affordance is simply absent while one remains rather than present and refusing.
    /// </summary>
    internal bool Close(int index)
    {
        if (_slots.Count <= 1 || index < 0 || index >= _slots.Count) return false;

        _slots.RemoveAt(index);

        // Closing a tab to the left of the selected one shifts it; closing the selected one hands the
        // selection to whatever slid into its place, which is the next tab (or the last, at the end).
        if (index < _active) _active--;
        else if (index == _active) _active = Math.Min(_active, _slots.Count - 1);
        return true;
    }

    /// <summary>
    /// Exchanges this column's whole stack with the other's - every tab moves, not just the selected
    /// one, so Swap A &lt;-&gt; B reads as the two columns changing places (#398 owner request).
    /// </summary>
    internal void SwapWith(SideTabs other)
    {
        List<Slot> mine = new(_slots);
        int myActive = _active;

        Adopt(other._slots, other._active);
        other.Adopt(mine, myActive);
    }

    private void Adopt(IReadOnlyList<Slot> slots, int active)
    {
        // Fresh keys from THIS side's counter. A key is the id one column's tab bar knows a tab by, so a
        // slot arriving from the other column must not bring an id this column has already spent - ImGui
        // would match it to the tab that used to hold it and carry that tab's state onto the newcomer.
        var adopted = slots.Select(slot => new Slot(_nextKey++, slot.Side)).ToList();

        _slots.Clear();
        _slots.AddRange(adopted);
        _active = Math.Clamp(active, 0, _slots.Count - 1);
    }

    /// <summary>
    /// A tab's caption: the unit's name, shortened to fit, or the empty-slot placeholder. Shortened here
    /// rather than left to ImGui's own tab clipping so the label is the same at every window width and
    /// can be pinned by a test - and so the "..." is ASCII, like every other string in the app.
    /// </summary>
    internal static string TabLabel(CalculatorSide side) => Shorten(side.UnitName);

    /// <inheritdoc cref="TabLabel"/>
    internal static string Shorten(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return EmptyLabel;

        return name.Length <= MaxLabelChars
            ? name
            : name[..(MaxLabelChars - 3)].TrimEnd() + "...";
    }
}
