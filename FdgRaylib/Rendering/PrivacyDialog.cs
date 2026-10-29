using System.Numerics;
using FdgRaylib.Analytics;
using FdgRaylib.Config;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// The anonymous-usage-statistics notice (#406). Opens by itself over the main menu until the player
/// has clicked through it once: the maintainer's message, the opt-out checkbox (ticked), and OK.
/// Nothing is sent from any install before that OK.
///
/// <para>Kept deliberately short (Chris, 2026-10-28): no itemized list here and no Privacy button on the
/// main menu - the full list is in the README's Privacy section, and opting out later is the
/// checkbox in the Options panel (<see cref="DrawSettingsControls"/>, main menu or in game).</para>
///
/// All text is ASCII-only (CLAUDE.md: the font atlas has no glyphs past U+00FF).
/// </summary>
public sealed class PrivacyDialog
{
    /// <summary>The maintainer's own words at the top of the notice, written by Chris (#406). Must stay
    /// plain ASCII - a test checks it.</summary>
    public const string IntroText =
        "Hi, I'm Chris, the guy who made this game.\n" +
        "\n" +
        "With your permission, I'd like to collect some data on when and how you play. It can't be traced back to you, and there's nothing in it worth selling.\n" +
        "\n" +
        "The biggest joy in making a game like this is knowing that people are playing and enjoying it. This is one way for me to do that. So I'd appreciate you letting me, but it's perfectly understandable if you'd rather not.\n" +
        "\n" +
        "Either way, thanks for playing!";

    public const string ChangeLaterText = "You can change this any time under Options.";

    private bool _open;
    private bool _noticeChoice = true;

    public bool IsOpen => _open;

    /// <summary>Opens the notice by itself while it has never been acknowledged, and draws it. Call once
    /// per frame from the main menu, outside its own window.</summary>
    public void Draw(int screenW, int screenH)
    {
        const string popupId = "Usage statistics##privacy";
        if (!_open)
        {
            if (UserConfig.Current.AnalyticsNoticeAcknowledged) return;
            _open = true;
            _noticeChoice = true;
            ImGui.OpenPopup(popupId);
        }

        ImGui.SetNextWindowSize(new Vector2(Math.Min(screenW * 0.55f, 900f), 0f), ImGuiCond.Always);
        ImGui.SetNextWindowPos(new Vector2(screenW * 0.5f, screenH * 0.5f), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        if (!ImGui.BeginPopupModal(popupId, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
                                            ImGuiWindowFlags.AlwaysAutoResize))
            return;

        ImGui.TextWrapped(IntroText);
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        UiButton.Checkbox("Send anonymous usage statistics", ref _noticeChoice);
        ImGui.TextDisabled(ChangeLaterText);
        ImGui.Spacing();
        if (UiButton.Confirm("OK", new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
        {
            AcknowledgeNotice(_noticeChoice);
            _open = false;
            ImGui.CloseCurrentPopup();
        }

        ImGui.EndPopup();
    }

    private static void AcknowledgeNotice(bool enabled)
    {
        if (UsageAnalytics.Client is { } client)
        {
            client.AcknowledgeNotice(enabled);
        }
        else
        {
            UserConfig.Current.AnalyticsNoticeAcknowledged = true;
            UserConfig.Current.AnalyticsEnabled = enabled;
            UserConfig.Save();
        }
        // This launch's app_launch was held back until now (a no-op if the player opted out).
        UsageAnalytics.SendLaunch();
    }

    /// <summary>The checkbox and Reset button in the Options panel (<see cref="OptionsPanel"/>). Changes apply
    /// immediately: opting out clears anything queued and the local ID.</summary>
    public static void DrawSettingsControls()
    {
        bool enabled = UserConfig.Current.AnalyticsEnabled;
        if (UiButton.Checkbox("Send anonymous usage statistics", ref enabled))
        {
            if (UsageAnalytics.Client is { } client) client.SetEnabled(enabled);
            else
            {
                UserConfig.Current.AnalyticsEnabled = enabled;
                if (!enabled) UserConfig.Current.AnalyticsInstallId = null;
                UserConfig.Save();
            }
            if (enabled) UsageAnalytics.SendLaunch(); // counts this launch if it wasn't counted yet
        }

        ImGui.BeginDisabled(!enabled);
        if (UiButton.NavigateSmall("Reset anonymous ID"))
            UsageAnalytics.Client?.ResetInstallId();
        ImGui.EndDisabled();
        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled))
            UiText.Tooltip("Replaces the random ID with a new one, so future statistics can't be linked to past ones.");

        ImGui.TextDisabled(enabled
            ? "Turning this off deletes the ID and anything not yet sent. Turning it back on makes a new ID."
            : "Nothing is sent. The ID has been deleted.");

        // A build or run that never sends regardless (a developer build, say) should say so rather than
        // let the checkbox look like it does something.
        if (UsageAnalytics.Client?.DisabledReason is { } reason)
            ImGui.TextDisabled($"This copy of the game does not send statistics ({reason}).");
    }
}
