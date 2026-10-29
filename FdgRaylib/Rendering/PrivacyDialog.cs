using System.Numerics;
using FdgRaylib.Analytics;
using FdgRaylib.Config;
using ImGuiNET;

namespace FdgRaylib.Rendering;

/// <summary>
/// The anonymous-usage-statistics UI (#406), in two modes over the main menu:
/// <list type="bullet">
/// <item><b>Notice</b> - opens by itself until the player has clicked through it once: the
/// maintainer's message, the itemized list of what is and isn't sent, the opt-out checkbox (ticked),
/// and OK. Nothing is sent from any install before this OK.</item>
/// <item><b>Settings</b> - the main menu's Privacy button: the same list and checkbox, plus Reset
/// anonymous ID.</item>
/// </list>
/// The escape menu's Options panel draws <see cref="DrawSettingsControls"/>, the same controls inline.
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

    /// <summary>What is sent, generated from the event schema the Worker enforces
    /// (tools/list-server/src/analytics.ts). Keep the two in step.</summary>
    public static readonly string[] SentItems =
    {
        "A random ID made on this computer, so players can be counted without knowing who they are. " +
            "It is not based on your hardware or account, and you can reset it below.",
        "That the game was opened, how long your last session lasted, and whether it closed normally.",
        "The game version, your operating system (Windows, Linux or macOS) and processor type.",
        "For games you host: points, game system, each army's faction, how many human, AI and " +
            "online players, which AI types, teams, the lobby settings, and how and when the game ended.",
        "For games you join: your army's faction, game system and points.",
        "Which parts of the game you open (Army Forge, Combat Calculator, Load Game, Host, Join) " +
            "and that a bug report was sent.",
    };

    public static readonly string[] NeverSentItems =
    {
        "Your player name, server names, chat, or army names and contents.",
        "File paths, your computer's name, your user name, or your language settings.",
    };

    public const string WhereText =
        "Statistics go to a small Cloudflare service run by the developer. Cloudflare sees your IP " +
        "address like any website does; the statistics themselves never store it.";

    private enum EMode { Closed, Notice, Settings }

    private EMode _mode = EMode.Closed;
    private bool _noticeChoice = true;
    private bool _popupOpened;

    public bool IsOpen => _mode != EMode.Closed;

    /// <summary>Opens the settings mode (the main menu's Privacy button).</summary>
    public void OpenSettings()
    {
        _mode = EMode.Settings;
        _popupOpened = false;
    }

    /// <summary>Draws the dialog if it is open, and opens the notice by itself while it has never been
    /// acknowledged. Call once per frame from the main menu, outside its own window.</summary>
    public void Draw(int screenW, int screenH)
    {
        if (_mode == EMode.Closed && !UserConfig.Current.AnalyticsNoticeAcknowledged)
        {
            _mode = EMode.Notice;
            _noticeChoice = true;
            _popupOpened = false;
        }
        if (_mode == EMode.Closed) return;

        const string popupId = "Usage statistics##privacy";
        if (!_popupOpened)
        {
            ImGui.OpenPopup(popupId);
            _popupOpened = true;
        }

        ImGui.SetNextWindowSize(new Vector2(Math.Min(screenW * 0.55f, 900f), 0f), ImGuiCond.Always);
        ImGui.SetNextWindowPos(new Vector2(screenW * 0.5f, screenH * 0.5f), ImGuiCond.Always, new Vector2(0.5f, 0.5f));
        if (!ImGui.BeginPopupModal(popupId, ImGuiWindowFlags.NoResize | ImGuiWindowFlags.NoMove |
                                            ImGuiWindowFlags.AlwaysAutoResize))
            return;

        if (_mode == EMode.Notice)
        {
            ImGui.TextWrapped(IntroText);
            ImGui.Spacing();
        }

        DrawItemizedList();
        ImGui.Spacing();
        ImGui.Separator();
        ImGui.Spacing();

        if (_mode == EMode.Notice)
        {
            UiButton.Checkbox("Send anonymous usage statistics", ref _noticeChoice);
            ImGui.TextDisabled("You can change this any time: Privacy on the main menu, or Options in a game.");
            ImGui.Spacing();
            if (UiButton.Confirm("OK", new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
            {
                AcknowledgeNotice(_noticeChoice);
                Close();
            }
        }
        else
        {
            DrawSettingsControls();
            ImGui.Spacing();
            if (UiButton.Back("Close", new Vector2(ImGui.GetContentRegionAvail().X, 0f)))
                Close();
        }

        ImGui.EndPopup();
    }

    private void Close()
    {
        _mode = EMode.Closed;
        ImGui.CloseCurrentPopup();
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

    /// <summary>The checkbox and Reset button, shared by the Privacy dialog and the in-game Options
    /// panel. Changes apply immediately: opting out clears anything queued and the local ID.</summary>
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

    /// <summary>The itemized what-is-sent list, also shown in the in-game Options panel on request.</summary>
    public static void DrawItemizedList()
    {
        ImGui.TextColored(ImGuiTheme.HeaderAccent, "What is sent");
        foreach (string item in SentItems) Bullet(item);
        ImGui.Spacing();
        ImGui.TextColored(ImGuiTheme.HeaderAccent, "Never sent");
        foreach (string item in NeverSentItems) Bullet(item);
        ImGui.Spacing();
        ImGui.TextWrapped(WhereText);
    }

    private static void Bullet(string text)
    {
        ImGui.Bullet();
        ImGui.SameLine();
        ImGui.TextWrapped(text);
    }
}
