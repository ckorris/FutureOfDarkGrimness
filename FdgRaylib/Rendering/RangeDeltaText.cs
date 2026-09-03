using System;

namespace FdgRaylib.Rendering;

/// <summary>
/// #387 — the one composer for "this weapon's range is modified against this target" text, so the
/// shoot panel, the CLI rows and the tactical overlay's band labels all say it the same way. The
/// effective range comes from the engine (WeaponTargetStats.EffectiveRangeInches, or the movement
/// request's WeaponRangeOverrides) — a resolver never folds rules itself (#325). An effective range
/// of 0 means "unstamped" and reads as unmodified.
/// </summary>
public static class RangeDeltaText
{
    public static bool IsModified(float baseRange, float effectiveRange) =>
        effectiveRange > 0f && Math.Abs(effectiveRange - baseRange) > 0.001f;

    /// <summary>The signed delta alone: <c>+6"</c> / <c>-4.5"</c>.</summary>
    public static string Delta(float baseRange, float effectiveRange) =>
        $"{effectiveRange - baseRange:+0.#;-0.#}\"";

    /// <summary>Compact parenthetical for row and band labels: <c> (+6")</c>; empty when unmodified.</summary>
    public static string Suffix(float baseRange, float effectiveRange) =>
        IsModified(baseRange, effectiveRange) ? $" ({Delta(baseRange, effectiveRange)})" : "";

    /// <summary>Row fact naming the number too: <c>range 30" (+6")</c>; empty when unmodified.</summary>
    public static string RowFact(float baseRange, float effectiveRange) =>
        IsModified(baseRange, effectiveRange)
            ? $"range {effectiveRange:0.#}\" ({Delta(baseRange, effectiveRange)})"
            : "";

    /// <summary>Full detail-pane line: <c>Range 30" (base 24", +6")</c>; null when unmodified.</summary>
    public static string? Detail(float baseRange, float effectiveRange) =>
        IsModified(baseRange, effectiveRange)
            ? $"Range {effectiveRange:0.#}\" (base {baseRange:0.#}\", {Delta(baseRange, effectiveRange)})"
            : null;
}
