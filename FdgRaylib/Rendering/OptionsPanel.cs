using FdgRaylib.Audio;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// The Options panel's contents, drawn by both the main menu and the in-game escape menu (#406) so the
/// two can never offer different settings. The caller owns the window, title and Back button, and calls
/// <see cref="ViewSettings.Persist"/> when the panel closes.
/// </summary>
public static class OptionsPanel
{
    /// <param name="audio">The live audio device, or null when there is none.</param>
    /// <param name="inGame">True in the escape menu: adds the controls cheat-sheet, which only means
    /// something with a table on screen.</param>
    public static void Draw(AudioManager? audio, bool inGame)
    {
        SectionHeader("Display");
        ImGui.Checkbox("Unit labels (N)", ref ViewSettings.ShowLabels);
        ImGui.Checkbox("Table grid", ref ViewSettings.ShowGrid);
        ImGui.Checkbox("Show all tokens (T, dev)", ref ViewSettings.ShowAllTokens);
        ImGui.Checkbox("Victory fireworks", ref ViewSettings.ShowVictoryFireworks);

        // #344: how long a settled dice panel stays up. Shown as a multiplier of the tuned default rather
        // than in seconds - the actual lifetime varies with the roll (a panel carrying modifier chips
        // already lingers longer), so a seconds figure would be a lie for most rolls.
        ImGui.TextUnformatted("Dice popup time");
        ImGui.SetNextItemWidth(-1f);
        ImGui.SliderFloat("##dicepopup", ref ViewSettings.DiceLingerScale,
            ViewSettings.DiceLingerMin, ViewSettings.DiceLingerMax, "%.2fx");
        Note("1.00x is the default. Hovering the dice always freezes them.");

        SectionHeader("Tactical overlay");
        ImGui.Checkbox("Weapon reach (V)", ref ViewSettings.ShowReachOverlay);
        Note("Ghosts while moving/placing; any unit you hover.");

        // #247: "Threat frontiers (F)" and "Anchor field on my position" are both gone. The red
        // frontier layer was outdated once the reach field could answer the same question in one
        // vocabulary, and pinning an enemy is now the gesture that asks for the target-anchored
        // picture -- so there is no second layer to toggle and no mode left to set.

        SectionHeader("Audio");
        if (audio != null && audio.Enabled)
        {
            ImGui.TextUnformatted("Master volume");
            ImGui.SetNextItemWidth(-1f);
            if (ImGui.SliderFloat("##mastervolume", ref ViewSettings.MasterVolume, 0f, 1f, "%.2f"))
                audio.SetMasterVolume(ViewSettings.MasterVolume);
        }
        else
        {
            Note("Audio unavailable.");
        }

        // #406: the opt-out after the first-launch notice (the only place it lives - see PrivacyDialog).
        SectionHeader("Privacy");
        PrivacyDialog.DrawSettingsControls();

        if (!inGame) return;

        SectionHeader("Controls");
        Note("Alt+drag: measure");
        Note("Alt+wheel: zoom");
        Note("Middle-drag: pan");
        Note("L: army lists (all players)");
        Note("N labels   T tokens   F threat");
        // #326: G toggles Group/Single -- it is Ctrl+wheel that cycles the formation (#277). The old line
        // said "G: cycle formation", which named the wrong key for the wrong action.
        Note("G: group / single mode (during moves)");
        Note("Ctrl+wheel: cycle formation (group moves)");
        Note($"{Resolvers.ResolverHotkeys.CycleHint}: pick model (single moves)");
        Note("Click a model: switch to it (single moves)");
        Note($"{Resolvers.ResolverKeybinds.Confirm.Hint}: auto-assign / confirm");
        Note($"{Resolvers.ResolverKeybinds.Back.Hint}: undo / back out");
        Note("Esc: open this menu");
    }

    /// <summary>Greyed explanatory text that wraps to the panel instead of running off its edge.</summary>
    public static void Note(string text)
    {
        ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetStyle().Colors[(int)ImGuiCol.TextDisabled]);
        ImGui.TextWrapped(text);
        ImGui.PopStyleColor();
    }

    // A small labelled divider between Options sections (SeparatorText isn't in this ImGui binding).
    private static void SectionHeader(string text)
    {
        ImGui.Spacing();
        ImGui.TextDisabled(text);
        ImGui.Separator();
        ImGui.Spacing();
    }
}
