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

    /// <summary>Longest tab label however wide the column is - past this a longer name stops telling
    /// you anything a reader needs, and the neighbouring tabs would rather have the room.</summary>
    internal const int MaxLabelChars = 20;

    /// <summary>And the shortest it may be cut to. Below this a name is not a name any more, so a very
    /// narrow column gets a scrolling tab bar rather than a row of initials.</summary>
    internal const int MinLabelChars = 7;

    /// <summary>Between a joined pair's two names.</summary>
    internal const string JoinSeparator = " + ";

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
    /// The "+" tab: a copy of the unit in hand, added at the END of the stack and selected. A copy
    /// rather than a blank slot because the question being asked is almost always a variation on the
    /// unit already configured - and a blank tab is one click away anyway, via Choose unit.
    ///
    /// <para>At the end rather than next to its original because that is where the "+" itself sits, and
    /// because ImGui keeps its own tab order: appending is the one position where the bar's order and
    /// this list's order cannot drift apart.</para>
    /// </summary>
    internal int Duplicate()
    {
        _slots.Add(new Slot(_nextKey++, Current.Clone()));
        return _active = _slots.Count - 1;
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
    /// <param name="budget">How many characters the strip can spare for this tab. The caller measures
    /// it against the column that actually exists; the default is the widest a label ever gets.</param>
    internal static string TabLabel(CalculatorSide side, int budget = MaxLabelChars)
    {
        IReadOnlyList<string> names = side.UnitNames;
        if (names.Count == 0) return EmptyLabel;
        if (names.Count == 1) return Shorten(names[0], budget);

        // A joined pair splits the budget, because both names have to survive: a tab reading only
        // "Vanguard Warriors" hides the captain standing in it, and the captain is why the tab exists.
        int each = Math.Max(MinLabelChars, (budget - JoinSeparator.Length) / 2);
        return string.Join(JoinSeparator, names.Select(name => Shorten(name, each)));
    }

    /// <inheritdoc cref="TabLabel"/>
    internal static string Shorten(string name, int max)
    {
        if (string.IsNullOrWhiteSpace(name)) return EmptyLabel;

        return name.Length <= max ? name : name[..(max - 3)].TrimEnd() + "...";
    }
}
