using FdgRaylib.Config;

namespace FdgRaylib.Rendering;

/// <summary>
/// Global in-game view toggles, shared between the canvas overlays that read them and the Options panel
/// that sets them (#246). Static because there is one board on screen at a time and these are display
/// preferences, not per-game state.
///
/// <para>Saved between runs in <see cref="UserConfig"/> (#412): <see cref="Load"/> at startup,
/// <see cref="Persist"/> when an Options panel closes and at app exit (which also catches the N/T/V
/// hotkeys).</para>
/// </summary>
public static class ViewSettings
{
    /// <summary>Unit-name labels on the table (hotkey N; #329 moved it off L, which now opens
    /// the army list overlay).</summary>
    public static bool ShowLabels = true;

    /// <summary>Etched grid + felt vignette under the table (was RaylibRenderer.ShowGrid).</summary>
    public static bool ShowGrid = true;

    /// <summary>Dev toggle (hotkey T): reveal Invisible bookkeeping tokens in chips/tooltips.</summary>
    public static bool ShowAllTokens = false;

    /// <summary>
    /// #230/#247 (hotkey V): the master switch for the tactical overlay's reach picture — the opportunity
    /// field, with LoS and cover. One toggle covers every anchor it can take: the ghosts of a move or
    /// placement in progress, and the unit under the cursor (which wins while hovering, so only ever one
    /// field is on screen). Off means none of them draw. On by default.
    /// </summary>
    public static bool ShowReachOverlay = true;

    /// <summary>#331: firework bursts in the winning side's colours behind the game-over card.</summary>
    public static bool ShowVictoryFireworks = true;

    /// <summary>
    /// #344: multiplier on how long a dice-roll panel LINGERS on the #327 stack after it has settled.
    /// 1.0 is the tuned default; the slider spans <see cref="DiceLingerMin"/>..<see cref="DiceLingerMax"/>,
    /// so a player who reads fast can clear the caption zone in a third of the time and one who wants to
    /// study the arithmetic can double it.
    ///
    /// <para>The linger is the only part that scales. A panel's lifetime is <c>paced + linger</c>, and the
    /// PACED part is the engine's own wait on the beat (<c>PresentationBeat.NominalDuration</c>) - the
    /// window the dice tumble and settle in. Scaling that would retire the panel before the roll it is
    /// showing had finished, on a client whose engine is still waiting. So the knob moves the part that is
    /// purely "how long you get to re-read it", which is what the setting is actually for.</para>
    /// </summary>
    public static float DiceLingerScale = 1f;

    /// <summary>Shortest the dice panels may linger: a third of the default (#344).</summary>
    public const float DiceLingerMin = 1f / 3f;

    /// <summary>Longest the dice panels may linger: twice the default (#344).</summary>
    public const float DiceLingerMax = 2f;

    /// <summary>Master volume, 0..1. The renderer hands it to the <c>AudioManager</c> when the device
    /// opens; the Options slider sets both.</summary>
    public static float MasterVolume = 1f;

    /// <summary>#448: the main menu's war table - Animated (default), Still (one frame, for weak GPUs or
    /// anyone who finds motion distracting) or Off.</summary>
    public static EMenuBackground MenuBackground = EMenuBackground.Animated;

    /// <summary>Takes the saved values (#412), clamping anything a hand-edit pushed out of range.</summary>
    public static void Load(SavedViewSettings saved)
    {
        ShowLabels           = saved.ShowLabels;
        ShowGrid             = saved.ShowGrid;
        ShowAllTokens        = saved.ShowAllTokens;
        ShowReachOverlay     = saved.ShowReachOverlay;
        ShowVictoryFireworks = saved.ShowVictoryFireworks;
        DiceLingerScale      = float.IsFinite(saved.DiceLingerScale)
            ? Math.Clamp(saved.DiceLingerScale, DiceLingerMin, DiceLingerMax) : 1f;
        MasterVolume         = float.IsFinite(saved.MasterVolume) ? Math.Clamp(saved.MasterVolume, 0f, 1f) : 1f;
        MenuBackground       = Enum.IsDefined(saved.MenuBackground) ? saved.MenuBackground : EMenuBackground.Animated;
    }

    public static SavedViewSettings Capture() => new()
    {
        ShowLabels           = ShowLabels,
        ShowGrid             = ShowGrid,
        ShowAllTokens        = ShowAllTokens,
        ShowReachOverlay     = ShowReachOverlay,
        ShowVictoryFireworks = ShowVictoryFireworks,
        DiceLingerScale      = DiceLingerScale,
        MasterVolume         = MasterVolume,
        MenuBackground       = MenuBackground,
    };

    /// <summary>Writes the current values to the config file, if they differ from what it holds.</summary>
    public static void Persist()
    {
        SavedViewSettings now = Capture();
        if (now == UserConfig.Current.View) return;
        UserConfig.Current.View = now;
        UserConfig.Save();
    }
}
