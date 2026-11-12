namespace FdgRaylib.Rendering.MenuBackground;

/// <summary>
/// The timeline of the main menu's war table (#448 slice 4). Each pairing plays one "battle" of
/// <see cref="BattleLength"/> seconds: it flickers into being, the fight starts, advancing units walk in
/// and then join it, patrolling units keep moving throughout (see <see cref="HoloUnit.ProgressAt"/>), a
/// measuring line appears between two units once both stand still, the camera glides in on a fight and
/// back out, and the whole thing dissolves before a new battle is dealt.
/// Underneath, the camera drifts slowly all the time on a much longer period, so no two battles are
/// framed alike.
///
/// <para>Pure functions of time - "battle time" counts from the current pairing's start, "wall time" is
/// the app clock - so the schedule is unit-testable and the renderer just samples it each frame.</para>
/// </summary>
internal static class HoloDirector
{
    public const float BattleLength = 32f;
    public const float FadeIn = 1f;
    public const float FadeOut = 2f;

    /// <summary>Shooting starts once the table has faded in (and, for an advancing unit, once it has
    /// arrived - see <see cref="HoloShot.StartsAt"/>).</summary>
    public const float FightsFrom = FadeIn;

    /// <summary>The moment the "Still" setting (and the preview's default) shows: every advancer has
    /// arrived, the fights and the measure are on, the camera is on the overview.</summary>
    public const float StillMoment = 13.5f;

    // The camera's one move per battle: overview until FocusIn, glide in, hold, glide back out.
    public const float FocusIn = 14f, FocusHeld = 18f, FocusOut = 24f, FocusBack = 28f;

    private const float YawBase = -16f, YawSwing = 7f, YawPeriod = 45f;
    private const float PitchBase = 47f, PitchSwing = 3f, PitchPeriod = 60f;

    /// <summary>Smoothstep: eases a 0..1 ramp in and out (clamped).</summary>
    public static float Smooth(float x)
    {
        x = Math.Clamp(x, 0f, 1f);
        return x * x * (3f - 2f * x);
    }

    /// <summary>How fully the battle is on the table, 0..1: fading in at the start, out at the end.</summary>
    public static float Presence(float battleTime) =>
        MathF.Min(Smooth(battleTime / FadeIn), Smooth((BattleLength - battleTime) / FadeOut));

    /// <summary>0 = whole-table overview, 1 = framed on the action.</summary>
    public static float FocusWeight(float battleTime)
    {
        if (battleTime < FocusIn) return 0f;
        if (battleTime < FocusHeld) return Smooth((battleTime - FocusIn) / (FocusHeld - FocusIn));
        if (battleTime < FocusOut) return 1f;
        return 1f - Smooth((battleTime - FocusOut) / (FocusBack - FocusOut));
    }

    /// <summary>The slow drift round the table, degrees (the signed-off framing is the centre of it).</summary>
    public static float YawDeg(float wallTime) => YawBase + YawSwing * MathF.Sin(2f * MathF.PI * wallTime / YawPeriod);

    public static float PitchDeg(float wallTime) =>
        PitchBase + PitchSwing * MathF.Sin(2f * MathF.PI * wallTime / PitchPeriod + 1f);

    /// <summary>The drift's extremes, for tests that must hold at every angle the camera can reach.</summary>
    public static (float Min, float Max) YawRange => (YawBase - YawSwing, YawBase + YawSwing);
    public static (float Min, float Max) PitchRange => (PitchBase - PitchSwing, PitchBase + PitchSwing);
}
