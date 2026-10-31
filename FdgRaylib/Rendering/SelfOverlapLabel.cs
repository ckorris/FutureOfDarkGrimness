namespace FdgRaylib.Rendering;

/// <summary>
/// #399 - the "why is this step red" caption for a group move whose own models would end stacked on
/// each other. A sibling of <see cref="ImpassibleBlockLabel"/>: same two-line shape, drawn by the same
/// <c>DrawTerrainReasonLabel</c>, so every blocked-step explanation on the canvas reads alike.
///
/// <para>Worth spelling out rather than just painting the bases red, because the cause is invisible:
/// the models' centres never got closer - the group step turned their bases, and two rectangles that
/// were side by side ended up nose to tail. ASCII only, per CLAUDE.md.</para>
/// </summary>
internal static class SelfOverlapLabel
{
    internal const string HEADER = "Models Would Stack";

    internal const string DETAIL = "Two of this unit's bases would overlap";
}
