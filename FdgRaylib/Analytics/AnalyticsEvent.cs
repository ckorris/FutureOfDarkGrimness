using System.Runtime.InteropServices;
using System.Text.Json.Serialization;

namespace FdgRaylib.Analytics;

/// <summary>
/// One usage event on the wire (#412). The field set, names and value shapes are a contract with
/// the Worker's allowlist (<c>tools/list-server/src/analytics.ts</c>, <c>SCHEMA</c>): an unknown
/// event name or prop key makes the Worker reject the whole batch, so add a prop there first.
/// </summary>
public sealed record AnalyticsEvent(
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("t")] long T,
    [property: JsonPropertyName("install")] string Install,
    [property: JsonPropertyName("session")] string Session,
    [property: JsonPropertyName("game")] string? Game,
    [property: JsonPropertyName("ver")] string Ver,
    [property: JsonPropertyName("os")] string Os,
    [property: JsonPropertyName("arch")] string Arch,
    [property: JsonPropertyName("props")] IReadOnlyDictionary<string, object?> Props)
{
    /// <summary>The Worker's caps: version strings longer than this are rejected.</summary>
    public const int MaxVersionChars = 64;

    public static string CurrentOs() =>
        OperatingSystem.IsWindows() ? "windows"
        : OperatingSystem.IsLinux() ? "linux"
        : OperatingSystem.IsMacOS() ? "macos"
        : "other";

    public static string CurrentArch() =>
        RuntimeInformation.OSArchitecture switch
        {
            Architecture.X64 => "x64",
            Architecture.Arm64 => "arm64",
            _ => "other",
        };
}

/// <summary>The event names the Worker accepts (#412). Kept in one place so a typo can't silently
/// produce a batch the server rejects.</summary>
public static class AnalyticsEvents
{
    public const string AppLaunch = "app_launch";
    public const string GameStart = "game_start";
    public const string GameJoin = "game_join";
    public const string GameEnd = "game_end";
    public const string GameAbandon = "game_abandon";
    public const string FeatureOpen = "feature_open";
}

/// <summary><c>feature_open.feature</c> values (#412), mirroring the Worker's enum. Only one: menu clicks
/// were dropped (Chris, 2026-10-28), and the calculator counts only after a minute open (DwellTimer).</summary>
public static class AnalyticsFeatures
{
    public const string CombatCalculator = "combat_calculator";
}
